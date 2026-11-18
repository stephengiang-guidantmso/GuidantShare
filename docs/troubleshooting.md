# Troubleshooting

## Starting the app

| Symptom | Cause | Fix |
|---|---|---|
| `Access to the path '.../data/keys/master.key' is denied` | The key file is owned by another account | Run the app as the account that owns `data/`, or give the service account read access (`setfacl -m u:ACCOUNT:r data/keys/master.key`) |
| Build fails with `MSB3374 ... last write time ... cannot be set` | `obj/` or `bin/` files were created by a different user | Delete `bin/` and `obj/` in each project (they are regenerated), then build as the account that runs the app. Use one account for building and running |
| `address already in use` | Another process owns the port | `scripts/system.sh --stop`, or change `Urls` |
| App exits right away | Read `.run/server.out` | Look for missing key permissions, an unreadable database, or a bad setting |
| Blank page or old UI | `wwwroot` is missing or stale | `cd src/GuidantShare.Web && npm run build`, then restart |

## Signing in

| Symptom | Cause | Fix |
|---|---|---|
| "Invalid email or password." | Wrong credentials or a deactivated account | Reset the password or reactivate the user |
| "Too many attempts. Try again later." | 5 failures lock the account 15 minutes | Wait, or reset the password |
| Invite link says invalid or expired | Used already, revoked, or older than 72 hours | Resend the invite |
| No invite email | `Email:Mode` is `Log`, or Graph is misconfigured | Copy the link from the invite dialog or the log, or fix Graph (see `/health`) |
| "Invalid CSRF token. Refresh the page." | Stale session or token | Reload; sign in again |
| Constant `429` | Many requests from one address (behind a proxy without forwarded headers, every user shares one) | Configure forwarded headers |

## ShareFile

| Symptom | Cause | Fix |
|---|---|---|
| "ShareFile is disconnected. Ask a SuperAdmin to reconnect." | No refresh token, or ShareFile rejected it | Settings → Reconnect |
| Connect returns to Settings with "connection failed" | Wrong redirect URI or the approval timed out (10 minutes) | Make the redirect URI match the ShareFile app exactly, try again |
| "ShareFile rejected the request (400)" when finishing | Bad or already-used code | Start the connect again and paste the new code |
| Saving a folder: "doesn't look like a ShareFile folder link" | The address has no `fo...` id | Open the folder in ShareFile and copy the address from the browser |
| Saving a folder: "ShareFile can't find that folder" | The connected account cannot open it, or the id is wrong | Check access for the owner account; copy the link again |
| Saving a folder: "Couldn't check the link in ShareFile" | ShareFile is disconnected or unreachable | Connect first, then save |
| "ShareFile could not be reached" | Network or ShareFile outage | Retry; check the log for the status code |
| Startup log: "folder ... no longer exists in ShareFile" | A folder was deleted in ShareFile | Edit the entry with a new link, or remove it |

## Files and uploads

| Symptom | Cause | Fix |
|---|---|---|
| A folder shows no files | Nothing there yet, or the listing is cached up to 30 seconds | Press Refresh |
| A subfolder will not open | It was not listed first, or belongs to another folder | Go back to the root and open it from the list |
| 409 on upload | A file with that name exists | Choose overwrite or rename |
| "Only these file types are allowed" | The folder restricts extensions | Use an allowed type, or change `AllowedExtensions` |
| "File is larger than the ... MB limit" | Over the folder or global limit | Split the file or raise `Upload:MaxBytes` / `MaxSizeBytes`; also raise any proxy body limit |
| "Upload failed: ShareFile refused to start the upload" | The connected account cannot upload to that folder | Fix permissions in ShareFile; `Failed` audit rows have the message |
| Upload succeeded but the partner has no file | FileSync has not copied it yet, or its job failed | Check the FileSync job for that folder (host, path and username are on the entry) |

## Tests

| Symptom | Cause | Fix |
|---|---|---|
| Tests fail to start the host | Another test host is using the same settings | Tests must run serially (they already do); do not enable parallelization |
| Build errors about file ownership | Mixed users | Build in a copy or as the owning account |

## Collecting information for a bug report

1. The exact message and what you clicked.
2. The time (so the log lines can be found).
3. `GET /health` output.
4. The relevant lines from `data/logs/wrapper-YYYYMMDD.log` (remove invite or reset links first).
