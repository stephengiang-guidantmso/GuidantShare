# ShareFile integration

## Registering the ShareFile app

In ShareFile, create an API application (OAuth 2.0 authorization code). You will get a client ID and client secret and choose a redirect URI. In GuidantShare Settings save:

- **Subdomain**: the part before `.sharefile.com` (stored lower-cased)
- **Client ID** and **Client secret**
- **Redirect URI**: exactly what is registered on the ShareFile app

Two redirect styles work:

1. `https://<your-host>/api/sharefile/callback`. One-click: after you approve, ShareFile sends the browser back to the app, which finishes the connection and returns you to Settings.
2. `https://secure.sharefile.com/oauth/oauthcomplete.aspx`. ShareFile shows a page with a code; paste the code or the full address into *Finish connecting*.

## Connecting

1. *Connect to ShareFile* builds `https://{sub}.sharefile.com/oauth/authorize?response_type=code&client_id=...&redirect_uri=...&state=...`. The `state` value is random, single-use and valid for 10 minutes.
2. You sign in with the owner account and approve.
3. The app exchanges the code at `https://{sub}.sharefile.com/oauth/token` (`grant_type=authorization_code`) and stores the **refresh token** encrypted, plus who connected and when.

## Staying connected

- API calls use `https://{sub}.sf-api.com/sf/v3` with a bearer **access token** held in memory.
- Access tokens are refreshed from the stored refresh token about 15 minutes before expiry, or immediately after a `401`. If ShareFile issues a new refresh token the app stores it.
- If refresh fails the status shows an error and users see "ShareFile is disconnected" until a superadmin reconnects.
- Rate-limit and server errors (`429`, `5xx`) are retried up to three times with backoff, honoring `Retry-After`.

## What the app calls

| Purpose | ShareFile call |
|---|---|
| Check a pasted folder link | `GET Items({id})` |
| List a folder | `GET Items({id})/Children?$top=1000`, following `odata.nextLink` |
| Start an upload | `POST Items({folderId})/Upload?raw=true&fileName=...[&overwrite=true]` returns a `ChunkUri` |
| Send the bytes | `POST {ChunkUri}` with the raw body as `application/octet-stream` |
| Find the new item | list the folder and take the newest file with that name |
| Open in the web app | link `https://{sub}.sharefile.com/home/shared/{folderId}` (built locally) |

The upload method matches what FileSync does (negotiate with `raw=true`, then post raw bytes), so both tools behave the same.

## Folder links and ids

Folder ids start with `fo`. The catalog stores the id; the path text shown to users is a label. Details are on the [Catalog](catalog.md) page.

At startup the app checks that every stored folder id still exists and logs an error for any that do not. It also warns if ShareFile is not connected.

## Failure behavior

| Situation | What users and admins see |
|---|---|
| Not connected or refresh token rejected | `503` with code `disconnected`; admins see the error on Settings |
| ShareFile or network failure | `502` "ShareFile could not be reached" (listing) or "Upload failed: ..." (uploads) |
| Folder deleted in ShareFile | Listing fails; the startup log names the entry |
| Upload refused (permissions) | "ShareFile refused to start the upload (status)..." and a `Failed` audit row |

## Using this with FileSync

FileSync and GuidantShare both write to and read from the same ShareFile folders but are otherwise independent. FileSync keeps the partner side in sync; GuidantShare never touches the partners. If a file uploaded here does not reach a partner, check FileSync's job for that folder (the entry's remote host, path and username show which one).
