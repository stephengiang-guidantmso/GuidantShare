# The catalog

The catalog is the list of **health plans** and the **folders** (catalog entries) under each. It lives in the database (`HealthPlans` and `CatalogEntries`) and is edited in **Settings → Health plans and folders**. There is no seed file.

## Plan

A plan is one card on the home page.

| Field | Notes |
|---|---|
| Name | Required, up to 100 characters, unique among active plans. A removed plan's name can be reused. |
| Order | Plans appear in creation order. |

## Folder (catalog entry)

| Field | Required | Notes |
|---|---|---|
| Direction | yes | `Download` (partner ➜ ShareFile) or `Upload` (ShareFile ➜ partner) |
| ShareFile folder link | **yes** | Web address of the folder. See below. |
| Label | no | Button text users see. **Default: the plan name.** Up to 100 characters. |
| Shown to users as | no | The ShareFile path text people see. **Default: `Shared Folders/SFTP Drop {plan}` for downloads, `Shared Folders/SFTP Uploads/{plan}` for uploads.** |
| Connects through | yes | `Sftp` or `Portal` |
| SFTP host / port | for SFTP | Port defaults to 22; must be 1 to 65535 |
| Portal name | for Portal | For example "Aetna SecureTransport (st1.aetna.com)" |
| Remote folder path | no | Where FileSync reads from (downloads) or writes to (uploads), for example `/Incoming/278` |
| Login username | no | The login *name* only, up to 200 characters. Never a password. |
| Note | no | Optional text shown to users |

Folders also have two optional limits that the editor does not expose yet and that are set in the database: `AllowedExtensions` (comma separated, for example `csv,txt`) and `MaxSizeBytes`. Uploads honor both.

## The ShareFile link

Open the folder in ShareFile and copy the address from the browser, for example:

```
https://guidantmso.sharefile.com/home/shared/fo0ea564-6572-49c0-9f79-416c34cea03c
```

The folder id is the part that starts with `fo`. On save the app:

1. Extracts the id from the link (any text containing an id starting with `fo` and at least 8 characters works, including the bare id).
2. Asks ShareFile for that item using the connected account.
3. Rejects the save if the link has no id, the folder cannot be found, the account cannot open it, or the link points to a file.
4. Stores the folder id. The id, not the path text, is what the app uses from then on.

So ShareFile must be connected before you can add or edit a folder. If you move or rename a folder in ShareFile the saved id keeps working; if it is deleted, the startup check logs an error for that entry.

## Defaults in practice

Adding the CAQH plan with a download and an upload folder and leaving label and path blank gives:

| Direction | Label | Shown as |
|---|---|---|
| Download | CAQH | Shared Folders/SFTP Drop CAQH |
| Upload | CAQH | Shared Folders/SFTP Uploads/CAQH |

Type a value in either field to override. For folders nested deeper in ShareFile, type the full path yourself, because the default assumes the standard layout.

## Several accounts at one partner

Some partners have more than one login (for example two Clever Care accounts or two Aetna portal users). Model this inside **one plan**: add a folder for each account, each with its own username. If two entries point at the same ShareFile folder, users see it once; the admin table shows both so the matrix of "which login feeds which folder" stays complete.

## Parent folders

A folder may point at a parent that has subfolders (for example Clever Care "FromCCHP" points at the IPA folder that contains Quality). Users can open **every** subfolder beneath it.

## Removing

*Remove* sets the plan or folder inactive. It disappears for users and in Settings, but the rows stay so upload history can still be read. Nothing in ShareFile is deleted. Removing a plan removes all its folders from view.

## Starting from nothing (new environment)

1. Create the superadmin and connect ShareFile.
2. Add each plan.
3. For each plan add the download and upload folders with their links.
4. Spot-check by signing in as a normal user and opening each plan.

There is no import. If you have many plans to enter, the Settings page is the supported way; the schema is documented in [Data model](data-model.md) if you ever need to load rows another way.
