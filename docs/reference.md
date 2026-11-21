# Reference

## API reference

Base path `/api` (plus `/health`). JSON in and out. Errors are `{ "error": "message" }` and sometimes a `code`.

**Auth:** a cookie session (`sfw.auth`). Signed out gives `401`; a signed-in non-superadmin calling an admin route gives `403`.

**CSRF:** every non-GET call to `/api/...` must send the header `X-CSRF-TOKEN` with the value from `GET /api/auth/csrf`. Missing or wrong gives `400 Invalid CSRF token`. Fetch a fresh token after signing in or out.

**Rate limits:** auth routes 20 per minute per client address; uploads 120 and FileSync proxy calls 600 per minute per user. Over the limit gives `429`.

Legend: 🌐 public, 🔑 any signed-in user, 👑 superadmin.

### Health

| | Route | Returns |
|---|---|---|
| 🌐 | `GET /health` | `{ status, email, emailMode, fileSync, sharefile }` where `sharefile` is `connected`, `disconnected` or `not_configured`, and `fileSync` is `ok` or `unreachable` |

### Authentication

| | Route | Body / query | Notes |
|---|---|---|---|
| 🌐 | `GET /api/auth/csrf` | | `{ token }` |
| 🌐 | `POST /api/auth/login` | `{ email, password }` | Same `401` message for unknown user and wrong password. 5 failures lock the account for 15 minutes. Returns `{ id, email, role }` |
| 🌐 | `POST /api/auth/logout` | | |
| 🔑 | `GET /api/auth/me` | | `{ id, email, role }`; `401` if inactive |
| 🌐 | `GET /api/auth/invite-info?token=` | | `{ email }` or `404` |
| 🌐 | `POST /api/auth/accept-invite` | `{ token, password }` | Single use. Password rules apply |
| 🌐 | `POST /api/auth/forgot-password` | `{ email }` | Always `200`; sends a link only for active accounts |
| 🌐 | `POST /api/auth/reset-password` | `{ email, token, password }` | |

### Users and invites (👑)

| Route | Body | Notes |
|---|---|---|
| `GET /api/users` | | `{ users: [...], invites: [...] }` (pending invites only) |
| `PATCH /api/users/{id}` | `{ isActive?, role? }` | Role is `User` or `SuperAdmin`. You cannot deactivate or demote yourself |
| `DELETE /api/users/{id}` | | Removes the account (not yourself). Their upload history is kept |
| `POST /api/users/{id}/password` | `{ password }` | The admin sets a new password (normal password rules); the user is signed out everywhere |
| `POST /api/users/{id}/reset-password` | | `{ ok, error, resetLink }` (emails a reset link) |
| `POST /api/invites` | `{ email, role, emailOnly?, noEmail? }` | `emailOnly: true` hides the link in the response. `noEmail: true` with `emailOnly: false` creates the invite without sending and returns `inviteLink` |
| `POST /api/invites/{id}/resend` | | New token, one per minute, up to 20 attempts |
| `POST /api/invites/{id}/revoke` | | |

Invite response: `{ id, email, emailStatus, emailError, inviteLink }`.

### ShareFile connection (👑)

| Route | Body | Notes |
|---|---|---|
| `GET /api/sharefile/status` | | `{ configured, connected, subdomain, connectedBy, connectedUtc, error, redirectUri, clientId }` |
| `POST /api/sharefile/settings` | `{ subdomain, clientId, clientSecret?, redirectUri? }` | Blank secret keeps the saved one |
| `POST /api/sharefile/connect` | | `{ url }` to open in a new tab |
| `GET /api/sharefile/callback` | `?code&state` | Landing page when the redirect URI points at this app |
| `POST /api/sharefile/exchange` | `{ code }` | Accepts the bare code or the full address containing `code=` |
| `POST /api/sharefile/disconnect` | | |

### FileSync proxy (👑)

`GET|POST|PUT|DELETE /api/filesync/{path}` forwards a call to FileSync's private API (`/api/{path}`) and returns its answer unchanged (status, JSON, CSV downloads). Only the calls the FileSync screens need are allowed (`workflows`, `workflows/{id}/run|toggle|cancel|verify|browse`, `verifications/...`, `sftp/verify`, `runs`, `transfers...`, `logs`, `catalog`, `portal-code`, `health`). Anything else, including `credentials`, `debug` and `swagger`, gets `403` and is never forwarded. CSRF applies to every non-GET call. Changes are recorded in `AdminAudits` (who, which call, result; never the request body). Rate limit: 600 per minute per user. If FileSync cannot be reached the answer is `502`.

### Plans, folders, files and uploads (🔑)

The plans and folders come from FileSync's enabled jobs (see [Plans and folders](admin-guide.md#plans-and-folders)). Plans are identified by a `key` (for example `clever-care`) and folders by a `key` that includes the direction (for example `upload-278`).

