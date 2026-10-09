import base64
import csv
import hashlib
import hmac
import io
import json
import logging
import mimetypes
import os
import re
import secrets
import shutil
import uuid
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException, Request, UploadFile
from fastapi.responses import FileResponse, JSONResponse, Response
from fastapi.staticfiles import StaticFiles

from . import campaigns, db, sheets, worker
from .merge import render, used_fields, valid_email
from .senders import (AuthError, SendError, cfg_of, graph_account, graph_login_state, graph_sign_out,
                      graph_start_login, make_sender)

logging.basicConfig(level=logging.INFO)
HERE = os.path.dirname(__file__)
APP_PASSWORD = os.environ.get("APP_PASSWORD", "")
MAX_UPLOAD = 100 * 1024 * 1024
SECRET_KEYS = {"password", "imap_password"}


@asynccontextmanager
async def lifespan(app):
    db.init()
    worker.start()
    yield
    worker.stop.set()


app = FastAPI(title="MailMerge", lifespan=lifespan)


@app.middleware("http")
async def basic_auth(request: Request, call_next):
    if APP_PASSWORD and request.url.path != "/healthz":
        ok = False
        h = request.headers.get("authorization", "")
        if h.lower().startswith("basic "):
            try:
                _, _, pw = base64.b64decode(h[6:]).decode().partition(":")
                ok = hmac.compare_digest(pw.encode(), APP_PASSWORD.encode())
            except Exception:
                ok = False
        if not ok:
            return Response("Password required", 401, headers={"WWW-Authenticate": 'Basic realm="MailMerge"'})
    return await call_next(request)


@app.exception_handler(HTTPException)
async def http_err(_, e: HTTPException):
    return JSONResponse({"error": e.detail}, status_code=e.status_code)


def bad(msg, code=400):
    raise HTTPException(code, msg)


def _need(conn, table, id_, what):
    r = db.row(conn, f"SELECT * FROM {table} WHERE id=?", id_)
    if not r:
        bad(f"{what} not found", 404)
    return r


@app.get("/healthz")
def healthz():
    return {"ok": True}


@app.get("/api/info")
def info():
    return {"default_client_id": os.environ.get("GRAPH_CLIENT_ID", ""),
            "default_tenant": os.environ.get("GRAPH_TENANT", "common")}


# ------------------------------------------------------------------ accounts (profiles)

def _profile_out(p):
    cfg = cfg_of(p)
    safe = {k: v for k, v in cfg.items() if k not in SECRET_KEYS}
    out = {"id": p["id"], "name": p["name"], "kind": p["kind"], "from_email": p["from_email"],
           "per_minute": p["per_minute"], "config": safe,
           "has_password": bool(cfg.get("password"))}
    if p["kind"] == "graph":
        out["signed_in_as"] = graph_account(p)
    return out


@app.get("/api/profiles")
def profiles():
    with db.connect() as c:
        return [_profile_out(p) for p in db.rows(c, "SELECT * FROM profiles ORDER BY id")]


@app.post("/api/profiles")
async def profile_create(request: Request):
    b = await request.json()
    kind = b.get("kind")
    if kind not in ("graph", "smtp", "dryrun"):
        bad("Pick an account type")
    name = (b.get("name") or "").strip() or "My account"
    with db.connect() as c:
        cur = c.execute("INSERT INTO profiles(name, kind, from_email, config, per_minute) VALUES(?,?,?,?,?)",
                        (name, kind, (b.get("from_email") or "").strip(), json.dumps(b.get("config") or {}),
                         int(b.get("per_minute") or 20)))
        return _profile_out(db.row(c, "SELECT * FROM profiles WHERE id=?", cur.lastrowid))


@app.put("/api/profiles/{pid}")
async def profile_update(pid: int, request: Request):
    b = await request.json()
    with db.connect() as c:
        p = _need(c, "profiles", pid, "Account")
        old = cfg_of(p)
        new = dict(b.get("config") or {})
        for k in SECRET_KEYS:  # blank password in the form means "keep the existing one"
            if not new.get(k) and old.get(k):
                new[k] = old[k]
        c.execute("UPDATE profiles SET name=?, from_email=?, config=?, per_minute=? WHERE id=?",
                  ((b.get("name") or p["name"]).strip(), (b.get("from_email") or "").strip(),
                   json.dumps(new), max(1, min(600, int(b.get("per_minute") or p["per_minute"]))), pid))
        return _profile_out(db.row(c, "SELECT * FROM profiles WHERE id=?", pid))


