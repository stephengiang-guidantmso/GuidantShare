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

Ready-made files are in `deploy/`: `Caddyfile`, `publish-windows.ps1`, `install-api-service.ps1`. Review them before use; they were written without being run on the VM.

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

`deploy\Caddyfile` serves the SPA files with a fallback to `index.html`, proxies `/api/*` and `/health` to `127.0.0.1:5080`, gets and renews the TLS certificate automatically, and adds the security headers for the static files (the API adds its own). Caddy does not run as a Windows service by itself; run it with a service wrapper such as NSSM or WinSW. The app trusts `X-Forwarded-*` headers only from the loopback address, so the login rate limit sees real client addresses.

Uploads can be large: the Caddyfile allows long read and write timeouts on `/api/*`.

### Single instance

Run exactly one copy. The folder cache, OAuth state, catalog copy and folder allow-list are in memory, and SQLite is a single-writer file.

### Data and backups

Back up `data\app.db` and `data\keys\master.key` on a schedule, separately and securely. See [Operations](operations.md#day-to-day-operations).

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

Deploys both repos to `SFTPSRVR`, first reachable only from your own IP under a free Azure DNS name, then (when the domain is ready) publicly. **FileSync is upgraded in place**: its `data\` folder holds the jobs, the encrypted passwords and ShareFile token, and the transfer ledger and file state. The new version only adds two columns. Do **not** wipe it: an empty database loses the passwords and makes every file look new, which can re-send files to partners. GuidantShare starts with an empty database.

Pick a quiet period: FileSync stops during the upgrade. Placeholders: `<FQDN>` = the Azure DNS name, `<HOMEIP>` = your PC's public IP (`curl.exe https://ifconfig.me` on your PC).

> Written without being run on the VM. Check each step's output before going on.

### The API key: where it lives and how to set it

FileSync's API is protected by one shared secret, the **API key**. It is **never in the frontend** and never reaches the browser: the browser talks to GuidantShare's own API, and only GuidantShare's server sends the key to FileSync. It lives in exactly two places on the VM, and the two values must be **identical**:

| Where | Name | How it is set | Who reads it |
|---|---|---|---|
| FileSync service (`FileSyncService`) | `Api__Key` | Environment variable on the service, stored in its registry key (step D2) | FileSync, to accept or reject each call (`401` without it) |
| GuidantShare API service (`GuidantShareApi`) | `FileSync__ApiKey` | Prompted for by `deploy\install-api-service.ps1`, stored in that service's registry key (step E3) | GuidantShare's server, to attach it to every call it makes to FileSync |

Rules:

- **Make it long and random** (32+ random bytes). Generate it once with `[Convert]::ToBase64String((1..32 | % { Get-Random -Maximum 256 }) -as [byte[]])` and keep it in your password manager.
- **Never put it in git, in `appsettings.json` or in the web app.** Environment variables on the service are the only place. (Local development uses the throwaway value `dev-key`, set by `scripts/system.sh`; never reuse it.)
- **FileSync will not start in Production without it**, and GuidantShare shows "unavailable" (and logs a `401`) if the two differ.
- **To change (rotate) it:** generate a new value, update `Api__Key` on `FileSyncService` and `FileSync__ApiKey` on `GuidantShareApi` (re-run `install-api-service.ps1`), then restart both services. There is a short gap where plans show "unavailable" if you restart them one after the other.
- **To check it:** `Invoke-RestMethod http://localhost:5000/api/workflows` without the key must fail with `401`; with `-Headers @{ "X-Api-Key" = $key }` it must work. `https://guidantshare.guidantmso.com/health` should then show `fileSync: ok`.

### Step 1. Azure portal
1. Virtual machines → `SFTPSRVR` → **Overview** → click the **Public IP address**.
2. **Configuration**: Assignment **Static**; **DNS name label** e.g. `guidant-sftp` → Save. Note `<FQDN>` (`guidant-sftp.<region>.cloudapp.azure.com`).
3. VM → **Networking** (Network settings) → note whether there is an NSG on the NIC, the subnet, or both; add the rules to each. **Add inbound port rule** twice:

| Name | Source | Source IP | Dest port | Protocol | Priority |
|---|---|---|---|---|---|
| Allow-HTTPS-Home | IP Addresses | `<HOMEIP>/32` | 443 | TCP | 300 |
| Allow-HTTP | Any | | 80 | TCP | 310 |

Port 80 is open to Any because Let's Encrypt checks from its own addresses to issue the certificate; Caddy only answers that check and redirects there. No rule for 5000, 5080 or 5173. Leave the RDP rules as they are.

### Step 2. Reachability test (changes nothing in FileSync)
VM, PowerShell as Administrator:
```powershell
Get-NetTCPConnection -LocalPort 80,443 -State Listen -ErrorAction SilentlyContinue   # should print nothing (IIS etc.)
New-NetFirewallRule -DisplayName "GuidantShare HTTP" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow
New-NetFirewallRule -DisplayName "GuidantShare HTTPS" -Direction Inbound -Protocol TCP -LocalPort 443 -RemoteAddress <HOMEIP> -Action Allow
```
Throwaway listener (answers only `/api/health`):
```powershell
$l = [System.Net.HttpListener]::new(); $l.Prefixes.Add("http://+:80/"); $l.Start(); "listening, Ctrl+C to stop"
try { while ($true) {
  $t = $l.GetContextAsync(); while (-not $t.Wait(500)) {}
  $c = $t.Result; "$($c.Request.RemoteEndPoint) $($c.Request.Url.AbsolutePath)"
  if ($c.Request.Url.AbsolutePath -eq "/api/health") { $b = [Text.Encoding]::UTF8.GetBytes('{"status":"ok"}') } else { $c.Response.StatusCode = 404; $b = [byte[]]@() }
  $c.Response.OutputStream.Write($b, 0, $b.Length); $c.Response.Close()
} } finally { $l.Stop() }
```
Your PC: `curl.exe http://<FQDN>/api/health` → `{"status":"ok"}`. Then Ctrl+C on the VM. If it hangs: check both NSGs ("Effective security rules"), the Windows rule, and `nslookup <FQDN>`. **Do not continue until this works.**

### Step 3. Record and back up (before touching FileSync)
```powershell
cd C:\Services\FileSync
sc.exe qc FileSyncService                      # confirm the exe path (expected C:\Services\FileSync\bin)
(Get-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Services\FileSyncService).Environment   # note existing env vars
git rev-parse HEAD; git status                 # note the commit; local edits would block the pull
Get-Content bin\appsettings.json               # deploy empties bin: note any customisations
Invoke-RestMethod http://localhost:5000/api/workflows | % { $_ } | Select name, workflowId, isEnabled, direction | Export-Csv C:\Services\jobs-before.csv -NoTypeInformation
.\scripts\filesync.ps1 -Action Stop            # the OLD script: stops service and old dashboard
$bk = "C:\Services\backup-$(Get-Date -Format yyyyMMdd-HHmm)"
robocopy C:\Services\FileSync $bk\FileSync /E /XD ms-playwright node_modules .git
.\bin\FileSync.Service.exe --export-master-key  # store the key in your password manager
```
Rollback = stop the service, copy `$bk\FileSync\bin` and `$bk\FileSync\data` back, start. The old code still reads a database with the two new columns.

If `bin\appsettings.json` or an environment variable sets `Api:Urls` to anything but localhost, or `ASPNETCORE_ENVIRONMENT` to `Development`, fix that first.

### Step 4. Upgrade FileSync in place
```powershell
$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); $key = [Convert]::ToBase64String($b)
$key    # save in your password manager
$p = "HKLM:\SYSTEM\CurrentControlSet\Services\FileSyncService"
$cur = @((Get-ItemProperty $p -Name Environment -ErrorAction SilentlyContinue).Environment | ? { $_ -and $_ -notlike "Api__Key=*" })
Set-ItemProperty $p -Name Environment -Type MultiString -Value ($cur + "Api__Key=$key")

cd C:\Services\FileSync
git pull origin main
.\scripts\filesync.ps1 -Action Deploy
```
Do not delete `data\`, `staging\` or the service account. Check:
```powershell
$h = @{ "X-Api-Key" = $key }
Invoke-RestMethod http://localhost:5000/api/health
Invoke-RestMethod http://localhost:5000/api/workflows                  # must FAIL with 401
(Invoke-RestMethod http://localhost:5000/api/workflows -Headers $h) | % { $_ } | Select name, isEnabled   # matches jobs-before.csv
Invoke-RestMethod "http://localhost:5000/api/runs?limit=10" -Headers $h  # new runs succeed (passwords and ShareFile token carried over)
```
Then set website names:
```powershell
.\scripts\Set-JobPlans.ps1 -Export C:\Services\plans.csv -ApiKey $key     # fill plan and folderLabel in the CSV
.\scripts\Set-JobPlans.ps1 -Apply  C:\Services\plans.csv -ApiKey $key -WhatIf
.\scripts\Set-JobPlans.ps1 -Apply  C:\Services\plans.csv -ApiKey $key
```
Afterwards delete leftover `src\FileSync.Dashboard` (untracked `node_modules`/`dist`).

### Step 5. Install GuidantShare
Prerequisites: `dotnet --version` (10), `node --version`, `git --version`; the VM's GitHub access must cover the GuidantShare repo (a deploy key works for one repo only).
```powershell
git clone git@github.com:stephengiang-guidantmso/GuidantShare.git C:\Services\repos\GuidantShare
cd C:\Services\repos\GuidantShare
.\deploy\publish-windows.ps1
.\deploy\install-api-service.ps1 -PublicHost <FQDN>        # paste the same $key; answer N to mail for now
curl.exe http://127.0.0.1:5080/health                    # fileSync: ok
cd C:\Services\GuidantShare\api
.\GuidantShare.Api.exe create-superadmin --email you@guidantmso.com        # open the link later with <FQDN> as the host
```

### Step 6. Caddy (HTTPS)
Download `caddy_windows_amd64` to `C:\Services\Caddy\caddy.exe`, and NSSM (nssm.cc).
```powershell
Copy-Item C:\Services\repos\GuidantShare\deploy\Caddyfile C:\Services\Caddy\
cd C:\Services\Caddy; $env:GS_HOST = "<FQDN>"; .\caddy.exe validate --config Caddyfile
nssm install Caddy C:\Services\Caddy\caddy.exe run --config C:\Services\Caddy\Caddyfile
nssm set Caddy AppDirectory C:\Services\Caddy
nssm set Caddy AppEnvironmentExtra GS_HOST=<FQDN>
nssm set Caddy AppStderr C:\Services\Caddy\caddy.log
nssm start Caddy
Get-Content C:\Services\Caddy\caddy.log -Tail 30     # "certificate obtained successfully"
```
With 443 limited to your IP, Caddy proves ownership over port 80. If issuance fails, read the log; as a fallback add `tls internal` (browser warning).

### Step 7. ShareFile for GuidantShare
Register a second ShareFile API app (separate connection from FileSync) with redirect URI `https://<FQDN>/api/sharefile/callback`. Sign in at `https://<FQDN>` → **Settings** → save → **Connect**. If you cannot register one yet, use redirect `https://secure.sharefile.com/oauth/oauthcomplete.aspx` and paste the code. Do not copy FileSync's refresh token.

### Verification (from your PC)
- `curl.exe https://<FQDN>/health` → `status ok`, `fileSync ok`, `sharefile connected`; certificate is valid.
- `Test-NetConnection <FQDN> -Port 5000` and `-Port 5080` → False.
- From another network (phone off Wi-Fi): `https://<FQDN>` does not load (443 is home-only).
- Browser: sign in; FileSync screen lists the same jobs; a real run and Verify Sync succeed; Health Plans shows the plans and folders; download listing works; upload one harmless test file and confirm FileSync delivers it (choose a safe job).
- `curl.exe https://<FQDN>/api/filesync/workflows` → 401.
- Restart `GuidantShareApi`: FileSync runs are unaffected.
- Monday morning: check the FileSync run history and retry queue.

### Later, when the domain is ready
A record `guidantshare.guidantmso.com` → the same static IP; set `GS_HOST` to it and restart Caddy; re-run `install-api-service.ps1 -PublicHost guidantshare.guidantmso.com`; update the ShareFile redirect URI; change the 443 rule (Azure and Windows) from your IP to Any.

### Troubleshooting the deployment

| Symptom | Likely cause | Fix |
|---|---|---|
| The reachability test hangs | An NSG rule (NIC or subnet) or the Windows rule is missing | Azure "Effective security rules"; `Get-NetFirewallRule -DisplayName "GuidantShare*"` |
| FileSync service will not start after the upgrade | `Api__Key` is not set on the service | Set it (Step 4), then `.\scripts\filesync.ps1 -Action Start` |
| `400 Bad Request` for the site's address | The host name is not in `AllowedHosts` | Re-run `install-api-service.ps1 -PublicHost <name>` |
| Health Plans says "unavailable" | FileSync is down, or the two API keys differ | Check `http://localhost:5000/api/health`; the keys must match exactly |
| Sign-in does nothing | The site is opened over `http://` | Use `https://`; the session cookie is only sent over HTTPS |
| No certificate | Port 80 blocked, or DNS label not set | Read `C:\Services\Caddy\caddy.log` |

The scripts `Export-Jobs.ps1` and `Import-Jobs.ps1` are **not** part of this upgrade. They exist for rebuilding FileSync on a different machine, where passwords have to be typed in again.
