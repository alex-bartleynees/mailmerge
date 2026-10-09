# MailMerge: Python → .NET migration plan

Status: proposal, 2026-10-10. Nothing in the Python version has been deployed or holds real data, so
this is a rewrite with a behaviour inventory, not a data migration.

## 1. Review of what exists

### What carries over unchanged (domain behaviour)

These are the parts worth keeping. Every one gets a .NET acceptance test before it is considered ported.

| Behaviour | Python source | Python test |
|---|---|---|
| `{{Field}}` / `{{Field\|fallback}}`, case-insensitive lookup, HTML-escape values in HTML mode | `merge.py` | `test_preview_and_unknown_fields`, `test_html_mode_escapes_values` |
| Excel/CSV import: upload → preview (sheet pick, email-column detection, invalid/blank counts) → commit (new or existing list, case-insensitive column mapping, update-existing toggle, dedupe) | `sheets.py`, `main.py` import routes | `test_import_review_and_commit`, `test_csv_import_and_bad_file` |
| Contact editing, add/remove column, export to .xlsx | `main.py` | `test_edit_add_column_and_fix_email`, `test_export_roundtrip` |
| Opt-out list; skip invalid / "don't email" / opted-out contacts | `campaigns.classify` | `test_suppression` |
| Materialize one message row per contact; re-runs only process new people | `campaigns.materialize` | `test_rerun_only_new_people` |
| Plan ("what happens if I press the button") | `campaigns.plan` | — (add one) |
| Draft-then-send: draft id saved the moment it exists, so a restart never makes a second draft | `worker._tick` | `test_create_drafts_then_send` |
| Crash mid-send → `uncertain`, never auto-retried; user marks sent or retries | `worker.start` | `test_crash_mid_send_is_never_resent` |
| Auth/permission errors pause the campaign; 5 failures in a row pause it | `worker._tick` | `test_auth_problem_pauses_instead_of_failing` |
| Pacing: N/minute per account with ±30% jitter, pause is near-instant | `worker._wait_between` | — (add one with a fake `TimeProvider`) |
| Graph: draft, inline attachment ≤3 MB, upload session in 320 KiB-multiple chunks above that, 429/503/504 back-off honouring `Retry-After`, 401/403 → auth error | `senders.GraphSender` | `test_graph_*` |
| SMTP send with attachment; optional IMAP for Drafts and Sent copies | `senders.SmtpSender` | `test_smtp_send_with_attachment` |
| Dry-run sender writing `.eml` files | `senders.DryRunSender` | used by flow tests |
| Secrets (SMTP/IMAP passwords) never returned by the API | `main.py` `SECRET_KEYS` | `test_password_never_returned` |

### Problems found in the review (fix in the port, don't copy)

1. **Retrying `POST /send` on 503/504.** `GraphSender._call` retries every request on 503/504, including
   `/me/messages/{id}/send`. A gateway timeout can mean "sent but no reply". Today the second attempt
   gets a 404 (the draft already moved to Sent Items), so the row is marked **failed** when it was
   actually sent, and "Retry failed" repeats that. In .NET: only retry `/send` on 429. Treat
   5xx/timeout/404-after-send as `uncertain`.
2. **One running campaign globally.** The worker picks `status='running' ORDER BY id LIMIT 1`, so two
   staff sending from two mailboxes queue behind each other. Once there are several users and
   workspaces, the worker must schedule and pace **per sending account**.
3. **No ownership model.** Every row is global and the only gate is one shared Basic-auth password.
   That's the main reason to move to Keycloak. See §3.
4. **Tokens and SMTP passwords stored in plain text** under `./data`. Use ASP.NET Data Protection to
   encrypt them, with keys persisted the same way the BFF does.
5. **Login state kept in process memory** (`_login_state`), so it's lost on restart and can't work
   with more than one replica. Move it to the database.
6. **Device-code sign-in.** It works, but Microsoft increasingly blocks device-code flow through
   Conditional Access. Now that this is a real web app behind HTTPS, use the auth-code + PKCE flow with
   a redirect URI. See §5.
