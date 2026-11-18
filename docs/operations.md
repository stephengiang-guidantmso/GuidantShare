# Operations

## scripts/system.sh

```bash
scripts/system.sh --start     # build, start in the background, wait for /health
scripts/system.sh --stop      # stop the app
scripts/system.sh --restart   # stop, rebuild, start
scripts/system.sh --help
```

| Detail | Value |
|---|---|
| pid file | `.run/server.pid` |
| output | `.run/server.out` |
| port | `GUIDANTSHARE_PORT` (default 5080), used to detect and stop whatever listens there |
| `--start` | Does nothing if the app is already running. Rebuilds first, reports build errors, waits up to 30 seconds for `/health`, and prints the last log lines if the process exits during startup |
| `--stop` | Stops the process group recorded in the pid file and anything still listening on the port; escalates to a kill after 10 seconds |
| Exit status | Non-zero for a failed build or start, or for no option or an unknown one (usage is printed) |

The script suits a small host or a developer machine. In production prefer a service manager (see [Deployment](deployment.md)).

## Health

`GET /health` (no login):

```json
{ "status": "ok", "email": "ok", "emailMode": "Log", "sharefile": "connected" }
```

`sharefile` is `connected`, `disconnected` or `not_configured`; `email` is `misconfigured` when Graph mode is set up wrongly. Point a monitor at it.

## Logs

| Log | Where |
|---|---|
| Application log (Serilog, daily files) | `data/logs/wrapper-YYYYMMDD.log` |
| Console / script output | `.run/server.out` |

The application log holds request lines, ShareFile failures with the status and a trimmed response body, email sending results, and startup check errors. In `Log` email mode it also contains invite and reset links, so treat the log as sensitive. There is no log rotation by size or retention policy; delete or archive old daily files periodically.

Useful searches:

```bash
grep -i "ShareFile" data/logs/wrapper-*.log | tail -50
grep -i "startup check\|Email misconfigured\|no longer exists" data/logs/wrapper-*.log
```

## Backups

Back up two things, separately and securely:

1. `data/app.db` (with the app stopped, or use SQLite's online backup)
   ```bash
   sqlite3 data/app.db ".backup 'backup/app-$(date +%F).db'"
   ```
2. `data/keys/master.key`

A database backup without the key cannot decrypt the ShareFile credentials (you would re-enter them and reconnect). The audit tables and catalog restore fine either way.

**Restore:** stop the app, put `app.db` and `master.key` back in `data/` (matching pair), start, check `/health`.

## Routine tasks

| Task | How |
|---|---|
| Add or change a plan or folder | Settings → Health plans and folders |
| Invite, deactivate or reset a user | Users page |
| Reconnect ShareFile | Settings → Reconnect |
| Rotate the ShareFile client secret | Create a new secret in ShareFile, save it in Settings, then Reconnect |
| Rotate the Graph mail secret | Update `Email__ClientSecret`, restart |
| Check who uploaded what | Query `UploadAudits` (see below) |
| Check sign-in activity | Query `LoginAudits` |

### Handy queries

```sql
-- latest uploads with who and where
SELECT a.CreatedUtc, u.Email, e.Label, a.FileName, a.SizeBytes, a.Status, a.Error
FROM UploadAudits a
JOIN AspNetUsers u ON u.Id = a.UserId
JOIN CatalogEntries e ON e.EntryId = a.EntryId
ORDER BY a.AuditId DESC LIMIT 50;

-- failed sign-ins in the last day
SELECT CreatedUtc, Ip FROM LoginAudits
WHERE Event = 'login_failed' AND CreatedUtc > datetime('now','-1 day') ORDER BY Id DESC;

-- catalog overview
SELECT p.Name, e.Direction, e.Label, e.RemoteUsername, e.SharefileDisplayPath
FROM HealthPlans p JOIN CatalogEntries e ON e.PlanId = p.PlanId
WHERE p.IsActive = 1 AND e.IsActive = 1 ORDER BY p.SortOrder, e.Direction, e.EntryId;
```

Run them read-only: `sqlite3 -readonly data/app.db "..."`.

## When something looks wrong

Check `/health`, then the log, then [Troubleshooting](troubleshooting.md).
