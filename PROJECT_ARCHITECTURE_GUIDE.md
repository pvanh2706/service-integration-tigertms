# ServiceIntegrationTigerTMS - Project Architecture Guide

Tai lieu nay duoc viet de lan sau chi can nhin cau truc project la co the hieu nhanh he thong va xu ly task.
Pham vi: toan bo source trong `src/ServiceIntegrationDemo` + tai lieu `README*.md`, `Cautruc.md`, `case-test-loi.md`, `scripts/rabbitmq-topology.md`.

## 1. Muc tieu du an

Service nay la 1 integration service cho su kien CHECKIN:
- Nhan event CHECKIN tu client/PMS qua HTTP API.
- Dua payload vao RabbitMQ (asynchronous decoupling).
- Worker consume queue, chuyen doi JSON -> Tiger SOAP/XML, goi TigerTMS.
- Neu Tiger thanh cong thi callback lai PMS.
- Neu loi thi republish vao retry exchange theo cac muc delay 10s/1m/5m/30m, cuoi cung vao dead queue.

Service duoc thiet ke theo huong at-least-once processing (khong phai exactly-once).

## 2. Cong nghe va package

- Runtime: .NET 8 (`net8.0`)
- App type: ASP.NET Core Minimal API + Hosted Background Worker
- Messaging: RabbitMQ.Client 6.8.1
- Logging: Serilog + Console + Elasticsearch sink
- API docs: Swagger (Swashbuckle)

File xac nhan: `src/ServiceIntegrationDemo/ServiceIntegrationDemo.csproj`.

## 3. Cau truc thu muc

```text
src/ServiceIntegrationDemo
|- Program.cs
|- appsettings.json
|- GlobalUsings.cs
|- Core
|  |- Abstractions
|  |  |- IEventHandler.cs
|  |  |- IIdempotencyStore.cs
|  |  |- IIntegrationQueue.cs
|  |  |- IPmsCallbackClient.cs
|  |  |- IQueueConsumer.cs
|  |  |- ITigerClient.cs
|  |  `- MessageHeaders.cs
|  |- Contracts
|  |  |- EventEnvelope.cs
|  |  `- CheckInPayload.cs
|  `- Services
|     |- EventHandlerRegistry.cs
|     |- MessageOrchestrator.cs
|     |- RetryRouter.cs
|     `- CheckInEventHandler.cs
`- Infrastructure
   |- Options.cs
   |- MemoryIdempotencyStore.cs
   |- Pms
   |  `- PmsCallbackClient.cs
   |- RabbitMq
   |  |- RabbitConnectionFactory.cs
   |  |- RabbitTopology.cs
   |  |- RabbitPublisher.cs
   |  |- RabbitPublisher copy.cs (legacy/commented)
   |  `- RabbitConsumer.cs
   |- Tiger
   |  |- TigerSoapBuilder.cs
   |  `- TigerClient.cs
   `- Worker
      `- QueueWorker.cs
```

Ngoai source:
- `README.md`: huong dan chay nhanh local.
- `README-WindowsService.md`: huong dan publish/chay duoi Windows Service.
- `scripts/rabbitmq-topology.md`: topology retry/dead theo TTL + DLX.
- `Cautruc.md`, `case-test-loi.md`: tai lieu mo ta va test case (mang tinh tham khao).

## 4. Diem vao va startup flow

File trung tam: `Program.cs`.

### 4.1 Startup

1. Tao WebApplication builder.
2. Cau hinh Serilog:
- Luon log Console.
- Neu `Elastic.Enabled=true` va `Elastic.Uri` hop le thi bat Elasticsearch sink.
3. Dang ky Swagger.
4. Bind config section:
- `RabbitMq` -> `RabbitOptions`
- `TigerTms` -> `TigerOptions`
- `PmsCallback` -> `PmsCallbackOptions`
- `RetryPolicy` -> `RetryPolicyOptions`
5. Dang ky DI cho:
- Rabbit connection/topology/publisher/consumer
- Tiger client, PMS callback client
- Idempotency store (in-memory)
- RetryRouter, handler registry, orchestrator
- Hosted service `QueueWorker`
6. `RabbitTopology.Ensure()` duoc goi trong try/catch:
- Neu fail ket noi Rabbit, service van boot API nhung publish/consume co the khong hoat dong.
7. Map API endpoints va `app.Run()`.

### 4.2 HTTP endpoints

- `GET /health`
- `POST /pms/callback` (demo endpoint)
- `POST /events/checkin` (main ingest endpoint)

## 5. Contract va message schema

### 5.1 API input: `EventEnvelope`

Truong chinh:
- `eventId` (string)
- `hotelId` (string)
- `eventType` (set lai thanh `CHECKIN` trong endpoint)
- `occurredAt` (DateTimeOffset)
- `payload` (JsonElement)

### 5.2 Payload checkin: `CheckInPayload`

Map cac field JSON checkin sang model strongly typed:
- Bat buoc theo logic nghiep vu Tiger: `reservationNumber`, `site`, `room`.
- Optional: title/last/first/guestId/lang/group/vip/email/mobile/arrival/departure/tv/minibar/viewbill/expressco.

