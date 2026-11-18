# Architecture

## Components

```
Browser (React SPA)
   │  JSON over HTTPS, cookie session, CSRF header on writes
   ▼
GuidantShare.Api  (ASP.NET Core, .NET 10, minimal APIs)
   ├─ Identity + cookie auth, roles (User, SuperAdmin)
   ├─ Catalog endpoints, upload endpoint, admin endpoints
   ├─ Rate limiting, security headers, CSRF middleware
   ├─ SQLite via EF Core  ──►  data/app.db
   └─ ShareFileService ─────►  ShareFile REST v3 (OAuth refresh token)
                              Microsoft Graph (optional, for invite email)
```

| Project | Responsibility |
|---|---|
| `src/GuidantShare.Api` | Web host, endpoints, auth setup, invites, email senders, startup checks, CLI |
| `src/GuidantShare.Core` | Entities, interfaces (`IShareFileService`, `ISecretStore`, `IEmailSender`), AES-GCM cipher |
| `src/GuidantShare.Data` | EF Core `AppDbContext`, identity user, encrypted `SecretStore` |
| `src/GuidantShare.ShareFile` | ShareFile REST client with OAuth, retries and the two-step upload |
| `src/GuidantShare.Web` | React + Vite single page app, built into `Api/wwwroot` |
| `tests/GuidantShare.Tests` | Unit, integration and system tests |

## Request flow (a typical download listing)

1. The browser calls `GET /api/entries/{id}/files`.
2. Cookie auth identifies the user; the endpoint looks up the catalog entry in SQLite.
3. The entry's stored ShareFile folder id is used, never a name. The optional `folder` parameter must be the entry's root or a folder previously seen by listing it (see "Folder access" below).
4. `ShareFileService` fetches the children (cached for 30 seconds per folder, `refresh=true` bypasses the cache), using a short-lived access token it refreshes from the stored refresh token.
5. The API returns files (sorted newest first, paged) and subfolders, plus a link to open the folder in ShareFile.

## Upload flow

1. The browser sends a multipart POST to `/api/entries/{id}/uploads` with the CSRF header.
2. The API validates the entry (must be an Upload entry), target folder, file name, size and allowed extensions, then checks for a same-named file.
3. If the name exists and `overwrite` is not set, it answers `409 exists` and records a `Conflict` audit.
4. Otherwise `ShareFileService` negotiates with `Items(folder)/Upload?raw=true`, then posts the raw bytes to the returned `ChunkUri`, then finds the new item by name.
5. An `UploadAudit` row records who, what, when, the size, the ShareFile item and the outcome (`Succeeded`, `Failed`, `Conflict`).

## Folder access

Every catalog entry has a root ShareFile folder. Users may open that folder and any subfolder reachable from it, but only by id values the server has seen while listing. The server remembers `entryId:folderId` pairs in memory; a folder id that was never listed for that entry is refused with `400 That folder isn't part of this plan.` This prevents guessing ids to reach unrelated folders.

## Design decisions

- **Catalog in the database.** Plans and folders are edited in Settings, not in a file. A new database starts empty.
- **Links, not path lookups.** Each folder requires its ShareFile web link. The app reads the folder id from the link and verifies it with ShareFile when you save. There is no separate "resolve" step.
- **Soft deletes.** Removing a plan or folder sets `IsActive = false`. Rows remain so the upload history stays readable.
- **One connection, encrypted.** The ShareFile client secret and refresh token are encrypted with AES-256-GCM using a master key file, and stored in the `AppSecrets` table.
- **Single instance.** The folder cache, OAuth state and folder allow-list are in memory, so run one copy of the app.
- **No migrations yet.** The schema is created with `EnsureCreated`; a small startup step adds columns introduced later.
- **Independence from FileSync.** There is no code path that reads FileSync files, databases or secrets.

## Front end

The SPA uses React Router. Routes: `/login`, `/accept-invite`, `/forgot-password`, `/reset-password`, `/` (plans), `/plans/:planId` and `/plans/:planId/:direction` (folders), `/entries/:entryId` (files), `/admin/users` and `/admin/sharefile` (superadmin only). The API returns `401` when signed out and `403` for non-superadmins, so the UI never receives redirects.
