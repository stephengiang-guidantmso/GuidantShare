# Configuration

Settings come from, in increasing priority: `src/GuidantShare.Api/appsettings.json`, `appsettings.{Environment}.json`, `appsettings.Local.json` (gitignored, optional), environment variables, and command-line arguments. Environment variables use `__` for nesting, for example `Email__Mode=Graph`.

## Settings

| Setting | Default | Purpose |
|---|---|---|
| `Urls` | `http://localhost:5080` | Address(es) Kestrel listens on |
| `AllowedHosts` | `*` | Host header allow-list. Set to your hostname in production |
| `Upload:MaxBytes` | `524288000` (500 MB) | Upload size limit; a folder's own `MaxSizeBytes` overrides it. The request body limit is this plus 1 MB |
| `Data:Dir` | `./data` | Folder for the database, key and logs |
| `Data:KeyPath` | `{Data:Dir}/keys/master.key` | AES key that encrypts stored secrets |
| `Data:ConnectionString` | `Data Source={Data:Dir}/app.db` | SQLite connection |
| `Email:Mode` | `Log` | `Log` or `Graph` |
| `Email:PublicBaseUrl` | `http://localhost:5080` | Base of invite and reset links |
| `Email:TenantId`, `Email:ClientId`, `Email:ClientSecret`, `Email:Mailbox` | none | Microsoft Graph sending (mode `Graph`) |
| `Email:FromName` | `GuidantShare` | Display name |
| `Logging:*` | Information | Standard .NET logging; Serilog reads the same section |
| `ASPNETCORE_ENVIRONMENT` | `Production` unless set | `Development` relaxes the secure-cookie requirement and disables HSTS |

## Email modes

- **Log:** invite and reset emails are written to the log. Good for development and for installs where an admin shares links by hand.
- **Graph:** emails are sent with Microsoft Graph using an app registration (client credentials) that is allowed to send as `Email:Mailbox`. A startup check logs an error if the configuration is wrong, and `/health` reports `email: "misconfigured"`. Transient failures (throttling, 5xx) are retried up to three times with backoff; permanent failures (rejected message, mailbox not allowed to send) are recorded on the invite so an admin can see and resend.

## ShareFile settings are not in these files

The subdomain, client ID, client secret, redirect URI and refresh token are set in the app (Settings) and stored encrypted in the database. See [ShareFile integration](sharefile-integration.md).

## Fixed in code

| Behavior | Value | Where |
|---|---|---|
| Password rules | at least 8 characters, a small common-password blocklist, at least 5 distinct characters, must not contain the email name | `Program.cs`, `CommonPasswordValidator.cs` |
| Lockout | 5 failures, 15 minutes | `Program.cs` |
| Session | 8 hours, sliding | `Program.cs` |
| Invite lifetime | 72 hours | `InviteService.cs` |
| Folder listing cache | 30 seconds | `Endpoints.cs` |
| Rate limits | auth 20/min per address; uploads 120/min per user | `Program.cs` |
| ShareFile HTTP timeout | 10 minutes | `ShareFileService.cs` |
| Default page size | 25 (1 to 100) | `Endpoints.cs` |

## Example `appsettings.Local.json`

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
