# GuidantShare

Invite-only web app on top of ShareFile for health-plan files. Users sign in, pick a health plan, and download or upload files in the ShareFile folders for that plan. Superadmins also get the **FileSync** screens (jobs, transfers, retries, logs), **Users** and **Settings**.

GuidantShare works together with **FileSync**, a separate private service on the same machine that syncs the health plans' SFTP servers and portals with ShareFile. The health plans and folders users see are built live from FileSync's jobs (nothing is entered twice). FileSync is never exposed to the internet: only the reverse proxy on 443 is public.

- **API:** .NET 10 (`src/GuidantShare.Api`, plus `Core`, `Data`, `ShareFile` libraries), SQLite. Handles sign-in, invites, ShareFile browsing and uploads, and a SuperAdmin-only proxy to FileSync.
- **UI:** React + Vite (`src/GuidantShare.Web`). The FileSync screens live in `src/filesync` and are only loaded for superadmins.
- **Tests:** `tests/GuidantShare.Tests`

## Documentation

Full documentation is in [`docs/`](docs/README.md): user and admin guides, how plans and folders work, configuration, security, deployment, operations, troubleshooting, testing, local testing, development, the API reference and the data model.

## Run it (development)

See [Testing locally](docs/development.md#testing-locally-on-a-development-machine-wsl) for the full setup with mock data (FileSync with mock connectors, an offline ShareFile, sample jobs). The short version:

```bash
scripts/system.sh --start      # build the API and start it on http://localhost:5080
scripts/system.sh --stop
scripts/system.sh --restart    # stop, rebuild, start
scripts/system.sh --help
```

The script finds the app by its port (no pid files) and writes console output to `src/GuidantShare.Api/data/logs/console.out`. Set `GUIDANTSHARE_PORT` if the app listens on a different port.

The API needs FileSync's address and key to list plans: `FileSync__BaseUrl` (default `http://localhost:5000`) and `FileSync__ApiKey` (the same value as FileSync's `Api__Key`). Without a reachable FileSync there are no plans to show.

Build the UI for the dev API to serve (`wwwroot` is not committed), or use the Vite dev server:

```bash
cd src/GuidantShare.Web && npm install && npm run build     # or: npm run dev  (port 5173, proxies /api to 5080)
```

Dotnet is expected on the `PATH` or in `~/.dotnet`.

**Use one user for building and running.** Build output (`obj/`, `bin/`) and `data/` must be owned by the account that runs the app. If two accounts share the folder, builds fail with "access denied" on timestamps and the app can't read `data/keys/master.key`.

## Tests

```bash
dotnet test                                      # everything
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration
dotnet test --filter Category=System
```

All three suites run without touching ShareFile, FileSync, email or your real data (each test host uses a temporary folder).

| Suite | What it covers | What is mocked |
|---|---|---|
| **Unit** | FileSync client (cache, stale copy), proxy allow-list, offline ShareFile mock, `ShareFileService` (token, items, two-step upload), encryption, invite tokens, email status mapping | ShareFile's HTTP API, the secret store, FileSync |
| **Integration** | The real app hosted in-process (routing, auth, roles, CSRF, SQLite): sign-in, invites, user admin, the FileSync proxy, browsing, subfolders, uploads and their failures | `IShareFileService` and `IFileSyncClient` are replaced by in-memory fakes; email is captured |
| **System** | A full journey over HTTP: configure and connect ShareFile, FileSync supplies the plans, invite and sign in a user, browse, upload, the plan disappears, restart. Also checks secrets are encrypted at rest and `scripts/system.sh --help` | Only the outside world: a fake ShareFile REST API and a fake FileSync. The real `ShareFileService` runs against it |

Test hosts read their settings from environment variables, so the tests run one at a time. Shared fakes live in `tests/GuidantShare.Tests/Support`.

## First-time setup

1. **Create a superadmin** (prints a one-time set-password link if you omit `--password`):
   ```bash
   dotnet run --project src/GuidantShare.Api -- create-superadmin --email you@example.com
   ```
