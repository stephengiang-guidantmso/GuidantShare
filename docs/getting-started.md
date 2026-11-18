# Getting started

## Requirements

- .NET 10 SDK (the project expects `dotnet` on the `PATH` or in `~/.dotnet`)
- Node.js and npm (for the web UI)
- A ShareFile account you can authorize an API app on (client ID, client secret, subdomain)

## Build and run on a developer machine

```bash
# 1. Build the UI (writes into src/GuidantShare.Api/wwwroot, which is not committed)
cd src/GuidantShare.Web && npm install && npm run build && cd ../..

# 2. Start the API in the background on http://localhost:5080
scripts/system.sh --start
```

`scripts/system.sh --stop`, `--restart` and `--help` are described in [Operations](operations.md). You can also run it in the foreground with `dotnet run --project src/GuidantShare.Api`.

> Use one operating-system user for building and running. Mixing users leaves files the other cannot update (for example `obj/` timestamps or `data/keys/master.key`).

## First-time setup

1. **Create a superadmin.**
   ```bash
   dotnet run --project src/GuidantShare.Api -- create-superadmin --email you@example.com
   ```
   Without `--password` it prints a one-time set-password link (valid 72 hours). With `--password` the account is created directly.
2. **Sign in** at http://localhost:5080 and open **Settings**.
3. **Save the ShareFile app settings** (section 1): subdomain (the part before `.sharefile.com`), client ID, client secret, and the redirect URI registered on the ShareFile app.
4. **Connect** (section 2): click *Connect to ShareFile* and sign in as the owner account. If your registered redirect URI is ShareFile's generic `oauthcomplete.aspx` page, paste the code (or the whole address containing `code=`) into the box and click *Finish connecting*.
5. **Add health plans and folders** (section 3). See the [Catalog](catalog.md) page.
6. **Invite users** from the Users page.

The email setting defaults to `Log`, which writes invite and reset links to the application log instead of sending them. In that mode the invite dialog also shows the link so you can copy it. See [Configuration](configuration.md) for real email.

## Where things are stored

| Thing | Location |
|---|---|
| Database (users, catalog, audits, encrypted secrets) | `src/GuidantShare.Api/data/app.db` |
| Master encryption key | `src/GuidantShare.Api/data/keys/master.key` |
| Application logs | `src/GuidantShare.Api/data/logs/wrapper-YYYYMMDD.log` |
| Script pid and output | `.run/server.pid`, `.run/server.out` |

All of these are gitignored. Deleting the database gives you a clean slate (you will need to recreate the superadmin and re-enter the ShareFile settings; the master key must stay with the database it encrypted).

## Running the tests

```bash
dotnet test
```

See [Testing](testing.md).