### 5.3 Rabbit headers

Headers duoc tao tai endpoint `/events/checkin`:
- `x-hotel-id`
- `x-event-id`
- `x-event-type` = `CHECKIN`
- `x-correlation-id` = GUID N format
- `x-attempt` = 0
- `x-wsuserkey` (neu co cau hinh Tiger)

Headers nay duoc dung xuyen suot cho orchestration + retry.

## 6. Luong xu ly end-to-end

### 6.1 Ingress

Client goi `POST /events/checkin`.

Endpoint thuc hien:
1. Validate `hotelId`, `eventId`.
2. Force `eventType = CHECKIN`.
3. Serialize `envelope.Payload` thanh bytes (chi payload, khong bao envelope day du).
4. Tao `MessageHeaders`.
5. `queue.PublishAsync(body, headers, CancellationToken.None)`.
6. Tra ve ngay: `{ status: "QUEUED", eventId, hotelId }`.

### 6.2 Consume

`QueueWorker` start `RabbitConsumer.StartAsync(...)`.

`RabbitConsumer`:
- Create connection + channel.
- Set QoS prefetch 20.
- Consume queue events voi `autoAck: false`.
- Moi message wrap thanh `ConsumedMessage` gom body + headers + delegate Ack/Nack.

### 6.3 Orchestration

`MessageOrchestrator.ProcessAsync`:
1. Lay headers `x-hotel-id`, `x-event-id`, `x-event-type`, `x-correlation-id`.
2. Neu thieu header can thiet -> ACK de tranh poison loop.
3. Tim handler tu `EventHandlerRegistry` theo `eventType`.
4. Neu khong co handler -> ACK bo qua.
5. Tao `EventContext` va goi `handler.HandleAsync`.

### 6.4 Handler CHECKIN

`CheckInEventHandler.HandleAsync`:
1. Dedupe (`IIdempotencyStore.SeenRecently`): neu trung -> ACK.
2. Parse body JSON sang `CheckInPayload` (case-insensitive).
- Parse fail -> republish dead + ACK.
3. Lay `x-wsuserkey`:
- Neu thieu -> republish retry + ACK.
4. Build XML noi bo checkin qua `TigerSoapBuilder.BuildCheckInInnerXml`.
5. Goi Tiger qua `ITigerClient.SendCheckInAsync`.
- Neu fail -> republish retry + ACK.
6. Neu Tiger success, goi callback PMS qua `IPmsCallbackClient.NotifyAsync`.
- Callback fail -> republish retry + ACK.
7. Neu thanh cong tat ca:
- Mark idempotency 6 gio.
- ACK message.

Luu y: trong code hien tai, handler thuong ACK message goc sau khi republish retry/dead (khong dung Nack/requeue).

## 7. Retry va dead-letter strategy

`RetryRouter.Decide(attempt)`:
- attempt <= 0 -> Retry10s
- attempt == 1 -> Retry1m
- attempt == 2 -> Retry5m
- attempt == 3 -> Retry30m
- con lai -> Dead

`Republish(...)` trong handler:
- Tang `x-attempt`.
- Set `x-last-error`.
- Xac dinh route -> routing key qua `RabbitPublisher.RoutingKeyForRetry`.
- Publish vao retry exchange.

Y nghia: service bo message goc (ACK) va tao message moi voi routing retry/dead.

## 8. Rabbit topology

### 8.1 Topology duoc code tao (`RabbitTopology.Ensure`)

Duoc khai bao trong code:
- Exchanges: events, retry
- Queue: events (bind events exchange with rk `events`)
- Queue: dead (bind retry exchange with rk `dead`)

### 8.2 Topology can co de retry delay hoat dong day du

Theo `scripts/rabbitmq-topology.md`, can co them cac queue retry TTL + DLX:
- `tigertms.retry.10s.q` (TTL 10s)
- `tigertms.retry.1m.q` (TTL 1m)
- `tigertms.retry.5m.q` (TTL 5m)
- `tigertms.retry.30m.q` (TTL 30m)

Va binding vao retry exchange voi rk:
- `retry.10s`, `retry.1m`, `retry.5m`, `retry.30m`

Sau TTL, message DLX quay ve events exchange/rk events.

Quan trong: code hien tai KHONG tao cac retry TTL queues nay. Can tao san bang script/infra RabbitMQ.

## 9. Cac implementation layer

### 9.1 Core/Abstractions

- Chia contract ro rang giua nghiep vu va ha tang.
- Giup mo rong event moi ma khong sua flow chung.

### 9.2 Core/Services

- `EventHandlerRegistry`: map `eventType -> handler`.
- `MessageOrchestrator`: dieu phoi consume message den dung handler.
- `RetryRouter`: policy route retry.
- `CheckInEventHandler`: nghiep vu checkin.

### 9.3 Infrastructure