@app.delete("/api/profiles/{pid}")
def profile_delete(pid: int):
    with db.connect() as c:
        c.execute("DELETE FROM profiles WHERE id=?", (pid,))
    graph_sign_out(pid)
    return {"ok": True}


@app.post("/api/profiles/{pid}/login")
def profile_login(pid: int):
    with db.connect() as c:
        p = _need(c, "profiles", pid, "Account")
    if p["kind"] != "graph":
        bad("Only Microsoft accounts use sign-in")
    try:
        return graph_start_login(p)
    except AuthError as e:
        bad(str(e))


@app.get("/api/profiles/{pid}/login")
def profile_login_status(pid: int):
    st = graph_login_state(pid)
    if not st:
        return {"status": "none"}
    if st["status"] == "connected" and st.get("account"):
        with db.connect() as c:
            p = db.row(c, "SELECT * FROM profiles WHERE id=?", pid)
            if p and not p["from_email"]:
                c.execute("UPDATE profiles SET from_email=? WHERE id=?", (st["account"], pid))
    return st


@app.post("/api/profiles/{pid}/check")
def profile_check(pid: int):
    with db.connect() as c:
        p = _need(c, "profiles", pid, "Account")
    try:
        return {"ok": True, "message": make_sender(p).check()}
    except (AuthError, SendError) as e:
        return {"ok": False, "message": str(e)}
    except Exception as e:  # noqa
        return {"ok": False, "message": f"{type(e).__name__}: {e}"}


# ------------------------------------------------------------------ lists & contacts

def _list_out(c, l):
    n = c.execute("SELECT COUNT(*) FROM contacts WHERE list_id=?", (l["id"],)).fetchone()[0]
    return {"id": l["id"], "name": l["name"], "columns": db.jload(l["columns"], []), "count": n}


@app.get("/api/lists")
def lists():
    with db.connect() as c:
        return [_list_out(c, l) for l in db.rows(c, "SELECT * FROM lists ORDER BY id")]


@app.post("/api/lists")
async def list_create(request: Request):
    b = await request.json()
    name = (b.get("name") or "").strip() or "New list"
    with db.connect() as c:
        cur = c.execute("INSERT INTO lists(name, columns, created) VALUES(?,?,?)",
                        (name, json.dumps(["Email", "FirstName", "LastName"]), db.now()))
        return _list_out(c, db.row(c, "SELECT * FROM lists WHERE id=?", cur.lastrowid))


@app.put("/api/lists/{lid}")
async def list_rename(lid: int, request: Request):
    b = await request.json()
    with db.connect() as c:
        _need(c, "lists", lid, "List")
        c.execute("UPDATE lists SET name=? WHERE id=?", ((b.get("name") or "").strip() or "List", lid))
        return _list_out(c, db.row(c, "SELECT * FROM lists WHERE id=?", lid))


@app.delete("/api/lists/{lid}")
def list_delete(lid: int):
    with db.connect() as c:
        c.execute("DELETE FROM lists WHERE id=?", (lid,))
    return {"ok": True}


def _contact_out(ct, sup):
    return {"id": ct["id"], "email": ct["email"], "skip": bool(ct["skip"]),
            "data": campaigns.contact_data(ct), "valid": valid_email(ct["email"]),
            "opted_out": ct["email"] in sup}


@app.get("/api/lists/{lid}/contacts")
def list_contacts(lid: int):
    with db.connect() as c:
        l = _need(c, "lists", lid, "List")
        sup = campaigns.suppressed_set(c)
        cts = db.rows(c, "SELECT * FROM contacts WHERE list_id=? ORDER BY id", lid)
        return {"id": lid, "name": l["name"], "columns": db.jload(l["columns"], []),
                "contacts": [_contact_out(ct, sup) for ct in cts]}


@app.post("/api/lists/{lid}/contacts")
async def contact_add(lid: int, request: Request):
    b = await request.json()
    data = {k: str(v or "").strip() for k, v in (b.get("data") or {}).items()}
    email = (data.get("Email") or "").strip()
    if not email:
        bad("Enter an email address")
    with db.connect() as c:
        l = _need(c, "lists", lid, "List")
        cols = db.jload(l["columns"], [])
        for k in data:
            if k.lower() not in [x.lower() for x in cols]:
                cols.append(k)
        try:
            cur = c.execute("INSERT INTO contacts(list_id, email, data) VALUES(?,?,?)",
                            (lid, email.lower(), json.dumps(data)))
        except Exception:
            bad("That email is already in this list", 409)
        c.execute("UPDATE lists SET columns=? WHERE id=?", (json.dumps(cols), lid))
        return _contact_out(db.row(c, "SELECT * FROM contacts WHERE id=?", cur.lastrowid), set())


