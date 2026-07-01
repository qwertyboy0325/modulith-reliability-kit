#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Two-instance exactly-once demo (JetStream + FOR UPDATE SKIP LOCKED).

Usage:
  ./scripts/demo.sh up        # bring everything up, seed data, print the exactly-once invariant
  ./scripts/demo.sh verify    # re-print the invariant (instances must be up)
  ./scripts/demo.sh down      # stop both API instances (keeps Postgres/NATS + data volume)
  ./scripts/demo.sh nuke      # down + docker compose down -v (drops the data volume too)

What `up` proves: two API processes share one NATS and one PostgreSQL. Every product yields exactly
one announcement even though both instances drain the shared outbox (at-least-once publish), JetStream
is at-least-once, and both drain the shared inbox concurrently. The idempotent inbox + SKIP LOCKED
collapse both duplication sources to one effect, so:

    products = inbox_rows = announcements = distinct_products,  not_processed = 0

Env overrides: PORT_A(5099) PORT_B(5098) PRODUCTS(10)
USAGE
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
cd "$repo_root"

PORT_A="${PORT_A:-5099}"
PORT_B="${PORT_B:-5098}"
PRODUCTS="${PRODUCTS:-10}"

COMPOSE_FILE="docker-compose.postgres.yml"
PG_CONTAINER="modulith_reliability_kit-postgres"
PG_USER="modulith_reliability_kit"
PG_DB="modulith_reliability_kit"
API_PROJECT="src/Api/ModulithReliabilityKit.Api/ModulithReliabilityKit.Api.csproj"
run_dir="${repo_root}/.demo"

require() { command -v "$1" >/dev/null 2>&1 || { echo "error: '$1' is required but not found" >&2; exit 1; }; }

psql_do() { docker exec -i "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" "$@"; }
psql_val() { docker exec -i "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -tAc "$1" 2>/dev/null | tr -d '[:space:]'; }

wait_for() { # wait_for <desc> <timeout_s> <command...>
  local desc="$1" timeout="$2"; shift 2
  local waited=0
  until "$@" >/dev/null 2>&1; do
    sleep 1; waited=$((waited + 1))
    if [ "$waited" -ge "$timeout" ]; then echo "error: timed out waiting for ${desc}" >&2; return 1; fi
  done
}

start_instance() { # start_instance <port> <logfile>
  local port="$1" log="$2"
  Messaging__Transport=Nats \
    nohup dotnet run --no-build --project "$API_PROJECT" --urls "http://localhost:${port}" \
    > "$log" 2>&1 &
  echo $! >> "${run_dir}/pids"
}

kill_port() { # kill listeners on a TCP port (kills the actual server child, not just `dotnet run`)
  local port="$1" p
  for p in $(lsof -ti "tcp:${port}" -sTCP:LISTEN 2>/dev/null || true); do
    kill "$p" 2>/dev/null && echo "stopped listener on :${port} (pid ${p})" || true
  done
}

print_invariant() {
  echo
  echo "=== exactly-once invariant ==="
  psql_do -c "SELECT
  (SELECT count(*) FROM catalog.products)                                        AS products,
  (SELECT count(*) FROM notifications.inbox_messages)                            AS inbox_rows,
  (SELECT count(*) FROM notifications.inbox_messages WHERE status<>'processed')  AS not_processed,
  (SELECT count(*) FROM notifications.product_announcements)                     AS announcements,
  (SELECT count(DISTINCT product_id) FROM notifications.product_announcements)   AS distinct_products;"
  echo "=== per-instance apply counts (SKIP LOCKED split; timing-dependent) ==="
  echo -n "instance A (:${PORT_A}) "; curl -s "http://localhost:${PORT_A}/metrics" | grep '^messaging_inbox_processed_total' || echo "(0)"
  echo -n "instance B (:${PORT_B}) "; curl -s "http://localhost:${PORT_B}/metrics" | grep '^messaging_inbox_processed_total' || echo "(0)"
}

cmd_up() {
  require docker; require dotnet; require curl; require lsof
  mkdir -p "$run_dir"; : > "${run_dir}/pids"

  echo "==> Starting Postgres + NATS (JetStream)"
  docker compose -f "$COMPOSE_FILE" up -d
  wait_for "Postgres" 60 docker exec "$PG_CONTAINER" pg_isready -U "$PG_USER" -d "$PG_DB"

  echo "==> Clean slate (truncate demo tables if they already exist)"
  psql_do -c "DO \$\$ BEGIN
    IF to_regclass('catalog.products') IS NOT NULL THEN
      TRUNCATE catalog.products, catalog.outbox_messages,
               notifications.inbox_messages, notifications.inbox_dead_letters,
               notifications.product_announcements RESTART IDENTITY CASCADE;
    END IF;
  END \$\$;" >/dev/null

  echo "==> Building once (both instances run --no-build to avoid output contention)"
  dotnet build "$API_PROJECT" -clp:ErrorsOnly >/dev/null

  echo "==> Starting instance A on :${PORT_A} (applies migrations first)"
  start_instance "$PORT_A" "${run_dir}/instance-a.log"
  wait_for "instance A /health" 90 curl -sf "http://localhost:${PORT_A}/health"

  echo "==> Starting instance B on :${PORT_B} (migration is a no-op)"
  start_instance "$PORT_B" "${run_dir}/instance-b.log"
  wait_for "instance B /health" 90 curl -sf "http://localhost:${PORT_B}/health"

  echo "==> Seeding ${PRODUCTS} products across both instances"
  for i in $(seq 1 "$PRODUCTS"); do
    if [ $((i % 2)) -eq 0 ]; then port="$PORT_A"; else port="$PORT_B"; fi
    curl -s -X POST "http://localhost:${port}/catalog/products/" \
      -H "Content-Type: application/json" \
      -d "{\"name\":\"MP-${i}\",\"price\":${i},\"currency\":\"usd\"}" >/dev/null
  done

  echo "==> Waiting for the outbox (10s) + inbox (5s) drains to converge"
  local waited=0
  until [ "$(psql_val 'SELECT count(*) FROM notifications.product_announcements')" = "$PRODUCTS" ]; do
    sleep 2; waited=$((waited + 2))
    if [ "$waited" -ge 60 ]; then echo "warning: not converged after ${waited}s; printing current state" >&2; break; fi
  done

  print_invariant
  echo
  echo "Instances are still running (logs in ${run_dir}). Explore, then stop with:"
  echo "    ./scripts/demo.sh down"
}

cmd_down() {
  require lsof
  kill_port "$PORT_A"; kill_port "$PORT_B"
  [ -f "${run_dir}/pids" ] && rm -f "${run_dir}/pids"
  echo "Instances stopped. Postgres/NATS still up (data kept). 'nuke' also drops the volume."
}

cmd_nuke() {
  cmd_down || true
  docker compose -f "$COMPOSE_FILE" down -v
  echo "Compose down; data volume dropped."
}

case "${1:-}" in
  up)     cmd_up ;;
  verify) require docker; require curl; print_invariant ;;
  down)   cmd_down ;;
  nuke)   cmd_nuke ;;
  ""|-h|--help|help) usage ;;
  *) echo "unknown command: $1" >&2; echo; usage; exit 1 ;;
esac
