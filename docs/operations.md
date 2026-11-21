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

For the one-time setup of the VM, the Azure and DNS steps and carrying the FileSync jobs over, follow the [Go-live guide](operations.md#go-live-from-scratch-clean-install-on-the-sftp-server-vm). This page is the reference.

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

The API and the SPA are separate: the API is published to `api\`, the SPA build to `web\` (served by Caddy). The API can still serve a SPA from its own `wwwroot` for development, but production does not rely on it.

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
| Blank page or old UI | `wwwroot` is missing or stale | `cd src/GuidantShare.Web && npm run build`, then restart |

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

## Go live from scratch: clean install on the SFTP server VM

How to set up `SFTPSRVR` (the Windows VM) from scratch, point `guidantshare.guidantmso.com` at it, and carry your **FileSync jobs** over to a clean FileSync. Production data is erased on purpose: a new FileSync database, a new GuidantShare database, new keys. Only the job *definitions* are saved and re-loaded.

> **Written without being run on the VM.** Every command and screen name below is from documentation and the repo's scripts, not from a live run. Where a step depends on how your Azure subscription or Wix account is set up, it says so. Read the whole guide once before starting.

### 0. The plan in one page

| Part | What | Who | Downtime |
|---|---|---|---|
| A | Save the old jobs, the old plan list and notes on settings (old system still running) | you | none |
| B | Azure public IP and firewall: let 443 (and 80) in | someone with rights on the VM's resource group | none |
| C | DNS: the A record for `guidantshare.guidantmso.com` | whoever manages the Wix DNS (Sheel offered) | none |
| D | FileSync: wipe, install the new version, import jobs, check each, enable | you | **transfers stop** until jobs are enabled again |
| E | GuidantShare: install, connect ShareFile, create the admin | you | none |
| F | Caddy (HTTPS) | you | none |
| G | Verify from outside | you | none |
| H | Cut over and clean up | you | none |

Do A, B and C first (they have waiting time: DNS and access requests). Do D in a quiet period.

Ports at the end (only `443`, and `80` for certificates, may be reachable from the internet):

| Port | What | Reachable from |
|---|---|---|
| 443, 80 | Caddy | the internet |
| 5080 | GuidantShare.Api | this machine only (`127.0.0.1`) |
| 5000 | FileSync API | this machine only (`localhost`) |
| 5173 | old FileSync dashboard | **retired**: remove it and its firewall rule |

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

### 1. What you need

- Administrator access on the VM (RDP).
- On the Azure subscription, for the VM's resource group `SFTPSRVR-RG`: a role that can edit the VM's network settings (for example **Network Contributor**), or someone who has it. With only *Security Reader* the Networking page fails with an authorization error.
- Access to wherever the DNS for `guidantmso.com` is managed (it was at Wix), or the person who has it.
- The ShareFile owner account, and the ShareFile API apps' client ID and secret (FileSync's existing one; a second one for GuidantShare, see E2).
- The Microsoft 365 (Entra) app for mail if you want invite emails and FileSync alert/code emails (see D5 and E5).
- A password manager for the new secrets.
- On the VM: .NET 10 SDK (to build) or the ASP.NET Core 10 runtime (to run published output), Git, and either Node.js (to build the web app on the VM) or a build made on another machine.

### A. Save what you want to keep (old system still running)

Do this before wiping anything. Put the results in a folder such as `C:\Services\migration-<date>\` and keep it until you are happy with the new system.

#### A1. Export the FileSync jobs

From the FileSync repo on the VM (or any machine that can reach FileSync's API):

```powershell
cd C:\Services\FileSync\scripts      # or wherever the repo is
.\Export-Jobs.ps1 -Out C:\Services\migration-<date>\jobs-export.json
```

(The old FileSync has no API key, so none is needed. With the new one add `-ApiKey`.)

What it saves per job: name, direction, remote type (SFTP, Aetna, Champion, SCAN), polling and retry settings, include/exclude patterns, remote path, ShareFile folder id and display path, and the connection details **without the password**: host, port, user name, the learned host-key fingerprint and the private-key file *path*.

What it deliberately does **not** save: passwords and key passphrases (FileSync never reveals them; you type them again during import) and private key files (copy those files yourself).

#### A2. Save the old plan list (so jobs get their website plan and folder name automatically)

If a GuidantShare with its hand-entered catalog is running somewhere, export its catalog as a CSV. The columns are what the import script matches on (ShareFile folder id + direction + user name). Stop that service or copy its `app.db` first so the file is consistent, then:

```powershell
sqlite3 -readonly -header -csv app.db "SELECT p.Name AS Plan, e.Label, e.Direction, e.SharefileFolderId, e.RemoteUsername AS Username FROM CatalogEntries e JOIN HealthPlans p ON p.PlanId = e.PlanId WHERE e.IsActive = 1 AND p.IsActive = 1 ORDER BY p.Name, e.Direction, e.Label;" > C:\Services\migration-<date>\old-catalog.csv
```

(`sqlite3` is a small separate tool; if the VM does not have it, copy `app.db` to a machine that does. Do not open the live file with a read-write tool.) If there is no old GuidantShare, skip this: you will type *Plan* and *Folder name* on each job in the new FileSync screen, or in the JSON file.

#### A3. Write down what is not in the export

| Item | Where it lives now | In the clean install |
|---|---|---|
| FileSync's ShareFile connection | `C:\Services\FileSync\data` (encrypted) | Sign in again (D4) |
| Graph mail settings (SCAN code reader, alert emails) | `C:\Services\FileSync\data\graph-mail.json` | Copy that one file to the new `data\` (D5). It holds the tenant/app/mailbox and alert recipients; its client secret was already moved into the encrypted database, so put the secret in the file again |
| Private key files for key-based SFTP | paths shown in the job | Copy the files to the same paths |
| Web-portal sign-in state (SCAN "remember this device") | `C:\Services\FileSync\data\browser-profiles` | Starts empty: SCAN will ask for a one-time code on the first run |
| Master encryption key | `...\data\keys\master.key` | A new one is created; the old one is only needed to read the *old* database |
| Service environment variables | the services' registry keys | Set again (D2, E4) |
| Old transfer history (ledger, runs, 30-day archive) | `data\filesync.db`, `staging\` | Gone. Keep the old folder if you need to look something up |

#### A4. Keep a full copy of the old data

Stop `FileSyncService` (this pauses transfers, so pick the quiet period), copy `C:\Services\FileSync\data`, `C:\Services\FileSync\staging` and `C:\Services\FileSync\bin\appsettings.json` into the migration folder, and keep it somewhere safe. Do not copy the old `master.key` anywhere insecure: with the old database it decrypts every saved password.

### B. Azure: public IP and the network rules

1. **Find the VM.** Azure portal → *Virtual machines* → `SFTPSRVR` (resource group `SFTPSRVR-RG`, subscription "Azure subscription 1"). The default list showed other machines; search by name or filter by subscription.
2. **Know your IPs.** On the VM's *Overview*, note the **Public IP address**. This is the address people will connect *to*. The address the partners see FileSync connecting *from* (`20.169.248.229` in your notes) is the outbound address. They may be the same, but confirm: on the VM run `curl.exe https://ifconfig.me` (outbound) and compare with the portal. Do not assume.
3. **Make the public IP static.** Public IP resource → *Configuration* → Assignment **Static** → Save. If it is dynamic, the address can change when the VM is deallocated, which would break the DNS record and could break the partners' IP allow-lists.
4. **Open 443 and 80.** VM → *Networking* (or *Network settings*) → the network security group → *Add inbound port rule*: Source *Any* (or your own ranges), destination port **443**, protocol TCP, action Allow, a name like `Allow-HTTPS`; repeat for **80** (`Allow-HTTP`, used by Caddy to get and renew the certificate and to redirect to HTTPS). If this page shows an authorization error, you need the role in section 1.
5. **Do not open anything else.** In particular there must be no inbound rule for ports 5000, 5080 or 5173.
6. **Windows Firewall on the VM** (PowerShell as Administrator):
   ```powershell
   New-NetFirewallRule -DisplayName "GuidantShare HTTPS" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
   New-NetFirewallRule -DisplayName "GuidantShare HTTP"  -Direction Inbound -Protocol TCP -LocalPort 80  -Action Allow
   Get-NetFirewallRule -Enabled True -Direction Inbound | Get-NetFirewallPortFilter | Where-Object { $_.LocalPort -in 5000,5080,5173 }
   ```
   The last command must print nothing. If it lists a rule such as `FileSync API test` (from the earlier exposure tests), remove it: `Remove-NetFirewallRule -DisplayName "FileSync API test"`.

### C. DNS: the A record

`guidantshare.guidantmso.com` must resolve to the VM's **public IP** from step B2. Only a DNS record is needed (no Wix website hosting).

1. Whoever manages the domain's DNS (it was at Wix; Sheel offered): Wix dashboard → *Domains* → `guidantmso.com` → *Manage DNS records* (names vary slightly by Wix version).
2. Under **A (Host)** add a record: **Host name** `guidantshare`, **Points to** the public IP, TTL 1 hour (or the lowest offered). Save.
3. If the domain's name servers are not Wix's, the record belongs wherever the name servers point. Check with `nslookup -type=NS guidantmso.com`.
4. Check from any machine (changes can take minutes to hours):
   ```powershell
   nslookup guidantshare.guidantmso.com
   ```
   It should answer with the public IP. Do not start Caddy (F) until it does, or certificate issuance will fail and may be rate-limited for a while.

### D. FileSync: wipe, install the new version, import the jobs

#### D1. Stop and set aside the old data

```powershell
Stop-Service FileSyncService
Rename-Item C:\Services\FileSync\data    data-old-<date>
Rename-Item C:\Services\FileSync\staging staging-old-<date>
```

(Renaming keeps a way back. Delete them when you are sure.) FileSync creates fresh `data\` and `staging\` on its next start.

#### D2. Install the new FileSync and give it an API key

1. Update the code (your usual route, for example `scripts\filesync.ps1 Deploy`, or `git pull` and publish).
2. Choose a **long random API key** and keep it in your password manager. For example: `[Convert]::ToBase64String((1..32 | % { Get-Random -Maximum 256 }) -as [byte[]])`.
3. Give it to the FileSync service as an environment variable (a Windows service reads these from its registry key). As Administrator:
   ```powershell
   $key = Read-Host "FileSync API key"
   Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\FileSyncService" -Name Environment -Type MultiString -Value @("Api__Key=$key")
   ```
   If the service already has other `Environment` entries, keep them: read them first with `Get-ItemProperty` and add to the array. Outside Development FileSync **refuses to start without `Api__Key`**.
4. Keep it local: do **not** set `Api__Urls`. The default `http://localhost:5000` is what you want.
5. `Start-Service FileSyncService`, then check: `Invoke-RestMethod http://localhost:5000/api/health` answers, and `Invoke-RestMethod http://localhost:5000/api/workflows` is rejected (`401`) without the key.

#### D3. Install Chromium for the portal jobs

```powershell
C:\Services\FileSync\bin\FileSync.Service.exe --install-browsers
```

(Check `FileSync\scripts\filesync.ps1` to see whether your deploy step already does this.) The Aetna, Champion and SCAN jobs need it.

#### D4. Sign FileSync in to ShareFile

Put `secrets.json` (ShareFile app settings: client ID, secret, subdomain, redirect URI) in `C:\Services\FileSync`, then follow FileSync's `docs\SHAREFILE_OAUTH_GUIDE.md` (it prints an approval link and asks for the code; the command is `FileSync.Service.exe --authorize-sharefile`). The file is imported into the encrypted database and deleted. This is FileSync's own connection; GuidantShare gets a separate one (E3).

#### D5. Mail settings (optional but recommended)

Copy `graph-mail.json` from the migration folder (or from `config\graph-mail.example.json`) to `C:\Services\FileSync\data\graph-mail.json`, fill in the client secret, and see `docs\PORTAL_CONNECTORS.md` for the permissions. FileSync moves the secret into its encrypted database on first use and blanks it in the file.

#### D6. Import the jobs

Edit `jobs-export.json` only if you want to change something (for example set `plan` and `folderLabel` yourself). Then, from the FileSync scripts folder:

```powershell
# 1. Dry run: shows what would happen and which jobs would have no website plan
.\Import-Jobs.ps1 -File C:\Services\migration-<date>\jobs-export.json -CatalogCsv C:\Services\migration-<date>\old-catalog.csv -ApiKey $key -WhatIf

# 2. For real: asks for each server's password (hidden), imports every job DISABLED
.\Import-Jobs.ps1 -File C:\Services\migration-<date>\jobs-export.json -CatalogCsv C:\Services\migration-<date>\old-catalog.csv -ApiKey $key
```

- Jobs are imported **disabled** so nothing runs until you have checked it.
- `-CatalogCsv` fills *Website Health Plan* and *Website Folder Name* from the old catalog (matched by ShareFile folder id, direction and user name). Existing values are never overwritten. The script lists jobs that got no plan and old catalog rows that matched no job.
- Passwords are sent straight to FileSync and not written to disk. (`-PasswordsFile` exists for unattended use; delete that file afterwards.)
- A job whose id already exists in the target is skipped, so the import can be re-run safely.
- Copy any private key files to the paths the jobs name.

#### D7. Check every job, then enable it

In the FileSync screen (for now at `http://localhost:5173` from the VM, or later in the website under **FileSync**):

1. Open a job → **Test connection**. Fix any password or host key problem. Saving a job re-tests it.
2. Use **Verify Sync** to compare the SFTP and ShareFile folders.
3. Check the **Website Health Plan** and **Website Folder Name**; jobs of the same folder must share one ShareFile folder id (a yellow warning shows if not).
4. Enable the job (the toggle). Do the low-risk jobs first, and run one **Observation** (dry run) per new job before its first live run if you like.
5. SCAN's first sign-in will ask for a one-time code (typed into the prompt, or read from the mailbox if Graph is set up).

### E. GuidantShare

#### E1. Build and publish

On the VM (needs .NET 10 SDK, Node.js and Git), from the GuidantShare repo:

```powershell
.\deploy\publish-windows.ps1       # API -> C:\Services\GuidantShare\api, web -> C:\Services\GuidantShare\web
```

Or build on another machine and copy the two folders.

#### E2. A ShareFile API app for GuidantShare

FileSync and GuidantShare each use their **own** OAuth connection to the same ShareFile account (a refresh token belongs to one app). In ShareFile's API app registration create (or reuse a second) app and set its redirect URI to `https://guidantshare.guidantmso.com/api/sharefile/callback`. Keep its client ID and secret for E6.

#### E3. Install the service

```powershell
.\deploy\install-api-service.ps1
```

It asks for FileSync's API key (the same value as in D2) and, optionally, the Graph mail settings, creates the `GuidantShareApi` Windows service (automatic start, restart on failure) and starts it. The app listens on `127.0.0.1:5080` only (`appsettings.Production.json`).

#### E4. Create the first administrator

Run it **from the api folder** so it uses the same `data\` folder as the service:

```powershell
cd C:\Services\GuidantShare\api
.\GuidantShare.Api.exe create-superadmin --email you@guidantmso.com
```

It prints a one-time set-password link (valid 72 hours); the host in the link is the `Email:PublicBaseUrl` set for Production. Open it after Caddy (F) is running, or use `--password '<12+ characters>'` to set the password directly.

#### E5. Mail (optional)

If you did not set it in E3, invite emails are only written to the log and shown to the admin to copy. For real email set `Email__Mode=Graph`, `Email__TenantId`, `Email__ClientId`, `Email__Mailbox` and `Email__ClientSecret` (see [Configuration](reference.md#configuration)); the Entra app needs permission to send as that mailbox.

#### E6. Connect ShareFile

After F: sign in → **Settings** → save the subdomain, client ID, secret and the redirect URI from E2 → **Connect to ShareFile** and approve as the owner account.

### F. Caddy (HTTPS)

1. Install Caddy for Windows (download from caddyserver.com, or `winget`; check the package name) to a folder such as `C:\Services\Caddy`.
2. Copy `deploy\Caddyfile` there. It serves `C:\Services\GuidantShare\web`, proxies `/api/*` and `/health` to `127.0.0.1:5080` and gets the certificate automatically for `guidantshare.guidantmso.com`.
3. Check it: `caddy validate --config Caddyfile`.
4. Run it as a service with a wrapper (for example NSSM or WinSW) that runs `caddy run --config C:\Services\Caddy\Caddyfile` and starts automatically. Caddy itself does not install as a Windows service.
5. First start needs DNS (C) and ports 80/443 (B) working; watch Caddy's log for the certificate being issued.

### G. Verify from outside

From a machine **not** on the VM's network (your home PC, or a phone off Wi-Fi):

```powershell
Invoke-WebRequest https://guidantshare.guidantmso.com/health            # 200, status ok
Test-NetConnection guidantshare.guidantmso.com -Port 443               # True
Test-NetConnection <the public IP> -Port 5000                          # must FAIL (FileSync)
Test-NetConnection <the public IP> -Port 5080                          # must FAIL (API)
Test-NetConnection <the public IP> -Port 5173                          # must FAIL (old dashboard)
```

Then in the browser:

- [ ] The padlock is valid; `http://` redirects to `https://`.
- [ ] Sign-in works; **FileSync**, **Users** and **Settings** show for the admin only.
- [ ] Health Plans lists your plans; each plan's download and upload folders open; a test upload reaches ShareFile and is later sent on by FileSync.
- [ ] `https://guidantshare.guidantmso.com/api/filesync/workflows` signed out gives `401`; as a normal user `403`.
- [ ] In the FileSync screen, create/edit/run/cancel a job. Check on the partner side that the connection comes from the whitelisted IP.
- [ ] Restart `GuidantShareApi`: a running transfer in FileSync is not interrupted.
- [ ] `/health` shows `fileSync: ok`, `sharefile: connected`, `email: ok`.

### H. Cut over and clean up

1. Invite the real users (Users page) and tell them the address.
2. Retire the old FileSync dashboard: stop whatever serves port 5173 and delete its firewall rule. Remove `src\FileSync.Dashboard` from the FileSync repo once you are happy (it is no longer used).
3. Remove any other test rules or bindings (the `FileSync API test` rule, `bin\appsettings.json.bak` from the exposure tests).
4. Back up `C:\Services\GuidantShare\api\data` (`app.db` and `keys\master.key`, separately) and FileSync's `data\` on a schedule. See [Operations](operations.md#day-to-day-operations).
5. Delete the `data-old-<date>`, `staging-old-<date>` and migration folders when you no longer need them: they hold the old database and key.

### Rolling back

Nothing in the old setup is deleted until step H5, so you can go back: stop the new services, rename `data-old-<date>` and `staging-old-<date>` back, start the old FileSync, and remove the inbound 443/80 rules (and the DNS record) if you want the site gone.

### Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Caddy cannot get a certificate | DNS not pointing at the VM yet, or 80/443 blocked | Check C and B; `nslookup`, then the NSG rules and Windows Firewall |
| Site loads but Health Plans says "unavailable" | FileSync is down, or the two API keys differ | `Invoke-RestMethod http://localhost:5000/api/health`; the keys in D2 and E3 must match exactly (a `401` is in the GuidantShare log) |
| FileSync service will not start | `Api__Key` not set on the service | D2 step 3 |
| A plan or folder is missing | The job has no *Website Health Plan*, is disabled, or has no ShareFile folder id | FileSync screen; the yellow warning lists problems |
| Folder opens with an error | GuidantShare's ShareFile account cannot open that folder | Share the folder with the account that GuidantShare is connected as |
| Job shows "connection test failed" after import | Password skipped or wrong, host key changed | Edit the job, enter the password, test again |
| SCAN asks for a code every run | The portal's device trust was lost with the old profile | Enter the code once; the profile is remembered afterwards |
| `create-superadmin` made an admin the site does not know | It ran from a different folder, so it used another `data\` | Run it from `C:\Services\GuidantShare\api` (E4) |
