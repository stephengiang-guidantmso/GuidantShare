# Testing

```bash
dotnet test                                   # all suites (about 100 tests, ~20 s)
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration
dotnet test --filter Category=System
```

Nothing touches real ShareFile, email or your data. Each hosted test app uses its own temporary data folder (database, key and logs) and deletes it afterwards.

## The three suites

### Unit (`tests/GuidantShare.Tests/Unit`, plus `CoreTests.cs`)

No web host. Fast and exact.

- **Catalog rules:** link parsing, every validation message, the label and display-path defaults, SFTP versus portal handling, port and length limits, and that a rejected entry is left unchanged.
- **ShareFileService:** configure ➜ connect ➜ disconnect status, bad codes, the client secret being kept when settings are saved blank, listing, missing folders, the two-step upload and web links. It runs the real service against a fake HTTP handler and an in-memory secret store.
- **Core:** encryption round trip and tamper detection, file name sanitizing, invite token hashing, Microsoft Graph status mapping.

### Integration (`Integration/`)

The real application hosted in-process with `WebApplicationFactory`: real routing, authentication, roles, CSRF and a real SQLite database. **ShareFile is replaced by an in-memory fake** (`FakeShareFile`) and email is captured.

- **Auth:** protected routes, sign-in, role checks, CSRF, invite lifecycle (single use, password rules, duplicates), deactivation.
- **Catalog admin:** add, rename and remove plans, entries with defaults, link validation and "nothing saved on failure", edit, soft delete, name reuse, portal entries.
- **Browse:** plan ordering and flags, de-duplicating shared folders, listing order and sizes, subfolders at any depth (including the parent-folder case), folders outside the entry, search, paging, disconnected (503) and outage (502).
- **Upload:** success and audit, name sanitizing, conflicts and overwrite, extension and size limits, direction checks, subfolder uploads, failures, receipts.

### System (`System/`)

End to end over HTTP with the real `ShareFileService` talking to a **fake ShareFile REST API** (`FakeShareFileHttp`). Only the outside world is mocked.

- The full journey: fresh install ➜ create superadmin ➜ save settings ➜ connect (bad code first) ➜ build the catalog from links ➜ invite and sign in a user ➜ browse and open a subfolder ➜ upload, hit a conflict, overwrite ➜ remove the plan.
- Secrets are not present in plain text in the database files.
- The catalog and the ShareFile connection survive an app restart.
- `scripts/system.sh`: `--help` lists every option, no option or an unknown one fails with usage, the script is valid bash. It never starts or stops the app.

## Support code (`tests/GuidantShare.Tests/Support`)

| File | Purpose |
|---|---|
| `TestApp.cs` | Hosts the app with a temp data folder; helpers to add users, seed catalog rows, query the database; `ApiClient` is a cookie-keeping client that fetches a CSRF token for every write |
| `FakeShareFile.cs` | In-memory `IShareFileService` with folders, files, upload recording, and switches (`Disconnected`, `FailUploads`, `ListError`) |
| `FakeShareFileHttp.cs` | A fake ShareFile REST API (token, Items, Children, Upload and the chunk upload) for the system tests |
| `AssemblyInfo.cs` | Turns off parallel test execution |

## Writing tests

- Pick the lowest level that can prove the behavior. A validation rule belongs in Unit, an endpoint's status code in Integration, a multi-step journey in System.
- Tag with `[Trait("Category", "Unit" | "Integration" | "System")]`.
- Use unique ShareFile folder ids per test (`FakeShareFile.NewId()`). The app keeps a static 30-second folder cache and a folder allow-list, so reusing ids across tests can hide bugs; pass `refresh=true` when listing.
- Seed with `SeedEntryAsync` when the catalog is not what you are testing; use the admin endpoints when it is.
- Create users with `AddUserAsync`, then sign in with `ApiClient.Login`.
- Add a new fake behavior to `FakeShareFile` (a switch) rather than a one-off mock.

## Why tests run one at a time

The hosted app reads its settings (data folder, key path, email mode) from process-wide environment variables, because `Program.cs` reads them before a test host can inject configuration. Running tests in parallel would let them overwrite each other's settings, so parallelization is disabled for the assembly.

## File ownership caution

If more than one user account works in the same checkout, run tests in a copy or as the account that owns `bin/` and `obj/`; otherwise .NET cannot update build timestamps. See [Troubleshooting](troubleshooting.md).

## Not covered

- The React UI has no automated tests. It is checked by hand against the API behavior above.
- Real ShareFile and Microsoft Graph behavior is only covered through the fakes. Do a manual end-to-end check after changing the ShareFile client.
