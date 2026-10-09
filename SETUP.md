# One-off setup (for the person installing it)

The shop never sees any of this. Staff get a web address, sign in, and click **Sign in with Microsoft** once.

Placeholders: `mailmerge.alexbartleynees.com` (the app), `identity.alexbartleynees.com` (Keycloak).

## 1. Keycloak

MailMerge uses your existing realms: **production** for the deployed app, **development** for local runs
(`docker compose`). Do the steps below in each realm you need.

These realms are shared with your other apps, and other apps add users to them (coffee-journal's signup creates
production accounts). So signing in isn't enough to get into MailMerge: a user also needs the `mailmerge-user` client role and
membership of a shop group. Don't add `mailmerge-user` to the realm's default roles.

1. **Clients > Create client** `mailmerge` (OpenID Connect):
   - Client authentication **on** (confidential), Standard flow **on**, everything else off.
   - Valid redirect URIs: production `https://mailmerge.alexbartleynees.com/signin-oidc`; development
     `http://localhost:5224/*`.
   - Valid post-logout redirect URIs: production `https://mailmerge.alexbartleynees.com/*`; development
     `http://localhost:5224/*`.
   - Copy the client secret from **Credentials**.
2. **Client roles:** on the `mailmerge` client, add role `mailmerge-user`.
3. **Client scopes > mailmerge-dedicated > Add mapper:**
   - **Audience**: name `mailmerge-api`, *Included Custom Audience* `mailmerge-api`, add to access token. The API
     only accepts tokens with this audience, so tokens issued to your other apps' clients are rejected.
   - **Group Membership**: name `groups`, token claim `groups`, *Full group path* **on**, add to access token.
     Groups from other apps appear in the claim too; MailMerge ignores anything outside `/mailmerge/`.
4. **Groups:** create `mailmerge`, then a child per business, e.g. `mailmerge/max-wilson`. The group's last segment
   becomes the shop's initial name ("Max Wilson"; rename it in the app). On the parent `mailmerge` group, assign
   the client role `mailmerge-user` under **Role mapping**; subgroups inherit it, so adding someone to their shop's
   group is all it takes.
5. **Users:** create each staff member (or reuse an existing account) and add them to their shop's group. Add
   yourself too if you want to help them inside the app.
6. **Realm settings** (these affect every app in the realm, so check rather than change): **Security defences >
   Brute force detection** should be on, so repeated wrong passwords lock the account for a while.

Someone in two shop groups can use both; the app uses the first alphabetically.

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

```sh
kubectl create namespace mailmerge
kubectl -n mailmerge create secret generic mailmerge-db-secret \
  --from-literal=CONNECTION_STRING='Host=<postgres-host>;Database=mailmerge;Username=mailmerge;Password=<pw>'
kubectl -n mailmerge create secret generic mailmerge-microsoft \
  --from-literal=CLIENT_ID=<entra-client-id> --from-literal=CLIENT_SECRET=<entra-client-secret>
kubectl -n mailmerge create secret generic keycloak-auth \
  --from-literal=MAILMERGE_CLIENT_SECRET=<keycloak-client-secret>
```

Create the `mailmerge` database and user on the cluster's Postgres first. The app applies its own migrations on
startup.

## 4. Deploy

1. Build and push the image (SharedKernel restores from GitHub Packages: the token needs `read:packages`):
   ```sh
   docker build --secret id=github_packages_token,env=GITHUB_PACKAGES_TOKEN -t creativefree/mailmerge-api:<tag> .
   docker push creativefree/mailmerge-api:<tag>
   ```
   Set `image.tag` in `deployment/helm-charts/mailmerge-api/values.yaml`.
2. **BFF:** copy `deployment/bff/values-mailmerge.yaml` into the authgateway-bff repo's chart folder. No chart
   changes are needed: it already uses the production realm.
3. **Argo CD:** copy `deployment/argocd/*.yaml` into `portfolio-cluster/components/applications/` and list them in
   its `kustomization.yaml`.
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
