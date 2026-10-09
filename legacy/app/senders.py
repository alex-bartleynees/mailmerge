"""Sending backends. Each one implements the same small interface:

    supports_drafts            -> bool
    create_draft(msg) -> id    -> a real draft in the mailbox's Drafts folder
    send_draft(id)             -> sends that draft (it then appears in Sent Items)
    send_now(msg)              -> create + send in one go
    check() -> str             -> verify the connection, returns a human message
"""
import base64
import imaplib
import json
import logging
import mimetypes
import os
import re
import smtplib
import ssl
import threading
import time
import uuid
from dataclasses import dataclass, field
from email.message import EmailMessage
from email.utils import formatdate, make_msgid

import msal
import requests

from . import db
from .merge import html_to_text

log = logging.getLogger("mailmerge.senders")


class AuthError(Exception):
    """Account needs attention (not signed in, token expired, permission denied)."""


class SendError(Exception):
    """This one message failed."""


@dataclass
class OutMessage:
    to: str
    subject: str
    body: str
    html: bool = False
    attachments: list = field(default_factory=list)  # dicts: filename, content_type, path, size


def cfg_of(profile) -> dict:
    return db.jload(profile.get("config") or "{}")


def build_mime(msg: OutMessage, from_email: str, from_name: str = "") -> EmailMessage:
    m = EmailMessage()
    m["From"] = f'"{from_name}" <{from_email}>' if from_name else from_email
    m["To"] = msg.to
    m["Subject"] = msg.subject
    m["Date"] = formatdate(localtime=True)
    m["Message-ID"] = make_msgid(domain=(from_email.split("@")[-1] or "localhost"))
    if msg.html:
        m.set_content(html_to_text(msg.body) or " ")
        m.add_alternative(msg.body, subtype="html")
    else:
        m.set_content(msg.body or " ")
    for a in msg.attachments:
        ctype = a.get("content_type") or mimetypes.guess_type(a["filename"])[0] or "application/octet-stream"
        maintype, _, subtype = ctype.partition("/")
        with open(a["path"], "rb") as f:
            m.add_attachment(f.read(), maintype=maintype, subtype=subtype or "octet-stream",
                             filename=a["filename"])
    return m


# --------------------------------------------------------------------------- Microsoft Graph

GRAPH = "https://graph.microsoft.com/v1.0"
GRAPH_SCOPES = ["Mail.ReadWrite", "Mail.Send", "User.Read"]
_login_state = {}  # profile id -> dict(status, user_code, url, message, error, account)
_cache_lock = threading.Lock()


def _msal_app(profile):
    cfg = cfg_of(profile)
    client_id = (cfg.get("client_id") or os.environ.get("GRAPH_CLIENT_ID") or "").strip()
    if not client_id:
        raise AuthError("No Azure app (client) ID set. See the README, 'Connect Outlook / Microsoft 365'.")
    tenant = (cfg.get("tenant") or os.environ.get("GRAPH_TENANT") or "common").strip()
    path = os.path.join(db.DATA_DIR, "tokens", f"profile-{profile['id']}.json")
    cache = msal.SerializableTokenCache()
    with _cache_lock:
        if os.path.exists(path):
            cache.deserialize(open(path).read())
    try:
        app = msal.PublicClientApplication(
            client_id, authority=f"https://login.microsoftonline.com/{tenant}", token_cache=cache)
    except (requests.RequestException, ValueError) as e:
        raise AuthError(f"Couldn't reach Microsoft sign-in (check the internet connection and the tenant "
                        f"setting): {str(e)[:160]}")

    def save():
        if cache.has_state_changed:
            with _cache_lock:
                with open(path, "w") as f:
                    f.write(cache.serialize())
            try:
                os.chmod(path, 0o600)
            except OSError:
                pass
    return app, save


def graph_start_login(profile) -> dict:
    app, save = _msal_app(profile)
    flow = app.initiate_device_flow(scopes=GRAPH_SCOPES)
    if "user_code" not in flow:
        raise AuthError(flow.get("error_description") or "Couldn't start sign-in.")
    st = {"status": "pending", "user_code": flow["user_code"], "url": flow["verification_uri"],
          "message": flow.get("message", ""), "error": None, "account": None}
    _login_state[profile["id"]] = st

    def wait():
        try:
            res = app.acquire_token_by_device_flow(flow)
            if "access_token" in res:
                save()
                st["status"] = "connected"
                st["account"] = (res.get("id_token_claims") or {}).get("preferred_username") or ""
            else:
                st["status"] = "error"
                st["error"] = res.get("error_description") or res.get("error") or "Sign-in failed."
        except Exception as e:  # noqa
            st["status"] = "error"
            st["error"] = str(e)

    threading.Thread(target=wait, daemon=True).start()
    return st