@app.put("/api/contacts/{cid}")
async def contact_update(cid: int, request: Request):
    b = await request.json()
    with db.connect() as c:
        ct = _need(c, "contacts", cid, "Contact")
        data = {k: str(v or "").strip() for k, v in (b.get("data") or campaigns.contact_data(ct)).items()}
        email = (data.get("Email") or "").strip().lower()
        if not email:
            bad("Email can't be empty. Delete the row instead.")
        skip = int(bool(b["skip"])) if "skip" in b else ct["skip"]
        try:
            c.execute("UPDATE contacts SET email=?, data=?, skip=? WHERE id=?",
                      (email, json.dumps(data), skip, cid))
        except Exception:
            bad("Another contact in this list already has that email", 409)
        if email != ct["email"]:  # keep not-yet-sent campaign rows pointing at the fixed address
            c.execute("UPDATE OR IGNORE messages SET email=? WHERE contact_id=? AND status IN ('pending','skipped')",
                      (email, cid))
        return _contact_out(db.row(c, "SELECT * FROM contacts WHERE id=?", cid), campaigns.suppressed_set(c))


@app.delete("/api/contacts/{cid}")
def contact_delete(cid: int):
    with db.connect() as c:
        c.execute("DELETE FROM contacts WHERE id=?", (cid,))
    return {"ok": True}


@app.post("/api/lists/{lid}/columns")
async def column_add(lid: int, request: Request):
    name = ((await request.json()).get("name") or "").strip()
    if not name or re.search(r"[{}|]", name):
        bad("Column names can't be empty or contain { } |")
    with db.connect() as c:
        l = _need(c, "lists", lid, "List")
        cols = db.jload(l["columns"], [])
        if name.lower() in [x.lower() for x in cols]:
            bad("That column already exists", 409)
        cols.append(name)
        c.execute("UPDATE lists SET columns=? WHERE id=?", (json.dumps(cols), lid))
        return {"columns": cols}


@app.delete("/api/lists/{lid}/columns/{name}")
def column_delete(lid: int, name: str):
    if name == "Email":
        bad("The Email column can't be removed")
    with db.connect() as c:
        l = _need(c, "lists", lid, "List")
        cols = [x for x in db.jload(l["columns"], []) if x != name]
        c.execute("BEGIN")
        for ct in db.rows(c, "SELECT id, data FROM contacts WHERE list_id=?", lid):
            d = db.jload(ct["data"])
            if name in d:
                d.pop(name)
                c.execute("UPDATE contacts SET data=? WHERE id=?", (json.dumps(d), ct["id"]))
        c.execute("UPDATE lists SET columns=? WHERE id=?", (json.dumps(cols), lid))
        c.execute("COMMIT")
        return {"columns": cols}


@app.get("/api/lists/{lid}/export")
def list_export(lid: int):
    with db.connect() as c:
        l = _need(c, "lists", lid, "List")
        cols = db.jload(l["columns"], [])
        recs = [campaigns.contact_data(ct) for ct in db.rows(c, "SELECT * FROM contacts WHERE list_id=? ORDER BY id", lid)]
    path = os.path.join(db.DATA_DIR, "uploads", f"export-{uuid.uuid4().hex}.xlsx")
    sheets.write_xlsx(path, cols, recs)
    data = open(path, "rb").read()
    os.remove(path)
    fname = re.sub(r"[^\w\- ]", "", l["name"]).strip() or "contacts"
    return Response(data, media_type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    headers={"Content-Disposition": f'attachment; filename="{fname}.xlsx"'})


# ------------------------------------------------------------------ import (upload -> review -> commit)

TOKEN = re.compile(r"^[0-9a-f]{32}$")


def _upload_path(token):
    if not TOKEN.match(token):
        bad("Bad upload id", 404)
    d = os.path.join(db.DATA_DIR, "uploads")
    for f in os.listdir(d):
        if f.startswith(token + "."):
            return os.path.join(d, f)
    bad("That upload has expired. Please choose the file again.", 404)


