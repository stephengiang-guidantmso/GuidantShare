# Architecture

### The whole system

```
Browser --443/TLS--> reverse proxy (Caddy) on SFTPSRVR
                      |-- /      -> static files of GuidantShare.Web
                      '-- /api/* -> GuidantShare.Api (127.0.0.1:5080, Windows service)
                                         |  http://localhost:5000 + X-Api-Key
                                         v
                                   FileSync (private, localhost only, Windows service)
                                         |  SFTP / portals, from the VM's whitelisted IP
                                         v
                                   health plans' servers
```

Only the proxy listens on a public port. GuidantShare.Api and FileSync listen on loopback only. FileSync holds the SFTP passwords and the transfer jobs; the public process holds neither and cannot restart a transfer by itself.

### GuidantShare components

```
Browser (React SPA)
   │  JSON over HTTPS, cookie session, CSRF header on writes
   ▼
GuidantShare.Api  (ASP.NET Core, .NET 10, minimal APIs)
   ├─ Identity + cookie auth, roles (User, SuperAdmin)
   ├─ Plans / files / upload endpoints, user and invite admin
   ├─ SuperAdmin proxy  /api/filesync/*  ─►  FileSync (allow-listed calls)
   ├─ FileSyncClient  ─►  FileSync GET /api/catalog (cached ~45 s)
   ├─ Rate limiting, security headers, forwarded headers, CSRF middleware
   ├─ SQLite via EF Core  ──►  data/app.db
   └─ ShareFileService ─────►  ShareFile REST v3 (its own OAuth refresh token)
                              Microsoft Graph (optional, for invite email)
```

| Project | Responsibility |
|---|---|
| `src/GuidantShare.Api` | Web host, endpoints, auth setup, invites, email senders, FileSync client and proxy, startup checks, CLI |
| `src/GuidantShare.Core` | Entities, interfaces (`IShareFileService`, `IFileSyncClient`, `ISecretStore`, `IEmailSender`), catalog DTOs, AES-GCM cipher |
| `src/GuidantShare.Data` | EF Core `AppDbContext`, identity user, encrypted `SecretStore` |
| `src/GuidantShare.ShareFile` | ShareFile REST client with OAuth, retries and the two-step upload; an offline mock for development |
| `src/GuidantShare.Web` | React + Vite single page app: the plan browser for everyone, plus Users, Settings and the FileSync screens for superadmins |
| `tests/GuidantShare.Tests` | Unit, integration and system tests |

### Where plans come from

FileSync's `GET /api/catalog` returns the plans and folders built from its enabled jobs (see [Plans and folders](admin-guide.md#plans-and-folders)). `FileSyncClient` keeps the last good answer: reads are reused for about 45 seconds; if FileSync cannot be reached the last answer is served marked `stale`, and a failed attempt is not repeated for 10 seconds. If nothing was ever loaded the API answers `503 filesync_unavailable`.

### Request flow (a typical download listing)

1. The browser calls `GET /api/plans/{planKey}/folders/{folderKey}/files`.
2. Cookie auth identifies the user; the endpoint finds the folder in the FileSync catalog (by plan key and folder key).
3. The folder's ShareFile folder id is used, never a name. The optional `folder` parameter must be the folder's root or a folder previously seen by listing it (see "Folder access" below).
4. `ShareFileService` fetches the children (cached for 30 seconds per folder, `refresh=true` bypasses the cache), using a short-lived access token it refreshes from the stored refresh token.
5. The API returns files (sorted newest first, paged) and subfolders, plus a link to open the folder in ShareFile.

### Upload flow

1. The browser sends a multipart POST to `/api/plans/{planKey}/folders/{folderKey}/uploads` with the CSRF header.
2. The API validates the folder (must be an Upload folder), target folder, file name, size and optional extension list, then checks for a same-named file.
3. If the name exists and `overwrite` is not set, it answers `409 exists` and records a `Conflict` audit.
4. Otherwise `ShareFileService` negotiates with `Items(folder)/Upload?raw=true`, then posts the raw bytes to the returned `ChunkUri`, then finds the new item by name.
5. An `UploadAudit` row records who, what, plan and folder, when, the size, the ShareFile item and the outcome (`Succeeded`, `Failed`, `Conflict`). FileSync then picks the file up from ShareFile and sends it to the health plan.