- Rabbit:
- `RabbitConnectionFactory`: tao connection voi auto recovery.
- `RabbitPublisher`: publish persistent + publisher confirms.
- `RabbitConsumer`: manual ack consume.
- `RabbitTopology`: ensure exchange/queue co ban.
- Tiger:
- `TigerSoapBuilder`: tao XML + SOAP envelope va escape.
- `TigerClient`: call endpoint Tiger, detect success bang chuoi `SUCCESS`.
- PMS:
- `PmsCallbackClient`: POST callback JSON.
- Idempotency:
- `MemoryIdempotencyStore`: cache in-memory theo key `seen:{hotel}:{event}`.

## 10. Cau hinh appsettings (default hien tai)

File: `src/ServiceIntegrationDemo/appsettings.json`.

Section chinh:
- `Kestrel.Endpoints.Http.Url`: `http://0.0.0.0:5080`
- `RabbitMq`: nodes/port/vhost/user/password + exchange/queue/routing keys
- `Elastic`: enabled + URI + index prefix
- `TigerTms`: enabled + endpoint + timeout + wsuserkey + soapAction
- `PmsCallback`: enabled + baseUrl + timeout
- `RetryPolicy.MaxAttempts`: hien tai 5 (nhung `RetryRouter` dang hard-code sequence)
- `Logging.LogLevel`

Canh bao bao mat:
- File hien tai dang chua credential that (Rabbit password, Tiger wsuserkey). Nên move sang secret store/env var.

## 11. Logging va observability

Service log cac moc quan trong:
- Worker start/stop
- Rabbit consumer started
- Handle checkin attempt N
- Parse payload fail
- Tiger failed
- Callback fail
- Duplicate event
- Done -> ACK

Serilog Elasticsearch index format: `<IndexPrefix>`.

## 12. Do tin cay va han che hien tai

### 12.1 Diem manh

- Decouple API va processing (queue).
- Manual ACK giup chong mat message khi crash truoc ACK.
- Retry theo delay route.
- Co idempotency co ban.

### 12.2 Han che/risk

1. Idempotency la memory-local:
- Restart service la mat cache dedupe.
- Scale nhieu instance khong share dedupe store.

2. RetryPolicyOptions.MaxAttempts chua duoc su dung de cat luong retry:
- `RetryRouter` hard-code route theo attempt, khong tham chieu `MaxAttempts`.

3. Tiger success detection bang `raw.Contains("SUCCESS")`:
- Don gian, de false positive/negative neu response format thay doi.

4. Topology retry TTL khong duoc Ensure trong code:
- Neu infra chua set san, retry route co the khong hoat dong dung.

5. `/pms/callback` trong service la demo endpoint:
- Thuc te thuong callback qua he thong PMS ben ngoai.

6. File `RabbitPublisher copy.cs` la file backup/commented:
- Nen xoa de tranh nham lan.

7. Chuoi log tieng Viet dang bi loi encoding o mot vai message.

## 13. Cach mo rong them event moi

Checklist implementation:
1. Tao DTO payload moi trong `Core/Contracts`.
2. Tao handler moi implement `IEventHandler` trong `Core/Services`.
3. Dang ky DI:
- `builder.Services.AddSingleton<IEventHandler, NewEventHandler>();`
4. Tao endpoint ingest moi (hoac endpoint chung).
5. Neu can route retry rieng, cap nhat policy/router.
6. Bo sung test cases (success, tiger fail, callback fail, duplicate, invalid payload).

## 14. Runbook debug nhanh

### 14.1 API vao duoc nhung khong consume

Kiem tra:
- Rabbit reachable?
- `RabbitTopology.Ensure()` co loi startup?
- Queue `tigertms.events.q` co message pending khong?
- Worker co log `Rabbit consumer started` khong?

### 14.2 Message consume nhung khong callback

Kiem tra:
- Log `TIGER_FAILED` hay `CALLBACK_FAIL`.
- Header `x-wsuserkey` co duoc set?
- `TigerTms.Enabled` / `PmsCallback.Enabled`.
- Retry queues TTL da duoc tao dung topology chua?

### 14.3 Bi duplicate

Kiem tra:
- Cung `eventId`?
- Service restart gan day (mat memory dedupe)?
- Co nhieu instance service khong share dedupe store?

## 15. Thu tu doc code de onboard nhanh

Neu nguoi moi vao du an, nen doc theo thu tu:
1. `Program.cs`
2. `Core/Services/MessageOrchestrator.cs`
3. `Core/Services/CheckInEventHandler.cs`
4. `Infrastructure/RabbitMq/*`
5. `Infrastructure/Tiger/*`
6. `Infrastructure/Pms/PmsCallbackClient.cs`
7. `appsettings.json`
8. `scripts/rabbitmq-topology.md`

Doc theo thu tu nay se hieu duoc 90% hanh vi runtime trong 15-30 phut.

## 16. Quick reference (tam nho)

- API ingest: `POST /events/checkin`
- Health: `GET /health`
- Demo callback: `POST /pms/callback`
- Main queue: `tigertms.events.q`
- Dead queue: `tigertms.dead.q`
- Header bat buoc xu ly: `x-hotel-id`, `x-event-id`, `x-event-type`
- EventType hien co: `CHECKIN`
- Ack strategy: mostly ACK-after-republish

---

Tai lieu duoc tao tu source hien tai. Khi code thay doi, cap nhat file nay truoc de giu onboarding cost thap.
