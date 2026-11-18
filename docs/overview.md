# Overview

## What it is

GuidantShare gives a small team a simple way to find and send health-plan files. Behind it, a background tool (FileSync) copies files between each partner's SFTP server or web portal and folders in ShareFile. GuidantShare is the friendly front door to those ShareFile folders.

- **Download folders** hold files that arrived *from* a partner (FileSync copies partner ➜ ShareFile).
- **Upload folders** hold files going *to* a partner (you drop a file in; FileSync copies ShareFile ➜ partner).

## What users can do

- Sign in with an invited account.
- Pick a health plan from the home page.
- Browse a plan's download folders, including subfolders, search by name and page through files.
- Upload a file to a plan's upload folder, with a check for duplicate names and an overwrite choice.
- Open any folder directly in ShareFile.

## What superadmins can do

- Connect and disconnect the shared ShareFile account.
- Add, rename and remove health plans.
- Add, edit and remove the folders under each plan, each tied to a ShareFile folder link.
- Invite users, resend or revoke invites, deactivate users, reset passwords.

## What it deliberately does not do

- It does not move files between partners and ShareFile. FileSync does that.
- It does not store or display partner passwords.
- It does not offer self-service sign-up.
- It does not delete anything in ShareFile. Removing a plan or folder only hides it in the app.
- It does not proxy file contents for downloads. Users open files through ShareFile.

## Who the "partners" are

Each health plan or service has its own SFTP server or web portal (for example Aetna's SecureTransport portal, SCAN's SFTP and web portal, Office Ally's SFTP). The catalog records *how* each is reached, so users and admins can see which login and which remote path feed which ShareFile folder. The actual connections are made by FileSync, not by this app.

## Key terms

See the [Glossary](glossary.md). The most important: a **plan** is one card on the home page, and a **folder** (catalog entry) is one ShareFile folder with its direction, remote details and username.
