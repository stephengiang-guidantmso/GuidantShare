# Operations and deployment

## Day-to-day operations

### scripts/system.sh

```bash
scripts/system.sh --start     # build, start in the background, wait for /health
scripts/system.sh --stop      # stop the app
scripts/system.sh --restart   # stop, rebuild, start
scripts/system.sh --help
```

| Detail | Value |
|---|---|
| console output | `src/GuidantShare.Api/data/logs/console.out` |
| port | `GUIDANTSHARE_PORT` (default 5080), used to detect and stop whatever listens there |
| `--start` | Does nothing if the app is already running. Rebuilds first, reports build errors, waits up to 30 seconds for `/health`, and prints the last log lines if the process exits during startup |
| `--stop` | Stops whatever listens on the port (there are no pid files); escalates to a kill after 10 seconds |
| Exit status | Non-zero for a failed build or start, or for no option or an unknown one (usage is printed) |

The script suits a small host or a developer machine. In production prefer a service manager (see [Deployment](operations.md#deployment)).

### Health

`GET /health` (no login):

```json
{ "status": "ok", "email": "ok", "emailMode": "Log", "sharefile": "connected" }
```

`sharefile` is `connected`, `disconnected` or `not_configured`; `email` is `misconfigured` when Graph mode is set up wrongly. Point a monitor at it.

### Logs

| Log | Where |
|---|---|
| Application log (Serilog, daily files) | `data/logs/wrapper-YYYYMMDD.log` |
| Console / script output | `data/logs/console.out` |

The application log holds request lines, ShareFile failures with the status and a trimmed response body, email sending results, and startup check errors. In `Log` email mode it also contains invite and reset links, so treat the log as sensitive. There is no log rotation by size or retention policy; delete or archive old daily files periodically.

Useful searches:

```bash
grep -i "ShareFile" data/logs/wrapper-*.log | tail -50
grep -i "startup check\|Email misconfigured\|no longer exists" data/logs/wrapper-*.log
```

### Backups

Back up two things, separately and securely:

1. `data/app.db` (with the app stopped, or use SQLite's online backup)
   ```bash
   sqlite3 data/app.db ".backup 'backup/app-$(date +%F).db'"
   ```
2. `data/keys/master.key`

A database backup without the key cannot decrypt the ShareFile credentials (you would re-enter them and reconnect). The audit tables restore fine either way.

**Restore:** stop the app, put `app.db` and `master.key` back in `data/` (matching pair), start, check `/health`.

### Routine tasks

| Task | How |
|---|---|
| Add or change a plan or folder | FileSync → edit the job (Website Health Plan / Website Folder Name) |
| Invite, deactivate or reset a user | Users page |
| Reconnect ShareFile | Settings → Reconnect |
| Rotate the ShareFile client secret | Create a new secret in ShareFile, save it in Settings, then Reconnect |
| Rotate the Graph mail secret | Update `Email__ClientSecret`, restart |
| Check who uploaded what | Query `UploadAudits` (see below) |
| Check sign-in activity | Query `LoginAudits` |

#### Handy queries

```sql
-- latest uploads with who and where
SELECT a.CreatedUtc, u.Email, a.PlanName, a.FolderLabel, a.FileName, a.SizeBytes, a.Status, a.Error
FROM UploadAudits a
LEFT JOIN AspNetUsers u ON u.Id = a.UserId
ORDER BY a.AuditId DESC LIMIT 50;

-- failed sign-ins in the last day
SELECT CreatedUtc, Ip FROM LoginAudits
WHERE Event = 'login_failed' AND CreatedUtc > datetime('now','-1 day') ORDER BY Id DESC;

-- what SuperAdmins changed in FileSync through the website
SELECT a.CreatedUtc, u.Email, a.Action, a.Status
FROM AdminAudits a LEFT JOIN AspNetUsers u ON u.Id = a.UserId
ORDER BY a.Id DESC LIMIT 50;
```

Run them read-only: `sqlite3 -readonly data/app.db "..."`.

### When something looks wrong

Check `/health`, then the log, then [Troubleshooting](operations.md#troubleshooting).

## Deployment

Production runs on the Windows VM `SFTPSRVR`, next to FileSync. Three pieces:

| Piece | Runs as | Listens on |
|---|---|---|
| Caddy (reverse proxy, TLS, static SPA files) | Windows service | public `:443` (and `:80` for certificate validation) |
| GuidantShare.Api | Windows service `GuidantShareApi` | `127.0.0.1:5080` only |
| FileSync | Windows service `FileSyncService` (existing) | `localhost:5000` only |

Only the proxy is reachable from the internet. Do not weaken that: never bind the API or FileSync to `0.0.0.0` and never open their ports in the firewall or the Azure network security group.

For the one-time setup of the VM, the Azure and DNS steps and carrying the FileSync jobs over, follow the [Go-live guide](operations.md#go-live-deploy-to-the-sftp-server-vm). This page is the reference.

Ready-made files are in `deploy/`: `Caddyfile`, `publish-windows.ps1`, `install-api-service.ps1`. The repo is cloned to `C:\Services\GuidantShare`, so the code, `api\` and `web\` live under one folder.

### Before go-live (people, not code)

- [ ] Someone with rights on `SFTPSRVR-RG` opens inbound **443** (and **80**) to the VM in the Azure network security group (and Windows Firewall).
- [ ] The VM's *inbound* public IP is confirmed (the `20.169.248.229` address seen by partners is the *outbound* one).
- [ ] An A record `guidantshare.guidantmso.com` points at that inbound IP (DNS is at Wix; only the record is needed).
- [ ] Two ShareFile OAuth apps (or one app each) are registered: FileSync's existing one, and GuidantShare's with redirect URI `https://guidantshare.guidantmso.com/api/sharefile/callback`.

### Checklist

**Before first start**

- [ ] .NET 10 runtime installed on the VM
- [ ] `deploy/publish-windows.ps1` run (builds the API and the SPA into `C:\Services\GuidantShare\api` and `...\web`)
- [ ] FileSync updated to this version and `Api__Key` set on the FileSync service (long random value, kept out of git)
- [ ] The same value set as `FileSync__ApiKey` on the GuidantShare service (`install-api-service.ps1` prompts for it)
- [ ] `Email__Mode=Graph` with the Graph settings (secret via environment variable)
- [ ] `ASPNETCORE_ENVIRONMENT=Production` (the install script sets it; `appsettings.Production.json` then binds to `127.0.0.1:5080`, sets `AllowedHosts` and `Email:PublicBaseUrl`)
- [ ] Caddy installed with `deploy/Caddyfile`

**After first start**

- [ ] `create-superadmin` run on the production database
- [ ] ShareFile app settings saved and the account connected (Settings)
- [ ] In FileSync, *Website Health Plan* and *Website Folder Name* set on each job you want users to see, and the jobs enabled
- [ ] A normal user invited and a real download and upload tested end to end
- [ ] `GET https://guidantshare.guidantmso.com/health` shows `status: ok`, `sharefile: connected`, `fileSync: ok`, `email: ok`
- [ ] From outside the VM, ports 5000 and 5080 do not answer; only 443 does

### Build and publish

```powershell
deploy\publish-windows.ps1        # dotnet publish + npm ci + npm run build into C:\Services\GuidantShare
```

The API and the SPA are separate: the API is published to `api\`, the SPA build to `web\` (served by Caddy). The API serves no web files at all.

### Windows service

`deploy\install-api-service.ps1` creates the service `GuidantShareApi` (automatic start, runs `GuidantShare.Api.exe`), stores its environment variables in the service's registry key, and starts it. The app detects the service host automatically and resolves `data\` next to the executable.

### Reverse proxy

`deploy\Caddyfile` serves the SPA files with a fallback to `index.html`, proxies `/api/*` and `/health` to `127.0.0.1:5080`, gets and renews the TLS certificate automatically, and adds the security headers for the static files (the API adds its own). Caddy runs as a Windows service by itself (`sc.exe create`, see the go-live steps); no wrapper is needed. The app trusts `X-Forwarded-*` headers only from the loopback address, so the login rate limit sees real client addresses.

Uploads can be large: the Caddyfile allows long read and write timeouts on `/api/*`.

### Single instance

Run exactly one copy. The folder cache, OAuth state, catalog copy and folder allow-list are in memory, and SQLite is a single-writer file.

### Data and backups

A nightly scheduled task copies both databases and key files; see [Nightly backups](#nightly-backups).

### Upgrades

1. Back up `data\` (and FileSync's `data\`).
2. Deploy FileSync changes in a quiet period: restarting `FileSyncService` interrupts running transfers.
3. Stop `GuidantShareApi`, deploy the new build and SPA, start it.
4. Check `/health`.

### Rolling back

Stop the API, restore the previous build and the `data\` backup from before the upgrade (users and uploads made since are lost).

### New environment from scratch

Start FileSync, then the API; create the superadmin, connect ShareFile, and set the website fields on the FileSync jobs. There is no catalog to enter in GuidantShare.

## Troubleshooting

### Starting the app

| Symptom | Cause | Fix |
|---|---|---|
| `Access to the path '.../data/keys/master.key' is denied` | The key file is owned by another account | Run the app as the account that owns `data/`, or give the service account read access (`setfacl -m u:ACCOUNT:r data/keys/master.key`) |
| Build fails with `MSB3374 ... last write time ... cannot be set` | `obj/` or `bin/` files were created by a different user | Delete `bin/` and `obj/` in each project (they are regenerated), then build as the account that runs the app. Use one account for building and running |
| `address already in use` | Another process owns the port | `scripts/system.sh --stop`, or change `Urls` |
| App exits right away | Read `src/GuidantShare.Api/data/logs/console.out` | Look for missing key permissions, an unreadable database, or a bad setting |
| Blank page or old UI | The browser cached an old copy, or Caddy points at the wrong folder | Hard refresh (Ctrl+Shift+R); in dev open http://localhost:5173; in production check the `root` in the Caddyfile and that `publish-windows.ps1` ran |

### Signing in

| Symptom | Cause | Fix |
|---|---|---|
| "Invalid email or password." | Wrong credentials or a deactivated account | Reset the password or reactivate the user |
| "Too many attempts. Try again later." | 5 failures lock the account 15 minutes | Wait, or reset the password |
| Invite link says invalid or expired | Used already, revoked, or older than 72 hours | Resend the invite |
| No invite email | `Email:Mode` is `Log`, or Graph is misconfigured | Copy the link from the invite dialog or the log, or fix Graph (see `/health`) |
| "Invalid CSRF token. Refresh the page." | Stale session or token | Reload; sign in again |
| Constant `429` | Many requests from one address (behind a proxy without forwarded headers, every user shares one) | Configure forwarded headers |

### ShareFile

| Symptom | Cause | Fix |
|---|---|---|
| "ShareFile is disconnected. Ask a SuperAdmin to reconnect." | No refresh token, or ShareFile rejected it | Settings → Reconnect |
| Connect returns to Settings with "connection failed" | Wrong redirect URI or the approval timed out (10 minutes) | Make the redirect URI match the ShareFile app exactly, try again |
| "ShareFile rejected the request (400)" when finishing | Bad or already-used code | Start the connect again and paste the new code |
| A plan is missing or wrong on the home page | The FileSync job has no *Website Health Plan*, is disabled, or has no ShareFile folder id | Open FileSync, fix the job; the yellow warning on that page lists problems. Changes show within about a minute |
| Users see "may be out of date" / admins see "FileSync can't be reached" | FileSync is down, or `FileSync:ApiKey` does not match FileSync's `Api:Key` | Check the FileSync service and the key (a `401` in the log means a mismatch) |
| Users get "unavailable" with no plans | FileSync has been unreachable since the website started | Start FileSync, then reload |
| "ShareFile could not be reached" | Network or ShareFile outage | Retry; check the log for the status code |
| Startup log: "ShareFile folder ... can't be opened" | The folder was deleted, or the website's ShareFile account cannot open it | Share the folder with that account, or fix the job's ShareFile Folder ID in FileSync |

### Files and uploads

| Symptom | Cause | Fix |
|---|---|---|
| A folder shows no files | Nothing there yet, or the listing is cached up to 30 seconds | Press Refresh |
| A subfolder will not open | It was not listed first, or belongs to another folder | Go back to the root and open it from the list |
| 409 on upload | A file with that name exists | Choose overwrite or rename |
| "Only these file types are allowed" | The folder restricts extensions | Use an allowed type, or change `AllowedExtensions` |
| "File is larger than the ... MB limit" | Over the upload limit | Split the file or raise `Upload:MaxBytes`; also raise any proxy body limit |
| "Upload failed: ShareFile refused to start the upload" | The connected account cannot upload to that folder | Fix permissions in ShareFile; `Failed` audit rows have the message |
| Upload succeeded but the partner has no file | FileSync has not copied it yet, or its job failed | Check the FileSync jobs under that plan's folder in the FileSync screen |

### Tests

| Symptom | Cause | Fix |
|---|---|---|
| Tests fail to start the host | Another test host is using the same settings | Tests must run serially (they already do); do not enable parallelization |
| Build errors about file ownership | Mixed users | Build in a copy or as the owning account |

### Collecting information for a bug report

1. The exact message and what you clicked.
2. The time (so the log lines can be found).
3. `GET /health` output.
4. The relevant lines from `data/logs/wrapper-YYYYMMDD.log` (remove invite or reset links first).

## Go live: deploy to the SFTP server VM

How `SFTPSRVR` was set up, in order. **FileSync is upgraded in place**: its `data\` folder holds the jobs, the encrypted passwords and ShareFile token, and the transfer ledger and file state. Do **not** wipe it: an empty database loses the passwords and makes every file look new, which can re-send files to partners. GuidantShare starts with an empty database. Run VM commands in PowerShell as Administrator. `<FQDN>` is the site's address, `<HOMEIP>` your own public IP.

### The API key

FileSync's API needs one shared secret. It is **never in the web app**; only GuidantShare's server sends it.

| Service | Variable | Set by |
|---|---|---|
| `FileSyncService` | `Api__Key` | its registry `Environment` value (Step 3) |
| `GuidantShareApi` | `FileSync__ApiKey` | `deploy\install-api-service.ps1` |

Both must match, or health shows `fileSync: unreachable`. Read the current key from FileSync's service:

```powershell
$key = (((Get-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Services\FileSyncService).Environment | Where-Object { $_ -like "Api__Key=*" }) -replace '^Api__Key=', '')
```

To change it, set the new value on both services and restart both.

### Step 1. Network and address

- Azure: VM → **Network settings** → the security group on the network card. Add inbound TCP rules: **443** from `<HOMEIP>/32` while testing, **80** from Any (certificate issue and renewal). Never open 5000 or 5080.
- Windows Firewall: matching rules named `GuidantShare HTTPS` (`-RemoteAddress <HOMEIP>`) and `GuidantShare HTTP`.
- The VM's public IP is `20.169.248.229` (in and out). The IP resource is in another resource group; ask its owner to confirm it is **Static**.
- Address: `guidantshare.guidantmso.com` once its A record exists. Until then `20-169-248-229.sslip.io` (a free public name for that IP) works and gets a real certificate.
- To prove reachability before installing anything, run a temporary `System.Net.HttpListener` on port 80 that answers `/api/health`, and request it from outside.

### Step 2. Record and back up FileSync

```powershell
cd C:\Services\FileSync
git rev-parse --short HEAD; git status
.\scripts\filesync.ps1 -Action Stop
$bk = "C:\Services\backup-$(Get-Date -Format yyyyMMdd-HHmm)"
robocopy C:\Services\FileSync "$bk\FileSync" /E /XD ms-playwright node_modules .git /NFL /NDL
.\bin\FileSync.Service.exe --export-master-key     # keep it in a password manager
```

Rolling back: stop the service, copy `$bk\FileSync\bin` and `$bk\FileSync\data` back, start.

### Step 3. Upgrade FileSync

```powershell
$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); $key = [Convert]::ToBase64String($b); $key   # password manager
Set-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Services\FileSyncService -Name Environment -Type MultiString -Value @("Api__Key=$key")
cd C:\Services\FileSync; git pull origin main; .\scripts\filesync.ps1 -Action Deploy
```

The deploy script can report success when the service failed to start, so check: `Get-Service FileSyncService`; `/api/health` answers; `/api/workflows` is `401` without the key; with it, all jobs are listed and new runs complete.

Set the website plan and folder names with `scripts\Set-JobPlans.ps1` (`-Export`, fill the CSV, `-Apply -WhatIf`, `-Apply`).

### Step 4. Install GuidantShare

```powershell
git clone https://github.com/stephengiang-guidantmso/GuidantShare.git C:\Services\GuidantShare
cd C:\Services\GuidantShare
.\deploy\publish-windows.ps1
.\deploy\install-api-service.ps1 -PublicHost <FQDN>
curl.exe http://127.0.0.1:5080/health                    # status ok, fileSync ok
```

Always pass `-PublicHost`; without it the real domain is assumed. The installer replaces all the service's variables, so answer its mail questions again if mail is set up.

Create the first admin from the `api` folder, keeping the password out of history:

```powershell
$s = Read-Host "New admin password" -AsSecureString
$pw = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
cd C:\Services\GuidantShare\api; .\GuidantShare.Api.exe create-superadmin --email you@guidantmso.com --password $pw; Remove-Variable pw, s
```

### Step 5. Caddy (HTTPS)

```powershell
New-Item -ItemType Directory C:\Services\Caddy -Force | Out-Null; cd C:\Services\Caddy
Invoke-WebRequest "https://caddyserver.com/api/download?os=windows&arch=amd64" -OutFile caddy.exe -UseBasicParsing
$top = "{`n`tstorage file_system C:/Services/Caddy/data`n`tlog {`n`t`toutput file C:/Services/Caddy/caddy.log`n`t}`n}`n`n"
Set-Content Caddyfile ($top + (Get-Content C:\Services\GuidantShare\deploy\Caddyfile -Raw))
$env:GS_HOST = "<FQDN>"; .\caddy.exe validate --config Caddyfile
sc.exe create caddy start= auto binPath= "C:\Services\Caddy\caddy.exe run --config C:\Services\Caddy\Caddyfile"
Set-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Services\caddy -Name Environment -Type MultiString -Value @("GS_HOST=<FQDN>")
sc.exe start caddy
```

`caddy.log` should say `certificate obtained successfully`.

### Step 6. ShareFile

Sign in at `https://<FQDN>` → **Settings** → save the ShareFile app details → **Connect**. This is GuidantShare's own connection; do not copy FileSync's token.

### Step 7. Mail

1. Entra: register an app used only by the website (`GuidantShare Mail`), add Microsoft Graph **Mail.Send** (application), grant admin consent.
2. Create a shared mailbox to send from (`guidantshare@guidantmso.com`).
3. Limit the app to that mailbox (Exchange Online PowerShell). A shared mailbox cannot be named directly, so use a mail-enabled security group containing only it:
   ```powershell
   Connect-ExchangeOnline -DisableWAM
   New-DistributionGroup -Name "GuidantShare Mail Senders" -Alias guidantshare-senders -Type Security -Members guidantshare@guidantmso.com
   New-ApplicationAccessPolicy -AppId <client-id> -PolicyScopeGroupId guidantshare-senders@guidantmso.com -AccessRight RestrictAccess -Description "GuidantShare mail only"
   Test-ApplicationAccessPolicy -AppId <client-id> -Identity guidantshare@guidantmso.com    # Granted
   ```
   Any other mailbox must test as **Denied**.
4. Create a client secret (note when it expires: mail stops then) and re-run `install-api-service.ps1 -PublicHost <FQDN>`, answering `y` to Graph.
5. Health shows `email: ok`, `emailMode: Graph`; send a test invite.

### Nightly backups

Put `sqlite3.exe` (from sqlite.org) in `C:\Services\tools`, then:

```powershell
New-Item -ItemType Directory C:\Services\backups -Force | Out-Null
icacls C:\Services\backups /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F"
@'
$ErrorActionPreference = "Stop"
$dest = "C:\Services\backups\$(Get-Date -Format yyyyMMdd-HHmm)"
New-Item -ItemType Directory $dest -Force | Out-Null
$sqlite = "C:\Services\tools\sqlite3.exe"
& $sqlite C:\Services\FileSync\data\filesync.db ".backup '$dest\filesync.db'"
if ($LASTEXITCODE -ne 0) { throw "FileSync backup failed" }
& $sqlite C:\Services\GuidantShare\api\data\app.db ".backup '$dest\app.db'"
if ($LASTEXITCODE -ne 0) { throw "GuidantShare backup failed" }
Copy-Item C:\Services\FileSync\data\keys\master.key "$dest\filesync-master.key"
Copy-Item C:\Services\GuidantShare\api\data\keys\master.key "$dest\guidantshare-master.key"
Copy-Item C:\Services\FileSync\data\graph-mail.json $dest
Get-ChildItem C:\Services\backups -Directory | Where-Object CreationTime -lt (Get-Date).AddDays(-14) | Remove-Item -Recurse -Force
'@ | Set-Content C:\Services\backup.ps1
schtasks /create /tn "Guidant backup" /tr "powershell -NoProfile -ExecutionPolicy Bypass -File C:\Services\backup.ps1" /sc daily /st 02:00 /ru SYSTEM
```

These copies are on the same disk; also turn on Azure Backup for the VM. The key files only work on this machine: on a rebuilt server FileSync needs the exported master key, and GuidantShare's ShareFile connection has to be set up again.

### Switching to the real domain and opening the site

After `guidantshare.guidantmso.com` resolves to `20.169.248.229`:

```powershell
# edits the two address settings only, so the API key and mail secret stay as they are
$p = "HKLM:\SYSTEM\CurrentControlSet\Services\GuidantShareApi"
$vars = @((Get-ItemProperty $p).Environment | Where-Object { $_ -notlike "AllowedHosts=*" -and $_ -notlike "Email__PublicBaseUrl=*" })
Set-ItemProperty $p -Name Environment -Type MultiString -Value ($vars + "AllowedHosts=guidantshare.guidantmso.com;localhost;127.0.0.1" + "Email__PublicBaseUrl=https://guidantshare.guidantmso.com")
Restart-Service GuidantShareApi
Set-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Services\caddy -Name Environment -Type MultiString -Value @("GS_HOST=guidantshare.guidantmso.com"); Restart-Service caddy
Set-NetFirewallRule -DisplayName "GuidantShare HTTPS" -RemoteAddress Any
```

Then set the Azure 443 rule's source to **Any**. If the ShareFile redirect address used the old name, update it in the ShareFile app and in Settings. Invite users after the switch.

### Verification

- `https://<FQDN>/health`: `status ok`, `fileSync ok`, `sharefile connected`, `email ok`.
- Ports 5000 and 5080 do not answer from outside.
- `https://<FQDN>/api/filesync/workflows` signed out gives `401`.
- A normal user sees no FileSync, Users or Settings; browsing and a test upload work.

### Troubleshooting the deployment

| Symptom | Likely cause | Fix |
|---|---|---|
| The reachability test hangs | An Azure rule or the Windows rule is missing | Check both |
| FileSync will not start after the upgrade | `Api__Key` not set on the service | Step 3 |
| `fileSync: unreachable` | The two keys differ | Re-copy FileSync's key into `FileSync__ApiKey`, restart `GuidantShareApi` |
| `400 Bad Request` | The address is not in `AllowedHosts` | Fix the address settings (see switching to the real domain) |
| Sign-in does nothing | Opened over `http://` | Use `https://` |
| No certificate | Port 80 blocked | Read `C:\Services\Caddy\caddy.log`; do not restart repeatedly |
| Mail fails right after restricting | The policy is still applying | Wait an hour |

`Export-Jobs.ps1` and `Import-Jobs.ps1` are **not** part of this upgrade. They are for rebuilding FileSync on a different machine, where passwords have to be typed in again.
