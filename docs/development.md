# Development and testing

## Development

### Repository layout

```
GuidantShare.sln
README.md
docs/                       this documentation
scripts/system.sh           start, stop and restart the API
src/
  GuidantShare.Api/         web host: Program.cs, Endpoints.cs, Services/, StartupChecks.cs, Cli.cs
  GuidantShare.Core/        Entities.cs, Services.cs (interfaces and the AES-GCM cipher)
  GuidantShare.Data/        AppDbContext, SecretStore
  GuidantShare.ShareFile/   ShareFileService (REST v3 client)
  GuidantShare.Web/         React + Vite app (src/pages/*; src/filesync/* is the FileSync UI for superadmins)
tests/GuidantShare.Tests/   Unit, Integration, System, Support
```

Runtime data (`data/`) and build output are gitignored.

### Working on the API

```bash
scripts/system.sh --restart          # rebuild and restart after a change
dotnet test                          # run everything
```

Key places:

| To change... | Look in |
|---|---|
| An endpoint or validation | `src/GuidantShare.Api/Endpoints.cs` |
| Reading plans from FileSync, caching, stale handling | `src/GuidantShare.Api/Services/FileSyncClient.cs` |
| Which FileSync calls the proxy allows | `src/GuidantShare.Api/Services/FileSyncProxyRules.cs` |
| Auth, cookies, password rules, rate limits, security headers | `src/GuidantShare.Api/Program.cs` |
| Invites | `src/GuidantShare.Api/Services/InviteService.cs` |
| Email sending | `src/GuidantShare.Api/Services/Email.cs` |
| Talking to ShareFile | `src/GuidantShare.ShareFile/ShareFileService.cs` |
| Table shape | `src/GuidantShare.Core/Entities.cs`, `src/GuidantShare.Data/AppDbContext.cs` |
| Startup self-checks | `src/GuidantShare.Api/StartupChecks.cs` |

The API uses minimal-API endpoints grouped as public (`api`), signed-in (`auth`) and superadmin (`admin`). New routes join the group that matches who may call them; the group applies the authorization policy.

### Working on the UI

```bash
cd src/GuidantShare.Web
npm install
npm run dev            # Vite dev server; /api and /health are proxied to http://localhost:5080
npm run build          # type-checks (tsc) and writes to dist/ (production builds pass --outDir)
npm run lint
```

Start the API first (`scripts/system.sh --start`) so the dev server's proxy has something to talk to. Pages live in `src/pages`; `src/api.ts` is the only place that talks to the API. It adds the CSRF header to every write and turns error bodies into `ApiError`. Styles are in `src/index.css` (plain CSS with design tokens).

Admin pages are guarded in `App.tsx`; the server enforces the same rules, so the UI guard is for convenience only.

### Conventions

- **Passwords never enter the system for partners.** Do not add fields that hold SFTP or portal passwords. Login names are fine.
- **Settings are not in git.** Local overrides go in `appsettings.Local.json` or environment variables.
- **Errors are user-readable.** Endpoints return `{ error: "..." }` with a message a user could act on. Log details server-side.
- **FileSync is the source of plans.** Do not add a second catalog. Go through `IFileSyncClient`; never read FileSync's files, database or configuration.
- **The proxy is an allow-list.** A new FileSync call for the admin screens must be added to `FileSyncProxyRules` and its test. Never allow `credentials`, `debug` or `swagger`.
- **Comments say why**, not what.

### Common changes

#### Add a field to a folder on the website

The fields come from FileSync. Add it to `CatalogFolder` and `CatalogBuilder` in FileSync (`src/FileSync.Core`), then to `CatalogFolderDto` (`Core/FileSyncCatalog.cs`) and the folder response in `Endpoints.cs` here. Never add connection details or secrets.

#### Add an endpoint

