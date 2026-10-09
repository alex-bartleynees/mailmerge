# One-off setup (for the person installing it)

The shop never sees any of this. You do it once; they just click **Sign in**.

## 1. Run the app

```bash
cp docker-compose.yml docker-compose.override.yml   # optional
# edit docker-compose.yml: set APP_PASSWORD, and GRAPH_CLIENT_ID after step 2
docker compose up -d --build
```

Open `http://<server>:8080`. Put it behind your reverse proxy with HTTPS (the password prompt is sent in the clear otherwise), or reach it over Tailscale. Back up `./data`.

## 2. Register one Microsoft app (about 10 minutes, once, reusable for any future shop)

Do this in **your own** Microsoft account at <https://entra.microsoft.com> (a free Azure/Entra account is fine).

1. **App registrations > New registration**
   - Name: `MailMerge`
   - Supported account types: **Accounts in any organizational directory (Multitenant)**
   - Redirect URI: leave empty
2. **Authentication > Advanced settings > Allow public client flows = Yes**, then Save.
3. **API permissions > Add a permission > Microsoft Graph > Delegated permissions**: `Mail.ReadWrite`, `Mail.Send`, `User.Read`. (No need to click "Grant admin consent" here; the shop's owner does that for their own business in step 3.)
4. Copy the **Application (client) ID** into `GRAPH_CLIENT_ID` in `docker-compose.yml`, then `docker compose up -d`.

The app only ever acts as the person who signs in, on that person's own mailbox. It can't see anyone else's.

## 3. First sign-in at the shop (the owner goes first)

Microsoft doesn't let ordinary staff approve apps that read or send mail, so the **owner** (whoever manages the shop's Microsoft 365 account, a "Global admin") signs in first:

1. Open the app > **Accounts > Add account > Outlook / Microsoft 365 > Save > Sign in**.
2. Click the sign-in link, type the code, sign in as the owner.
3. On the Microsoft screen, **tick "Consent on behalf of your organization"**, then Accept.

After that, each staff member just adds their own account and signs in, with no approval screen. You can do this on a short video call with the owner.

**If the owner can't be an admin** (for example a web designer set up the Microsoft account): ask whoever is the admin to do step 3, or ask them to open <https://admin.microsoft.com> > Users > Active users and make the owner an admin.

**If Microsoft still refuses** (some tenants block all outside apps): register the app inside the shop's own tenant instead. The owner signs in at <https://entra.microsoft.com> and does step 2 above with *Single tenant* selected, then clicks **Grant admin consent for <shop>** on the API permissions page. Set `GRAPH_TENANT` to the tenant ID shown on the app's Overview page.

> I couldn't test this against a real Microsoft tenant, so screen wording may differ slightly. The consent flow itself is standard.

## Checks for maxwilsondiamondjeweller.co.nz (looked up from public DNS)

- Email is on **Microsoft 365** (MX points to Microsoft). Use the Outlook / Microsoft 365 account type.
- **SPF** includes Microsoft, and **DKIM** (`selector1`) is published. Mail sent through their own mailbox will pass both.
- **No DMARC record** (`_dmarc.maxwilsondiamondjeweller.co.nz`). Worth adding before sending to many people, because Gmail and Yahoo like it and it protects the domain from forgery. Their DNS is on Cloudflare; add a TXT record named `_dmarc` with value `v=DMARC1; p=none; rua=mailto:sales@maxwilsondiamondjeweller.co.nz`. Start with `p=none` (monitor only).

## Other mailboxes

If another customer's email is hosted by a normal web host (not Microsoft), use **Any other email** with their mail server settings and mailbox password. Fill in IMAP as well to get drafts and Sent copies.
