Ok, mình giải thích cấu trúc source demo theo kiểu “đọc là hiểu ngay”, kèm vai trò từng file và luồng chạy qua các lớp. (Tất cả ghi chú tiếng Việt.)

---

## 1) Tổng quan thư mục

```
src/ServiceIntegrationDemo
├─ Program.cs
├─ appsettings.json
├─ GlobalUsings.cs
├─ Core
│  ├─ Abstractions
│  │  ├─ IEventHandler.cs
│  │  ├─ IIdempotencyStore.cs
│  │  ├─ IIntegrationQueue.cs
│  │  ├─ IPmsCallbackClient.cs
│  │  ├─ ITigerClient.cs
│  │  ├─ IQueueConsumer.cs
│  │  └─ MessageHeaders.cs
│  ├─ Contracts
│  │  ├─ EventEnvelope.cs
│  │  └─ CheckInPayload.cs
│  └─ Services
│     ├─ CheckInEventHandler.cs
│     ├─ EventHandlerRegistry.cs
│     ├─ MessageOrchestrator.cs
│     └─ RetryRouter.cs
└─ Infrastructure
   ├─ Options.cs
   ├─ MemoryIdempotencyStore.cs
   ├─ RabbitMq
   │  ├─ RabbitConnectionFactory.cs
   │  ├─ RabbitConsumer.cs
   │  ├─ RabbitPublisher.cs
   │  └─ RabbitTopology.cs
   ├─ Tiger
   │  ├─ TigerClient.cs
   │  └─ TigerSoapBuilder.cs
   ├─ Pms
   │  └─ PmsCallbackClient.cs
   └─ Worker
      └─ QueueWorker.cs
```

---

## 2) “Điểm vào” của ứng dụng

### `Program.cs`

Đây là file quan trọng nhất, nó làm 5 việc:

1. **Config log** Serilog (Console + Elastic nếu bật)
2. **Đăng ký DI** (Rabbit, Tiger, PMS callback, handlers…)
3. **Ensure topology RabbitMQ** (tạo exchange/queue/binding cơ bản)
4. **Start Worker** (`QueueWorker`) để consume queue
5. Expose API:

   * `POST /events/checkin` : nhận event từ PMS → publish MQ
   * `POST /pms/callback` : endpoint mock callback (demo)

> Nếu cần hiểu luồng chạy, đọc `Program.cs` đầu tiên.

---

## 3) Core là gì? (logic nghiệp vụ, không phụ thuộc Rabbit/Tiger/PMS thật)

### `Core/Abstractions/*`

Đây là các **interface** giúp code dễ mở rộng (sau này leader muốn thêm DB cũng chỉ cắm implementation).

* `IEventHandler`
  → “Mỗi eventType có 1 handler”. Đây là điểm mở rộng lớn nhất.

* `IIntegrationQueue`
  → publish message vào queue (hiện impl là RabbitPublisher)

* `IQueueConsumer`
  → consume message từ queue (hiện impl là RabbitConsumer)

* `ITigerClient`
  → gọi Tiger (SOAP)

* `IPmsCallbackClient`
  → gọi callback về PMS

* `IIdempotencyStore`
  → chống trùng “best effort” (hiện là MemoryCache)

* `MessageHeaders`
  → wrapper để đọc/ghi headers RabbitMQ (`x-hotel-id`, `x-event-id`, `x-attempt`, …)

---

## 4) Contracts là gì? (DTO)

### `Core/Contracts/EventEnvelope.cs`

Model cho API input khi PMS gọi vào service:

* `eventId`
* `hotelId`
* `occurredAt`
* `payload` (JSON object)

### `Core/Contracts/CheckInPayload.cs`

Model của payload event CHECKIN (các field map sang XML Tiger).

---

## 5) Services (luồng xử lý trong Core)

### `Core/Services/EventHandlerRegistry.cs`

* Nắm danh sách handler theo `eventType`
* Ví dụ: `"CHECKIN" -> CheckInEventHandler`

### `Core/Services/MessageOrchestrator.cs`

* Là “dispatcher”: nhận 1 message từ queue → đọc headers → tìm handler → gọi `handler.HandleAsync()`

