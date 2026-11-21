#!/usr/bin/env bash
# Seeds a LOCAL development FileSync with sample jobs so the website has plans to show.
# Never run this against production. Needs FileSync running with UseMockConnectors=true (Development) and the same Api key:
#
#   FILESYNC_API_KEY=dev-key scripts/dev-seed.sh            # sample jobs
#   FILESYNC_API_KEY=dev-key scripts/dev-seed.sh --with-conflict   # also one job whose ShareFile folder disagrees (shows the admin warning)
#   scripts/dev-seed.sh --help
#
# Jobs created (all enabled except "Hidden Test"):
#   Clever Care 1 and 2: each with Main (download), 278 and UM (uploads)   -> one "Clever Care" plan, three folders
#   Aetna (download + upload, portal type), Champion (upload 5063), and a disabled job that must NOT show for users.
set -u

FILESYNC_URL="${FILESYNC_URL:-http://localhost:5000}"
KEY="${FILESYNC_API_KEY:-}"
WITH_CONFLICT=0

usage() { sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'; }
for a in "$@"; do
  case "$a" in
    --with-conflict) WITH_CONFLICT=1 ;;
    --help|-h) usage; exit 0 ;;
    *) echo "Unknown option: $a" >&2; usage; exit 1 ;;
  esac
done
[ -n "$KEY" ] || { echo "Set FILESYNC_API_KEY to FileSync's Api__Key." >&2; exit 1; }
command -v curl >/dev/null || { echo "curl is required." >&2; exit 1; }

curl -fs -o /dev/null "$FILESYNC_URL/api/health" || { echo "FileSync does not answer at $FILESYNC_URL (start it first)." >&2; exit 1; }

# job <name> <plan> <folderLabel> <Direction> <endpointType> <enabled> <shareFileFolderId> <host> <user> <remotePath>
job() {
  if printf '%s\n' "$EXISTING" | grep -Fxq -- "$1"; then printf '%-34s %s\n' "$1" "already exists, skipped"; return; fi
  local name="$1" plan="$2" label="$3" dir="$4" type="$5" enabled="$6" sf="$7" host="$8" user="$9" path="${10}"
  local body
  body=$(cat <<JSON
{
  "name": "$name", "plan": "$plan", "folderLabel": "$label", "direction": "$dir", "endpointType": "$type",
  "isEnabled": $enabled, "pollingIntervalSeconds": 600, "stabilityPeriodSeconds": 60,
  "sftpRemotePath": "$path", "shareFileFolderId": "$sf", "shareFileDisplayPath": "Shared Folders/Dev/$plan/$label",
  "includePatterns": ["*.*"], "excludePatterns": ["*.tmp"],
  "sftpConfig": { "host": "$host", "port": 22, "username": "$user", "password": "dev-only-not-a-real-password" }
}
JSON
)
  local code
  code=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$FILESYNC_URL/api/workflows" \
    -H "X-Api-Key: $KEY" -H "Content-Type: application/json" -d "$body")
  printf '%-34s %s\n' "$name" "$code"
}

# Names already in FileSync are skipped, so running the script again does not create duplicates
EXISTING="$(curl -s -H "X-Api-Key: $KEY" "$FILESYNC_URL/api/workflows" | grep -o '"name":"[^"]*"' | sed 's/^"name":"//; s/"$//')"

echo "Creating sample jobs at $FILESYNC_URL ..."
# Clever Care: two logins feed the same three folders
for n in 1 2; do
  job "Clever Care $n - Main" "Clever Care" "Main" SftpToShareFile Sftp true "fo-dev-cleverCare-main" "sftp.dev.example" "cc-login-$n" "/"
  job "Clever Care $n - 278"  "Clever Care" "278"  ShareFileToSftp Sftp true "fo-dev-cleverCare-278"  "sftp.dev.example" "cc-login-$n" "/Incoming/278"
  job "Clever Care $n - UM"   "Clever Care" "UM"   ShareFileToSftp Sftp true "fo-dev-cleverCare-um"   "sftp.dev.example" "cc-login-$n" "/Incoming/UM"
done
job "Aetna Download" "Aetna" "Aetna" SftpToShareFile AetnaSecureTransport true "fo-dev-aetna-down" "https://st1.dev.example/" "aetna-user" "/"
job "Aetna Upload"   "Aetna" "Aetna" ShareFileToSftp AetnaSecureTransport true "fo-dev-aetna-up"   "https://st1.dev.example/" "aetna-user" "/toAetna"
job "Champion 5063"  "Champion" "5063" ShareFileToSftp FilesCom true "fo-dev-champion-5063" "https://champion.dev.example" "champion-user" "/To_CPS"
job "Hidden Test"    "Hidden Plan" "Hidden" SftpToShareFile Sftp false "fo-dev-hidden" "sftp.dev.example" "hidden-user" "/"
if [ "$WITH_CONFLICT" = 1 ]; then
  job "Clever Care 3 - UM (typo)" "Clever Care" "UM" ShareFileToSftp Sftp true "fo-dev-TYPO-um" "sftp.dev.example" "cc-login-3" "/Incoming/UM"
fi

echo
echo "Done. Expected on the website (normal user): Aetna, Champion, Clever Care (Download: Main; Upload: 278, UM)."
echo "'Hidden Plan' must not appear. Create a superadmin and a user as described in docs/development.md."