7. **No unsubscribe link**, which NZ rules require for promotional email. Note that Graph only accepts
   custom `x-` headers, so `List-Unsubscribe` can't be set via Graph. A footer link is the realistic
   option. SMTP can set the header as well.
8. **Brute-force lockout, per-user logins and HTTPS** are all handled by Keycloak and the BFF, so
   those open items disappear rather than needing code.

## 2. Target architecture

```
browser ──► mailmerge-bff (authgateway-bff image, config only)
              ├─ /bff/*            login/logout/user/antiforgery (Keycloak OIDC, cookie session)
              ├─ /api/public/*     → mailmerge-api, anonymous (unsubscribe)
              ├─ /api/*            → mailmerge-api, Bearer token injected, X-CSRF-TOKEN enforced
              └─ /{**catch-all}    → mailmerge-api static UI (wwwroot) or a separate SPA container
mailmerge-api (.NET 10 minimal API)
              ├─ JWT bearer validated against the Keycloak realm
              ├─ Postgres (EF Core): all state, including attachment bytes
              └─ CampaignWorker BackgroundService → Microsoft Graph / SMTP+IMAP
```

The BFF needs no code changes; it's the same reusable `creativefree/product-feedback-bff` image that
coffee-journal uses, configured with environment variables:

- `OpenIdConnectOptions__ClientId=mailmerge`, plus the client secret from a k8s secret.
- YARP routes as listed above.
- `Kestrel__Limits__MaxRequestBodySize`, raised from the 30 MB default, because attachments and
  spreadsheets go through the BFF.

### Project layout (vertical slices, strict REPR)

The layout follows coffee-journal-api's rules, translated to C#. Each operation owns its own request,
endpoint and response. Use-case orchestration lives with the operation, and adapters belong to the
feature that uses them. It's a single API project, without DopamineKick's
Api/Application/Domain/Infrastructure split, because this is one bounded context. DopamineKick
contributes the shared-kernel packages, the `IEndpointDefinition` registration, `Result<T>`, the
OpenTelemetry setup, the Testcontainers fixture and `TestAuthHandler`.

```
MailMerge.slnx
global.json · Directory.Build.props · Directory.Packages.props (CPM) · nuget.config (SharedKernel.* from GitHub Packages)
src/MailMerge.Api/
├── Program.cs                              # 5-line launch boundary
├── Host/                                   # AddMailMerge(), auth, OTel, problem details, endpoint discovery
├── Features/
│   ├── Workspaces/   GetCurrent/                         # who am I, which workspace, setup checklist state
│   ├── Accounts/     List/ Create/ Update/ Delete/ Check/
│   │                 MicrosoftSignIn/ (Start, Callback) SignOut/
│   │                 Sending/  IMailSender, GraphMailSender, SmtpMailSender, DryRunMailSender,
│   │                           MailSenderFactory, MicrosoftTokenStore
│   ├── Lists/        List/ Create/ Rename/ Delete/ AddColumn/ RemoveColumn/ Export/
│   ├── Contacts/     List/ Add/ Update/ Delete/
│   ├── Imports/      Upload/ GetSheet/ Commit/   SpreadsheetReader, EmailColumnDetector
│   ├── Campaigns/    List/ Create/ Get/ Update/ Delete/ AddAttachment/ RemoveAttachment/
│   │                 Preview/ SendTest/ GetPlan/ Run/ Pause/ RetryFailed/
│   │                 Messages/ (List, Mark) ExportLog/
│   │                 Delivery/  CampaignWorker, DeliveryPlanner (classify/materialize/plan), MessageBuilder
│   ├── OptOuts/      List/ Add/ Remove/ Unsubscribe/ (public, signed token)
│   └── Privacy/      FindPerson/ ErasePerson/ GetRetention/ UpdateRetention/
│                     RetentionService (BackgroundService, daily purge)
├── Shared/
│   ├── Persistence/   MailMergeDbContext, migrations, workspace query filter
│   ├── Auth/          CurrentUser (sub, email, workspace) from claims
│   ├── Merging/       MergeTemplate (render/used fields/html→text), EmailAddress
│   └── Protection/    SecretProtector (Data Protection wrapper)
└── wwwroot/          UI (see §6)
tests/
├── MailMerge.UnitTests/           merge, spreadsheet parsing, email detection, planner, pacing
└── MailMerge.IntegrationTests/    WebApplicationFactory + Testcontainers Postgres + TestAuthHandler,
                                   fake Graph (WireMock.Net), SMTP/IMAP (GreenMail container)
deployment/helm-charts/mailmerge/
```

