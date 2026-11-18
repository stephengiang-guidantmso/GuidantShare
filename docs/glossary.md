# Glossary

| Term | Meaning |
|---|---|
| **Health plan** | A card on the home page, usually one partner or service (Aetna, SCAN, CAQH). Stored in `HealthPlans`. |
| **Folder / catalog entry** | One ShareFile folder under a plan, with a direction, how the partner is reached, and a login name. Stored in `CatalogEntries`. |
| **Direction** | `Download` = files from the partner into ShareFile. `Upload` = files you drop in ShareFile to be sent to the partner. |
| **ShareFile folder link** | The web address of a folder in ShareFile. The app reads the folder id from it and checks it with ShareFile. |
| **Folder id** | ShareFile's identifier for a folder; starts with `fo`. The app uses ids, not path text. |
| **Shown to users as** | The ShareFile path text displayed to users. A label only. |
| **Remote** | The partner side of a folder: an SFTP server (host, port, path) or a web portal. |
| **SFTP** | Secure file transfer to a partner's server. FileSync performs these transfers. |
| **Portal** | A partner's website used instead of SFTP (for example Aetna SecureTransport, SCAN provider portal, Champion on files.com). |
| **Login username** | The partner account name FileSync uses. Recorded for reference. Passwords are never stored. |
| **FileSync** | The separate tool that moves files between partners and ShareFile. GuidantShare is independent of it. |
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
