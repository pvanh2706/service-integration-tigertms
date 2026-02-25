# RabbitMQ topology (demo production-like)

## Exchange
- `tigertms.events.x` (direct) — publish/consume chính
- `tigertms.retry.x`  (direct) — exchange cho retry routing

## Queues
- `tigertms.events.q` (durable) — main queue, worker consume
- `tigertms.retry.10s.q` (durable, TTL=10000, DLX -> tigertms.events.x)
- `tigertms.retry.1m.q`  (durable, TTL=60000, DLX -> tigertms.events.x)
- `tigertms.retry.5m.q`  (durable, TTL=300000, DLX -> tigertms.events.x)
- `tigertms.retry.30m.q` (durable, TTL=1800000, DLX -> tigertms.events.x)
- `tigertms.dead.q` (durable) — dead letter

## Bindings
- `tigertms.events.q` bind `tigertms.events.x` with routingKey: `events`
- Retry queues bind `tigertms.retry.x` with routingKeys:
  - `retry.10s`
  - `retry.1m`
  - `retry.5m`
  - `retry.30m`
- `tigertms.dead.q` bind `tigertms.retry.x` with routingKey: `dead`

## Notes
- Worker sẽ republish message vào retry exchange với routingKey phù hợp, kèm header `x-attempt` tăng dần.
- Khi TTL của retry queue hết, message sẽ DLX về `tigertms.events.x` với routingKey `events` (quay lại main queue).
