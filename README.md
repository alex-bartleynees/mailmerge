# MailMerge

A small self-hosted web app for sending personal bulk email **through an existing mailbox** (Outlook / Microsoft 365, or any SMTP account). Upload your clients from Excel, write one email with `{{merge fields}}`, then either:

- **Create drafts**: a real draft per person appears in the mailbox's **Drafts** folder to review and tweak in Outlook, or
- **Send**: each person gets their own individual email, and it lands in **Sent Items**.

Replies go to the mailbox as normal. No extra mail server, no BCC blasts.

## Run it

```bash
docker compose up -d --build
# open http://localhost:8080   (set APP_PASSWORD in docker-compose.yml first)
```

Data (database, attachments, sign-in tokens) lives in `./data`. Back that folder up.
Put it behind your reverse proxy or Tailscale; it isn't meant to face the open internet.

No Docker? `pip install -r requirements.txt && DATA_DIR=./data uvicorn app.main:app --port 8080 --workers 1`
(keep it at **one** worker: the send queue runs inside the process).

## Connecting Outlook / Microsoft 365

See **[SETUP.md](SETUP.md)**. It's a one-off job for whoever installs the app (register one Microsoft app, then the shop owner signs in first and approves it for the business). The shop's staff only ever click *Sign in*; give them **[HOW-TO.md](HOW-TO.md)**.

## Any other email provider

**Accounts > Add account > Any other email**: SMTP host/port/user/password (use an *app password* for Gmail and similar). Fill in the optional **IMAP** section too and the app will save drafts and a copy in Sent, just like Outlook. Without IMAP, sending works but nothing shows in the Sent folder and "Create drafts" is unavailable.

## Day-to-day use

1. **Contacts**: upload an `.xlsx` (or `.csv`). The first row is the headings. Pick the email column, check the preview, import. After that you can edit any cell, add people, add columns, tick *Don't email*, and download the list back as Excel. Uploading again into the same list adds new people (and can optionally update existing ones).
2. **Emails**: pick the account and list, write the subject and message. Click a column name to insert it (`{{FirstName}}`; `{{FirstName|there}}` falls back to "there" when blank). Plain text or lightly formatted (bold, italic, lists, links). Attach files if needed (they go to everyone).
3. **Preview** with real people's details, **Send me a test**, then **Create drafts** or **Send**. Add more contacts later and run it again: only the new people are processed.
4. **Opt-outs**: anyone listed is never emailed, in any campaign.

## Safety behaviour

- One email per person, sent slowly (default 20/minute with random jitter; change per account).
- Resumable: stop or restart whenever; it picks up where it left off.
- It never double-sends. If the app dies at the exact moment of sending, that message is marked **Check** (not retried); look in Sent Items, then mark it sent or retry.
- Sign-in problems pause the campaign (nothing is marked failed). Five failures in a row also pause it.
- Invalid addresses are flagged red in the grid and skipped.
- Contact values are HTML-escaped in formatted emails.
- Test mode account type writes `.eml` files instead of sending, which is handy for practising.

## Limits and good manners

- Microsoft 365 limits sending per mailbox (roughly 30 messages/minute and 10,000 recipients/day; check Microsoft's current numbers). Hundreds per batch is fine, tens of thousands isn't. This isn't a marketing-blast tool.
- Mass email from a work mailbox affects that domain's reputation if people mark it as spam. Only email people who expect it and include an easy way to opt out (then add them to Opt-outs). NZ/AU spam laws require consent and an unsubscribe option for marketing email.
- Outlook's saved signature is not added automatically; type or paste it at the bottom of the message.
- Graph creates drafts/messages as plain text or HTML only (no Outlook stationery).

## Security notes

- `APP_PASSWORD` gives a browser password prompt. Use HTTPS (via your proxy) so it isn't sent in the clear.
- Microsoft tokens are stored in `data/tokens/` and SMTP/IMAP passwords in the SQLite database, unencrypted. Protect the `data` volume.

## Development

```bash
pip install -r requirements.txt pytest aiosmtpd
python -m pytest tests -q
```

Tests cover the whole flow (Excel import and review, editing, drafts then send, re-runs, opt-outs, crash safety, HTML escaping) in Test mode, plus the Graph sender against a fake local Graph server (retry on 429, large-attachment upload, auth failures) and SMTP against a local fake SMTP server. **Not covered by automated tests:** a real Microsoft tenant and real IMAP servers; try a Test send to yourself first.

Layout: `app/main.py` (API), `app/senders.py` (Graph / SMTP / test mode), `app/worker.py` (send queue), `app/static/index.html` (UI).