def _preview(path, sheet=None, fname=""):
    try:
        names = sheets.sheet_names(path)
        sheet = sheet if sheet in names else names[0]
        headers, rows = sheets.read(path, sheet)
    except sheets.SheetError as e:
        bad(str(e))
    token = os.path.basename(path).split(".")[0]
    em = sheets.detect_email_column(headers, rows)
    sample_emails = [r[headers.index(em)] for r in rows] if em else []
    return {"token": token, "filename": fname, "sheets": names, "sheet": sheet, "headers": headers,
            "email_column": em, "row_count": len(rows), "sample": rows[:6],
            "invalid_count": sum(1 for e in sample_emails if e and not valid_email(e)),
            "blank_count": sum(1 for e in sample_emails if not e)}


async def _read_upload(request: Request, field="file"):
    form = await request.form()
    f = form.get(field)
    if not isinstance(f, UploadFile) and not hasattr(f, "filename"):
        bad("No file received")
    return form, f


@app.post("/api/import/preview")
async def import_preview(request: Request):
    _, f = await _read_upload(request)
    ext = os.path.splitext(f.filename or "")[1].lower()
    if ext not in (".xlsx", ".xlsm", ".csv", ".tsv", ".txt"):
        bad("Please upload an Excel .xlsx file (or a .csv). Older .xls files: open in Excel and Save As .xlsx.")
    token = uuid.uuid4().hex
    path = os.path.join(db.DATA_DIR, "uploads", token + ext)
    size = 0
    with open(path, "wb") as out:
        while chunk := await f.read(1024 * 1024):
            size += len(chunk)
            if size > MAX_UPLOAD:
                out.close()
                os.remove(path)
                bad("File is too large")
            out.write(chunk)
    return _preview(path, None, f.filename)


@app.get("/api/import/{token}")
def import_sheet(token: str, sheet: str = None):
    return _preview(_upload_path(token), sheet)


@app.post("/api/import/{token}/commit")
async def import_commit(token: str, request: Request):
    b = await request.json()
    path = _upload_path(token)
    try:
        headers, rows = sheets.read(path, b.get("sheet"))
    except sheets.SheetError as e:
        bad(str(e))
    email_col = b.get("email_column")
    if email_col not in headers:
        bad("Choose which column holds the email addresses")
    update_existing = bool(b.get("update_existing"))
    with db.connect() as c:
        if b.get("list_id"):
            l = _need(c, "lists", int(b["list_id"]), "List")
            lid, cols = l["id"], db.jload(l["columns"], [])
        else:
            cur = c.execute("INSERT INTO lists(name, columns, created) VALUES(?,?,?)",
                            ((b.get("new_list_name") or "Imported list").strip(), '["Email"]', db.now()))
            lid, cols = cur.lastrowid, ["Email"]
        # map file headers -> list columns (case-insensitive); email column becomes "Email"
        by_lower = {x.lower(): x for x in cols}
        mapping = {}
        for h in headers:
            target = "Email" if h == email_col else by_lower.get(h.lower(), h)
            if h != email_col and target == "Email":
                target = f"{h} (2)"
            mapping[h] = target
            if target.lower() not in by_lower:
                cols.append(target)
                by_lower[target.lower()] = target
        existing = {r["email"]: r["id"] for r in c.execute("SELECT id, email FROM contacts WHERE list_id=?", (lid,))}
        res = {"added": 0, "updated": 0, "duplicates": 0, "invalid": 0, "no_email": 0}
        ei = headers.index(email_col)
        c.execute("BEGIN")
        for r in rows:
            email = r[ei].strip().lower()
            if not email:
                res["no_email"] += 1
                continue
            rec = {mapping[h]: r[i].strip() for i, h in enumerate(headers)}
            rec["Email"] = email
            if email in existing:
                if update_existing:
                    old = db.jload(c.execute("SELECT data FROM contacts WHERE id=?", (existing[email],)).fetchone()[0])
                    old.update({k: v for k, v in rec.items() if v != ""})
                    c.execute("UPDATE contacts SET data=? WHERE id=?", (json.dumps(old), existing[email]))
                    res["updated"] += 1
                else:
                    res["duplicates"] += 1
                continue
            cur = c.execute("INSERT INTO contacts(list_id, email, data) VALUES(?,?,?)", (lid, email, json.dumps(rec)))
            existing[email] = cur.lastrowid
            res["added"] += 1
            if not valid_email(email):
                res["invalid"] += 1
        c.execute("UPDATE lists SET columns=? WHERE id=?", (json.dumps(cols), lid))
        c.execute("COMMIT")
    os.remove(path)
    res["list_id"] = lid
    return res