2. **Connect ShareFile** (Settings, superadmins only): save the subdomain, client ID and client secret (stored encrypted), click **Connect to ShareFile** and sign in as the owner account. This is GuidantShare's own connection; FileSync has a separate one. The redirect URI registered on the ShareFile app must match the one saved here: `<host>/api/sharefile/callback`, or the `oauthcomplete.aspx` URI plus the paste-the-code box.
3. **Set up the plans in FileSync:** on each job set *Website Health Plan* and *Website Folder Name*, and enable it. See [Plans and folders](docs/admin-guide.md#plans-and-folders).
4. **Invite users** from the Users page.

## Plans and folders

They come from FileSync's enabled jobs. Jobs with the same *Website Health Plan* are one plan; jobs with the same plan, direction and *Website Folder Name* are one folder, however many logins feed them. So "Clever Care 1" and "Clever Care 2" (each with 278 and UM) show as one **Clever Care** with one 278 and one UM. Hosts, logins and passwords never reach the website. Details and examples: [Plans and folders](docs/admin-guide.md#plans-and-folders).

## Configuration

Set in `src/GuidantShare.Api/appsettings.json`, `appsettings.Production.json`, `appsettings.Local.json` (gitignored) or environment variables (`Email__Mode`, `FileSync__ApiKey`, and so on).

| Setting | Default | Notes |
|---|---|---|
| `Urls` | `http://localhost:5080` | Listen address. Production: `http://127.0.0.1:5080` so only the proxy reaches it |
| `AllowedHosts` | `*` | Production: `guidantshare.guidantmso.com` |
| `FileSync:BaseUrl` | `http://localhost:5000` | FileSync's private API |
| `FileSync:ApiKey` | none | Same as FileSync's `Api:Key`. Use an environment variable |
| `FileSync:CacheSeconds` | 45 | How long the plan list is reused |
| `Email:Mode` | `Log` | `Log` writes invite links to the log. Use `Graph` with `Email:TenantId`, `ClientId`, `ClientSecret`, `Mailbox` to send real email |
| `Email:PublicBaseUrl` | `http://localhost:5080` | Used in invite and reset links; set to the public URL |
| `Upload:MaxBytes` | 500 MB | Maximum upload size |
| `Upload:AllowedExtensions` | none | Optional list, for example `csv,txt` |
| `ShareFile:Mode` | none | `Mock` = offline ShareFile for development only |
| `Data:Dir` | `./data` | Database, encryption key and logs |
| `Data:KeyPath` | `data/keys/master.key` | Encrypts the stored ShareFile secrets. Back it up; without it the ShareFile settings must be re-entered |
| `Data:ConnectionString` | SQLite file in `Data:Dir` | |

## Production

See [Deployment](docs/operations.md#deployment) and the files in `deploy/`: a Caddy reverse proxy serves the web files and forwards `/api/*` to this API (a Windows service on `127.0.0.1:5080`), which talks to FileSync on localhost.

- Set `ASPNETCORE_ENVIRONMENT=Production` (cookies are then `Secure` and HSTS is on); `appsettings.Production.json` binds to loopback and sets the host name.
- Forwarded headers are trusted from the loopback proxy, so the login rate limit sees real client addresses.
- Run a **single instance**: the folder cache, OAuth state, plan list copy and subfolder allow-list are in memory.
- Persist and back up `data/` (`app.db` and `keys/master.key`, separately).
- Going live from scratch (new database, jobs carried over from the old FileSync): see the [go-live guide](docs/operations.md#go-live-from-scratch-clean-install-on-the-sftp-server-vm).

## Notes

- The schema is created with `EnsureCreated` (no EF migrations yet), so a schema change means a new database or a hand-written `ALTER TABLE`.
- Only superadmins see FileSync, Users and Settings.
- The Content-Security-Policy allows Google Fonts; remove it if the server has no internet access.