Each operation folder looks like this:

```
Features/Campaigns/Run/
  RunCampaignRequest.cs     # record + FluentValidation validator (validation that defines the request)
  RunCampaignEndpoint.cs    # binds route/body/CurrentUser → calls handler → maps Result to TypedResults
  RunCampaignHandler.cs     # orchestration; depends on DbContext + DeliveryPlanner
  RunCampaignResponse.cs
```

Conventions:

- **No Mediator.** Endpoints call the handler directly, which matches coffee-journal's
  endpoint → use-case flow. Handlers are registered by scanning.
- Each feature has one `IEndpointDefinition` that acts as the router: it maps the group, calls
  `RequireAuthorization("mailmerge")`, and maps each operation's static `Handle`.
- Handlers return `Result<T>` (SharedKernel.Results). Endpoints declare `Results<Ok<..>, NotFound<Error>, ...>`
  so OpenAPI stays accurate.
- Messages shown to shop staff, such as "Choose a sending account first", keep the Python wording.
  They go in `Error.Description` and the UI shows them verbatim.
- Data access uses the DbContext directly from handlers. There's no generic repository, in line
  with coffee-journal's "no broad database facade" rule. Read handlers use `AsNoTracking()`.

### Packages

| Need | Package |
|---|---|
| Postgres | `Npgsql.EntityFrameworkCore.PostgreSQL` (contact fields as `jsonb`) |
| Auth | `Microsoft.AspNetCore.Authentication.JwtBearer` |
| Validation | `FluentValidation` + `SharpGrip.FluentValidation.AutoValidation.Endpoints` (as in DopamineKick) |
| Excel read/write | `ClosedXML` (MIT). Optionally `ExcelDataReader` so old `.xls` files work, which Python couldn't do |
| CSV | `Sep` or `CsvHelper`, with delimiter sniffing ported from `sheets.read` |
| Microsoft sign-in + token cache | `Microsoft.Identity.Client` (MSAL.NET), with the cache serialized encrypted into Postgres |
| Graph calls | plain `HttpClient` + `Microsoft.Extensions.Http.Resilience` (429-only retry on `/send`). The Graph SDK isn't worth the weight here |
| SMTP/IMAP | `MailKit` / `MimeKit` |
| Telemetry | OpenTelemetry packages as in DopamineKick → cluster collector |
| Shared kernel | `SharedKernel.Results`, `SharedKernel.AspNetCore`, `SharedKernel.Abstractions` |

## 3. Keycloak integration and data ownership

- **Client:** create a confidential client `mailmerge` for the BFF, with redirect URI
  `https://mailmerge.<domain>/signin-oidc` and `http://localhost:5224/*` for dev.
- **Audience:** add an audience mapper so access tokens carry `aud: mailmerge-api`, and validate that in
  the API. DopamineKick validates `aud: account`, which every client in the realm receives, so don't
  copy that.
- **Who can get in:** the API requires the policy `mailmerge`, meaning the token has the client role
  `mailmerge-user`. Without it, anyone who can sign in to the realm (for example a coffee-journal
  self-signup) would reach the API.
