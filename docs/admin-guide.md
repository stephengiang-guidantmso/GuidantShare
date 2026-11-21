# Admin guide

For superadmins. Only superadmins see **FileSync**, **Users** and **Settings**.

### Users page

#### Inviting someone

1. Click *Invite*, enter the email and choose a role (*User* or *SuperAdmin*).
2. The app saves the invite first, then emails a link (valid **72 hours**, **single use**). If sending fails the invite still exists; the page shows the status and error and you can resend.
3. You can also copy the link and send it yourself. The link is only shown to you.
4. The person opens the link, picks a password, and can then sign in.

Invite rules: the email must be valid, not already a user, and not already have a pending invite (use *Resend* instead). *Resend* issues a fresh link, at most once a minute and up to 20 times per invite. *Revoke* cancels a pending invite.

#### Managing users

- **Role:** change between User and SuperAdmin.
- **Deactivate:** blocks sign-in, and the app treats the account as signed out from then on. Reactivate any time.
- **Change password:** you type a new password for the person (the usual password rules apply). They are signed out everywhere; tell them the new password yourself.
- **Send reset link:** emails a reset link and also shows it to you.
- **Remove:** deletes the account. Their upload history is kept.
- You cannot deactivate, demote or remove yourself.

#### Roles

| Role | Can do |
|---|---|
| User | Browse plans, download, upload |
| SuperAdmin | Everything a user can, plus FileSync, Users and Settings |

### Settings page

Settings holds **GuidantShare's own ShareFile connection**. (FileSync has a separate connection to the same ShareFile account; connecting one does not affect the other.)

#### 1. ShareFile app settings

Save the subdomain, client ID and client secret of the ShareFile API app, and the redirect URI registered on it. The secret is encrypted and never shown again. Leave the secret blank to keep the saved one.

Use either:

- `https://<your-host>/api/sharefile/callback` (one-click connect), or
- `https://secure.sharefile.com/oauth/oauthcomplete.aspx` and paste the code afterwards.

The value saved here must match what is registered on the ShareFile app exactly.

#### 2. Connect

*Connect to ShareFile* opens ShareFile in a new tab. Sign in as the **owner account** and approve. The app keeps only the refresh token (encrypted). *Reconnect* replaces it; *Disconnect* removes it and users will see "ShareFile is disconnected".

### FileSync page

The same screens as FileSync's own dashboard (jobs, transfer ledger, retry queue, SFTP explorer, audit logs, Verify Sync, portal code prompt), served through GuidantShare. FileSync itself is not reachable from the internet. Two additions:

- Each job has **Website Health Plan** and **Website Folder Name** fields. They decide where the job appears for users. See [Plans and folders](admin-guide.md#plans-and-folders).
- A yellow warning lists jobs that are labelled wrongly (no plan, no ShareFile folder, or ShareFile folders that disagree inside one website folder).

Saved partner passwords are never shown here. Leave the password blank to keep the saved one, or type a new one to replace it. Every change you make on this page is recorded (who, which call, result).

### What admins never see

Partner passwords. The app has no place to enter them and no field that holds them.

## Plans and folders

GuidantShare has no catalog of its own. The health plans and folders users see are **built live from FileSync's jobs**, so they cannot go stale and nobody enters them twice.

### How a job becomes a folder

Each FileSync job has two website fields, set in **FileSync → Transfer Jobs → edit job**:

| Job field | Meaning |
|---|---|
| **Website Health Plan** | The plan card on the home page, for example `Clever Care`. Blank = the job is not shown to users |
| **Website Folder Name** | The folder inside the plan, for example `278` or `UM`. Blank = the plan name |

The job's direction decides Download or Upload: *remote ➜ ShareFile* is **Download**, *ShareFile ➜ remote* is **Upload**. Only **enabled** jobs are shown. A disabled job stays visible in the FileSync screen only.

### Grouping

Jobs with the same plan, direction and folder name become **one** folder, whatever login or server they use. Plan and folder names are matched ignoring upper and lower case.

Example. Four jobs, all with Website Health Plan `Clever Care`:

| Job | Direction | Folder name |
|---|---|---|
| Clever Care 1 - 278 | Upload | 278 |
| Clever Care 1 - UM | Upload | UM |
| Clever Care 2 - 278 | Upload | 278 |
| Clever Care 2 - UM | Upload | UM |

Users see one **Clever Care** card. Inside it, **Upload** lists two folders, **278** and **UM**. The two logins stay separate in FileSync, which does the syncing.

### The ShareFile folder

A folder's ShareFile location is the job's **ShareFile Folder ID**. Jobs that are grouped into one folder should point at the same ShareFile folder. If they do not (for example a typo in one job), the FileSync screen shows a warning naming both jobs. Users still see the folder, using the ShareFile folder of the **oldest** job, so nothing disappears while you fix it.

GuidantShare reads that folder with its own ShareFile connection (Settings), which must be able to open every folder. The startup log lists any folder it cannot open.

### What users never see

The website only receives plan name, folder name, direction, ShareFile folder id and its display path. Hosts, logins, remote paths and passwords stay in FileSync.

### Freshness and outages

GuidantShare asks FileSync for the list about every 45 seconds. A change in FileSync shows up on the website within about a minute (SuperAdmins can force it with `?refresh=true`). If FileSync cannot be reached, users keep seeing the last list with a notice that it may be out of date, and browsing and uploading in ShareFile keep working. If FileSync was never reachable since the site started, users see a clear "unavailable" message.

### Subfolders

A folder may point at a parent that has subfolders. Users can open every subfolder beneath it, but only folders the server has listed from it.

### Removing a plan or folder

Disable or delete the job in FileSync (or clear its Website Health Plan). Nothing is deleted in ShareFile. Upload history stays readable.

### New environment

1. Create the superadmin and connect ShareFile (Settings).
2. In FileSync, set Website Health Plan and Website Folder Name on each job and enable it.
3. Sign in as a normal user and open each plan.
