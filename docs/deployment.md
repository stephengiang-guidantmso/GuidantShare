# Deployment

The app is a single .NET process with a SQLite file. It can run on a Linux server, a VM or Windows behind a reverse proxy.

## Checklist

**Before first start**

- [ ] .NET 10 runtime (or SDK if you build on the server)
- [ ] The UI is built: `cd src/GuidantShare.Web && npm ci && npm run build`
- [ ] `ASPNETCORE_ENVIRONMENT=Production`
- [ ] `Urls` bound to the address the proxy will use (for example `http://127.0.0.1:5080`)
- [ ] `AllowedHosts` set to your hostname
- [ ] `Email:Mode=Graph` with the Graph settings, and `Email:PublicBaseUrl=https://your-host`
- [ ] `Data:Dir` on persistent storage, owned by the service account only
- [ ] HTTPS terminated at a proxy or by Kestrel
- [ ] Forwarded headers configured if you use a proxy (see Security)

**After first start**

- [ ] `create-superadmin` run on the production database
- [ ] ShareFile app settings saved and the account connected
- [ ] Plans and folders added in Settings (a new database has none)
- [ ] A normal user invited and a real download and upload tested end to end
- [ ] `GET /health` shows `status: ok`, `sharefile: connected`, `email: ok`

## Build and publish

```bash
dotnet publish src/GuidantShare.Api -c Release -o publish
# publish/ now contains the API; copy the built UI if it is not already inside:
# the UI build writes to src/GuidantShare.Api/wwwroot, which publish includes
```

Run the output with `dotnet publish/GuidantShare.Api.dll`, or keep using `scripts/system.sh` on a small host.

## systemd example

```ini
[Unit]
Description=GuidantShare
After=network.target

[Service]
WorkingDirectory=/srv/guidantshare
ExecStart=/usr/bin/dotnet /srv/guidantshare/GuidantShare.Api.dll
Restart=on-failure
User=guidantshare
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=Urls=http://127.0.0.1:5080
Environment=AllowedHosts=share.example.com
Environment=Email__Mode=Graph
Environment=Email__PublicBaseUrl=https://share.example.com
EnvironmentFile=/etc/guidantshare/secrets.env   # Email__ClientSecret=...

[Install]
WantedBy=multi-user.target
```

## Reverse proxy

Terminate TLS at the proxy and pass `X-Forwarded-For` and `X-Forwarded-Proto`. The app does not yet enable forwarded-header handling, so add `UseForwardedHeaders` in `Program.cs` (with your proxy listed as known) before relying on client addresses in the rate limit and login audit. Allow request bodies at least as large as `Upload:MaxBytes`, and set generous timeouts for large uploads.

## Single instance

Run exactly one copy. The folder cache, OAuth state and folder allow-list are in memory, and SQLite is a single-writer file. Scaling out would need those moved to shared storage first.

## Data and backups

Back up `data/app.db` and `data/keys/master.key` on a schedule, separately and securely. See [Operations](operations.md) for how.

## Upgrades

1. Stop the app (`scripts/system.sh --stop` or your service manager).
2. Back up `data/`.
3. Deploy the new build and the rebuilt UI.
4. Start the app. The schema is created on first run and a small step adds columns introduced later; there are no migrations yet, so read release notes for anything that changes the data model.
5. Check `/health`.

## New environment from scratch

There is no catalog import. Start the app, create the superadmin, connect ShareFile and enter the plans in Settings.
