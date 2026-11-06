# Next steps (candidate GitHub issues)

These items are tracked as [GitHub issues #1–#19](https://github.com/MarceloCambraia/dotnet-reliable-messaging/issues),
labelled by area. Item 12 is done (see the README's Screenshots section), so it has no issue; the
issue numbers therefore differ from the list numbers below from item 13 on.

## Production hardening
1. **Outbox/inbox retention job.** Delete processed outbox rows and old inbox rows after a
   configurable retention, in small batches (`DELETE … WHERE ctid IN (SELECT … LIMIT n)`). Measure the
   effect on the partial index.
2. **Time-partitioned outbox table.** Monthly partitions, so cleanup becomes `DROP PARTITION`.
3. **Per-aggregate ordering.** Opt-in mode with one publisher per partition (hash of aggregate id),
   plus consumer-side version checks. Document the throughput cost.
4. **Wake the publisher with `LISTEN/NOTIFY`.** Cut latency to near zero without lowering the polling
   interval; keep polling as a fallback.
5. **Lease-based claiming (`locked_until`) as an alternative to holding row locks during publish.**
   Benchmark both.
6. **Key the inbox by `(consumer, message_id)`,** so several handlers can share one database.
7. **TLS and per-service credentials** for RabbitMQ and Postgres; secrets from environment or a vault.
8. **Run migrations outside the app** (EF migration bundles in CI/CD) instead of on startup.

## Observability
9. **Outbox metrics** (OpenTelemetry Meter): pending count, age of the oldest pending row, publish
   duration, parked messages. Add Prometheus and Grafana to compose, with an alert on outbox lag.
10. **Consumer metrics:** processed / duplicate / retried / dead-lettered counters, and handler duration.
11. **Worker health endpoint** (or a heartbeat file) for container health checks.
12. ~~**Screenshots/GIF** of the RabbitMQ management UI (retry tiers and DLQ) and of a Jaeger trace in
    the README.~~ Done.

## Operations tooling
13. **DLQ replay endpoint or CLI:** list, inspect, replay one or all, purge, with an audit log.
14. **Re-publish parked outbox messages:** endpoint to reset `failed_at` and `attempts` for chosen rows.

## Testing
15. **Chaos tests with Toxiproxy:** latency, connection resets mid-publish, half-open TCP.
16. **Crash-between-publish-and-mark test:** kill the publisher after the confirm and before the
    commit, then assert the duplicate is absorbed by the inbox.
17. **Contract tests** for `OrderCreated` (consumer-driven), and a `v2` evolution example.
18. **Load test** (k6 or NBomber): orders/s with 1 vs 3 publishers, and outbox lag under load.

## Alternatives worth comparing
19. **CDC branch:** Debezium + Postgres logical replication instead of polling, with a side-by-side
    write-up.
20. **MassTransit branch:** the same scenarios using MassTransit's EF Core outbox and inbox, to compare
    the amount of code and the behaviour.
