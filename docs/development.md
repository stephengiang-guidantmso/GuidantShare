# Development

## Repository layout

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
  GuidantShare.Web/         React + Vite app (src/pages/*)
tests/GuidantShare.Tests/   Unit, Integration, System, Support
```

Runtime data (`data/`, `.run/`) and build output are gitignored.

## Working on the API

```bash
scripts/system.sh --restart          # rebuild and restart after a change
dotnet test                          # run everything
```

Key places:

| To change... | Look in |
|---|---|
| An endpoint, validation, defaults | `src/GuidantShare.Api/Endpoints.cs` (`ApplyEntryAsync` holds the catalog entry rules) |
| Auth, cookies, password rules, rate limits, security headers | `src/GuidantShare.Api/Program.cs` |
| Invites | `src/GuidantShare.Api/Services/InviteService.cs` |
| Email sending | `src/GuidantShare.Api/Services/Email.cs` |
| Talking to ShareFile | `src/GuidantShare.ShareFile/ShareFileService.cs` |
| Table shape | `src/GuidantShare.Core/Entities.cs`, `src/GuidantShare.Data/AppDbContext.cs` |
| Startup self-checks | `src/GuidantShare.Api/StartupChecks.cs` |

The API uses minimal-API endpoints grouped as public (`api`), signed-in (`auth`) and superadmin (`admin`). New routes join the group that matches who may call them; the group applies the authorization policy.

## Working on the UI

```bash
cd src/GuidantShare.Web
npm install
npm run dev            # Vite dev server; /api and /health are proxied to http://localhost:5080
npm run build          # type-checks (tsc) and writes to ../GuidantShare.Api/wwwroot
npm run lint
```

Start the API first (`scripts/system.sh --start`) so the dev server's proxy has something to talk to. Pages live in `src/pages`; `src/api.ts` is the only place that talks to the API. It adds the CSRF header to every write and turns error bodies into `ApiError`. Styles are in `src/index.css` (plain CSS with design tokens).

Admin pages are guarded in `App.tsx`; the server enforces the same rules, so the UI guard is for convenience only.

## Conventions

- **Passwords never enter the system for partners.** Do not add fields that hold SFTP or portal passwords. Login names are fine.
- **Settings are not in git.** Local overrides go in `appsettings.Local.json` or environment variables.
- **Errors are user-readable.** Endpoints return `{ error: "..." }` with a message a user could act on. Log details server-side.
- **Soft delete catalog data.** Set `IsActive = false` rather than removing rows, so audit history stays readable.
- **Independence from FileSync.** Do not read FileSync's files, database or configuration.
- **Comments say why**, not what.

## Common changes

### Add a field to a catalog entry

1. Add the property to `CatalogEntry` in `Entities.cs`.
2. Because the schema comes from `EnsureCreated`, existing databases will not gain the column. Add a guarded `ALTER TABLE` to the startup block in `Program.cs`, following the existing `RemoteUsername` example (check `pragma_table_info`, then add the column).
3. Add it to `EntryReq`, `ApplyEntryAsync` and the `/api/catalog` response in `Endpoints.cs`.
4. Add it to the form and table in `src/pages/CatalogEditor.tsx` (and keep the table's `<colgroup>` widths adding up).
5. Add unit tests for the rule and an integration test for the endpoint.

### Add an endpoint

Add it in the right group in `MapEndpoints`, return `{ error }` for failures, add an integration test using `ApiClient`, and update the [API reference](api-reference.md).

### Change the defaults for label or path

`ApplyEntryAsync` in `Endpoints.cs` and the placeholders in `CatalogEditor.tsx`. Update the unit tests in `Unit/CatalogRulesTests.cs` and the [Catalog](catalog.md) page.

### Introduce migrations

Today there are none. If the schema starts changing often, switch to EF Core migrations: generate an initial migration that matches the current model, replace `EnsureCreated` with `Migrate`, and drop the manual `ALTER TABLE` block. Test against a copy of a real database first.

## Pull request checklist

- [ ] `dotnet test` passes
- [ ] `npm run build` passes (type-check)
- [ ] No secrets, databases, logs or spreadsheets staged (`git status`)
- [ ] Docs updated if behavior, settings or endpoints changed
- [ ] New behavior has a test at the right level

## Known gaps

- Forwarded headers are not configured for reverse proxies.
- The UI has no automated tests.
- Catalog entries' `AllowedExtensions` and `MaxSizeBytes` can only be set in the database.
- There is no catalog import or export.
