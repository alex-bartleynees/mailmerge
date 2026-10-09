import html
import re

TAG = re.compile(r"\{\{\s*([^}|]+?)\s*(?:\|([^}]*))?\}\}")
EMAIL = re.compile(r"^[^@\s<>\",;]+@[^@\s<>\",;]+\.[^@\s<>\",;]{2,}$")


def valid_email(s: str) -> bool:
    return bool(s) and len(s) <= 254 and bool(EMAIL.match(s.strip()))


def _lookup(data: dict, key: str) -> str:
    if key in data:
        return str(data[key] or "")
    k = key.strip().lower()
    for name, val in data.items():
        if name.strip().lower() == k:
            return str(val or "")
    return ""


def render(template: str, data: dict, as_html: bool) -> str:
    """Replace {{Column}} or {{Column|fallback}} with the contact's value."""

    def sub(m):
        val = _lookup(data, m.group(1))
        if not val.strip():
            val = (m.group(2) or "").strip()
        return html.escape(val) if as_html else val

    return TAG.sub(sub, template or "")


def used_fields(template: str) -> list:
    return [m.group(1).strip() for m in TAG.finditer(template or "")]


def html_to_text(s: str) -> str:
    s = re.sub(r"(?i)<br\s*/?>|</p>|</div>|</li>|</h[1-6]>", "\n", s)
    s = re.sub(r"(?s)<[^>]+>", "", s)
    s = html.unescape(s)
    return re.sub(r"\n{3,}", "\n\n", s).strip()