| Route | Notes |
|---|---|
| `GET /api/plans` | `{ stale, fetchedUtc, plans: [ { key, name, hasDownload, hasUpload } ] }`. `stale: true` means FileSync could not be reached and the last good list is shown. `?refresh=true` skips the cache (superadmins only) |
| `GET /api/plans/{planKey}` | `{ key, name, stale, folders: [ { key, label, direction, shareFilePath } ] }` or `404` |
| `GET /api/plans/{planKey}/folders/{folderKey}/files` | Query: `folder`, `search`, `page`, `pageSize` (1 to 100, default 25), `refresh` |
| `GET /api/plans/{planKey}/folders/{folderKey}/exists?name=&folder=` | `{ exists }` for a same-named file (case-insensitive) |
| `POST /api/plans/{planKey}/folders/{folderKey}/uploads` | Multipart form: `file`, optional `folder`, optional `overwrite=true`. Upload folders only |
| `GET /api/uploads/{id}` | Your own receipt (superadmins can read anyone's): `{ auditId, fileName, status, sharefileLink, error, createdUtc }` |

Files response:

```json
{
  "plan": "CAQH", "label": "CAQH", "direction": "Download", "stale": false,
  "total": 12, "page": 1, "pageSize": 25, "refreshedUtc": "...",
  "isRoot": true,
  "folderLink": "https://acme.sharefile.com/home/shared/fo...",
  "location": { "shareFilePath": "Shared Folders/SFTP Drop CAQH" },
  "folders": [ { "id": "fo...", "name": "2026" } ],
  "items": [ { "id": "fi...", "name": "roster.csv", "size": 2048, "modifiedUtc": "..." } ]
}
```

#### Status codes you may see

| Code | Meaning |
|---|---|
| 400 | Validation failure, CSRF failure, or a folder that is not part of the plan folder |
| 401 / 403 | Signed out / not a superadmin |
| 404 | Unknown or removed plan, folder or receipt |
| 409 | Upload name already exists (`code: "exists"`) |
| 429 | Rate limit |
| 502 | ShareFile (or, on the proxy, FileSync) could not be reached or failed |
| 503 | ShareFile disconnected (`code: "disconnected"`), or FileSync unreachable with no catalog loaded yet (`code: "filesync_unavailable"`) |

## Configuration

Settings come from, in increasing priority: `src/GuidantShare.Api/appsettings.json`, `appsettings.{Environment}.json`, `appsettings.Local.json` (gitignored, optional), environment variables, and command-line arguments. Environment variables use `__` for nesting, for example `Email__Mode=Graph`.

### Settings

| Setting | Default | Purpose |
|---|---|---|
| `Urls` | `http://localhost:5080` | Address(es) Kestrel listens on. Production: `http://127.0.0.1:5080` (set in `appsettings.Production.json`), so only the reverse proxy can reach it |
| `AllowedHosts` | `*` | Host header allow-list. Set to your hostname in production |
| `Upload:MaxBytes` | `524288000` (500 MB) | Upload size limit. The request body limit is this plus 1 MB |
| `Upload:AllowedExtensions` | none | Optional comma-separated list (for example `csv,txt`) applied to every upload folder |
| `FileSync:BaseUrl` | `http://localhost:5000` | FileSync's private API. It must stay on this machine |
| `FileSync:ApiKey` | none | Must equal FileSync's `Api:Key`. Set it with an environment variable (`FileSync__ApiKey`), never in git |
| `FileSync:CacheSeconds` | `45` | How long the plan list is reused before FileSync is asked again |
| `ShareFile:Mode` | none | `Mock` = offline stand-in for ShareFile, for local development only (refused in Production) |
| `Data:Dir` | `./data` | Folder for the database, key and logs |
| `Data:KeyPath` | `{Data:Dir}/keys/master.key` | AES key that encrypts stored secrets |
| `Data:ConnectionString` | `Data Source={Data:Dir}/app.db` | SQLite connection |
| `Email:Mode` | `Log` | `Log` or `Graph` |
| `Email:PublicBaseUrl` | `http://localhost:5080` | Base of invite and reset links |
| `Email:TenantId`, `Email:ClientId`, `Email:ClientSecret`, `Email:Mailbox` | none | Microsoft Graph sending (mode `Graph`) |
| `Email:FromName` | `GuidantShare` | Display name |
| `Logging:*` | Information | Standard .NET logging; Serilog reads the same section |
| `ASPNETCORE_ENVIRONMENT` | `Production` unless set | `Development` relaxes the secure-cookie requirement and disables HSTS |

### Email modes

- **Log:** invite and reset emails are written to the log. Good for development and for installs where an admin shares links by hand.
- **Graph:** emails are sent with Microsoft Graph using an app registration (client credentials) that is allowed to send as `Email:Mailbox`. A startup check logs an error if the configuration is wrong, and `/health` reports `email: "misconfigured"`. Transient failures (throttling, 5xx) are retried up to three times with backoff; permanent failures (rejected message, mailbox not allowed to send) are recorded on the invite so an admin can see and resend.

### ShareFile settings are not in these files

The subdomain, client ID, client secret, redirect URI and refresh token are set in the app (Settings) and stored encrypted in the database. See [ShareFile integration](architecture.md#sharefile-integration).

### Fixed in code

| Behavior | Value | Where |
|---|---|---|
| Password rules | at least 8 characters, a small common-password blocklist, at least 5 distinct characters, must not contain the email name | `Program.cs`, `CommonPasswordValidator.cs` |
| Lockout | 5 failures, 15 minutes | `Program.cs` |
| Session | 8 hours, sliding | `Program.cs` |
| Invite lifetime | 72 hours | `InviteService.cs` |
| Folder listing cache | 30 seconds | `Endpoints.cs` |
| FileSync retry after a failure | 10 seconds | `FileSyncClient.cs` |
| Rate limits | auth 20/min per address; uploads 120/min and FileSync proxy 600/min per user | `Program.cs` |
| ShareFile HTTP timeout | 10 minutes | `ShareFileService.cs` |
| Default page size | 25 (1 to 100) | `Endpoints.cs` |

### Example `appsettings.Local.json`

```json
{
  "Urls": "http://localhost:5080",
  "AllowedHosts": "share.example.com",
  "Email": {
    "Mode": "Graph",
    "PublicBaseUrl": "https://share.example.com",
    "TenantId": "<tenant guid>",
    "ClientId": "<app guid>",
    "ClientSecret": "<set via environment variable instead>",
    "Mailbox": "noreply@example.com"
  },
  "Upload": { "MaxBytes": 524288000 }
}
```

Prefer environment variables for secrets such as `Email__ClientSecret` so they never sit in a file.
