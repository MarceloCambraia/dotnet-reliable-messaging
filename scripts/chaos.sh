#!/usr/bin/env bash
# Reproduces the failure scenarios live against `docker compose up`.
#
#   ./scripts/chaos.sh broker-down   # orders keep working while RabbitMQ is stopped (default)
#   ./scripts/chaos.sh duplicate     # the same message delivered twice has one effect
#   ./scripts/chaos.sh dlq           # a failing message is retried, then dead-lettered
#
# Requires: docker compose, curl. Run from the repository root.
set -euo pipefail

API="${API_URL:-http://localhost:8080}"
MGMT="${RABBITMQ_MGMT_URL:-http://localhost:15672}"
MGMT_AUTH="${RABBITMQ_MGMT_AUTH:-app:app}"
QUEUE="inventory.order-created"

step() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
info() { printf '    %s\n' "$*"; }

uuid() {
  if command -v uuidgen >/dev/null 2>&1; then uuidgen | tr '[:upper:]' '[:lower:]'
  else cat /proc/sys/kernel/random/uuid; fi
}

outbox_stats() { curl -fsS "$API/diagnostics/outbox"; }
json_number() { grep -o "\"$1\":[0-9]*" | head -1 | cut -d: -f2; }

psql_inventory() {
  docker compose exec -T postgres psql -U postgres -d inventory -At -c "$1"
}

queue_depth() {
  curl -fsS -u "$MGMT_AUTH" "$MGMT/api/queues/%2F/$1" | json_number messages
}

create_order() { # $1 = sku, $2 = correlation id
  curl -sS -o /dev/null -w '%{http_code}' -X POST "$API/orders" \
    -H 'Content-Type: application/json' -H "X-Correlation-Id: $2" \
    -d "{\"customerId\":\"chaos-demo\",\"items\":[{\"sku\":\"$1\",\"quantity\":1,\"unitPrice\":10}]}"
}

broker_down() {
  step "Outbox before the outage"
  info "$(outbox_stats)"

  step "Stopping RabbitMQ"
  docker compose stop rabbitmq >/dev/null
  info "broker stopped"

  step "Creating 5 orders while the broker is down"
  for i in 1 2 3 4 5; do
    info "POST /orders -> HTTP $(create_order SKU-1 "chaos-outage-$i")"
  done

  step "Health: readiness stays green, RabbitMQ shows as Degraded"
  info "$(curl -sS "$API/health")"
  info "ready: HTTP $(curl -s -o /dev/null -w '%{http_code}' "$API/health/ready")"

  step "Events wait in the outbox (same transaction as the orders)"
  sleep 3
  info "$(outbox_stats)"

  step "Starting RabbitMQ again"
  docker compose start rabbitmq >/dev/null

  step "Waiting for the publisher to reconnect and drain the outbox"
  for _ in $(seq 1 90); do
    pending=$(outbox_stats | json_number pending)
    if [ "${pending:-1}" = "0" ]; then break; fi
    printf '    pending=%s\r' "$pending"; sleep 1
  done
  info "$(outbox_stats)"

  step "Reservations in the inventory database (by status)"
  sleep 2
  info "$(psql_inventory "select status || ': ' || count(*) from stock_reservations group by status")"
  info "Open Jaeger (http://localhost:16686) to see the request and the late publish in one trace."
}

publish_raw() { # $1 = envelope JSON
  local body
  body=$(printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g')
  curl -fsS -u "$MGMT_AUTH" -H 'Content-Type: application/json' \
    -X POST "$MGMT/api/exchanges/%2F/orders.events/publish" \
    -d "{\"properties\":{\"delivery_mode\":2,\"content_type\":\"application/json\"},\"routing_key\":\"orders.order-created.v1\",\"payload\":\"$body\",\"payload_encoding\":\"string\"}"
}

duplicate() {
  local message_id order_id now envelope
  message_id=$(uuid); order_id=$(uuid); now=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  envelope="{\"messageId\":\"$message_id\",\"type\":\"orders.order-created.v1\",\"correlationId\":\"chaos-duplicate\",\"occurredAt\":\"$now\",\"payload\":{\"orderId\":\"$order_id\",\"customerId\":\"chaos-demo\",\"items\":[{\"sku\":\"SKU-2\",\"quantity\":1}],\"total\":10,\"placedAt\":\"$now\"}}"

  step "Stock of SKU-2 before"
  info "$(psql_inventory "select available_quantity from products where sku = 'SKU-2'")"

  step "Publishing the SAME message (MessageId $message_id) twice"
  info "$(publish_raw "$envelope")"
  info "$(publish_raw "$envelope")"
  sleep 3

  step "Result: one reservation, stock decremented once, one inbox row"
  info "reservations: $(psql_inventory "select count(*) from stock_reservations where order_id = '$order_id'")"
  info "inbox rows:   $(psql_inventory "select count(*) from inbox_messages where message_id = '$message_id'")"
  info "SKU-2 stock:  $(psql_inventory "select available_quantity from products where sku = 'SKU-2'")"
  info "worker log:   $(docker compose logs inventory-worker 2>/dev/null | grep -c "Duplicate message $message_id") duplicate(s) ignored"
}

dlq() {
  step "DLQ depth before: $(queue_depth "$QUEUE.dlq")"

  step "Creating an order with SKU-FAIL (the worker is configured to fail on it)"
  info "POST /orders -> HTTP $(create_order SKU-FAIL chaos-dlq)"

  step "Watching retries (2s, 5s, 10s) and the dead-letter queue"
  for _ in $(seq 1 30); do
    printf '    main=%s retry.1=%s retry.2=%s retry.3=%s dlq=%s   \r' \
      "$(queue_depth "$QUEUE")" "$(queue_depth "$QUEUE.retry.1")" "$(queue_depth "$QUEUE.retry.2")" \
      "$(queue_depth "$QUEUE.retry.3")" "$(queue_depth "$QUEUE.dlq")"
    sleep 1
  done
  echo
  step "DLQ depth after: $(queue_depth "$QUEUE.dlq")"
  info "Inspect it at $MGMT/#/queues/%2F/$QUEUE.dlq (Get messages: see x-retry-count and x-exception-* headers)."
}

case "${1:-broker-down}" in
  broker-down) broker_down ;;
  duplicate) duplicate ;;
  dlq) dlq ;;
  *) echo "usage: $0 [broker-down|duplicate|dlq]" >&2; exit 2 ;;
esac
