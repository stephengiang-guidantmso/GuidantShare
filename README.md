# GuidantShare

Invite-only web app on top of ShareFile for health-plan files. Users sign in, pick a health plan, and download or upload files in the ShareFile folders for that plan. The app is independent of FileSync: it only talks to ShareFile and never reads FileSync's database, config or secrets.

- **API:** .NET 10 (`src/GuidantShare.Api`, plus `Core`, `Data`, `ShareFile` libraries), SQLite
- **UI:** React + Vite (`src/GuidantShare.Web`), built into `src/GuidantShare.Api/wwwroot`
- **Tests:** `tests/GuidantShare.Tests`

## Documentation

Full documentation is in [`docs/`](docs/README.md): user and admin guides, the catalog, configuration, security, deployment, operations, troubleshooting, testing, development, the API reference and the data model.

## Run it

```bash
scripts/system.sh --start      # build the API and start it on http://localhost:5080
scripts/system.sh --stop
scripts/system.sh --restart    # stop, rebuild, start
scripts/system.sh --help
```

The script writes its pid and output to `.run/` (gitignored). Set `GUIDANTSHARE_PORT` if the app listens on a different port.

Build the UI first, and again after changing it (`wwwroot` is not committed):

```bash
cd src/GuidantShare.Web && npm install && npm run build
```

Dotnet is expected on the `PATH` or in `~/.dotnet`.

**Use one user for building and running.** Build output (`obj/`, `bin/`) and `data/` must be owned by the account that runs the app. If two accounts share the folder, builds fail with "access denied" on timestamps and the app can't read `data/keys/master.key`.

## Tests

```bash
dotnet test                                      # everything (about 100 tests, ~20 s)
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration
dotnet test --filter Category=System
```

All three suites run without touching ShareFile, email or your real data (each test host uses a temporary folder).

| Suite | What it covers | What is mocked |
|---|---|---|
| **Unit** | Link parsing, entry validation and defaults, `ShareFileService` (token, items, two-step upload), encryption, invite tokens, email status mapping | ShareFile's HTTP API and the secret store |
| **Integration** | The real app hosted in-process (routing, auth, roles, CSRF, SQLite): sign-in, invites, catalog admin, browsing, subfolders, uploads and their failures | `IShareFileService` is replaced by an in-memory fake; email is captured |
| **System** | A full journey over HTTP: configure and connect ShareFile, build the catalog from links, invite and sign in a user, browse, upload, remove a plan, restart. Also checks secrets are encrypted at rest and `scripts/system.sh --help` | Only the outside world: a fake ShareFile REST API. The real `ShareFileService` runs against it |

Test hosts read their settings from environment variables, so the tests run one at a time. Shared fakes live in `tests/GuidantShare.Tests/Support`.

## First-time setup

1. **Create a superadmin** (prints a one-time set-password link if you omit `--password`):
   ```bash
   dotnet run --project src/GuidantShare.Api -- create-superadmin --email you@example.com
   ```
2. **Connect ShareFile** (Settings, superadmins only): save the subdomain, client ID and client secret (stored encrypted), click **Connect to ShareFile** and sign in as the owner account. The connection is stored on the server and shared by everyone. The redirect URI registered on the ShareFile app must match the one saved here: `<host>/api/sharefile/callback`, or the `oauthcomplete.aspx` URI plus the paste-the-code box.
3. **Add health plans and folders** (Settings, section 3). See below.
4. **Invite users** from the Users page.

## Health plans and folders

The catalog lives in the database (tables `HealthPlans` and `CatalogEntries`) and is managed in **Settings → Health plans and folders**. There is no seed file.

- A **health plan** is one card on the home page. You can add, rename and remove plans.
- A plan has any number of **folders**. Each has a direction (Download = partner ➜ ShareFile, Upload = ShareFile ➜ partner), a label, how the partner is reached (SFTP host and port, or a web portal), the remote path, and the login username.
- **The ShareFile folder link is required.** Paste the folder's address from ShareFile (it contains an id starting with `fo`). It is checked against ShareFile when you save, so ShareFile must be connected first and the connected account must be able to open the folder.
- **Defaults:** a blank label becomes the plan name. A blank "Shown to users as" becomes `Shared Folders/SFTP Drop {plan}` for downloads and `Shared Folders/SFTP Uploads/{plan}` for uploads.
- Several accounts at one partner (for example two logins at the same SFTP server) are separate folders inside the same plan. If they point at the same ShareFile folder, users see it once.
- **Passwords are never entered, stored or shown.** Only login names are kept.
- Removing a plan or folder hides it in the app. Nothing is deleted in ShareFile, and upload history stays readable.

## Configuration

Set in `src/GuidantShare.Api/appsettings.json`, `appsettings.Local.json` (gitignored) or environment variables (`Email__Mode`, and so on).

| Setting | Default | Notes |
|---|---|---|
| `Urls` | `http://localhost:5080` | Listen address |
| `AllowedHosts` | `*` | Set to your hostname in production |
| `Email:Mode` | `Log` | `Log` writes invite links to the log. Use `Graph` with `Email:TenantId`, `ClientId`, `ClientSecret`, `Mailbox` to send real email |
| `Email:PublicBaseUrl` | `http://localhost:5080` | Used in invite and reset links; set to the public URL |
| `Upload:MaxBytes` | 500 MB | Maximum upload size |
| `Data:Dir` | `./data` | Database, encryption key and logs |
| `Data:KeyPath` | `data/keys/master.key` | Encrypts the stored ShareFile secrets. Back it up; without it the ShareFile settings must be re-entered |
| `Data:ConnectionString` | SQLite file in `Data:Dir` | |

## Production checklist

- Set `Urls`, `AllowedHosts`, `Email:Mode=Graph` and `Email:PublicBaseUrl` as above, and `ASPNETCORE_ENVIRONMENT=Production` (cookies are then `Secure` and HSTS is on).
- Serve over HTTPS. Behind a reverse proxy, configure forwarded headers so the login rate limit sees real client addresses (it is currently keyed on the connection's remote IP).
- Run a **single instance**: the folder cache, OAuth state and subfolder allow-list are in memory.
- Persist and back up `data/` (`app.db` and `keys/master.key`, separately).
- Build the UI in the deploy step.
- A new database has no plans: connect ShareFile, then add the plans and folders in Settings.

## Notes

- The schema is created with `EnsureCreated` (no migrations yet).
- Only superadmins see Settings and Users.
- The Content-Security-Policy allows Google Fonts; remove it if the server has no internet access.
