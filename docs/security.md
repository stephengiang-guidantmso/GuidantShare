# Security

## Summary of controls

| Area | Control |
|---|---|
| Sign-up | Invite only. Tokens are 256-bit random, stored only as SHA-256 hashes, expire after 72 hours and work once |
| Sessions | HttpOnly cookie `sfw.auth`, SameSite Lax, `Secure` outside Development, 8 hours sliding. The API answers `401`/`403` instead of redirecting |
| Passwords | Hashed by ASP.NET Core Identity. At least 8 characters, a blocklist of common passwords, at least 5 distinct characters, must not contain the email name |
| Brute force | Lockout after 5 failures for 15 minutes. Auth routes limited to 20 requests per minute per client address |
| CSRF | Every non-GET `/api` call needs the `X-CSRF-TOKEN` header (antiforgery cookie `sfw.csrf`, SameSite Strict) |
| Roles | `User` and `SuperAdmin`. Settings, Users, invites and catalog administration require `SuperAdmin` |
| Account enumeration | Wrong password and unknown user return the same message. Forgot-password always answers `200` |
| Secrets at rest | ShareFile client secret and refresh token are AES-256-GCM encrypted with `data/keys/master.key` |
| Partner passwords | **Never accepted, stored or displayed.** Only login names are kept |
| Browser headers | `Content-Security-Policy` (self, plus Google Fonts), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, HSTS outside Development |
| Folder access | Users reach only a catalog entry's root folder and folders listed beneath it. Folder ids are never taken on trust |
| Uploads | Name sanitized (no path parts or control characters, up to 200 characters), empty files refused, size and extension limits, rate limit 120/min per user, every attempt audited |
| Auditing | `LoginAudits` (logins and failures with address) and `UploadAudits` (every upload attempt) |

## The master key

`data/keys/master.key` is a 32-byte key created on first start. It decrypts the stored ShareFile credentials.

- Keep it out of source control and out of backups that are less protected than the database itself.
- Back it up **separately** from `app.db`. Losing it means re-entering the ShareFile settings and reconnecting; leaking it together with the database exposes the ShareFile client secret and refresh token.
- Restrict its file permissions to the account that runs the app.

## ShareFile credentials

The app uses its own OAuth app and stores a refresh token for the owner account. Anyone who can use the app can reach every folder in the catalog through that account, so:

- Limit the ShareFile account's own permissions to what the catalog needs.
- Treat superadmins as holders of that access.
- Disconnect in Settings if the account may be compromised, and rotate the client secret in ShareFile.

## Things to configure for production

- Serve over HTTPS only and set `ASPNETCORE_ENVIRONMENT=Production` (secure cookies, HSTS).
- Set `AllowedHosts` and `Email:PublicBaseUrl` to the real hostname.
- Behind a reverse proxy, configure forwarded headers. The app does not set them up, so without it the rate limit sees the proxy's address for every client.
- Use environment variables or a protected local file for the Graph client secret.
- Keep `data/` readable only by the service account.

## Known limitations

- The common-password list is small and built in. Consider a breached-password check.
- The CSP allows `fonts.googleapis.com` and `fonts.gstatic.com`. Remove it for networks without internet access (then host the font yourself).
- In-memory state means one instance only; multiple instances would break the folder allow-list and connect flow.
- No multi-factor authentication.
- The build reports an advisory on `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (GHSA-2m69-gcr7-jv3q); check for an updated package before release.
- Uploads are buffered by ASP.NET form handling up to the configured size; very large limits cost memory or disk.

## Reporting and response

If you suspect credentials are exposed: disconnect ShareFile in Settings, rotate the ShareFile client secret, rotate partner passwords in FileSync (not here), deactivate affected users, and review `LoginAudits` and `UploadAudits`.
