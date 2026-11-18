# GuidantShare documentation

GuidantShare is an invite-only web app on top of ShareFile for health-plan files. These pages explain how to use it, run it, and change it.

| If you are... | Start with |
|---|---|
| New to the project | [Overview](overview.md), then [Architecture](architecture.md) |
| A user who sends or receives files | [User guide](user-guide.md) |
| A superadmin setting things up | [Getting started](getting-started.md), [Admin guide](admin-guide.md), [Catalog](catalog.md) |
| Deploying it | [Configuration](configuration.md), [Deployment](deployment.md), [Security](security.md) |
| Keeping it running | [Operations](operations.md), [Troubleshooting](troubleshooting.md) |
| Changing the code | [Development](development.md), [Testing](testing.md), [API reference](api-reference.md), [Data model](data-model.md) |
| Looking up a term | [Glossary](glossary.md) |

## All pages

- [Overview](overview.md): what the app does and does not do
- [Architecture](architecture.md): components, request flow, design decisions
- [Getting started](getting-started.md): first run on a developer machine, first-time setup
- [User guide](user-guide.md): signing in, finding plans, downloading, uploading
- [Admin guide](admin-guide.md): Settings, users, invites, ShareFile connection
- [Catalog](catalog.md): health plans, folders, links, defaults, rules
- [Data model](data-model.md): tables and fields
- [API reference](api-reference.md): every endpoint, who may call it, and what it returns
- [Configuration](configuration.md): every setting and where it lives
- [Security](security.md): authentication, CSRF, secrets, rate limits, threat notes
- [ShareFile integration](sharefile-integration.md): OAuth, tokens, folder lookups, uploads
- [Deployment](deployment.md): production checklist and hosting notes
- [Operations](operations.md): `scripts/system.sh`, logs, backups, routine tasks
- [Troubleshooting](troubleshooting.md): symptoms, causes, fixes
- [Testing](testing.md): the three test suites and how to extend them
- [Development](development.md): repository layout, conventions, common changes
- [Glossary](glossary.md)

## Ground rules the app is built around

1. **One shared ShareFile connection.** A superadmin connects the owner's ShareFile account once; everyone uses it.
2. **No passwords for partners, ever.** The app stores login *names* for reference only. Partner SFTP and portal passwords are never entered, stored or shown.
3. **Independent of FileSync.** FileSync moves files between partners and ShareFile; GuidantShare only talks to ShareFile. It never reads FileSync's database, config or secrets.
4. **Invite-only.** There is no self sign-up.
5. **Only superadmins see Settings and Users.**
