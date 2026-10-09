# One-off setup (for the person installing it)

The shop never sees any of this. Staff get a web address, sign in, and click **Sign in with Microsoft** once.

Placeholders: `mailmerge.alexbartleynees.com` (the app), `identity.alexbartleynees.com` (Keycloak).

## 1. Keycloak

MailMerge uses your existing realms: **production** for the deployed app, **development** for local runs
(`docker compose`). Do the steps below in each realm you need.

These realms are shared with your other apps, and other apps add users to them (coffee-journal's signup creates
production accounts). MailMerge only accepts an access token that has all three of:

- `aud` containing `mailmerge-api`, added by a mapper that only the `mailmerge` client's dedicated scope has;
- the `mailmerge-user` role on the `mailmerge` client;
- a `/mailmerge/<shop>` group.

The audience is deliberately not the client ID `mailmerge`. Keycloak's built-in "audience resolve" mapper adds a
client's ID to the audience of any token whose user holds that client's roles, so a coffee-journal token for a
staff member would carry `aud: mailmerge`. Nothing adds `mailmerge-api` except the mapper below.

| Setting | Development | Production |
|---|---|---|
| Root URL | `http://localhost:5224` | `https://mailmerge.alexbartleynees.com` |
| Valid redirect URIs | `http://localhost:5224/signin-oidc` | `https://mailmerge.alexbartleynees.com/signin-oidc` |
| Valid post logout redirect URIs | `http://localhost:5224/*` | `https://mailmerge.alexbartleynees.com/*` |

1. **Clients > Create client**, OpenID Connect, Client ID `mailmerge`:
   - **Client authentication** on (confidential), **Authorization** off.
   - Authentication flow: **Standard flow** only. Untick *Direct access grants* (on by default); leave Implicit,
     Service accounts and Device grant off. PKCE method `S256` if offered.
   - Login settings from the table. Leave **Web origins** empty: the BFF talks to Keycloak server-side.
   - Copy the secret from **Credentials** (`MAILMERGE_CLIENT_SECRET` in `.env` locally, or the `keycloak-auth`
     secret in the cluster).
2. **Clients > mailmerge > Roles > Create role** `mailmerge-user`.
3. **Clients > mailmerge > Client scopes > mailmerge-dedicated > Add mapper > By configuration.** Use the client's
   dedicated scope, never a shared or realm-default client scope:
   - **Audience**: name `mailmerge-api audience`, *Included Client Audience* empty, *Included Custom Audience*
     `mailmerge-api`, access token on, ID token off.
   - **Group Membership**: name `groups`, token claim `groups`, *Full group path* **on**, access token on, ID token
     off. Groups from other apps appear in the claim too; MailMerge ignores anything outside `/mailmerge/`.
4. **Groups:** create `mailmerge`, then a child per business, e.g. `mailmerge/max-wilson`. The group's last segment
   becomes the shop's initial name ("Max Wilson"; rename it in the app). On the parent `mailmerge` group, open
   **Role mapping > Assign role**, filter by client roles and assign `mailmerge` `mailmerge-user`. Subgroups
   inherit it, so adding someone to their shop's group is all it takes. Never add `mailmerge-user` to the realm's
   default roles.
   - Name the child group just `max-wilson` (Keycloak builds the path from the parent), so the token shows
     `/mailmerge/max-wilson`. Naming it `mailmerge/max-wilson` gives `/mailmerge/mailmerge/max-wilson`.
   - A shop's data is tied to its **full group path**. Don't rename or move a shop's group once it's in use: the
     new path becomes a new, empty workspace. Change the shop's display name in the app instead.
5. **Users:** create each staff member (or reuse an existing account) and **Join group** for their shop. Add
   yourself too if you want to help them inside the app.
6. **Verify** with **Clients > mailmerge > Client scopes > Evaluate**: pick a staff user and open *Generated access
   token*. It must contain `"aud": [..."mailmerge-api"...]`, `"resource_access": {"mailmerge": {"roles":
   ["mailmerge-user"]}}` and `"groups": ["/mailmerge/max-wilson"]`. Then Evaluate the same user on another client
   (e.g. `coffee-journal`): that token must **not** contain `mailmerge-api` or a `groups` claim.
7. **Realm settings** (these affect every app in the realm, so check rather than change): **Security defences >
   Brute force detection** should be on, so repeated wrong passwords lock the account for a while.

Someone in two shop groups can use both; the app uses the first alphabetically.