### The FileSync proxy

`/api/filesync/{path}` (SuperAdmin only) forwards allow-listed calls to FileSync with the API key added. It refuses everything else (notably saved-password reads, the debug dump and Swagger), keeps CSRF on every change, streams CSV downloads and records changes in `AdminAudits`. The browser never learns the API key or FileSync's address.

### Folder access

Every folder has a root ShareFile folder. Users may open that folder and any subfolder reachable from it, but only by id values the server has seen while listing. The server remembers `planKey/folderKey:folderId` pairs in memory; a folder id that was never listed for that plan folder is refused with `400 That folder isn't part of this plan.` This prevents guessing ids to reach unrelated folders.

### Design decisions

- **FileSync is the source of truth.** There is no catalog in this database. Plans appear, change and disappear when jobs do.
- **Two backends.** The public process has no SFTP passwords or master key. FileSync is never exposed.
- **Two ShareFile connections.** FileSync and GuidantShare each have their own OAuth connection to the same ShareFile account.
- **One database of its own.** `app.db` holds users, invites, audits and the encrypted ShareFile settings.
- **Encrypted secrets.** The ShareFile client secret and refresh token are AES-256-GCM encrypted with a master key file, in the `AppSecrets` table.
- **Single instance.** The folder cache, OAuth state, FileSync catalog copy and folder allow-list are in memory, so run one copy of the app.
- **No migrations yet.** The schema is created with `EnsureCreated` for a new database.

### Front end

The SPA uses React Router. Routes: `/login`, `/accept-invite`, `/forgot-password`, `/reset-password`, `/` (plans), `/plans/:planKey` and `/plans/:planKey/:direction` (folders), `/plans/:planKey/folders/:folderKey` (files), and for superadmins only `/admin/filesync`, `/admin/users` and `/admin/settings`. The FileSync screens are loaded on demand, so normal users never download them. The API returns `401` when signed out and `403` for non-superadmins, so the UI never receives redirects. The FileSync screens use their own scoped stylesheet (`src/filesync/filesync.css`, everything under `.fs-root`) so they do not restyle the rest of the site.

## Security

### Summary of controls

| Area | Control |
|---|---|
| Sign-up | Invite only. Tokens are 256-bit random, stored only as SHA-256 hashes, expire after 72 hours and work once |
| Sessions | HttpOnly cookie `sfw.auth`, SameSite Lax, `Secure` outside Development, 8 hours sliding. The API answers `401`/`403` instead of redirecting |
| Passwords | Hashed by ASP.NET Core Identity. At least 8 characters, a blocklist of common passwords, at least 5 distinct characters, must not contain the email name |
| Brute force | Lockout after 5 failures for 15 minutes. Auth routes limited to 20 requests per minute per client address |
| CSRF | Every non-GET `/api` call needs the `X-CSRF-TOKEN` header (antiforgery cookie `sfw.csrf`, SameSite Strict) |
| Roles | `User` and `SuperAdmin`. Settings, Users, invites and the whole FileSync proxy require `SuperAdmin`, enforced on the server |
| FileSync | Never exposed. It listens on localhost and requires an `X-Api-Key`. Browsers reach it only through `/api/filesync/*`, which allows a fixed list of calls and always refuses saved-password reads, the debug dump and Swagger. Saved partner passwords are write-only |
| Account enumeration | Wrong password and unknown user return the same message. Forgot-password always answers `200` |
| Secrets at rest | ShareFile client secret and refresh token are AES-256-GCM encrypted with `data/keys/master.key` |
| Partner passwords | **Never accepted, stored or displayed.** Only login names are kept |
| Browser headers | `Content-Security-Policy` (self, plus Google Fonts), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, HSTS outside Development |
| Folder access | Users reach only a plan folder's root ShareFile folder and folders listed beneath it. Folder ids are never taken on trust |
| Uploads | Name sanitized (no path parts or control characters, up to 200 characters), empty files refused, size and extension limits, rate limit 120/min per user, every attempt audited |
| Auditing | `LoginAudits` (logins and failures with address), `UploadAudits` (every upload attempt) and `AdminAudits` (every SuperAdmin change made through the FileSync proxy) |

### The master key

`data/keys/master.key` is a 32-byte key created on first start. It decrypts the stored ShareFile credentials.

