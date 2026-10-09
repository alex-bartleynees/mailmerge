from . import db
from .merge import render, valid_email
from .senders import OutMessage, SendError


def suppressed_set(conn) -> set:
    return {r["email"] for r in conn.execute("SELECT email FROM suppressions")}


def classify(contact: dict, suppressed: set):
    """Returns None if the contact can be emailed, else the reason it's skipped."""
    if not valid_email(contact["email"]):
        return "Invalid email address"
    if contact["skip"]:
        return "Marked do-not-email"
    if contact["email"] in suppressed:
        return "On the opt-out list"
    return None


def materialize(conn, camp: dict) -> dict:
    """Create/refresh one message row per contact. Existing sent/failed rows are never touched,
    so re-running a campaign after adding contacts only sends to the new people."""
    contacts = db.rows(conn, "SELECT * FROM contacts WHERE list_id=? ORDER BY id", camp["list_id"])
    sup = suppressed_set(conn)
    conn.execute("BEGIN")
    try:
        for ct in contacts:
            reason = classify(ct, sup)
            conn.execute(
                """INSERT INTO messages(campaign_id, contact_id, email, status, error, updated)
                   VALUES(?,?,?,?,?,?)
                   ON CONFLICT(campaign_id, email) DO UPDATE SET
                     contact_id=excluded.contact_id,
                     status=CASE WHEN messages.status='skipped' THEN excluded.status ELSE messages.status END,
                     error=CASE WHEN messages.status='skipped' THEN excluded.error ELSE messages.error END,
                     updated=excluded.updated""",
                (camp["id"], ct["id"], ct["email"], "skipped" if reason else "pending", reason, db.now()))
        conn.execute("COMMIT")
    except Exception:
        conn.execute("ROLLBACK")
        raise
    return counts(conn, camp["id"])


def counts(conn, campaign_id: int) -> dict:
    out = {r["status"]: r["n"] for r in conn.execute(
        "SELECT status, COUNT(*) n FROM messages WHERE campaign_id=? GROUP BY status", (campaign_id,))}
    out["total"] = sum(out.values())
    return out


def plan(conn, camp: dict) -> dict:
    """What would happen if the user pressed the button now? (No changes made.)"""
    contacts = db.rows(conn, "SELECT * FROM contacts WHERE list_id=?", camp["list_id"])
    sup = suppressed_set(conn)
    existing = {r["email"]: r["status"] for r in conn.execute(
        "SELECT email, status FROM messages WHERE campaign_id=?", (camp["id"],))}
    p = {"contacts": len(contacts), "skipped": 0, "to_draft": 0, "to_send": 0,
         "already_sent": 0, "failed": 0, "uncertain": 0}
    for ct in contacts:
        if classify(ct, sup):
            p["skipped"] += 1
            continue
        st = existing.get(ct["email"])
        if st in (None, "pending", "skipping", "skipped"):
            p["to_draft"] += 1
            p["to_send"] += 1
        elif st in ("drafted", "drafting"):
            p["to_send"] += 1
        elif st == "sent":
            p["already_sent"] += 1
        elif st == "failed":
            p["failed"] += 1
        elif st in ("uncertain", "sending"):
            p["uncertain"] += 1
    return p


def contact_data(contact: dict) -> dict:
    data = db.jload(contact.get("data") or "{}")
    data.setdefault("Email", contact["email"])
    return data


def build_out(conn, camp: dict, data: dict, to: str, subject_prefix: str = "") -> OutMessage:
    html = bool(camp["is_html"])
    subject = (subject_prefix + render(camp["subject"], data, False)).strip()
    if not subject:
        raise SendError("Subject is empty.")
    body = render(camp["body"], data, html)
    atts = db.rows(conn, "SELECT * FROM attachments WHERE campaign_id=? ORDER BY id", camp["id"])
    return OutMessage(to=to, subject=subject, body=body, html=html, attachments=atts)
