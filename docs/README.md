# GuidantShare documentation

GuidantShare is an invite-only web app on top of ShareFile for health-plan files, fed by FileSync's jobs. The pages:

| Page | What is in it |
|---|---|
| [README](README.md) (this page) | Overview, ground rules, glossary, first-time setup on a developer machine |
| [User guide](user-guide.md) | Signing in, finding plans, downloading, uploading |
| [Admin guide](admin-guide.md#admin-guide) | Users, invites, Settings (ShareFile), the FileSync page, and how plans and folders are built from FileSync jobs |
| [Architecture](architecture.md#architecture) | Components, request flows, security, ShareFile integration, data model |
| [Reference](reference.md) | Every API endpoint and every configuration setting |
| [Operations and deployment](operations.md#day-to-day-operations) | Running it, logs, backups, production deployment, **go-live on the VM**, troubleshooting |
| [Hosting at the official address](go-public.md) | Steps and commands to move to guidantshare.guidantmso.com and open the site |
| [Development and testing](development.md#development) | Repository layout, conventions, the test suites, testing locally with mock data |

## Ground rules the app is built around

1. **One shared ShareFile connection for the website.** A superadmin connects the owner's ShareFile account once; everyone on the website uses it. (FileSync has its own, separate connection.)
2. **No passwords for partners, ever.** The app never stores or shows partner SFTP or portal passwords.
3. **FileSync is the source of the plans.** FileSync (private, on the same machine) syncs partners and ShareFile and holds the jobs. GuidantShare builds the health plans and folders from those jobs through FileSync's API-key-protected localhost API, and offers FileSync's screens to superadmins. It never reads FileSync's database, config or secrets, and FileSync is never exposed to the internet.
4. **Invite-only.** There is no self sign-up.
5. **Only superadmins see FileSync, Users and Settings.**

## Overview

### What it is

GuidantShare gives a small team a simple way to find and send health-plan files. Behind it, a background tool (FileSync) copies files between each partner's SFTP server or web portal and folders in ShareFile. GuidantShare is the friendly front door to those ShareFile folders. The list of health plans and folders is built from FileSync's jobs, so it is always current.

- **Download folders** hold files that arrived *from* a partner (FileSync copies partner ➜ ShareFile).
- **Upload folders** hold files going *to* a partner (you drop a file in; FileSync copies ShareFile ➜ partner).

### What users can do

- Sign in with an invited account.
- Pick a health plan from the home page.
- Browse a plan's download folders, including subfolders, search by name and page through files.
- Upload a file to a plan's upload folder, with a check for duplicate names and an overwrite choice.
- Open any folder directly in ShareFile.

### What superadmins can do

- Connect and disconnect the website's ShareFile account.
- Use the full FileSync screens (jobs, transfers, retries, logs, Verify Sync) and decide, per job, which health plan and folder it shows under.
- Invite users, resend or revoke invites, deactivate or remove users, change their passwords.

### What it deliberately does not do

- It does not move files between partners and ShareFile itself. FileSync does that.
- It does not store or display partner passwords.
- It does not offer self-service sign-up.
- It does not delete anything in ShareFile. Disabling a job only hides its folder in the app.
- It does not proxy file contents for downloads. Users open files through ShareFile.

### Who the "partners" are

Each health plan or service has its own SFTP server or web portal (for example Aetna's SecureTransport portal, SCAN's SFTP and web portal, Office Ally's SFTP). Those connections, logins and paths live in FileSync's jobs, not in this app, and users never see them.

### Key terms

See the [Glossary](README.md#glossary). The most important: a **plan** is one card on the home page, and a **folder** is one ShareFile folder with a direction (Download or Upload), shown inside its plan.

## Glossary

| Term | Meaning |
|---|---|
| **Health plan** | A card on the home page, usually one partner or service (Aetna, SCAN, CAQH). Built from the *Website Health Plan* field of FileSync jobs. |
| **Folder** | One ShareFile folder under a plan with a direction, for example *278* under Clever Care. Jobs with the same plan, direction and *Website Folder Name* are one folder. |
| **Direction** | `Download` = files from the partner into ShareFile. `Upload` = files you drop in ShareFile to be sent to the partner. |
| **ShareFile folder link** | The web address of a folder in ShareFile; it contains the folder id. In FileSync the id is entered on the job. |
| **Folder id** | ShareFile's identifier for a folder; starts with `fo`. The app uses ids, not path text. |
| **Shown to users as** | The ShareFile path text displayed to users. A label only. |
| **Remote** | The partner side of a FileSync job: an SFTP server (host, port, path) or a web portal. Not shown on the website. |
| **SFTP** | Secure file transfer to a partner's server. FileSync performs these transfers. |
| **Portal** | A partner's website used instead of SFTP (for example Aetna SecureTransport, SCAN provider portal, Champion on files.com). |
| **Login username** | The partner account name a FileSync job uses. Passwords live encrypted in FileSync and are never shown. |
| **FileSync** | The separate, private service that moves files between partners and ShareFile and holds the jobs. GuidantShare reads the plans from it and shows its screens to superadmins. |
| **Website Health Plan / Website Folder Name** | The two job fields that decide where a job appears on the website. |
| **Superadmin** | A user who can see Settings and Users. |
| **Invite** | A single-use, 72-hour link that lets someone choose a password. There is no self sign-up. |
| **Connection (ShareFile)** | The one shared, authorized link between the app and the owner's ShareFile account, held as an encrypted refresh token. |
| **Refresh token / access token** | Long-lived and short-lived ShareFile credentials. Only the refresh token is stored, encrypted. |
| **Master key** | `data/keys/master.key`, the AES-256 key that encrypts stored secrets. |
| **Audit** | A record of an upload attempt (`UploadAudits`) or sign-in event (`LoginAudits`). |
| **Soft delete** | Hiding a plan or folder (`IsActive = false`) without deleting its rows. |
| **CSRF token** | A value fetched from `/api/auth/csrf` and sent as `X-CSRF-TOKEN` on every write. |
| **Graph** | Microsoft Graph, used optionally to send invite and reset emails. |
| **Log mode** | The email setting that writes messages to the log instead of sending them. |

## Getting started

### Requirements

- .NET 10 SDK (the project expects `dotnet` on the `PATH` or in `~/.dotnet`)
- Node.js and npm (for the web UI)
- A ShareFile account you can authorize an API app on (client ID, client secret, subdomain)

### Build and run on a developer machine

```bash
# 1. Install the web dependencies and start the web dev server (http://localhost:5173)
cd src/GuidantShare.Web && npm install && npm run dev

# 2. In another terminal start the API in the background on http://localhost:5080 (it serves no UI)
scripts/system.sh --start
```

`scripts/system.sh --stop`, `--restart` and `--help` are described in [Operations](operations.md#day-to-day-operations). You can also run it in the foreground with `dotnet run --project src/GuidantShare.Api`.

> Use one operating-system user for building and running. Mixing users leaves files the other cannot update (for example `obj/` timestamps or `data/keys/master.key`).

### First-time setup

1. **Create a superadmin.**
   ```bash
   dotnet run --project src/GuidantShare.Api -- create-superadmin --email you@example.com
   ```
   Without `--password` it prints a one-time set-password link (valid 72 hours). With `--password` the account is created directly.
2. **Sign in** at http://localhost:5080 and open **Settings**.
3. **Save the ShareFile app settings** (section 1): subdomain (the part before `.sharefile.com`), client ID, client secret, and the redirect URI registered on the ShareFile app.
4. **Connect** (section 2): click *Connect to ShareFile* and sign in as the owner account. If your registered redirect URI is ShareFile's generic `oauthcomplete.aspx` page, paste the code (or the whole address containing `code=`) into the box and click *Finish connecting*.
5. **Set up the plans in FileSync.** On each FileSync job set *Website Health Plan* and *Website Folder Name* and enable it. See [Plans and folders](admin-guide.md#plans-and-folders). GuidantShare needs FileSync running (`FileSync:BaseUrl`, `FileSync:ApiKey`); for a development machine without FileSync data see the mock setup in [Development](development.md#development).
6. **Invite users** from the Users page.

The email setting defaults to `Log`, which writes invite and reset links to the application log instead of sending them. In that mode the invite dialog also shows the link so you can copy it. See [Configuration](reference.md#configuration) for real email.

### Where things are stored

| Thing | Location |
|---|---|
| Database (users, invites, audits, encrypted secrets) | `src/GuidantShare.Api/data/app.db` |
| Master encryption key | `src/GuidantShare.Api/data/keys/master.key` |
| Application logs | `src/GuidantShare.Api/data/logs/wrapper-YYYYMMDD.log` |
| Script console output | `src/GuidantShare.Api/data/logs/console.out` |

All of these are gitignored. Deleting the database gives you a clean slate (you will need to recreate the superadmin and re-enter the ShareFile settings; the master key must stay with the database it encrypted).

### Running the tests

```bash
dotnet test
```

See [Testing](development.md#testing).