- **Workspaces (one per shop/business):** this is the "reusable for other domains" requirement. Use
  Keycloak groups such as `/mailmerge/max-wilson`, emitted through a `groups` mapper. On first request,
  a workspace row is upserted by group path. Every entity has a `WorkspaceId`, enforced with an EF
  global query filter from `CurrentUser`. Integration tests prove that workspace A can't read B.
- **Sending accounts** belong to the workspace, so any staff member can use the shop mailbox. Each
  account records who signed it in to Microsoft.
- **Brute force, password policy, optional 2FA:** set in the realm (Keycloak's Brute Force Detection).
  That covers the "wrong-password lockout" item with no code.
- **Realm choice:** see Decision 2.

## 4. Data model (Postgres)

The tables stay close to the Python schema, with tenancy and the fixes added:

- `workspaces(id, keycloak_group, name, created)`
- `sending_accounts(id, workspace_id, name, kind[graph|smtp|dryrun], from_email, from_name, per_minute, config jsonb, protected_secrets bytea, msal_cache bytea, signed_in_by, signed_in_as)`
- `microsoft_sign_ins(state, account_id, user_sub, pkce_verifier_protected, expires)` replaces `_login_state`
- `contact_lists(id, workspace_id, name, columns text[], created)`
- `contacts(id, list_id, email citext, data jsonb, do_not_email, unique(list_id, email))`
- `import_uploads(token, workspace_id, file_name, bytes, created)`, purged after 24 h instead of being left on disk
- `workspace_settings(workspace_id, retention_days default 90)`
- `campaigns(id, workspace_id, name, account_id, list_id, subject, body, is_html, mode, status, note, unsubscribe_footer, created, last_activity_at, archived_at, summary jsonb)`
- `attachments(id, campaign_id, file_name, content_type, size, content bytea)`. Bytes go in Postgres
  because files are small, it means one backup, and there's no PVC. Revisit SeaweedFS if they grow.
- `messages(id, campaign_id, contact_id, email, status, error, provider_id, updated, unique(campaign_id, email))`
- `opt_outs(workspace_id, email_hash, email null, added, reason, source[manual|link|erasure], primary key(workspace_id, email_hash))`.
  `email_hash` is an HMAC-SHA256 of the normalised address, keyed per workspace. Suppression checks
  match on the hash, so an erased person stays opted out without their address being kept (§5a).
- `privacy_log(id, workspace_id, action[erase|retention_purge], performed_by, performed_at, counts jsonb)`
  holds counts only and no personal data, as proof that a request was honoured.

Message statuses stay the same (`pending, drafting, drafted, sending, sent, failed, skipped, uncertain`),
as does the startup recovery: `drafting → pending` and `sending → uncertain`.

## 5. Sending

- **Worker:** a `BackgroundService` with a `TimeProvider` so pacing is testable. Each tick does this:
  1. Pick the next account that is "due" among accounts with a running campaign.
  2. Claim one message with `SELECT … FOR UPDATE SKIP LOCKED`.
  3. Run the same state machine as Python.

  Pacing is tracked per account (`next_send_at`). The deployment stays at a single replica with the
  `Recreate` strategy. `SKIP LOCKED` keeps it safe anyway.
- **Microsoft:**
  - Sign-in uses the auth-code + PKCE flow. The user clicks "Sign in with Microsoft", which opens
    `/api/accounts/{id}/microsoft/start`, and Microsoft redirects back to
    `/api/accounts/microsoft/callback`. The callback is proxied by the BFF and carries the session
    cookie, so it's tied to the Keycloak user.
  - The Entra app is a multitenant web app with a client secret in a k8s secret. Permissions are
    unchanged (`Mail.ReadWrite Mail.Send User.Read offline_access`), and so is the owner-consent step
    in SETUP.md.
  - Device code stays available as a fallback setting.
- **Graph retries:** 429 is retried everywhere and honours `Retry-After`. 503/504 is retried for
  everything except `/send`. For `/send`, a 5xx, timeout or 404 marks the message `uncertain`.
- **SMTP:** MailKit, with IMAP APPEND to Drafts and Sent and special-use folder detection ported from
  `_folder`.
- **Unsubscribe:** an optional footer per campaign, `{{UnsubscribeUrl}}`, pointing to
  `/api/public/unsubscribe/{token}`. The token is a Data-Protection-protected
  (workspace, email, campaign) payload. The endpoint shows a confirmation page and the POST adds the
  opt-out. SMTP also sets `List-Unsubscribe` and `List-Unsubscribe-Post`.

## 5a. Privacy: retention and erasure

These keep the server's copy of customer data small and let the shop answer NZ Privacy Act requests.
Neither one touches the shop's Outlook mailbox. Drafts and Sent Items there are the shop's own
records, and HOW-TO.md will tell staff to delete those by hand if a customer asks.

### Retention (default 90 days, adjustable per workspace in Settings)

- **The clock** is `campaigns.last_activity_at`, updated by every worker step, run, pause, retry and
  mark. A campaign is eligible once it is **not running**, has **no `uncertain`, `drafting` or
  `sending` rows**, and `last_activity_at` is older than the retention period. A campaign whose
  rows still need a human decision is never purged.
- **What's purged:** attachment bytes and all `messages` rows. Summary counts (sent/failed/skipped)
  are written to `campaigns.summary` first. The campaign keeps its name, subject, body and
  `archived_at`, so the Emails tab can still show "Sent to 412 people on 3 Nov".
- **Archived campaigns can't be run again.** Without the message rows, "re-runs only reach new
  people" would no longer hold and everyone would get the email again. Instead, the UI offers
  **Copy as new email**.
- **Warning beforehand:** campaigns within 14 days of purge show a notice, and the log.csv export
  stays available until then.
- **Also purged:** import uploads older than 24 hours (already in §4).
- `RetentionService` runs daily (a `PeriodicTimer` driven by `TimeProvider`) and writes one
  `privacy_log` row per purge.
- **Not purged automatically:** contacts and lists. They're the shop's working data and stay until
  someone deletes them.

### Remove a person ("delete this person everywhere")

- `GET /api/privacy/people?email=` (FindPerson) shows what's held before deleting: which lists
  and which campaigns, with their message statuses.
- `POST /api/privacy/people/erase { email, keepOptedOut: true }` (ErasePerson) runs in **one
  transaction**:
  - deletes every contact row with that address in the workspace, across all lists;
  - deletes their `messages` rows in every campaign;
  - rewrites their opt-out row to hash-only (`email = null`, `source = erasure`). With
    `keepOptedOut` (the default) a hash-only opt-out is created even if none existed, so
    re-importing an old spreadsheet won't email them again. That's almost always what someone
    asking to be removed wants. Untick it only if they explicitly want to stay on the list later;
  - writes a `privacy_log` row (counts only).
- **If their message is mid-send** (`drafting`/`sending`) the request is refused with "This person
  is being emailed right now. Try again in a minute."
- **Unsent Graph drafts:** if the person has one, the response lists it so staff can delete it in
  Outlook. The API doesn't delete mailbox items itself.
- **UI:** an **Opt-outs & privacy** tab (the renamed Opt-outs tab) with a "Remove a person" box:
  enter the email, see what's held, then confirm. The confirmation names the counts.

## 6. UI

The current `index.html` (about 38 KB, vanilla JS) works and has been click-tested in jsdom. Two
options:

- **A (recommended first):** keep it and serve it from the API's `wwwroot` through the BFF catch-all
  route. The changes are:
  - Remove Basic auth.
  - Add a `fetch` wrapper that gets `/bff/antiforgery` once and sends `X-CSRF-TOKEN`.
  - Redirect to `/bff/login` on 401.
  - Show the user from `/bff/user`, with a logout button.
  - Replace the device-code dialog with a "Sign in with Microsoft" redirect.
- **B (later, optional):** rebuild in Angular as its own container behind the BFF, like
  coffee-journal-app. The API contract won't change, so this can happen any time after A.

## 7. Phases

Each phase ends green: tests pass and nothing is left half-ported.

0. **Baseline.** Commit the Python app as-is (the repo has no commits yet), then move it to `legacy/`
   for reference. This document is the behaviour inventory.
1. **Skeleton.**
   - Solution, CPM, `global.json`, `nuget.config`, flake/direnv as in sibling repos.
   - Host wiring: OTel, problem details, health check.
   - JWT auth plus the `mailmerge` policy, `CurrentUser`, workspace upsert and query filter.
   - DbContext with the first migration.
   - Integration fixture: Testcontainers Postgres and `TestAuthHandler` extended with role and group
     headers.
   - `docker-compose.yml`: Postgres, Redis, the API and the BFF pointed at the Keycloak realm.
   - Test: an unauthenticated request gets 401, a request without the role gets 403, workspace
     isolation holds.
2. **Contacts:** Lists, Contacts, Imports, OptOuts and Export. Port `test_import_*`,
   `test_edit_*`, `test_export_roundtrip`, `test_csv_import_and_bad_file` and `test_suppression`.
3. **Campaigns:** CRUD, attachments, preview, plan and MergeTemplate. Port `test_preview_*`,
   `test_attachment_upload` and `test_html_mode_escapes_values`.
4. **Senders:** DryRun first, then Graph against WireMock (draft, inline vs chunked attachments,
   429, 401/403 → auth error, `/send` 504 → uncertain), then SMTP/IMAP against GreenMail, then
   Microsoft auth-code sign-in. Secrets go through Data Protection; port `test_password_never_returned`.
5. **Worker:** run, pause, retry-failed, mark and log.csv. Port `test_create_drafts_then_send`,
   `test_rerun_only_new_people`, `test_crash_mid_send_is_never_resent` and
   `test_auth_problem_pauses_*`. New tests: per-account pacing with a fake clock, and two accounts
   sending concurrently.
6. **UI:** option A behind the BFF. Smoke test in a real browser through `docker compose`.
7. **New features:**
   - **Privacy (§5a).**
     - Retention tests use a fake clock: eligible campaigns are purged; running, `uncertain` and
       recent campaigns are untouched; archived campaigns refuse Run and allow Copy.
     - Erasure tests: the person is removed from every list and campaign; a re-import of the same
       address is skipped as opted out; mid-send is refused; workspace B is unaffected;
       `privacy_log` holds no email address.
   - Unsubscribe footer and public endpoint, plus the "Getting started" checklist
   state in `Workspaces/GetCurrent`.
8. **Deploy:**
   - Helm chart: one replica, `Recreate`, Postgres database in the cluster's `database.yaml` instance.
   - Data Protection keys go to Redis or Postgres.
   - Argo apps for `mailmerge-api` and `mailmerge-bff`.
   - Keycloak client, roles and groups.
   - Entra app registration.
   - Real tenant test: sign in, send a test to yourself, a 3-contact draft run, then a send run.
   - Rewrite README, SETUP and HOW-TO.
9. **Retire Python:** delete `legacy/` once phase 8 passes against the real tenant.

## 8. Decisions needed

1. **Hosting:** the plan assumes the portfolio-cluster (Keycloak, Postgres, Redis, OTel and Argo are
   already there). Is that OK for a client's business tool, or should it run somewhere separate?
2. **Keycloak realm:** a dedicated `mailmerge` realm (recommended: no self-registration, shop-specific
   password and 2FA policy, staff isolated from personal-project users) or the shared `production`
   realm with the `mailmerge-user` role gate?
3. **UI:** option A (keep vanilla, adapt to the BFF) now and Angular later, or go straight to Angular?
4. **Microsoft sign-in:** auth code (recommended, needs a client secret in the Entra app) or keep
   device code?
5. **List size:** still unknown. It decides whether sends should be spread over several days. Exchange
   Online allows 10,000 recipients/day per mailbox. At 20/min a 1,000-person list takes about 50 min,
   which is fine, so this only matters past a few thousand.
