# Hosting GuidantShare at guidantshare.guidantmso.com

Takes the site from the temporary test address to its official address, open to users. Run VM commands in PowerShell as Administrator. How the VM was first set up is in [Operations](operations.md#go-live-deploy-to-the-sftp-server-vm).

Only ports 80 and 443 are ever public. Never open 5000 (FileSync) or 5080 (GuidantShare's API).

The temporary address `https://20-169-248-229.sslip.io` stays up until step 3, which replaces it.

## 1. Ask for the DNS record and a static IP

| Ask | Details |
|---|---|
| A record, added where `guidantmso.com` DNS is managed (Wix) | Host `guidantshare`, value `20.169.248.229` |
| Confirm the public IP cannot change | Azure public IP `EDSVM-ip`, resource group `GuidantMSO_FS_RG`, assignment **Static** |

## 2. Wait for the record

```powershell
nslookup guidantshare.guidantmso.com
```

Continue only when it answers `20.169.248.229`.

## 3. Switch the services to the official name

Website service. This changes the two address settings only, so the FileSync key and mail secret stay as they are:

```powershell
$p = "HKLM:\SYSTEM\CurrentControlSet\Services\GuidantShareApi"
$vars = @((Get-ItemProperty $p).Environment | Where-Object { $_ -notlike "AllowedHosts=*" -and $_ -notlike "Email__PublicBaseUrl=*" })
Set-ItemProperty $p -Name Environment -Type MultiString -Value ($vars + "AllowedHosts=guidantshare.guidantmso.com;localhost;127.0.0.1" + "Email__PublicBaseUrl=https://guidantshare.guidantmso.com")
Restart-Service GuidantShareApi
```

Reverse proxy. After this the temporary address stops working:

```powershell
Set-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Services\caddy -Name Environment -Type MultiString -Value @("GS_HOST=guidantshare.guidantmso.com")
sc.exe config caddy start= auto
Restart-Service caddy
```

HTTPS stays limited to your own address while you test. If that address has changed, update it here and in the Azure rule `Allow-HTTPS-Home`:

```powershell
Set-NetFirewallRule -DisplayName "GuidantShare HTTPS" -RemoteAddress <your-ip>
```

## 4. Test

```powershell
Get-Content C:\Services\Caddy\caddy.log -Tail 20    # certificate obtained successfully
```

```powershell
curl.exe https://guidantshare.guidantmso.com/health
```

Expect `status ok`, `fileSync ok`, `sharefile connected`, `email ok`. Then in a browser:

- Sign in. The FileSync screens list the jobs.
- Open a plan, download a file and upload a harmless test file.
- Send yourself an invite and check that its link uses the new address.

## 5. ShareFile redirect address

In the ShareFile app registration used by GuidantShare, set the redirect URI to `https://guidantshare.guidantmso.com/api/sharefile/callback`, and save the same value under **Settings**. The existing connection keeps working; this matters the next time you press **Connect**.

## 6. Before opening to everyone

- [ ] The superadmin password is long and used nowhere else. There is no two-step sign-in; 5 wrong passwords lock an account for 15 minutes.
- [ ] Whoever owns security or compliance has agreed that these files can be reached from the internet.
- [ ] The vulnerable SQLite library that the build warns about (`NU1903`) is updated.
- [ ] The mail client secret's expiry date is in your calendar. Invites and password resets stop when it expires.
- [ ] The nightly task's last result is 0: `schtasks /query /tn "Guidant backup" /v /fo list`

Backups are the nightly copies on the VM only (no Azure Backup, by choice). They do not survive losing the disk, so copy the newest folder in `C:\Services\backups` off the VM now and then, to a place only admins can open.

## 7. Open the site

```powershell
Set-NetFirewallRule -DisplayName "GuidantShare HTTPS" -RemoteAddress Any
```

In Azure: VM → **Network settings** → rule `Allow-HTTPS-Home` → set **Source** to **Any** → Save.

Check from a phone with Wi-Fi off that the site loads, and that these do **not** answer:

```powershell
Test-NetConnection guidantshare.guidantmso.com -Port 5000
Test-NetConnection guidantshare.guidantmso.com -Port 5080
```

Run those two from your own computer; `TcpTestSucceeded` must be `False`.

## 8. Invite users

Send invites from **Users**. Resend any sent before the switch, since their links use the old address.

## Taking the site offline again

```powershell
Stop-Service caddy
```

This closes the website only. FileSync keeps transferring. `Start-Service caddy` brings it back.
