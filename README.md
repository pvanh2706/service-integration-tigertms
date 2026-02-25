# ServiceIntegrationDemo (RabbitMQ-only) — TigerTMS CHECKIN

Demo 1 service (Windows Service ready) gồm:
- Minimal API nhận sự kiện CHECKIN (JSON)
- Publish vào RabbitMQ (durable + persistent message)
- Worker consume -> map JSON -> TigerTMS SOAP/XML -> call TigerTMS
- Callback PMS (demo endpoint) + retry theo RabbitMQ delay queues (TTL) bằng cơ chế republish
- Log Serilog: Console + Elasticsearch (nếu cấu hình)

## Yêu cầu
- .NET 8 SDK
- RabbitMQ (khuyến nghị tạo đúng topology bằng `scripts/rabbitmq-topology.md`)
- (Tuỳ chọn) Elasticsearch

## Chạy local (console)
```bash
cd src/ServiceIntegrationDemo
dotnet restore
dotnet run
```

Service mặc định chạy:
- API: http://localhost:5080
- Swagger: http://localhost:5080/swagger

## Test checkin
```bash
curl -X POST http://localhost:5080/events/checkin   -H "Content-Type: application/json"   -d '{
    "eventId":"e1c0b1b2c3d44b8c9a0b1c2d3e4f5678",
    "hotelId":"EZ001",
    "occurredAt":"2026-02-25T10:00:00+07:00",
    "payload":{
      "reservationNumber":"102024",
      "site":"iLink",
      "room":"613",
      "title":"Mr",
      "last":"Black",
      "first":"John",
      "guestId":3211,
      "lang":"EA",
      "arrival":"25/02/2026",
      "departure":"26/02/2026",
      "tv":"Standard",
      "minibar":"Standard",
      "viewbill":true,
      "expressco":false
    }
  }'
```

## Mở rộng thêm event khác
- Tạo handler mới implement `IEventHandler`
- Đăng ký trong DI + `EventHandlerRegistry`
- Thêm endpoint mới (hoặc dùng endpoint chung `/events` với `eventType`)

## Chạy Windows Service
Xem `README-WindowsService.md`