Bạn có thể coi nó là **router** của hệ thống.

### `Core/Services/RetryRouter.cs`

* Quyết định retry route dựa vào attempt:

  * attempt 0 → retry 10s
  * attempt 1 → retry 1m
  * attempt 2 → retry 5m
  * attempt 3 → retry 30m
  * attempt >=4 → dead

### `Core/Services/CheckInEventHandler.cs`

File quan trọng thứ 2 sau Program.cs. Đây là “nghiệp vụ CHECKIN”:

Luồng trong handler:

1. Dedupe cache (demo)
2. Parse payload JSON → `CheckInPayload`
3. Lấy `wsuserkey` từ header/config
4. Build inner XML: `<checkinresults ...>`
5. Gọi `ITigerClient.SendCheckInAsync()`
6. Nếu Tiger OK → callback PMS (`IPmsCallbackClient.NotifyAsync()`)
7. Nếu Tiger fail/callback fail → **republish** sang retry exchange với `x-attempt + 1`
8. Nếu OK hết → ACK message

> Đây là nơi bạn sẽ test các case “Tiger timeout”, “callback fail”, “retry”, “dead-letter”.

---

## 6) Infrastructure là gì? (kết nối thật: Rabbit / Tiger / PMS / Worker)

### `Infrastructure/Options.cs`

Chứa class mapping config từ `appsettings.json`:

* `RabbitOptions`
* `TigerOptions`
* `PmsCallbackOptions`
* `ElasticOptions`

### `Infrastructure/RabbitMq/*`

* `RabbitConnectionFactory.cs`
  tạo connection tới cluster nodes

* `RabbitTopology.cs`
  tạo exchange/queue/binding cơ bản (events + dead)

* `RabbitPublisher.cs`
  publish message vào events exchange
  và publish retry vào retry exchange

* `RabbitConsumer.cs`
  consume từ `tigertms.events.q` và gọi `onMessage`

### `Infrastructure/Tiger/*`

* `TigerSoapBuilder.cs`
  build inner XML + wrap SOAP envelope

* `TigerClient.cs`
  gọi HTTP POST sang Tiger
  (Enabled=false thì mock SUCCESS)

### `Infrastructure/Pms/PmsCallbackClient.cs`

* gọi `POST /pms/callback` (demo)
* sau này bạn đổi URL sang PMS thật

### `Infrastructure/Worker/QueueWorker.cs`

* hosted background service
* start consumer và gọi `MessageOrchestrator`

---

## 7) Luồng chạy thực tế “từ API đến Tiger”

Bạn có thể trace như sau:

1. `Program.cs` endpoint `/events/checkin`

   * tạo headers (`x-event-id`, `x-hotel-id`, `x-attempt=0`, `x-wsuserkey`, …)
   * `RabbitPublisher.PublishAsync(...)`

2. RabbitMQ đưa message vào `tigertms.events.q`

3. `QueueWorker` → `RabbitConsumer` nhận message

4. `MessageOrchestrator.ProcessAsync(...)`

   * lấy `x-event-type=CHECKIN`
   * gọi `CheckInEventHandler.HandleAsync(...)`

5. `CheckInEventHandler`

   * build XML
   * `TigerClient.SendCheckInAsync`
   * callback PMS
   * ACK hoặc republish retry

---

## 8) Chỗ nào để “mở rộng cho event khác”?

Bạn chỉ cần:

1. Tạo payload DTO mới trong `Core/Contracts`
2. Tạo handler mới trong `Core/Services` implement `IEventHandler`
3. Đăng ký DI trong `Program.cs`:

   * `builder.Services.AddSingleton<IEventHandler, NewEventHandler>();`
4. Tạo endpoint mới `/events/<event>` hoặc dùng endpoint chung

Không cần sửa Rabbit/Worker.

---

Nếu bạn muốn, bước tiếp theo mình sẽ làm ngay (không cần bạn test trước):

* Viết tài liệu “**Test case & expected log**” cho 4 case bạn nêu (Tiger timeout, crash, callback fail, PMS gửi trùng) dựa đúng vào cấu trúc source này.
