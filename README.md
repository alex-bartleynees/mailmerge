# MailMerge

Send one personalised email per person to a client list from Excel, from a shop's own mailbox. Emails are created
as real drafts in Outlook's **Drafts** folder (for review), or sent so they land in **Sent Items**. Built for a small
jewellery shop on Microsoft 365, reusable for any business and mailbox.

- **Microsoft 365** mailboxes via Microsoft Graph (OAuth; no SMTP passwords; basic-auth SMTP is being retired)
- **Any other mailbox** via SMTP, plus IMAP for Drafts and Sent copies
- **Test mode** that sends nothing, with an outbox you can inspect

Staff sign in through Keycloak via the shared [authgateway-bff](../ProductFeedback.BFF). Each business is a
**workspace**, mapped from a Keycloak group, and can only see its own data.

## Architecture

```
browser ──► mailmerge-bff (authgateway-bff image)        Keycloak login, cookie session, CSRF
              └─► mailmerge-api (.NET 10 minimal API)    UI + /api + public /u/{token} unsubscribe
                    ├─ Postgres (EF Core)                all state, attachments, Data Protection keys
                    └─ CampaignWorker                    Graph / SMTP+IMAP, paced per account
```

One project, vertical slices, strict REPR (request → endpoint → response, with a handler per operation),
following coffee-journal-api's rules:

```
src/MailMerge.Api/
├── Host/                 composition: auth, persistence, telemetry, endpoint discovery
├── Features/
│   ├── Workspaces/       GetCurrent, Update (shop name, retention)
│   ├── Accounts/         CRUD, Check, Microsoft sign-in (auth code + PKCE), TestOutbox
│   │   ├── Sending/      IMailSender: GraphMailSender, SmtpMailSender, TestMailSender
│   │   └── Microsoft365/ MSAL token cache (encrypted), sign-in flow
│   ├── Lists/ Contacts/ Imports/
│   ├── Campaigns/        CRUD, Copy, attachments, Preview, Plan, SendTest, Run/Pause/RetryFailed,
│   │   │                 Messages, Mark, log.csv
│   │   └── Delivery/     CampaignWorker, DeliveryPlanner, MessageBuilder, Pacing
│   ├── OptOuts/          list/add/remove; Unsubscribe/ (public pages + signed links)
│   └── Privacy/          FindPerson, ErasePerson, privacy log; Retention/ (purge service)
├── Shared/               Auth (Keycloak claims, workspace filter), Persistence, Http, Merging
└── wwwroot/index.html    the UI (plain HTML/JS, no build step)
```

Each operation folder holds `XRequest.cs` (record + FluentValidation validator), `XEndpoint.cs` (transport only),
`XHandler.cs` (use case, returns `Result<T>`) and `XResponse.cs`. Shared models sit at the feature root.

### Sending safety rules

- Each person gets a separate email. Drafts are made first, then sent, and the draft id is saved as soon as the
  draft exists, so a retry never makes a second draft.
- A message is marked `drafting`/`sending` *before* the network call. After a crash, `sending` becomes **uncertain**
  and is never retried automatically: staff check Sent Items and mark it.
- Graph `/send` is retried only on 429. A 5xx, timeout or 404 there means "maybe sent", so it becomes uncertain
  instead of risking a duplicate.
- Account problems pause the campaign; five failures in a row pause it. Pacing is per sending account with
  jitter (default 20/minute).
- Re-running a campaign only reaches people added since.

### Privacy

- Opt-outs are matched by a per-workspace HMAC of the address, so **Remove a person** can delete the address
  everywhere while keeping them blocked.
- Retention (default 90 days) archives idle campaigns: per-recipient rows and attachment bytes are deleted and
  summary counts are kept.
- Unsubscribe links (`/u/{token}`) are Data Protection tokens: a confirmation page, plus RFC 8058 one-click.
- SMTP passwords and Microsoft token caches are encrypted with ASP.NET Data Protection (keys in Postgres).

## Development

Everything runs through the nix dev shell (`direnv allow`, or prefix commands with `nix develop -c`).

```sh
dotnet test                                   # unit + integration (needs Docker for Testcontainers)
dotnet ef migrations add <Name> -p src/MailMerge.Api -o Shared/Persistence/Migrations
```

Integration tests use real Postgres and GreenMail (SMTP/IMAP) containers, WireMock for Graph, and a test
auth handler that emits Keycloak-shaped claims.

### Run locally without Keycloak

Development sign-in treats every request as one staff member. It only works in the Development environment with
the flag set:

```sh
docker run -d --name mm-pg -e POSTGRES_USER=mailmerge -e POSTGRES_PASSWORD=mailmerge -p 55432:5432 postgres:17-alpine
ASPNETCORE_ENVIRONMENT=Development Auth__Development__Enabled=true Auth__Development__Workspace=my-shop \
ConnectionStrings__MailMerge="Host=localhost;Port=55432;Database=mailmerge;Username=mailmerge;Password=mailmerge" \
App__PublicBaseUrl=http://localhost:5080 ASPNETCORE_URLS=http://localhost:5080 \
dotnet run --project src/MailMerge.Api --no-launch-profile
```

Then open <http://localhost:5080> and use a **Test mode** account.

### Full stack (BFF + Keycloak)

`docker compose up -d --build` (see `docker-compose.yml` and `.env.example`). Browser entrypoint:
<http://localhost:5224>.

## Configuration

| Setting | Meaning |
|---|---|
| `ConnectionStrings__MailMerge` | Postgres |
| `App__PublicBaseUrl` | Browser-facing origin (the BFF): used in unsubscribe links and the Microsoft redirect |
| `App__MaxUploadMegabytes` | Spreadsheet/attachment limit (default 25) |
| `Auth__Authority`, `Auth__Audience` | Keycloak realm URL; audience added by the `mailmerge-api` audience mapper |
| `Auth__RoleClientId`, `Auth__RequiredRole` | Client role required to use the app (`mailmerge` / `mailmerge-user`) |
| `Auth__WorkspaceGroupPrefix` | Keycloak group prefix for workspaces (`/mailmerge/`) |
| `Microsoft__ClientId`, `Microsoft__ClientSecret`, `Microsoft__Tenant` | Entra app for Microsoft 365 mailboxes |
| `ConnectionStrings__OTLP_Endpoint` | OpenTelemetry collector (optional) |

Deployment: `deployment/helm-charts/mailmerge-api` here; the BFF values (`values-mailmerge.yaml`) live in the authgateway-bff repo and the Argo CD apps in portfolio-cluster.
See [SETUP.md](SETUP.md). Staff guide: [HOW-TO.md](HOW-TO.md). The original Python version is in `legacy/` for
reference until the .NET version has been tested against a real tenant.