Add it in the right group in `MapEndpoints`, return `{ error }` for failures, add an integration test using `ApiClient`, and update the [API reference](reference.md#api-reference).

#### Change how jobs are grouped

The rule lives in FileSync's `CatalogBuilder`. Change it there with its unit tests, and update [Plans and folders](admin-guide.md#plans-and-folders).

#### Run without real FileSync or ShareFile data

Set `ShareFile:Mode=Mock` (in-memory ShareFile, development only) and point `FileSync:BaseUrl` / `FileSync:ApiKey` at a FileSync started with `UseMockConnectors=true`. `scripts/dev-seed.sh` creates sample jobs (Clever Care 1 and 2, Aetna, Champion, a disabled one).

#### Change the schema

There are no EF migrations: `EnsureCreated` only creates a missing database. A change to an existing table needs a guarded `ALTER TABLE` (check `pragma_table_info`, then add the column) at startup in `Program.cs`, or the switch to EF Core migrations (generate an initial migration that matches the current model, replace `EnsureCreated` with `Migrate`). Test against a copy of a real database first.

### Pull request checklist

- [ ] `dotnet test` passes
- [ ] `npm run build` passes (type-check)
- [ ] No secrets, databases, logs or spreadsheets staged (`git status`)
- [ ] Docs updated if behavior, settings or endpoints changed
- [ ] New behavior has a test at the right level

### Known gaps

- The UI has no automated tests.
- The per-folder upload limits of the old catalog (`AllowedExtensions`, `MaxSizeBytes`) are gone; only the global `Upload:MaxBytes` and `Upload:AllowedExtensions` apply.
- The copied FileSync screens are plain JavaScript (`src/filesync/*.jsx`), not type-checked.

## Testing

```bash
dotnet test                                   # all suites (about 100 tests, ~20 s)
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration
dotnet test --filter Category=System
```

Nothing touches real ShareFile, email or your data. Each hosted test app uses its own temporary data folder (database, key and logs) and deletes it afterwards.

### The three suites

#### Unit (`tests/GuidantShare.Tests/Unit`, plus `CoreTests.cs`)

No web host. Fast and exact.

- **FileSync client:** the catalog is parsed, the API key is sent, results are cached, the last good copy is served as stale when FileSync fails, and a retry is not hammered.
- **Proxy rules:** every allowed FileSync call and a long list of refused ones (`credentials`, `debug`, dot segments, encoded characters, wrong methods).
- **ShareFile mock:** the offline development stand-in lists sample files and a subfolder for any folder id, keeps uploads in memory and can be disconnected.
- **ShareFileService:** configure ➜ connect ➜ disconnect status, bad codes, the client secret being kept when settings are saved blank, listing, missing folders, the two-step upload and web links. It runs the real service against a fake HTTP handler and an in-memory secret store.
- **Core:** encryption round trip and tamper detection, file name sanitizing, invite token hashing, Microsoft Graph status mapping.

#### Integration (`Integration/`)

The real application hosted in-process with `WebApplicationFactory`: real routing, authentication, roles, CSRF and a real SQLite database. **ShareFile is replaced by an in-memory fake** (`FakeShareFile`) and email is captured.

- **Auth:** protected routes, sign-in, role checks, CSRF, invite lifecycle (single use, password rules, duplicates), deactivation.
- **FileSync proxy:** 401/403/allowed, refused calls never reach FileSync, CSRF, audit rows for changes only, body and downloads pass through, 502 when FileSync is down.
- **Users:** admin sets a password, removes a user (not themselves), history is kept.
- **Browse:** plans and flags from the FileSync catalog, folders listed once, listing order and sizes, subfolders at any depth (including the parent-folder case), folders outside the plan folder, search, paging, disconnected (503), ShareFile outage (502), FileSync down (stale list) and never-loaded (503).
- **Upload:** success and audit (plan and folder recorded), name sanitizing, conflicts and overwrite, direction checks, subfolder uploads, failures, receipts.

#### System (`System/`)

End to end over HTTP with the real `ShareFileService` talking to a **fake ShareFile REST API** (`FakeShareFileHttp`). Only the outside world is mocked.

- The full journey: fresh install ➜ create superadmin ➜ save settings ➜ connect (bad code first) ➜ FileSync's catalog supplies the plans ➜ invite and sign in a user ➜ browse and open a subfolder ➜ upload, hit a conflict, overwrite ➜ the plan is disabled in FileSync and disappears.
- Secrets are not present in plain text in the database files.
- Users and the ShareFile connection survive an app restart, and the catalog comes from FileSync again.
- `scripts/system.sh`: `--help` lists every option, no option or an unknown one fails with usage, the script is valid bash. It never starts or stops the app.

### Support code (`tests/GuidantShare.Tests/Support`)

| File | Purpose |
|---|---|
| `TestApp.cs` | Hosts the app with a temp data folder; helpers to add users, seed the fake FileSync catalog (`Seed`), query the database; `ApiClient` is a cookie-keeping client that fetches a CSRF token for every write |
| `FakeFileSync.cs` | In-memory `IFileSyncClient`: a catalog the test builds, a `Down` switch, recorded proxy calls and a response `Handler` |
| `FakeShareFile.cs` | In-memory `IShareFileService` with folders, files, upload recording, and switches (`Disconnected`, `FailUploads`, `ListError`) |
| `FakeShareFileHttp.cs` | A fake ShareFile REST API (token, Items, Children, Upload and the chunk upload) for the system tests |
| `AssemblyInfo.cs` | Turns off parallel test execution |

### Writing tests

- Pick the lowest level that can prove the behavior. A validation rule belongs in Unit, an endpoint's status code in Integration, a multi-step journey in System.
- Tag with `[Trait("Category", "Unit" | "Integration" | "System")]`.
- Use unique ShareFile folder ids per test (`FakeShareFile.NewId()`). The app keeps a static 30-second folder cache and a folder allow-list, so reusing ids across tests can hide bugs; pass `refresh=true` when listing.
- Seed folders with `TestApp.Seed(plan, direction, folderId, label)`; it returns the folder's URL prefix (for example `/api/plans/aetna/folders/upload-main`).
- Create users with `AddUserAsync`, then sign in with `ApiClient.Login`.
- Add a new fake behavior to `FakeShareFile` (a switch) rather than a one-off mock.

### Why tests run one at a time

The hosted app reads its settings (data folder, key path, email mode) from process-wide environment variables, because `Program.cs` reads them before a test host can inject configuration. Running tests in parallel would let them overwrite each other's settings, so parallelization is disabled for the assembly.

### File ownership caution

If more than one user account works in the same checkout, run tests in a copy or as the account that owns `bin/` and `obj/`; otherwise .NET cannot update build timestamps. See [Troubleshooting](operations.md#troubleshooting).

### Not covered

- The React UI has no automated tests. It is checked by hand against the API behavior above.
- Real ShareFile and Microsoft Graph behavior is only covered through the fakes. Do a manual end-to-end check after changing the ShareFile client.

## Testing locally on a development machine (WSL)

Everything here uses mock data. It needs no partner servers and no real ShareFile or email. **Never copy the production database or keys to a development machine.**

What you cannot check locally (do these on the Windows VM): real partner SFTP/portal logins and the whitelisted IP, real ShareFile OAuth, Graph email, Caddy and TLS, the Windows services, and the firewall / network security group.

### 1. Set up (once per session)

Use three terminals. Ports: FileSync `5000`, GuidantShare.Api `5080`, web dev server `5173`. Pick any dev key; it is not a secret in development.

**FileSync** (in the FileSync repo). Development uses mock connectors, so SFTP and ShareFile are folders on disk:

```bash
export ASPNETCORE_ENVIRONMENT=Development
export Api__Key=dev-key
dotnet run --project src/FileSync.Service
```

**GuidantShare.Api** (in this repo):

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ShareFile__Mode=Mock          # in-memory ShareFile; refused in Production
export FileSync__ApiKey=dev-key      # same value as FileSync's Api__Key
export Email__Mode=Log               # invite links are printed in the log
dotnet run --project src/GuidantShare.Api -- create-superadmin --email admin@dev.local --password 'Dev-Admin-Passphrase-1'
dotnet run --project src/GuidantShare.Api
```

(Run the `create-superadmin` line once; it stops after creating the account. Then start the app.)

**Web**:

```bash
cd src/GuidantShare.Web && npm install && npm run dev      # http://localhost:5173, /api is proxied to :5080
```

**Sample data** (once FileSync is running):

```bash
FILESYNC_API_KEY=dev-key scripts/dev-seed.sh               # add --with-conflict for the conflict case
```

For a normal user: sign in as the admin, open **Users**, invite `user@dev.local`, copy the link from the invite message (or the log), open it and set a password.

### 2. Automated tests first

```bash
dotnet test                       # in this repo
dotnet test                       # in the FileSync repo (tests/automated/unit)
cd src/GuidantShare.Web && npm run build && npm run lint
```

### 3. Manual checklist (browser at http://localhost:5173)

1. **Signed out.** Any page redirects to the login. `curl -i localhost:5080/api/filesync/workflows` returns `401`.
2. **Normal user.** Health Plans shows **Aetna, Champion, Clever Care**, one card each (no "Hidden Plan"). Open Clever Care: **Download** has *Main*; **Upload** lists **278** and **UM** once each, not twice. No FileSync, Users or Settings buttons. Typing `/admin/users` or `/admin/filesync` sends you home. With the user's cookie, `/api/filesync/workflows` returns `403`.
3. **Upload.** Upload a file to Clever Care → Upload → 278: it appears in the list. A second upload of the same name asks to overwrite. Dropping files on a Download folder is not offered. An empty file is refused.
4. **Subfolders.** Open the *Archive* subfolder of a download folder, go back through the breadcrumb.
5. **Admin: Users.** Invite someone, accept the invite as them, deactivate and reactivate, **Change password** (the old password stops working), **Remove**. You cannot remove, demote or deactivate yourself.
6. **Admin: Settings.** Only the ShareFile connection (no health-plan section). In mock mode it shows connected.
7. **Admin: FileSync.** The jobs list loads. Edit a job: **Website Health Plan** and **Website Folder Name** are there, and the password field never shows a saved password (placeholder says one is saved). Change "Aetna" to a new plan name, save, reload Health Plans within a minute: it regroups. Create a second job with the same plan and folder name: still one folder. Disable a job: it disappears for users. Run, cancel and retry a mock job; the transfer ledger, logs, retry queue, SFTP explorer and Verify Sync (including the CSV link) work. The FileSync light/dark toggle only changes that page.
8. **Conflict warning.** `scripts/dev-seed.sh --with-conflict`: the FileSync page shows a yellow warning naming the jobs; users still see the UM folder.
9. **Security checks.**
   - `curl -i localhost:5000/api/workflows` → `401`; with `-H 'X-Api-Key: dev-key'` → `200`. `curl localhost:5000/api/health` → `200` (the only open call).
   - Through the proxy as admin (use the browser's dev tools, or a signed-in `curl`): `/api/filesync/workflows/<id>/credentials`, `/api/filesync/debug/state`, `/api/filesync/swagger` → `403`.
   - A `POST` to the proxy without the `X-CSRF-TOKEN` header → `400`.
   - Check `AdminAudits` has a row for each change you made (`sqlite3 -readonly src/GuidantShare.Api/data/app.db "select * from AdminAudits"`).
10. **FileSync down.** Stop FileSync. Reload Health Plans: the plans are still there with a "may be out of date" notice, ShareFile browsing of an open folder still works, and an admin sees the "FileSync can't be reached" banner. Restart GuidantShare.Api now (FileSync still stopped): users see a clear "unavailable" message. Start FileSync again and reload: it recovers.
11. **Restart.** Restart GuidantShare.Api while a mock job is running in FileSync: the job finishes (FileSync is a separate process).