# ------------------------------------------------------------------ opt-outs

@app.get("/api/suppressions")
def sup_list():
    with db.connect() as c:
        return db.rows(c, "SELECT * FROM suppressions ORDER BY added DESC")


@app.post("/api/suppressions")
async def sup_add(request: Request):
    b = await request.json()
    emails = {e.strip().lower() for e in re.split(r"[\s,;]+", b.get("emails") or "") if e.strip()}
    with db.connect() as c:
        for e in emails:
            c.execute("INSERT OR IGNORE INTO suppressions(email, added, reason) VALUES(?,?,?)",
                      (e, db.now(), (b.get("reason") or "").strip()))
    return {"added": len(emails)}


@app.delete("/api/suppressions/{email:path}")
def sup_delete(email: str):
    with db.connect() as c:
        c.execute("DELETE FROM suppressions WHERE email=?", (email.lower(),))
    return {"ok": True}


# ------------------------------------------------------------------ campaigns

def _camp_out(c, camp):
    out = dict(camp)
    out["is_html"] = bool(camp["is_html"])
    out["counts"] = campaigns.counts(c, camp["id"])
    out["attachments"] = [{"id": a["id"], "filename": a["filename"], "size": a["size"]}
                          for a in db.rows(c, "SELECT * FROM attachments WHERE campaign_id=? ORDER BY id", camp["id"])]
    return out


@app.get("/api/campaigns")
def camp_list():
    with db.connect() as c:
        return [_camp_out(c, x) for x in db.rows(c, "SELECT * FROM campaigns ORDER BY id DESC")]


@app.post("/api/campaigns")
async def camp_create(request: Request):
    b = await request.json()
    with db.connect() as c:
        pid = (db.row(c, "SELECT id FROM profiles ORDER BY id LIMIT 1") or {}).get("id")
        lid = (db.row(c, "SELECT id FROM lists ORDER BY id DESC LIMIT 1") or {}).get("id")
        cur = c.execute("INSERT INTO campaigns(name, profile_id, list_id, created) VALUES(?,?,?,?)",
                        ((b.get("name") or "").strip() or "New email", pid, lid, db.now()))
        return _camp_out(c, db.row(c, "SELECT * FROM campaigns WHERE id=?", cur.lastrowid))


@app.get("/api/campaigns/{cid}")
def camp_get(cid: int):
    with db.connect() as c:
        return _camp_out(c, _need(c, "campaigns", cid, "Campaign"))


@app.put("/api/campaigns/{cid}")
async def camp_update(cid: int, request: Request):
    b = await request.json()
    with db.connect() as c:
        camp = _need(c, "campaigns", cid, "Campaign")
        if camp["status"] == "running":
            bad("Pause the campaign before editing it")
        f = {k: b[k] for k in ("name", "profile_id", "list_id", "subject", "body") if k in b}
        if "is_html" in b:
            f["is_html"] = int(bool(b["is_html"]))
        if f:
            c.execute(f"UPDATE campaigns SET {', '.join(k + '=?' for k in f)} WHERE id=?", (*f.values(), cid))
        return _camp_out(c, db.row(c, "SELECT * FROM campaigns WHERE id=?", cid))


@app.delete("/api/campaigns/{cid}")
def camp_delete(cid: int):
    with db.connect() as c:
        camp = _need(c, "campaigns", cid, "Campaign")
        if camp["status"] == "running":
            bad("Pause the campaign first")
        c.execute("DELETE FROM campaigns WHERE id=?", (cid,))
    shutil.rmtree(os.path.join(db.DATA_DIR, "attachments", str(cid)), ignore_errors=True)
    return {"ok": True}


