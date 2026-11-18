# Data model

SQLite database at `data/app.db`, created with `EnsureCreated`. Enums are stored as text. Identity tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, and so on) come from ASP.NET Core Identity.

## HealthPlans

| Column | Type | Notes |
|---|---|---|
| PlanId | integer PK | |
| Name | text | Unique among active plans (enforced by the API) |
| SortOrder | integer | Creation order |
| IsActive | boolean | False = removed |

## CatalogEntries

| Column | Type | Notes |
|---|---|---|
| EntryId | integer PK | |
| PlanId | integer FK | Cascade delete from HealthPlans |
| Direction | text | `Download` or `Upload` |
| Label | text | |
| SharefileFolderId | text | The ShareFile folder id, taken from the link |
| SharefileDisplayPath | text | Path text shown to users |
| RemoteKind | text | `Sftp` or `Portal` |
| RemoteHost, RemotePort | text, integer | SFTP only |
| RemotePath | text | Remote folder path |
| PortalName | text | Portal only |
| RemoteNote | text | |
| RemoteUsername | text | Login name only |
| AllowedExtensions | text | Comma separated, null = any |
| MaxSizeBytes | integer | Null = use `Upload:MaxBytes` |
| IsActive | boolean | False = removed |

## Invites

`InviteId`, `Email`, `Role`, `TokenHash` (SHA-256 of the token; the token itself is never stored), `InvitedByUserId`, `CreatedUtc`, `ExpiresUtc` (72 hours after creation or resend), `AcceptedUtc`, `RevokedUtc`, `EmailStatus` (`Pending`, `Sent`, `Failed`), `EmailAttempts`, `LastEmailError`, `LastEmailAttemptUtc`.

## UploadAudits

One row per upload attempt.

| Column | Notes |
|---|---|
| AuditId | Returned to the UI as `auditId` |
| UserId, EntryId | Who and which folder |
| FileName | The sanitized name; includes the subfolder (`2026/jan.csv`) when uploaded below the folder root |
| SizeBytes | |
| SharefileItemId, SharefileLink | Set on success |
| Status | `Started`, `Succeeded`, `Failed`, `Conflict` |
| Error | First 300 characters of the failure |
| CreatedUtc | |

## LoginAudits

`Id`, `UserId`, `Event` (`login`, `login_failed`), `Ip`, `CreatedUtc`.

## AppSecrets

Encrypted key/value store (`Key`, `Value` blob, `UpdatedUtc`). Values are AES-256-GCM blobs: 12-byte nonce, 16-byte tag, then ciphertext. Keys:

| Key | Holds |
|---|---|
| `sharefile.subdomain`, `sharefile.client_id`, `sharefile.redirect_uri` | App settings |
| `sharefile.client_secret` | ShareFile app secret |
| `sharefile.refresh_token` | The shared connection |
| `sharefile.connected_by`, `sharefile.connected_utc` | Who connected and when |

## Users

`AspNetUsers` plus `IsActive`, `CreatedUtc`, `LastLoginUtc`. Roles are `User` and `SuperAdmin`.

## In-memory state (not in the database)

- Folder listing cache, 30 seconds per folder.
- OAuth `state` values for the connect flow, 10 minutes each.
- The set of folders reachable from each entry (see [Architecture](architecture.md)).

These reset on restart, which is why the app must run as a single instance.