Tokens issued to `mailmerge` still carry `aud: account` (from Keycloak's default `roles` scope), which APIs that
validate `aud: account` would accept. They stay server-side in the BFF, so the browser never sees them; the
robust fix is for each of your APIs to validate its own audience, as MailMerge does.

## 2. Microsoft (Entra) app (once, reusable for every shop)

Do this in **your own** Microsoft account at <https://entra.microsoft.com> (a free account is fine).

1. **App registrations > New registration**
   - Name: `MailMerge`
   - Supported account types: **Accounts in any organizational directory (Multitenant)**
   - Redirect URI: platform **Web**, `https://mailmerge.alexbartleynees.com/api/accounts/microsoft/callback`
     (add `http://localhost:5224/api/accounts/microsoft/callback` for local testing)
2. **Certificates & secrets > New client secret**. Copy the value now (it's shown once). Note the expiry date:
   staff will need to sign in again after you rotate it.
3. **API permissions > Add a permission > Microsoft Graph > Delegated permissions**: `Mail.ReadWrite`, `Mail.Send`,
   `User.Read`, `offline_access`. (No need to grant admin consent here; the shop owner does that for their business.)
4. Copy the **Application (client) ID**.

The app only ever acts as the person who signs in, on that mailbox. It can't see anyone else's.

### First sign-in at the shop (the owner goes first)

Microsoft doesn't let ordinary staff approve apps that read or send mail, so the **owner** (whoever is the
"Global admin" of the shop's Microsoft 365) signs in first:

1. In MailMerge: **Accounts > Add account > Outlook / Microsoft 365 > Save > Sign in with Microsoft**.
2. Sign in as the owner. On the permissions screen **tick "Consent on behalf of your organization"**, then **Accept**.

After that anyone can connect a mailbox with no approval screen. A short video call with the owner works well.

- **If the owner isn't an admin** (e.g. a web designer set up Microsoft 365): the admin does step 2, or makes the
  owner an admin at <https://admin.microsoft.com> > Users.
- **If Microsoft still refuses** (some tenants block all outside apps): register the app inside the shop's own
  tenant (single tenant), grant admin consent there, and set `microsoft.tenant` to that tenant ID.

> Not yet tested against a real tenant: screen wording may differ slightly; the consent flow itself is standard.

## 3. Secrets (namespace `mailmerge`)

Add these to `portfolio-cluster/components/secrets/secrets.encrypted.yaml` (SOPS, applied by Argo CD via KSOPS):

| Secret | Keys | Used by |
|---|---|---|
| `mailmerge-db-secret` | `CONNECTION_STRING`: `Host=<postgres-host>;Database=mailmerge;Username=mailmerge;Password=<pw>` | API |
| `mailmerge-microsoft` | `CLIENT_ID`, `CLIENT_SECRET` from the Entra app | API (optional: without it only SMTP and test accounts work) |
| `keycloak-auth` | `MAILMERGE_CLIENT_SECRET` from the Keycloak `mailmerge` client | BFF |

### Database (once)

The app doesn't create its database or login. Create them on the cluster's Postgres before the first deploy, using
the same password as in `mailmerge-db-secret`:

```sh
kubectl exec -it deployment/postgres -n database -- psql -U postgres
```

```sql
CREATE USER mailmerge WITH PASSWORD '<pw>';
CREATE DATABASE mailmerge OWNER mailmerge;
```

After that the app manages the schema itself. On every start it applies any pending EF Core migrations before
serving requests, so new versions upgrade the database automatically. If a migration fails, the pod fails to start
(see its logs) and the database stays at the previous version. That's safe because the API runs as a single
replica with the `Recreate` strategy, so only one instance migrates at a time.

## 4. Deploy

1. Build and push the image (SharedKernel restores from GitHub Packages: the token needs `read:packages`):
   ```sh
   docker build --secret id=github_packages_token,env=GITHUB_PACKAGES_TOKEN -t creativefree/mailmerge-api:<tag> .
   docker push creativefree/mailmerge-api:<tag>
   ```
   Set `image.tag` in `deployment/helm-charts/mailmerge-api/values.yaml`.
2. **BFF:** MailMerge's gateway config lives in the authgateway-bff repo as
   `deployment/helm-charts/authgateway-bff/values-mailmerge.yaml` (routes, client ID, ingress host). It uses the
   chart's default production realm.
3. **Argo CD:** `mailmerge-api.yaml` and `mailmerge-bff.yaml` live in `portfolio-cluster/components/applications/`.
   They sync once this repo is on GitHub.
4. **DNS:** point `mailmerge.alexbartleynees.com` at the cluster ingress. cert-manager issues the certificate.

The API runs as a single replica with the `Recreate` strategy: its delivery worker assumes it's the only one.
Back up the Postgres database: it holds the contact lists, encrypted sign-ins and the Data Protection keys (losing
those keys means everyone signs in to Microsoft again).

## 5. Check it end to end

1. Sign in, add a **Test mode** account, upload a small spreadsheet, send yourself a test.
2. Connect the shop mailbox (owner first, as above). **Test connection** should say "Connected as …".
3. Create drafts for 2–3 contacts (e.g. your own addresses) and check them in Outlook's Drafts folder.
4. Send those, and confirm they appear in **Sent Items** and arrive.
5. Click the unsubscribe link in one, confirm, and check it appears under **Opt-outs & privacy**.

## Checks for maxwilsondiamondjeweller.co.nz (from public DNS)

- Email is on **Microsoft 365** (MX points to Microsoft): use the Outlook / Microsoft 365 account type.
- **SPF** includes Microsoft and **DKIM** (`selector1`) is published, so mail sent through their mailbox passes both.
- **No DMARC record.** Worth adding before sending to many people (Gmail and Yahoo expect it). Their DNS is on
  Cloudflare: TXT record `_dmarc`, value `v=DMARC1; p=none; rua=mailto:sales@maxwilsondiamondjeweller.co.nz`.

## Other mailboxes

For a business whose email isn't Microsoft 365, use **Any other email** with their SMTP server and mailbox
password (or app password), and fill in IMAP so drafts and Sent copies appear in their mail app.
