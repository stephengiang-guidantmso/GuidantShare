# User guide

For people who send or receive health-plan files.

## Signing in

1. Open the link in your invite email and choose a password (at least 8 characters, not a common password, not containing your email name).
2. Sign in with your email and password. Sessions last 8 hours and extend while you are active.
3. After 5 wrong passwords the account is locked for 15 minutes.
4. Forgot your password? Use *Forgot password* on the sign-in page. You will receive a reset link if the address belongs to an active account. (The page always answers the same way so it never reveals who has an account.)

## The home page

The home page shows one card per health plan, in a fixed order. Click a card to open it.

## Opening a plan

A plan page offers **Download** and **Upload**.

- **Download** shows files that arrived from the partner.
- **Upload** is for files you want sent to the partner. If a plan has no upload folder the button is disabled.

If a plan has several folders (for example *Main*, *Quality*, *FromCCHP*, or separate numbered folders), you choose one first. Folders that several accounts share appear only once.

## Browsing files

- Files are listed **newest first** with their size and modified time.
- **Subfolders** appear at the top; click one to go in. You can open any folder beneath the one you started in.
- **Search** filters both files and subfolders by name.
- Use the page controls to move through long lists.
- **Refresh** reloads the folder. Listings are cached for about 30 seconds.
- **Open in ShareFile** opens the current folder in ShareFile's own web app, where you can download files.

The header of the page shows where the folder lives in ShareFile and which remote location feeds it, so you know what you are looking at.

## Uploading

1. Open the plan, choose **Upload**, then the folder.
2. Drag files onto the drop area or use the file picker.
3. If a file with the same name already exists you are asked whether to **overwrite** it. Choose carefully: overwriting replaces what the partner will receive.
4. After the upload you get a link to the file in ShareFile.

Rules the app enforces:

- File names cannot contain path characters; anything before the last `/` or `\` is dropped.
- Empty files are refused.
- The default size limit is 500 MB (an individual folder can set a lower limit).
- A folder may restrict file types (for example only `.csv`); other types are refused with a message listing what is allowed.

Every upload is recorded (who, what, when, size, result), including failures and name conflicts.

## What the messages mean

| Message | Meaning |
|---|---|
| "ShareFile is disconnected. Ask a SuperAdmin to reconnect." | The shared connection was removed or expired. Tell an admin. |
| "ShareFile could not be reached" | ShareFile or the network had a problem. Try again shortly. |
| "A file with that name already exists." | Choose overwrite or rename your file. |
| "That folder isn't part of this plan." | You tried to open a folder outside the one you started in. |
| "Uploads aren't available for this folder." | The folder is for downloads only. |
| "Too many attempts. Try again later." | Account lockout after repeated wrong passwords. |

## Tips

- You never need a partner's SFTP or portal password. FileSync handles those connections.
- Files you upload are picked up by FileSync on its next cycle; they do not reach the partner instantly.
- If a folder you expect is missing, ask an admin; plans and folders are managed in Settings.
