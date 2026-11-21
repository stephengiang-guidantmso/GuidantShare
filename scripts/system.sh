#!/usr/bin/env bash
# GuidantShare process control: scripts/system.sh --start | --stop | --restart | --help
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/GuidantShare.Api"
LOG_DIR="$PROJECT/data/logs"
LOG_FILE="$LOG_DIR/console.out"
PORT="${GUIDANTSHARE_PORT:-5080}"
export PATH="$HOME/.dotnet:$PATH"

usage() {
  cat <<EOF
Usage: scripts/system.sh <option>

  --start     Build the API and start it in the background on http://localhost:$PORT
  --stop      Stop the running API
  --restart   Stop, rebuild and start again
  --help      Show this help

Logs:    $LOG_FILE (the app's own daily logs are in the same folder)
Env:     GUIDANTSHARE_PORT  port to check and stop on (default 5080; the app's
         listen address itself is "Urls" in src/GuidantShare.Api/appsettings.json)
EOF
}

listener_pid() { ss -ltnp 2>/dev/null | awk -v p=":$PORT" '$4 ~ p"$" {print}' | grep -o 'pid=[0-9]*' | head -1 | cut -d= -f2; }

# No pid files: the app is whatever listens on its port
is_running() { [ -n "$(listener_pid)" ]; }

do_stop() {
  local listener; listener="$(listener_pid)"
  if [ -z "$listener" ]; then echo "GuidantShare is not running."; return 0; fi
  kill "$listener" 2>/dev/null
  for _ in $(seq 1 20); do [ -n "$(listener_pid)" ] || break; sleep 0.5; done
  if [ -n "$(listener_pid)" ]; then
    echo "Still running after 10s; sending SIGKILL."
    kill -9 "$(listener_pid)" 2>/dev/null
  fi
  echo "Stopped."
}

do_start() {
  if is_running; then echo "GuidantShare is already running on port $PORT (use --restart)."; return 0; fi
  command -v dotnet >/dev/null || { echo "dotnet not found (looked in PATH and ~/.dotnet)." >&2; return 1; }
  mkdir -p "$LOG_DIR"
  echo "Building…"
  dotnet build "$PROJECT" -v q 2>&1 | grep -E "error|Error\(s\)" || true
  dotnet build "$PROJECT" -v q >/dev/null 2>&1 || { echo "Build failed; run: dotnet build $PROJECT" >&2; return 1; }
  cd "$ROOT" || return 1
  setsid nohup dotnet run --no-build --project "$PROJECT" >"$LOG_FILE" 2>&1 &
  local started=$!
  for _ in $(seq 1 30); do
    if curl -fs -o /dev/null "http://localhost:$PORT/health"; then echo "Started: http://localhost:$PORT"; return 0; fi
    kill -0 "$started" 2>/dev/null || { echo "The app exited during startup. Last log lines:" >&2; tail -20 "$LOG_FILE" >&2; return 1; }
    sleep 1
  done
  echo "Started, but /health did not answer within 30s. See $LOG_FILE" >&2
  return 1
}

case "${1:-}" in
  --start)   do_start ;;
  --stop)    do_stop ;;
  --restart) do_stop; do_start ;;
  --help|-h) usage ;;
  *)         usage; exit 1 ;;
esac