@app.post("/api/campaigns/{cid}/attachments")
async def attach_add(cid: int, request: Request):
    _, f = await _read_upload(request)
    with db.connect() as c:
        _need(c, "campaigns", cid, "Campaign")
        folder = os.path.join(db.DATA_DIR, "attachments", str(cid))
        os.makedirs(folder, exist_ok=True)
        name = os.path.basename(f.filename or "attachment").replace("\x00", "") or "attachment"
        path = os.path.join(folder, uuid.uuid4().hex + "_" + re.sub(r"[^\w.\- ]", "_", name))
        size = 0
        with open(path, "wb") as out:
            while chunk := await f.read(1024 * 1024):
                size += len(chunk)
                if size > MAX_UPLOAD:
                    out.close()
                    os.remove(path)
                    bad("Attachment is too large")
                out.write(chunk)
        total = c.execute("SELECT COALESCE(SUM(size),0) FROM attachments WHERE campaign_id=?", (cid,)).fetchone()[0]
        ctype = f.content_type or mimetypes.guess_type(name)[0] or "application/octet-stream"
        c.execute("INSERT INTO attachments(campaign_id, filename, content_type, path, size) VALUES(?,?,?,?,?)",
                  (cid, name, ctype, path, size))
        return {"ok": True, "total_bytes": total + size}


@app.delete("/api/attachments/{aid}")
def attach_delete(aid: int):
    with db.connect() as c:
        a = db.row(c, "SELECT * FROM attachments WHERE id=?", aid)
        if a:
            c.execute("DELETE FROM attachments WHERE id=?", (aid,))
            try:
                os.remove(a["path"])
            except OSError:
                pass
    return {"ok": True}


def _first_contact(c, camp, contact_id=None):
    if contact_id:
        ct = db.row(c, "SELECT * FROM contacts WHERE id=?", int(contact_id))
    else:
        ct = db.row(c, "SELECT * FROM contacts WHERE list_id=? ORDER BY id LIMIT 1", camp["list_id"])
    return ct


@app.post("/api/campaigns/{cid}/preview")
async def camp_preview(cid: int, request: Request):
    b = await request.json()
    with db.connect() as c:
        camp = _need(c, "campaigns", cid, "Campaign")
        ct = _first_contact(c, camp, b.get("contact_id"))
        data = campaigns.contact_data(ct) if ct else {}
        n = c.execute("SELECT COUNT(*) FROM contacts WHERE list_id=?", (camp["list_id"],)).fetchone()[0] if camp["list_id"] else 0
        nxt = db.row(c, "SELECT id FROM contacts WHERE list_id=? AND id>? ORDER BY id LIMIT 1",
                     camp["list_id"], ct["id"]) if ct else None
        prv = db.row(c, "SELECT id FROM contacts WHERE list_id=? AND id<? ORDER BY id DESC LIMIT 1",
                     camp["list_id"], ct["id"]) if ct else None
        cols = [x.lower() for x in (db.jload(db.row(c, "SELECT columns FROM lists WHERE id=?", camp["list_id"])["columns"], [])
                                    if camp["list_id"] else [])]
    html = bool(camp["is_html"])
    unknown = sorted({f for f in used_fields(camp["subject"]) + used_fields(camp["body"]) if f.lower() not in cols})
    empty = sorted({f for f in used_fields(camp["subject"]) + used_fields(camp["body"])
                    if f.lower() in cols and not str(next((v for k, v in data.items() if k.lower() == f.lower()), "")).strip()})
    return {"to": data.get("Email", ""), "subject": render(camp["subject"], data, False),
            "body": render(camp["body"], data, html), "is_html": html, "contact_id": ct["id"] if ct else None,
            "next_id": nxt["id"] if nxt else None, "prev_id": prv["id"] if prv else None, "total": n,
            "unknown_fields": unknown, "empty_fields": empty}


@app.post("/api/campaigns/{cid}/test")
async def camp_test(cid: int, request: Request):
    b = await request.json()
    to = (b.get("to") or "").strip()
    if not valid_email(to):
        bad("Enter your own email address to send the test to")
    with db.connect() as c:
        camp = _need(c, "campaigns", cid, "Campaign")
        profile = db.row(c, "SELECT * FROM profiles WHERE id=?", camp["profile_id"]) if camp["profile_id"] else None
        if not profile:
            bad("Choose a sending account first")
        ct = _first_contact(c, camp)
        data = campaigns.contact_data(ct) if ct else {"Email": to}
        try:
            out = campaigns.build_out(c, camp, data, to, subject_prefix="[TEST] ")
            make_sender(profile).send_now(out)
        except (AuthError, SendError) as e:
            bad(str(e))
        except Exception as e:  # noqa
            bad(f"Test failed: {e}")
    return {"ok": True, "message": f"Test sent to {to} using the first contact's details."}