- Keep it out of source control and out of backups that are less protected than the database itself.
- Back it up **separately** from `app.db`. Losing it means re-entering the ShareFile settings and reconnecting; leaking it together with the database exposes the ShareFile client secret and refresh token.
- Restrict its file permissions to the account that runs the app.

### ShareFile credentials

The app uses its own OAuth app and stores a refresh token for the owner account. FileSync keeps its own, separate connection. Anyone who can use the app can reach every folder in the catalog through that account, so:

- Limit the ShareFile account's own permissions to the folders FileSync's jobs use.
- Treat superadmins as holders of that access.
- Disconnect in Settings if the account may be compromised, and rotate the client secret in ShareFile.

### Things to configure for production

- Serve over HTTPS only and set `ASPNETCORE_ENVIRONMENT=Production` (secure cookies, HSTS).
- Set `AllowedHosts` and `Email:PublicBaseUrl` to the real hostname.
- Behind a reverse proxy on the same machine, forwarded headers are trusted from the loopback address only (already configured), so the login rate limit sees real client addresses. Keep the app listening on `127.0.0.1` so nothing can bypass the proxy.
- Set `FileSync:ApiKey` (and the same value as FileSync's `Api:Key`) with an environment variable. Use a long random value.
- Use environment variables or a protected local file for the Graph client secret.
- Keep `data/` readable only by the service account.

### Known limitations

- The common-password list is small and built in. Consider a breached-password check.
- The CSP allows `fonts.googleapis.com` and `fonts.gstatic.com`. Remove it for networks without internet access (then host the font yourself).
- In-memory state means one instance only; multiple instances would break the folder allow-list and connect flow.
- No multi-factor authentication.
- The build reports an advisory on `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (GHSA-2m69-gcr7-jv3q); check for an updated package before release.
- Uploads are buffered by ASP.NET form handling up to the configured size; very large limits cost memory or disk.

### Reporting and response

If you suspect credentials are exposed: disconnect ShareFile in Settings, rotate the ShareFile client secret, rotate partner passwords in FileSync (the website cannot read them), deactivate affected users, and review `LoginAudits`, `UploadAudits` and `AdminAudits`.

## ShareFile integration

### Registering the ShareFile app

In ShareFile, create an API application (OAuth 2.0 authorization code). You will get a client ID and client secret and choose a redirect URI. In GuidantShare Settings save:

- **Subdomain**: the part before `.sharefile.com` (stored lower-cased)
- **Client ID** and **Client secret**
- **Redirect URI**: exactly what is registered on the ShareFile app

Two redirect styles work:

1. `https://<your-host>/api/sharefile/callback`. One-click: after you approve, ShareFile sends the browser back to the app, which finishes the connection and returns you to Settings.
2. `https://secure.sharefile.com/oauth/oauthcomplete.aspx`. ShareFile shows a page with a code; paste the code or the full address into *Finish connecting*.

### Connecting

1. *Connect to ShareFile* builds `https://{sub}.sharefile.com/oauth/authorize?response_type=code&client_id=...&redirect_uri=...&state=...`. The `state` value is random, single-use and valid for 10 minutes.
2. You sign in with the owner account and approve.
3. The app exchanges the code at `https://{sub}.sharefile.com/oauth/token` (`grant_type=authorization_code`) and stores the **refresh token** encrypted, plus who connected and when.

### Staying connected

- API calls use `https://{sub}.sf-api.com/sf/v3` with a bearer **access token** held in memory.
- Access tokens are refreshed from the stored refresh token about 15 minutes before expiry, or immediately after a `401`. If ShareFile issues a new refresh token the app stores it.
- If refresh fails the status shows an error and users see "ShareFile is disconnected" until a superadmin reconnects.
- Rate-limit and server errors (`429`, `5xx`) are retried up to three times with backoff, honoring `Retry-After`.

### What the app calls

| Purpose | ShareFile call |
|---|---|
| Check a pasted folder link | `GET Items({id})` |
| List a folder | `GET Items({id})/Children?$top=1000`, following `odata.nextLink` |
| Start an upload | `POST Items({folderId})/Upload?raw=true&fileName=...[&overwrite=true]` returns a `ChunkUri` |
| Send the bytes | `POST {ChunkUri}` with the raw body as `application/octet-stream` |
| Find the new item | list the folder and take the newest file with that name |
| Open in the web app | link `https://{sub}.sharefile.com/home/shared/{folderId}` (built locally) |

The upload method matches what FileSync does (negotiate with `raw=true`, then post raw bytes), so both tools behave the same.

### Folder links and ids

Folder ids start with `fo`. They come from FileSync's jobs; the path text shown to users is a label. Details are on the [Plans and folders](admin-guide.md#plans-and-folders) page.

At startup the app checks that every folder in FileSync's catalog can be opened with GuidantShare's ShareFile connection and logs an error for any that cannot. It also warns if ShareFile is not connected.

### Failure behavior

| Situation | What users and admins see |
|---|---|
| Not connected or refresh token rejected | `503` with code `disconnected`; admins see the error on Settings |
| ShareFile or network failure | `502` "ShareFile could not be reached" (listing) or "Upload failed: ..." (uploads) |
| Folder deleted in ShareFile (or not shared with this connection's account) | Listing fails; the startup log names the plan and folder |
| Upload refused (permissions) | "ShareFile refused to start the upload (status)..." and a `Failed` audit row |

### Using this with FileSync

FileSync and GuidantShare both write to and read from the same ShareFile folders, each with **its own OAuth connection** to the same ShareFile account (a refresh token belongs to one app, so they are not shared). FileSync keeps the partner side in sync; GuidantShare never touches the partners. If a file uploaded here does not reach a partner, open the FileSync screen and check the jobs of that plan's folder.

## Data model

SQLite database at `data/app.db`, created with `EnsureCreated`. Enums are stored as text. Identity tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, and so on) come from ASP.NET Core Identity.

### Plans and folders

There are none in this database. Health plans and folders come from FileSync's jobs (see [Plans and folders](admin-guide.md#plans-and-folders)).

### Invites

`InviteId`, `Email`, `Role`, `TokenHash` (SHA-256 of the token; the token itself is never stored), `InvitedByUserId`, `CreatedUtc`, `ExpiresUtc` (72 hours after creation or resend), `AcceptedUtc`, `RevokedUtc`, `EmailStatus` (`Pending`, `Sent`, `Failed`), `EmailAttempts`, `LastEmailError`, `LastEmailAttemptUtc`.

### UploadAudits

One row per upload attempt.

| Column | Notes |
|---|---|
| AuditId | Returned to the UI as `auditId` |
| UserId | Who |
| PlanName, FolderLabel, SharefileFolderId | Which plan and folder (from FileSync's catalog at the time) |
| FileName | The sanitized name; includes the subfolder (`2026/jan.csv`) when uploaded below the folder root |
| SizeBytes | |
| SharefileItemId, SharefileLink | Set on success |
| Status | `Started`, `Succeeded`, `Failed`, `Conflict` |
| Error | First 300 characters of the failure |
| CreatedUtc | |

### LoginAudits

`Id`, `UserId`, `Event` (`login`, `login_failed`), `Ip`, `CreatedUtc`.

### AdminAudits

One row per SuperAdmin change made through the FileSync proxy: `Id`, `UserId`, `Action` (for example `POST workflows/wf-1/run`), `Status` (HTTP status FileSync returned), `CreatedUtc`. Request bodies are never stored.

### AppSecrets

Encrypted key/value store (`Key`, `Value` blob, `UpdatedUtc`). Values are AES-256-GCM blobs: 12-byte nonce, 16-byte tag, then ciphertext. Keys:

| Key | Holds |
|---|---|
| `sharefile.subdomain`, `sharefile.client_id`, `sharefile.redirect_uri` | App settings |
| `sharefile.client_secret` | ShareFile app secret |
| `sharefile.refresh_token` | GuidantShare's own connection (FileSync keeps a separate one) |
| `sharefile.connected_by`, `sharefile.connected_utc` | Who connected and when |

### Users

`AspNetUsers` plus `IsActive`, `CreatedUtc`, `LastLoginUtc`. Roles are `User` and `SuperAdmin`.

### In-memory state (not in the database)

- Folder listing cache, 30 seconds per folder.
- OAuth `state` values for the connect flow, 10 minutes each.
- The last catalog read from FileSync (kept so a short FileSync outage does not hide the plans).
- The set of folders reachable from each plan folder (see [Architecture](architecture.md#architecture)).

These reset on restart, which is why the app must run as a single instance.
