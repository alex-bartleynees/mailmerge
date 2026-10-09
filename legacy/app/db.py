import json
import os
import sqlite3
import threading
from contextlib import contextmanager
from datetime import datetime, timezone

DATA_DIR = os.environ.get("DATA_DIR", os.path.join(os.getcwd(), "data"))
DB_PATH = os.path.join(DATA_DIR, "mailmerge.db")

SCHEMA = """
CREATE TABLE IF NOT EXISTS profiles(
  id INTEGER PRIMARY KEY, name TEXT NOT NULL, kind TEXT NOT NULL,
  from_email TEXT NOT NULL DEFAULT '', config TEXT NOT NULL DEFAULT '{}',
  per_minute INTEGER NOT NULL DEFAULT 20);
CREATE TABLE IF NOT EXISTS lists(
  id INTEGER PRIMARY KEY, name TEXT NOT NULL,
  columns TEXT NOT NULL DEFAULT '["Email"]', created TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS contacts(
  id INTEGER PRIMARY KEY, list_id INTEGER NOT NULL REFERENCES lists(id) ON DELETE CASCADE,
  email TEXT NOT NULL, data TEXT NOT NULL DEFAULT '{}', skip INTEGER NOT NULL DEFAULT 0,
  UNIQUE(list_id, email));
CREATE TABLE IF NOT EXISTS campaigns(
  id INTEGER PRIMARY KEY, name TEXT NOT NULL, profile_id INTEGER, list_id INTEGER,
  subject TEXT NOT NULL DEFAULT '', body TEXT NOT NULL DEFAULT '',
  is_html INTEGER NOT NULL DEFAULT 0, mode TEXT NOT NULL DEFAULT 'draft',
  status TEXT NOT NULL DEFAULT 'idle', note TEXT, created TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS attachments(
  id INTEGER PRIMARY KEY, campaign_id INTEGER NOT NULL REFERENCES campaigns(id) ON DELETE CASCADE,
  filename TEXT NOT NULL, content_type TEXT NOT NULL, path TEXT NOT NULL, size INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS messages(
  id INTEGER PRIMARY KEY, campaign_id INTEGER NOT NULL REFERENCES campaigns(id) ON DELETE CASCADE,
  contact_id INTEGER, email TEXT NOT NULL, status TEXT NOT NULL, error TEXT,
  provider_id TEXT, updated TEXT NOT NULL, UNIQUE(campaign_id, email));
CREATE TABLE IF NOT EXISTS suppressions(
  email TEXT PRIMARY KEY, added TEXT NOT NULL, reason TEXT);
"""

_lock = threading.Lock()


def now():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def init():
    os.makedirs(DATA_DIR, exist_ok=True)
    for sub in ("attachments", "uploads", "tokens", "outbox"):
        os.makedirs(os.path.join(DATA_DIR, sub), exist_ok=True)
    with connect() as c:
        c.execute("PRAGMA journal_mode=WAL")
        c.executescript(SCHEMA)


@contextmanager
def connect():
    conn = sqlite3.connect(DB_PATH, timeout=10, isolation_level=None)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys=ON")
    conn.execute("PRAGMA busy_timeout=5000")
    try:
        yield conn
    finally:
        conn.close()


def rows(conn, sql, *args):
    return [dict(r) for r in conn.execute(sql, args).fetchall()]


def row(conn, sql, *args):
    r = conn.execute(sql, args).fetchone()
    return dict(r) if r else None


def jload(s, default=None):
    try:
        return json.loads(s)
    except Exception:
        return {} if default is None else default
