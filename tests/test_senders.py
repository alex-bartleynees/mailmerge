"""Sender tests against local fake servers (no real Microsoft/SMTP account needed)."""
import json
import os
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer

os.environ.setdefault("DATA_DIR", tempfile.mkdtemp(prefix="mm-sender-"))

import pytest  # noqa: E402

from app import senders  # noqa: E402
from app.senders import GraphSender, OutMessage, SendError, SmtpSender  # noqa: E402


class FakeGraph(BaseHTTPRequestHandler):
    log = []
    throttled = False

    def log_message(self, *a):
        pass

    def _body(self):
        n = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(n) if n else b""

    def _send(self, code, obj=None, headers=None):
        data = json.dumps(obj).encode() if obj is not None else b""
        self.send_response(code)
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self):
        body = self._body()
        path = self.path
        FakeGraph.log.append(("POST", path, body, self.headers.get("Authorization")))
        if path == "/v1.0/me/messages":
            if not FakeGraph.throttled:
                FakeGraph.throttled = True
                return self._send(429, {"error": {"message": "slow down"}}, {"Retry-After": "0"})
            return self._send(201, {"id": "DRAFT1"})
        if path.endswith("/attachments"):
            return self._send(201, {"id": "A1"})
        if path.endswith("/createUploadSession"):
            return self._send(201, {"uploadUrl": f"http://127.0.0.1:{self.server.server_port}/upload/1"})
        if path.endswith("/send"):
            return self._send(202)
        self._send(404, {"error": {"message": "no route"}})

    def do_PUT(self):
        body = self._body()
        FakeGraph.log.append(("PUT", self.path, len(body), self.headers.get("Content-Range"), self.headers.get("Authorization")))
        self._send(200)

    def do_DELETE(self):
        FakeGraph.log.append(("DELETE", self.path))
        self._send(204)


@pytest.fixture()
def graph(monkeypatch):
    FakeGraph.log, FakeGraph.throttled = [], False
    srv = HTTPServer(("127.0.0.1", 0), FakeGraph)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    monkeypatch.setattr(senders, "GRAPH", f"http://127.0.0.1:{srv.server_port}/v1.0")
    monkeypatch.setattr(GraphSender, "_token", lambda self: "tok")
    yield GraphSender({"id": 1, "kind": "graph", "config": "{}", "from_email": "w@x.test"})
    srv.shutdown()


def test_graph_draft_attachments_and_send(graph, tmp_path):
    small = tmp_path / "small.txt"
    small.write_bytes(b"x" * 1000)
    big = tmp_path / "big.bin"
    big.write_bytes(os.urandom(3_500_000))      # > 3 MB -> upload session, 3 chunks
    msg = OutMessage("a@b.test", "Hello", "<p>Hi</p>", True, [
        {"filename": "small.txt", "content_type": "text/plain", "path": str(small), "size": 1000},
        {"filename": "big.bin", "content_type": "application/octet-stream", "path": str(big), "size": 3_500_000}])
    did = graph.create_draft(msg)
    assert did == "DRAFT1"
    graph.send_draft(did)
    posts = [e for e in FakeGraph.log if e[0] == "POST"]
    first = json.loads(next(e for e in posts if e[1] == "/v1.0/me/messages" and e[2])[2])
    assert first["subject"] == "Hello" and first["body"] == {"contentType": "HTML", "content": "<p>Hi</p>"}
    assert first["toRecipients"] == [{"emailAddress": {"address": "a@b.test"}}]
    assert sum(1 for e in posts if e[1] == "/v1.0/me/messages") == 2          # 429 was retried
    att = json.loads(next(e for e in posts if e[1].endswith("/attachments"))[2])
    assert att["@odata.type"] == "#microsoft.graph.fileAttachment" and att["name"] == "small.txt"
    puts = [e for e in FakeGraph.log if e[0] == "PUT"]
    assert [p[3] for p in puts] == ["bytes 0-1310719/3500000", "bytes 1310720-2621439/3500000", "bytes 2621440-3499999/3500000"]
    assert all(p[4] is None for p in puts)                                   # no bearer token leaked to upload URL
    assert posts[-1][1] == "/v1.0/me/messages/DRAFT1/send"
    assert posts[-1][3] == "Bearer tok"


def test_graph_plain_text_type(graph):
    graph.create_draft(OutMessage("a@b.test", "S", "plain body", False))
    sent = json.loads(next(e for e in FakeGraph.log if e[0] == "POST" and e[2] and e[1] == "/v1.0/me/messages")[2])
    assert sent["body"]["contentType"] == "Text"


def test_graph_denied_is_auth_error(monkeypatch):
    class Deny(FakeGraph):
        def do_POST(self):
            self._body()
            self._send(403, {"error": {"message": "Access is denied"}})
    srv = HTTPServer(("127.0.0.1", 0), Deny)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    monkeypatch.setattr(senders, "GRAPH", f"http://127.0.0.1:{srv.server_port}/v1.0")
    monkeypatch.setattr(GraphSender, "_token", lambda self: "tok")
    with pytest.raises(senders.AuthError):
        GraphSender({"id": 1, "kind": "graph", "config": "{}"}).create_draft(OutMessage("a@b.test", "S", "b"))
    srv.shutdown()


def test_smtp_send_with_attachment(tmp_path):
    from aiosmtpd.controller import Controller
    got = []

    class H:
        async def handle_DATA(self, server, session, envelope):
            got.append(envelope)
            return "250 OK"
    import socket
    with socket.socket() as sk:
        sk.bind(("127.0.0.1", 0))
        port = sk.getsockname()[1]
    ctl = Controller(H(), hostname="127.0.0.1", port=port)
    ctl.start()
    try:
        f = tmp_path / "doc.pdf"
        f.write_bytes(b"%PDF-fake")
        prof = {"id": 2, "kind": "smtp", "from_email": "me@x.test", "config": json.dumps(
            {"host": "127.0.0.1", "port": port, "security": "none", "from_name": "Me"})}
        s = SmtpSender(prof)
        assert s.supports_drafts is False
        s.send_now(OutMessage("to@y.test", "Subj", "<b>hi</b>", True,
                              [{"filename": "doc.pdf", "content_type": "application/pdf", "path": str(f), "size": 9}]))
        assert got and got[0].rcpt_tos == ["to@y.test"] and got[0].mail_from == "me@x.test"
        raw = got[0].content.decode()
        assert "Subject: Subj" in raw and 'filename="doc.pdf"' in raw and "text/html" in raw
        with pytest.raises(SendError):
            s.create_draft(OutMessage("to@y.test", "S", "b"))
    finally:
        ctl.stop()
