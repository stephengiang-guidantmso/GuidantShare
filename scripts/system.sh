#!/usr/bin/env bash
# GuidantShare process control: scripts/system.sh --start | --stop | --restart | --help
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/GuidantShare.Api"
RUN_DIR="$ROOT/.run"
PID_FILE="$RUN_DIR/server.pid"
LOG_FILE="$RUN_DIR/server.out"
PORT="${GUIDANTSHARE_PORT:-5080}"
export PATH="$HOME/.dotnet:$PATH"

usage() {
  cat <<EOF
Usage: scripts/system.sh <option>

  --start     Build the API and start it in the background on http://localhost:$PORT
  --stop      Stop the running API
  --restart   Stop, rebuild and start again
  --help      Show this help

Files:   pid  $PID_FILE
         log  $LOG_FILE (app logs also go to src/GuidantShare.Api/data/logs)
Env:     GUIDANTSHARE_PORT  port to check and stop on (default 5080; the app's
         listen address itself is "Urls" in src/GuidantShare.Api/appsettings.json)
EOF
}

pid_alive() { [ -n "${1:-}" ] && kill -0 "$1" 2>/dev/null; }
read_pid() { [ -f "$PID_FILE" ] && cat "$PID_FILE" 2>/dev/null; }
listener_pid() { ss -ltnp 2>/dev/null | awk -v p=":$PORT" '$4 ~ p"$" {print}' | grep -o 'pid=[0-9]*' | head -1 | cut -d= -f2; }

is_running() {
  local pid; pid="$(read_pid || true)"
  pid_alive "$pid" || [ -n "$(listener_pid)" ]
}

do_stop() {
  local pid listener; pid="$(read_pid || true)"; listener="$(listener_pid)"
  if ! pid_alive "$pid" && [ -z "$listener" ]; then
    echo "GuidantShare is not running."; rm -f "$PID_FILE"; return 0
  fi
  # the pid file holds the process group leader started by --start; also stop whatever owns the port
  pid_alive "$pid" && { kill -- "-$pid" 2>/dev/null || kill "$pid" 2>/dev/null; }
  [ -n "$listener" ] && kill "$listener" 2>/dev/null
  for _ in $(seq 1 20); do
    pid_alive "$pid" || [ -n "$(listener_pid)" ] || break
    sleep 0.5
  done
  if pid_alive "$pid" || [ -n "$(listener_pid)" ]; then
    echo "Still running after 10s; sending SIGKILL."
    pid_alive "$pid" && kill -9 -- "-$pid" 2>/dev/null
    [ -n "$(listener_pid)" ] && kill -9 "$(listener_pid)" 2>/dev/null
  fi
  rm -f "$PID_FILE"
  echo "Stopped."
}

do_start() {
  if is_running; then echo "GuidantShare is already running on port $PORT (use --restart)."; return 0; fi
  command -v dotnet >/dev/null || { echo "dotnet not found (looked in PATH and ~/.dotnet)." >&2; return 1; }
  mkdir -p "$RUN_DIR"
  echo "Building…"
  dotnet build "$PROJECT" -v q 2>&1 | grep -E "error|Error\(s\)" || true
  dotnet build "$PROJECT" -v q >/dev/null 2>&1 || { echo "Build failed; run: dotnet build $PROJECT" >&2; return 1; }
  cd "$ROOT" || return 1
  setsid nohup dotnet run --no-build --project "$PROJECT" >"$LOG_FILE" 2>&1 &
  echo $! >"$PID_FILE"
  for _ in $(seq 1 30); do
    if curl -fs -o /dev/null "http://localhost:$PORT/health"; then echo "Started: http://localhost:$PORT (pid $(read_pid))"; return 0; fi
    pid_alive "$(read_pid || true)" || { echo "The app exited during startup. Last log lines:" >&2; tail -20 "$LOG_FILE" >&2; return 1; }
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