def graph_login_state(pid: int):
    return _login_state.get(pid)


def graph_account(profile):
    """Signed-in account for this profile, read straight from the token cache (no network)."""
    path = os.path.join(db.DATA_DIR, "tokens", f"profile-{profile['id']}.json")
    try:
        with open(path) as f:
            accts = (json.load(f).get("Account") or {}).values()
        return next((a["username"] for a in accts if a.get("username")), None)
    except (OSError, ValueError):
        return None


def graph_sign_out(profile_id: int):
    path = os.path.join(db.DATA_DIR, "tokens", f"profile-{profile_id}.json")
    if os.path.exists(path):
        os.remove(path)
    _login_state.pop(profile_id, None)


class GraphSender:
    supports_drafts = True
    ATTACH_INLINE_MAX = 3_000_000
    CHUNK = 4 * 327_680  # must be a multiple of 320 KiB

    def __init__(self, profile):
        self.p = profile

    def _token(self) -> str:
        app, save = _msal_app(self.p)
        accts = app.get_accounts()
        if not accts:
            raise AuthError("Not signed in to Microsoft. Open Accounts and click Sign in.")
        res = app.acquire_token_silent(GRAPH_SCOPES, account=accts[0])
        if not res or "access_token" not in res:
            raise AuthError("Microsoft sign-in has expired. Open Accounts and sign in again.")
        save()
        return res["access_token"]

    def _call(self, method, path, **kw):
        url = path if path.startswith("http") else GRAPH + path
        for attempt in range(6):
            r = requests.request(method, url, headers={"Authorization": "Bearer " + self._token()},
                                 timeout=90, **kw)
            if r.status_code in (429, 503, 504) and attempt < 5:
                wait = r.headers.get("Retry-After", "")
                time.sleep(min(int(wait) if wait.isdigit() else 5 * (attempt + 1), 120))
                continue
            if r.status_code in (401, 403):
                raise AuthError(f"Microsoft refused the request ({r.status_code}). Your IT admin may need to "
                                f"approve the app, or sign in again. {self._err(r)}")
            if not r.ok:
                raise SendError(f"Microsoft Graph {r.status_code}: {self._err(r)}")
            return r
        raise SendError("Microsoft Graph kept asking us to slow down. Try again later.")

    @staticmethod
    def _err(r) -> str:
        try:
            return (r.json().get("error") or {}).get("message", "")[:300]
        except Exception:
            return r.text[:300]

    def create_draft(self, msg: OutMessage) -> str:
        payload = {"subject": msg.subject,
                   "body": {"contentType": "HTML" if msg.html else "Text", "content": msg.body},
                   "toRecipients": [{"emailAddress": {"address": msg.to}}]}
        mid = self._call("POST", "/me/messages", json=payload).json()["id"]
        try:
            for a in msg.attachments:
                self._attach(mid, a)
        except Exception:
            try:  # don't leave a half-built draft behind
                self._call("DELETE", f"/me/messages/{mid}")
            except Exception:
                pass
            raise
        return mid

    def _attach(self, mid, a):
        ctype = a.get("content_type") or mimetypes.guess_type(a["filename"])[0] or "application/octet-stream"
        size = os.path.getsize(a["path"])
        if size <= self.ATTACH_INLINE_MAX:
            data = base64.b64encode(open(a["path"], "rb").read()).decode()
            self._call("POST", f"/me/messages/{mid}/attachments", json={
                "@odata.type": "#microsoft.graph.fileAttachment", "name": a["filename"],
                "contentType": ctype, "contentBytes": data})
            return
        sess = self._call("POST", f"/me/messages/{mid}/attachments/createUploadSession", json={
            "AttachmentItem": {"attachmentType": "file", "name": a["filename"], "size": size,
                               "contentType": ctype}}).json()
        url, pos = sess["uploadUrl"], 0
        with open(a["path"], "rb") as f:
            while True:
                chunk = f.read(self.CHUNK)
                if not chunk:
                    break
                r = requests.put(url, data=chunk, timeout=300, headers={
                    "Content-Length": str(len(chunk)),
                    "Content-Range": f"bytes {pos}-{pos + len(chunk) - 1}/{size}"})
                if not r.ok:
                    raise SendError(f"Attachment upload failed ({r.status_code}): {r.text[:200]}")
                pos += len(chunk)

    def send_draft(self, draft_id: str):
        self._call("POST", f"/me/messages/{draft_id}/send")

    def send_now(self, msg: OutMessage):
        self.send_draft(self.create_draft(msg))

    def check(self) -> str:
        me = self._call("GET", "/me?$select=mail,userPrincipalName,displayName").json()
        return f"Connected as {me.get('mail') or me.get('userPrincipalName')}"