@app.get("/api/campaigns/{cid}/plan")
def camp_plan(cid: int):
    with db.connect() as c:
        camp = _need(c, "campaigns", cid, "Campaign")
        profile = db.row(c, "SELECT * FROM profiles WHERE id=?", camp["profile_id"]) if camp["profile_id"] else None
        base = campaigns.plan(c, camp) if camp["list_id"] else {}
        return {**base,
                "supports_drafts": bool(profile and make_sender(profile).supports_drafts),
                "account": profile["name"] if profile else None,
                "from_email": profile["from_email"] if profile else None}


@app.post("/api/campaigns/{cid}/run")
async def camp_run(cid: int, request: Request):
    b = await request.json()
    mode = b.get("mode")
    if mode not in ("draft", "send"):
        bad("mode must be draft or send")
    with db.connect() as c:
        camp = _need(c, "campaigns", cid, "Campaign")
        if camp["status"] == "running":
            bad("Already running")
        profile = db.row(c, "SELECT * FROM profiles WHERE id=?", camp["profile_id"]) if camp["profile_id"] else None
        if not profile:
            bad("Choose a sending account first")
        if not camp["list_id"]:
            bad("Choose who to send to first")
        if not camp["subject"].strip():
            bad("Add a subject first")
        if not camp["body"].strip():
            bad("Write the message first")
        if mode == "draft" and not make_sender(profile).supports_drafts:
            bad("This account can't save drafts (it has no IMAP settings). Use Send instead.")
        res = campaigns.materialize(c, camp)
        c.execute("UPDATE campaigns SET status='running', mode=?, note=NULL WHERE id=?", (mode, cid))
        return {"ok": True, "counts": res}


@app.post("/api/campaigns/{cid}/pause")
def camp_pause(cid: int):
    with db.connect() as c:
        c.execute("UPDATE campaigns SET status='paused', note=NULL WHERE id=? AND status='running'", (cid,))
    return {"ok": True}


@app.post("/api/campaigns/{cid}/retry-failed")
def camp_retry(cid: int):
    with db.connect() as c:
        c.execute("UPDATE messages SET status='pending', error=NULL, updated=? WHERE campaign_id=? AND status='failed'",
                  (db.now(), cid))
        return {"ok": True, "counts": campaigns.counts(c, cid)}


@app.get("/api/campaigns/{cid}/messages")
def camp_messages(cid: int, status: str = None, limit: int = 500):
    with db.connect() as c:
        q, args = "SELECT * FROM messages WHERE campaign_id=?", [cid]
        if status:
            q += " AND status=?"
            args.append(status)
        q += " ORDER BY id LIMIT ?"
        args.append(max(1, min(limit, 5000)))
        return db.rows(c, q, *args)


@app.post("/api/messages/{mid}/mark")
async def msg_mark(mid: int, request: Request):
    b = await request.json()
    status = b.get("status")
    if status not in ("sent", "pending", "skipped"):
        bad("Unsupported status")
    with db.connect() as c:
        m = _need(c, "messages", mid, "Message")
        if m["status"] not in ("uncertain", "failed", "skipped", "pending"):
            bad("Only unresolved messages can be changed")
        c.execute("UPDATE messages SET status=?, error=?, updated=? WHERE id=?",
                  (status, "Marked by you" if status != "pending" else None, db.now(), mid))
    return {"ok": True}


@app.get("/api/campaigns/{cid}/log.csv")
def camp_log(cid: int):
    with db.connect() as c:
        camp = _need(c, "campaigns", cid, "Campaign")
        msgs = db.rows(c, "SELECT email, status, error, updated FROM messages WHERE campaign_id=? ORDER BY id", cid)
    buf = io.StringIO()
    w = csv.writer(buf)
    w.writerow(["Email", "Status", "Note", "Updated (UTC)"])
    for m in msgs:
        w.writerow([m["email"], m["status"], m["error"] or "", m["updated"]])
    fname = re.sub(r"[^\w\- ]", "", camp["name"]).strip() or "campaign"
    return Response(buf.getvalue(), media_type="text/csv",
                    headers={"Content-Disposition": f'attachment; filename="{fname} log.csv"'})


# ------------------------------------------------------------------ UI

app.mount("/static", StaticFiles(directory=os.path.join(HERE, "static")), name="static")


@app.get("/")
def index():
    return FileResponse(os.path.join(HERE, "static", "index.html"))
