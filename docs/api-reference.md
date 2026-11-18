# API reference

Base path `/api` (plus `/health`). JSON in and out. Errors are `{ "error": "message" }` and sometimes a `code`.

**Auth:** a cookie session (`sfw.auth`). Signed out gives `401`; a signed-in non-superadmin calling an admin route gives `403`.

**CSRF:** every non-GET call to `/api/...` must send the header `X-CSRF-TOKEN` with the value from `GET /api/auth/csrf`. Missing or wrong gives `400 Invalid CSRF token`. Fetch a fresh token after signing in or out.

**Rate limits:** auth routes 20 per minute per client address; uploads 120 per minute per user. Over the limit gives `429`.

Legend: 🌐 public, 🔑 any signed-in user, 👑 superadmin.

## Health

| | Route | Returns |
|---|---|---|
| 🌐 | `GET /health` | `{ status, email, emailMode, sharefile }` where `sharefile` is `connected`, `disconnected` or `not_configured` |

## Authentication

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

## Users and invites (👑)

| Route | Body | Notes |
|---|---|---|
| `GET /api/users` | | `{ users: [...], invites: [...] }` (pending invites only) |
| `PATCH /api/users/{id}` | `{ isActive?, role? }` | Role is `User` or `SuperAdmin`. You cannot deactivate or demote yourself |
| `POST /api/users/{id}/reset-password` | | `{ ok, error, resetLink }` |
| `POST /api/invites` | `{ email, role, emailOnly?, noEmail? }` | `emailOnly: true` hides the link in the response. `noEmail: true` with `emailOnly: false` creates the invite without sending and returns `inviteLink` |
| `POST /api/invites/{id}/resend` | | New token, one per minute, up to 20 attempts |
| `POST /api/invites/{id}/revoke` | | |

Invite response: `{ id, email, emailStatus, emailError, inviteLink }`.

## ShareFile connection (👑)

| Route | Body | Notes |
|---|---|---|
| `GET /api/sharefile/status` | | `{ configured, connected, subdomain, connectedBy, connectedUtc, error, redirectUri, clientId }` |
| `POST /api/sharefile/settings` | `{ subdomain, clientId, clientSecret?, redirectUri? }` | Blank secret keeps the saved one |
| `POST /api/sharefile/connect` | | `{ url }` to open in a new tab |
| `GET /api/sharefile/callback` | `?code&state` | Landing page when the redirect URI points at this app |
| `POST /api/sharefile/exchange` | `{ code }` | Accepts the bare code or the full address containing `code=` |
| `POST /api/sharefile/disconnect` | | |

## Catalog administration (👑)

| Route | Body | Notes |
|---|---|---|
| `GET /api/catalog` | | `[ { planId, name, entries: [ { entryId, label, direction, remoteKind, remoteHost, remotePort, remotePath, portalName, remoteNote, remote, username, shareFilePath, shareFileLink } ] } ]` |
| `POST /api/catalog/plans` | `{ name }` | `{ planId }`; `400` for blank, too long or duplicate |
| `PATCH /api/catalog/plans/{id}` | `{ name }` | |
| `DELETE /api/catalog/plans/{id}` | | Soft delete, including its entries |
| `POST /api/catalog/entries` | see below | `{ entryId }` |
| `PUT /api/catalog/entries/{id}` | see below | Re-validates the link |
| `DELETE /api/catalog/entries/{id}` | | Soft delete |

Entry body:

```json
{
  "planId": 3,
  "direction": "Download",
  "label": null,
  "shareFileLink": "https://acme.sharefile.com/home/shared/fo0ea564-6572-49c0-9f79-416c34cea03c",
  "shareFilePath": null,
  "remoteKind": "Sftp",
  "remoteHost": "sftp.example.com",
  "remotePort": 22,
  "remotePath": "/Outgoing",
  "portalName": null,
  "remoteNote": null,
  "username": "svc-user"
}
```

Validation errors (`400`): missing link, link without a folder id, folder not found, link to a file, ShareFile unreachable or disconnected, bad direction or kind, missing host (SFTP) or portal name (Portal), port outside 1 to 65535, label over 100 characters, username over 200 characters.

## Plans, files and uploads (🔑)

| Route | Notes |
|---|---|
| `GET /api/plans` | `[ { id, name, hasDownload, hasUpload } ]` in order, active plans only |
| `GET /api/plans/{id}/entries?direction=Download\|Upload` | Folders of the plan; entries pointing at the same ShareFile folder are shown once. `400` for a bad direction |
| `GET /api/entries/{id}` | `{ entry, plan }` or `404` |
| `GET /api/entries/{id}/files` | Query: `folder`, `search`, `page`, `pageSize` (1 to 100, default 25), `refresh` |
| `GET /api/entries/{id}/exists?name=&folder=` | `{ exists }` for a same-named file (case-insensitive) |
| `POST /api/entries/{id}/uploads` | Multipart form: `file`, optional `folder`, optional `overwrite=true` |
| `GET /api/uploads/{id}` | Your own receipt (superadmins can read anyone's): `{ auditId, fileName, status, sharefileLink, error, createdUtc }` |

Files response:

```json
{
  "total": 12, "page": 1, "pageSize": 25, "refreshedUtc": "...",
  "isRoot": true,
  "folderLink": "https://acme.sharefile.com/home/shared/fo...",
  "location": { "shareFilePath": "Shared Folders/SFTP Drop CAQH", "remote": "sftp.example.com:22 /Outgoing" },
  "folders": [ { "id": "fo...", "name": "2026" } ],
  "items": [ { "id": "fi...", "name": "roster.csv", "size": 2048, "modifiedUtc": "..." } ]
}
```

### Status codes you may see

| Code | Meaning |
|---|---|
| 400 | Validation failure, CSRF failure, or a folder that is not part of the entry |
| 401 / 403 | Signed out / not a superadmin |
| 404 | Unknown or removed plan, entry or receipt |
| 409 | Upload name already exists (`code: "exists"`) |
| 429 | Rate limit |
| 502 | ShareFile could not be reached or failed |
| 503 | ShareFile disconnected (`code: "disconnected"`) |