# --------------------------------------------------------------------------- SMTP (+ optional IMAP)

class SmtpSender:
    def __init__(self, profile):
        self.p = profile
        self.c = cfg_of(profile)

    @property
    def supports_drafts(self) -> bool:
        return bool(self.c.get("imap_host"))

    def _from(self):
        return self.p["from_email"] or self.c.get("user", "")

    def _smtp(self):
        host, sec = self.c.get("host", ""), (self.c.get("security") or "starttls").lower()
        port = int(self.c.get("port") or (465 if sec == "ssl" else 587))
        if not host:
            raise AuthError("SMTP server isn't set up for this account.")
        try:
            if sec == "ssl":
                s = smtplib.SMTP_SSL(host, port, timeout=60, context=ssl.create_default_context())
            else:
                s = smtplib.SMTP(host, port, timeout=60)
                s.ehlo()
                if sec == "starttls":
                    s.starttls(context=ssl.create_default_context())
                    s.ehlo()
            if self.c.get("user"):
                s.login(self.c["user"], self.c.get("password", ""))
            return s
        except smtplib.SMTPAuthenticationError as e:
            raise AuthError(f"The mail server rejected the username/password ({e.smtp_code}). "
                            f"Many providers need an app password.")
        except (OSError, smtplib.SMTPException) as e:
            raise AuthError(f"Couldn't connect to the mail server: {e}")

    def _imap(self):
        host = self.c.get("imap_host")
        port = int(self.c.get("imap_port") or 993)
        try:
            if port == 993:
                im = imaplib.IMAP4_SSL(host, port, timeout=60, ssl_context=ssl.create_default_context())
            else:
                im = imaplib.IMAP4(host, port, timeout=60)
                im.starttls(ssl.create_default_context())
            im.login(self.c.get("imap_user") or self.c.get("user", ""),
                     self.c.get("imap_password") or self.c.get("password", ""))
            return im
        except imaplib.IMAP4.error as e:
            raise AuthError(f"IMAP login failed: {e}")
        except OSError as e:
            raise AuthError(f"Couldn't connect to the IMAP server: {e}")

    @staticmethod
    def _folder(im, special: str, override: str, fallbacks) -> str:
        if override:
            return override
        typ, data = im.list()
        names = []
        for line in data or []:
            if not line:
                continue
            line = line.decode("utf-8", "replace") if isinstance(line, bytes) else line
            m = re.match(r'\((?P<flags>[^)]*)\)\s+(?:"[^"]*"|NIL)\s+(?P<name>.+)$', line)
            if not m:
                continue
            name = m.group("name").strip().strip('"')
            names.append(name)
            if special.lower() in m.group("flags").lower():
                return name
        for f in fallbacks:
            if f in names:
                return f
        return fallbacks[0]

    @staticmethod
    def _q(name: str) -> str:
        return '"' + name.replace("\\", "\\\\").replace('"', '\\"') + '"'

    def _append(self, im, folder, flags, raw: bytes):
        typ, resp = im.append(self._q(folder), flags, imaplib.Time2Internaldate(time.time()), raw)
        if typ != "OK":
            raise SendError(f"Couldn't save to folder {folder}: {resp}")

    def _build(self, msg):
        return build_mime(msg, self._from(), self.c.get("from_name", ""))

    def create_draft(self, msg: OutMessage) -> str:
        if not self.supports_drafts:
            raise SendError("This account has no IMAP settings, so drafts can't be saved to the mailbox.")
        m = self._build(msg)
        im = self._imap()
        try:
            folder = self._folder(im, r"\Drafts", self.c.get("drafts_folder", ""), ["Drafts", "INBOX.Drafts"])
            self._append(im, folder, r"(\Draft)", m.as_bytes())
        finally:
            _quiet(im.logout)
        return m["Message-ID"].strip("<>")

    def send_draft(self, draft_id: str):
        im = self._imap()
        try:
            folder = self._folder(im, r"\Drafts", self.c.get("drafts_folder", ""), ["Drafts", "INBOX.Drafts"])
            im.select(self._q(folder))
            typ, data = im.search(None, "HEADER", "Message-ID", draft_id)
            nums = data[0].split() if typ == "OK" and data and data[0] else []
            if not nums:
                raise SendError("Draft not found in the Drafts folder (already sent or deleted?).")
            typ, d = im.fetch(nums[0], "(RFC822)")
            raw = d[0][1]
            self._smtp_send_raw(raw)
            # Sent. Everything below is housekeeping: never fail the message over it.
            try:
                sent = self._folder(im, r"\Sent", self.c.get("sent_folder", ""), ["Sent", "INBOX.Sent", "Sent Items"])
                self._append(im, sent, r"(\Seen)", raw)
                im.select(self._q(folder))
                im.store(nums[0], "+FLAGS", r"\Deleted")
                im.expunge()
            except Exception as e:  # noqa
                log.warning("Sent OK but couldn't tidy IMAP folders: %s", e)
        finally:
            _quiet(im.logout)

    def _smtp_send_raw(self, raw: bytes):
        s = self._smtp()
        try:
            from email import message_from_bytes
            parsed = message_from_bytes(raw)
            s.send_message(parsed)
        except (smtplib.SMTPRecipientsRefused, smtplib.SMTPSenderRefused, smtplib.SMTPDataError) as e:
            raise SendError(f"The mail server rejected this message: {e}")
        # Anything else (dropped connection, timeout) propagates: the worker marks it 'uncertain'
        # rather than risk sending twice.
        finally:
            _quiet(s.quit)

    def send_now(self, msg: OutMessage):
        m = self._build(msg)
        self._smtp_send_raw(m.as_bytes())
        if self.supports_drafts:
            try:
                im = self._imap()
                try:
                    sent = self._folder(im, r"\Sent", self.c.get("sent_folder", ""), ["Sent", "INBOX.Sent", "Sent Items"])
                    self._append(im, sent, r"(\Seen)", m.as_bytes())
                finally:
                    _quiet(im.logout)
            except Exception as e:  # noqa
                log.warning("Sent OK but couldn't copy to Sent folder: %s", e)

    def check(self) -> str:
        s = self._smtp()
        _quiet(s.quit)
        out = "Mail server login works"
        if self.supports_drafts:
            im = self._imap()
            _quiet(im.logout)
            out += ", and IMAP works (drafts and Sent copies will be saved)"
        else:
            out += ". No IMAP settings, so sent mail won't appear in the Sent folder and drafts are unavailable"
        return out + "."


