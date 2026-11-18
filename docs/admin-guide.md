# Admin guide

For superadmins. Only superadmins see **Settings** (ShareFile and catalog) and **Users**.

## Users page

### Inviting someone

1. Click *Invite*, enter the email and choose a role (*User* or *SuperAdmin*).
2. The app saves the invite first, then emails a link (valid **72 hours**, **single use**). If sending fails the invite still exists; the page shows the status and error and you can resend.
3. You can also copy the link and send it yourself. The link is only shown to you.
4. The person opens the link, picks a password, and can then sign in.

Invite rules: the email must be valid, not already a user, and not already have a pending invite (use *Resend* instead). *Resend* issues a fresh link, at most once a minute and up to 20 times per invite. *Revoke* cancels a pending invite.

### Managing users

- **Role:** change between User and SuperAdmin.
- **Deactivate:** blocks sign-in, and the app treats the account as signed out from then on. Reactivate any time.
- **Reset password:** sends a reset link and also shows it to you.
- You cannot deactivate or demote yourself.

### Roles

| Role | Can do |
|---|---|
| User | Browse plans, download, upload |
| SuperAdmin | Everything a user can, plus Settings and Users |

## Settings page

### 1. ShareFile app settings

Save the subdomain, client ID and client secret of the ShareFile API app, and the redirect URI registered on it. The secret is encrypted and never shown again. Leave the secret blank to keep the saved one.

Use either:

- `https://<your-host>/api/sharefile/callback` (one-click connect), or
- `https://secure.sharefile.com/oauth/oauthcomplete.aspx` and paste the code afterwards.

The value saved here must match what is registered on the ShareFile app exactly.

### 2. Connect

*Connect to ShareFile* opens ShareFile in a new tab. Sign in as the **owner account** and approve. The connection is shared by everyone, and the app keeps only the refresh token (encrypted). *Reconnect* replaces it; *Disconnect* removes it and users will see "ShareFile is disconnected".

### 3. Health plans and folders

This is where everything users see on the home page comes from. See the [Catalog](catalog.md) page for rules and examples. In short:

- **Add a health plan** by name. Names must be unique among active plans.
- **Add folder** to a plan: direction, label, the **ShareFile folder link (required)**, how the partner is reached (SFTP host and port, or web portal), the remote path, the login username and an optional note.
- **Edit** re-checks the link against ShareFile. **Remove** hides a folder or plan; nothing is deleted in ShareFile.
- ShareFile must be connected before you can save a folder, because the link is verified.

## Day-to-day tasks

| Task | How |
|---|---|
| Add a new partner | Add a health plan, then add its download and upload folders |
| A second login at the same partner | Add another folder in the same plan with that username; if it points at the same ShareFile folder users see it once |
| Change which ShareFile folder feeds a plan | Edit the folder and paste the new link |
| Rename a plan | *Rename* on the plan card |
| Take a plan away | *Remove* on the plan card (history stays) |
| Someone left the team | Deactivate their user |
| Rotate the ShareFile secret | Save new settings, then *Reconnect* |

## What admins never see

Partner passwords. The app has no place to enter them and no field that holds them.
