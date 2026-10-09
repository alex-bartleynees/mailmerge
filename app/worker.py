"""Background worker: sends one message at a time at a gentle pace.

Safety rules it follows:
  * Before the irreversible 'send' call a message is marked 'sending'. If the app dies at that
    moment the message becomes 'uncertain' on restart and is NEVER re-sent automatically.
  * A draft id is saved the moment the draft exists, so a restart never creates a second draft.
  * Account problems (signed out, access denied) pause the campaign instead of failing every row.
  * 5 failures in a row pause the campaign.
"""
import logging
import random
import threading

from . import campaigns, db
from .senders import AuthError, SendError, make_sender

log = logging.getLogger("mailmerge.worker")
stop = threading.Event()
MAX_STREAK = 5


def start():
    with db.connect() as c:
        c.execute("UPDATE messages SET status='pending', error=NULL WHERE status='drafting'")
        c.execute("UPDATE messages SET status='uncertain', error=? WHERE status='sending'",
                  ("The app stopped while sending this. Check your Sent Items, then mark it as sent or retry it.",))
    t = threading.Thread(target=_loop, name="mailmerge-worker", daemon=True)
    t.start()
    return t


def _loop():
    streaks = {}
    while not stop.is_set():
        try:
            busy = _tick(streaks)
        except Exception:  # noqa
            log.exception("worker error")
            stop.wait(3)
            busy = True
        if not busy:
            stop.wait(1)


def _set(c, mid, status, error=None, provider_id=None):
    if provider_id is None:
        c.execute("UPDATE messages SET status=?, error=?, updated=? WHERE id=?", (status, error, db.now(), mid))
    else:
        c.execute("UPDATE messages SET status=?, error=?, provider_id=?, updated=? WHERE id=?",
                  (status, error, provider_id, db.now(), mid))


def _pause(c, camp_id, note):
    c.execute("UPDATE campaigns SET status='paused', note=? WHERE id=?", (note, camp_id))


def _wait_between(camp_id, per_minute):
    delay = 60.0 / max(1, min(600, int(per_minute or 20))) * random.uniform(0.7, 1.3)
    waited, n = 0.0, 0
    while waited < delay and not stop.is_set():
        stop.wait(0.25)
        waited += 0.25
        n += 1
        if n % 8 == 0:  # re-check about every 2s so Pause feels instant
            with db.connect() as c:
                r = db.row(c, "SELECT status FROM campaigns WHERE id=?", camp_id)
            if not r or r["status"] != "running":
                return


def _tick(streaks) -> bool:
    with db.connect() as c:
        camp = db.row(c, "SELECT * FROM campaigns WHERE status='running' ORDER BY id LIMIT 1")
        if not camp:
            return False
        wanted = ("pending", "drafted") if camp["mode"] == "send" else ("pending",)
        msg = db.row(c, f"SELECT * FROM messages WHERE campaign_id=? AND status IN ({','.join('?' * len(wanted))}) "
                        f"ORDER BY id LIMIT 1", camp["id"], *wanted)
        if not msg:
            c.execute("UPDATE campaigns SET status='done', note=NULL WHERE id=?", (camp["id"],))
            streaks.pop(camp["id"], None)
            return True
        profile = db.row(c, "SELECT * FROM profiles WHERE id=?", camp["profile_id"])
        if not profile:
            _pause(c, camp["id"], "The sending account was removed. Pick another account and press Resume.")
            return True

        contact = db.row(c, "SELECT * FROM contacts WHERE id=?", msg["contact_id"])
        if not contact:
            _set(c, msg["id"], "skipped", "Contact was removed from the list")
            return True
        reason = campaigns.classify(contact, campaigns.suppressed_set(c))
        if reason:
            _set(c, msg["id"], "skipped", reason)
            return True

        prev = msg["status"]
        try:
            out = campaigns.build_out(c, camp, campaigns.contact_data(contact), msg["email"])
            sender = make_sender(profile)
            if sender.supports_drafts:
                draft_id = msg["provider_id"]
                if not draft_id:
                    _set(c, msg["id"], "drafting")
                    draft_id = sender.create_draft(out)
                    _set(c, msg["id"], "drafted", provider_id=draft_id)
                elif prev == "pending":  # retry of a message whose draft already exists
                    _set(c, msg["id"], "drafted")
                if camp["mode"] == "send":
                    _set(c, msg["id"], "sending")
                    sender.send_draft(draft_id)
                    _set(c, msg["id"], "sent")
            else:
                if camp["mode"] != "send":
                    raise AuthError("This account type can't save drafts. Use Send instead.")
                _set(c, msg["id"], "sending")
                sender.send_now(out)
                _set(c, msg["id"], "sent")
            streaks[camp["id"]] = 0
        except AuthError as e:
            _set(c, msg["id"], "drafted" if prev == "drafted" or msg["provider_id"] else "pending")
            _pause(c, camp["id"], str(e))
            return True
        except Exception as e:  # noqa
            cur = db.row(c, "SELECT status FROM messages WHERE id=?", msg["id"])["status"]
            if cur == "sending" and not isinstance(e, SendError):
                # Network trouble mid-send: we can't know whether it went out.
                _set(c, msg["id"], "uncertain", f"Unsure if this was sent ({e}). Check Sent Items.")
            else:
                _set(c, msg["id"], "failed", str(e)[:500])
            streaks[camp["id"]] = streaks.get(camp["id"], 0) + 1
            if streaks[camp["id"]] >= MAX_STREAK:
                _pause(c, camp["id"], f"Paused after {MAX_STREAK} failures in a row. Last error: {str(e)[:200]}")
                streaks[camp["id"]] = 0
                return True
    _wait_between(camp["id"], profile["per_minute"])
    return True