# --------------------------------------------------------------------------- Test mode

class DryRunSender:
    """Writes .eml files instead of sending. Safe for trying things out."""
    supports_drafts = True

    def __init__(self, profile):
        self.p = profile
        base = os.path.join(db.DATA_DIR, "outbox", str(profile["id"]))
        self.drafts, self.sent = os.path.join(base, "drafts"), os.path.join(base, "sent")
        os.makedirs(self.drafts, exist_ok=True)
        os.makedirs(self.sent, exist_ok=True)

    def _write(self, folder, msg) -> str:
        m = build_mime(msg, self.p["from_email"] or "test@example.com")
        name = f"{int(time.time() * 1000)}-{uuid.uuid4().hex[:6]}.eml"
        with open(os.path.join(folder, name), "wb") as f:
            f.write(m.as_bytes())
        return name

    def create_draft(self, msg):
        return self._write(self.drafts, msg)

    def send_draft(self, draft_id):
        src = os.path.join(self.drafts, os.path.basename(draft_id))
        if not os.path.exists(src):
            raise SendError("Test draft file not found.")
        os.replace(src, os.path.join(self.sent, os.path.basename(draft_id)))

    def send_now(self, msg):
        self._write(self.sent, msg)

    def check(self):
        return f"Test mode: messages are saved as .eml files under {os.path.dirname(self.drafts)}"


def _quiet(fn):
    try:
        fn()
    except Exception:
        pass


def make_sender(profile):
    kind = profile["kind"]
    if kind == "graph":
        return GraphSender(profile)
    if kind == "smtp":
        return SmtpSender(profile)
    if kind == "dryrun":
        return DryRunSender(profile)
    raise SendError(f"Unknown account type: {kind}")
