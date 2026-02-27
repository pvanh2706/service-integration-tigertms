# Architecture Diagram — Service Integration TigerTMS

> **Mô tả:** Sơ đồ kiến trúc tổng thể của service tích hợp giữa PMS và hệ thống TigerTMS,
> thể hiện các tầng (layer), luồng xử lý request, và vị trí kiểm tra Idempotency.

---

## 1. Sơ đồ kiến trúc phân tầng (Clean Architecture)

```mermaid
graph TB
    %% ============================================================
    %% EXTERNAL SYSTEMS
    %% ============================================================
    PMS_IN(["🏨 PMS / Client\n─────────────\nGửi sự kiện CHECK-IN\nqua HTTP POST"])
    TigerTMS(["🐯 TigerTMS\n─────────────\nHệ thống quản lý TV\nSOAP/XML Interface"])
    PMS_CB(["🔔 PMS Callback\n─────────────\nNhận kết quả xử lý\nHTTP POST trả về"])
    Elastic(["📊 Elasticsearch\n─────────────\nCentralized Logging\nLưu audit trail"])

    %% ============================================================
    %% PRESENTATION LAYER
    %% ============================================================
    subgraph PRES["① PRESENTATION LAYER — Endpoints/"]
        direction LR
        EP_CI["CheckInEndpoints\n─────────────\nPOST /events/checkin\nValidate → Enqueue"]
        EP_PMS["PmsEndpoints\n─────────────\nGET  /health\nPOST /pms/callback"]
    end

    %% ============================================================
    %% INFRASTRUCTURE — RabbitMQ
    %% ============================================================
    subgraph RABBIT["② INFRASTRUCTURE — RabbitMQ Broker"]
        direction LR
        Q_EVT[("📥 events.queue\n(durable)")]
        Q_R10[("⏱ retry.10s.queue")]
        Q_R1M[("⏱ retry.1m.queue")]
        Q_R5M[("⏱ retry.5m.queue")]
        Q_R30[("⏱ retry.30m.queue")]
        Q_DEAD[("💀 dead.queue")]
    end

    %% ============================================================
    %% INFRASTRUCTURE — Worker
    %% ============================================================
    subgraph WORKER["② INFRASTRUCTURE — Background Worker"]
        direction LR
        QW["QueueWorker\n(BackgroundService)"]
        RC["RabbitConsumer\n(IQueueConsumer)"]
    end

    %% ============================================================
    %% APPLICATION LAYER — Core
    %% ============================================================
    subgraph APP["③ APPLICATION LAYER — Core/Services/"]
        direction TB

        ORCH["MessageOrchestrator\n─────────────────────\nNhận ConsumedMessage\nRoute theo x-event-type"]

        subgraph REGISTRY["EventHandlerRegistry"]
            REG["TryGet(eventType)\n→ IEventHandler"]
        end

        subgraph HANDLER_BOX["CheckInEventHandler"]
            direction TB

            IDEMPO_CHK{"🔑 Idempotency\nSeenRecently?\n(HotelId + EventId)"}
            PARSE["Parse CheckInPayload\n(JSON → object)"]
            BUILD["TigerSoapBuilder\nBuildCheckInInnerXml()"]
            CALL_TIGER["Gọi ITigerClient\nSendCheckInAsync()"]
            CHK_TIGER{"TigerRes\n.IsSuccess?"}
            CALL_PMS["Gọi IPmsCallbackClient\nNotifyAsync() → PMS"]
            CHK_PMS{"callback\nok?"}
            MARK["MarkSeen (TTL 6h)\n→ ACK"]
            RETRY_ROUTER["RetryRouter\n─────────────\nattempt 0 → 10s\nattempt 1 → 1m\nattempt 2 → 5m\nattempt 3 → 30m\nattempt 4+ → Dead"]
        end
    end

    %% ============================================================
    %% INFRASTRUCTURE — Clients & Logging
    %% ============================================================
    subgraph INFRA["② INFRASTRUCTURE — Clients/"]
        direction LR
        PUB["RabbitPublisher\n(IIntegrationQueue)"]
        TC["TigerClient\n(ITigerClient)\nHTTP→SOAP"]
        PC["PmsCallbackClient\n(IPmsCallbackClient)\nHTTP POST"]
        EL["ElasticLogger\n(IElasticLogger)\nBulk HTTP"]
        IDS["MemoryIdempotencyStore\n(IIdempotencyStore)\nConcurrentDictionary"]
    end

    %% ============================================================
    %% FLOW — Ingress path (PMS → Enqueue)
    %% ============================================================
    PMS_IN -->|"POST /events/checkin\n(EventEnvelope JSON)"| EP_CI
    EP_CI -->|"Validate headers\nSet x-correlation-id\nPublish bytes + headers"| PUB
    PUB -->|"Publish to\nevents.exchange"| Q_EVT

    %% ============================================================
    %% FLOW — Consumer path (Queue → Handler)
    %% ============================================================
    Q_EVT -->|"Subscribe"| RC
    RC -->|"ConsumedMessage"| QW
    QW -->|"ProcessAsync(msg, ct)"| ORCH
    ORCH --> REG
    REG -->|"CHECKIN handler"| IDEMPO_CHK

    %% ============================================================
    %% FLOW — Handler internals
    %% ============================================================
    IDEMPO_CHK -->|"✅ Chưa thấy\n→ tiếp tục"| PARSE
    IDEMPO_CHK -->|"⚠️ Duplicate\n→ ACK / bỏ qua"| DONE_DUP(["ACK — bỏ qua\n(duplicate)"])

    PARSE -->|"❌ Parse lỗi\n→ forceDead=true"| RETRY_ROUTER
    PARSE -->|"✅ OK"| BUILD
    BUILD --> CALL_TIGER
    CALL_TIGER --> CHK_TIGER

    CHK_TIGER -->|"❌ Tiger lỗi\n→ retry"| RETRY_ROUTER
    CHK_TIGER -->|"✅ Thành công"| CALL_PMS
    CALL_PMS --> CHK_PMS

    CHK_PMS -->|"❌ Callback lỗi\n→ retry"| RETRY_ROUTER
    CHK_PMS -->|"✅ Thành công"| MARK
    MARK --> DONE_OK(["ACK — Xử lý\nthành công ✅"])

    %% ============================================================
    %% FLOW — Retry / Dead-letter
    %% ============================================================
    RETRY_ROUTER -->|"attempt 0"| Q_R10
    RETRY_ROUTER -->|"attempt 1"| Q_R1M
    RETRY_ROUTER -->|"attempt 2"| Q_R5M
    RETRY_ROUTER -->|"attempt 3"| Q_R30
    RETRY_ROUTER -->|"attempt 4+\nhoặc forceDead"| Q_DEAD

    Q_R10 & Q_R1M & Q_R5M & Q_R30 -->|"TTL expired\n→ re-route"| Q_EVT

    %% ============================================================
    %% FLOW — External calls
    %% ============================================================
    TC -->|"SOAP/XML\nHTTP POST"| TigerTMS
    PC -->|"HTTP POST JSON"| PMS_CB
    EL -->|"Bulk HTTP"| Elastic

    %% ============================================================
    %% FLOW — Logging (cross-cutting)
    %% ============================================================
    CALL_TIGER -.->|"Log TimedAsync"| EL
    CALL_PMS   -.->|"Log TimedAsync"| EL
    IDEMPO_CHK -.->|"Log warn"| EL
    RETRY_ROUTER -.->|"Log warn"| EL
    EP_CI      -.->|"Log ingress"| EL

    %% ============================================================
    %% FLOW — Handler → Infra clients
    %% ============================================================
    CALL_TIGER --> TC
    CALL_PMS   --> PC
    IDEMPO_CHK --> IDS
    MARK       --> IDS

    %% ============================================================
    %% STYLES
    %% ============================================================
    classDef external   fill:#dbeafe,stroke:#3b82f6,color:#1e3a5f,font-weight:bold
    classDef present    fill:#dcfce7,stroke:#16a34a,color:#14532d,font-weight:bold
    classDef appLayer   fill:#fef9c3,stroke:#ca8a04,color:#713f12
    classDef infraLayer fill:#f3e8ff,stroke:#9333ea,color:#3b0764
    classDef decision   fill:#fed7aa,stroke:#ea580c,color:#431407
    classDef done       fill:#d1fae5,stroke:#059669,color:#064e3b,font-weight:bold

    class PMS_IN,TigerTMS,PMS_CB,Elastic external
    class EP_CI,EP_PMS present
    class ORCH,REG,IDEMPO_CHK,PARSE,BUILD,CALL_TIGER,CHK_TIGER,CALL_PMS,CHK_PMS,MARK,RETRY_ROUTER appLayer
    class QW,RC,PUB,TC,PC,EL,IDS,Q_EVT,Q_R10,Q_R1M,Q_R5M,Q_R30,Q_DEAD infraLayer
    class DONE_DUP,DONE_OK done
```

---

## 2. Giải thích các tầng

### ① Presentation Layer — `Endpoints/`

| Class | Route | Vai trò |
|---|---|---|
| `CheckInEndpoints` | `POST /events/checkin` | Nhận sự kiện từ PMS, gán `correlationId`, publish vào RabbitMQ qua `IIntegrationQueue` |
| `PmsEndpoints` | `GET /health`, `POST /pms/callback` | Health check và nhận callback demo từ PMS |

> **Nguyên tắc:** Endpoint **không xử lý logic nghiệp vụ**. Chỉ validate input cơ bản (hotelId, eventId) rồi đưa message vào queue ngay lập tức — trả về `202 QUEUED` cho client. Đây là mẫu **async fire-and-forget** để tách biệt ingestion khỏi processing.

---

### ② Infrastructure Layer — `Infrastructure/`

| Thành phần | Class | Vai trò |
|---|---|---|
| **Queue Broker** | `RabbitPublisher`, `RabbitConsumer`, `RabbitTopology` | Quản lý kết nối, khai báo exchange/queue/binding, publish và subscribe message |
| **Background Worker** | `QueueWorker` *(BackgroundService)* | Vòng lặp liên tục consume message từ `IQueueConsumer`, chuyển tiếp xuống `MessageOrchestrator` |
| **TigerTMS Client** | `TigerClient` + `TigerSoapBuilder` | Xây dựng SOAP/XML và gọi HTTP POST đến TigerTMS endpoint |
| **PMS Callback Client** | `PmsCallbackClient` | Gọi HTTP POST trả kết quả về PMS gốc |
| **Logging** | `ElasticLogger` | Ghi audit log có cấu trúc (JSON) lên Elasticsearch, hỗ trợ `TimedAsync` để đo latency |
| **Idempotency Store** | `MemoryIdempotencyStore` | Lưu `(hotelId, eventId)` đã xử lý trong `ConcurrentDictionary` với TTL 6 giờ |

---

### ③ Application Layer — `Core/Services/`

| Class | Vai trò |
|---|---|
| `MessageOrchestrator` | Đọc header `x-event-type`, tra cứu handler trong `EventHandlerRegistry`, uỷ quyền xử lý |
| `EventHandlerRegistry` | Registry pattern — ánh xạ `eventType → IEventHandler` |
| `CheckInEventHandler` | Toàn bộ nghiệp vụ xử lý CHECKIN: idempotency → parse → build SOAP → gọi Tiger → gọi PMS callback → ACK |
| `RetryRouter` | Quyết định queue retry dựa theo số lần thử: `10s → 1m → 5m → 30m → Dead` |

---

## 3. Luồng xử lý chi tiết (Request Flow)

```mermaid
sequenceDiagram
    autonumber
    participant PMS     as 🏨 PMS / Client
    participant EP      as CheckInEndpoints
    participant MQ      as RabbitMQ<br/>(events.queue)
    participant WK      as QueueWorker
    participant ORC     as MessageOrchestrator
    participant HDL     as CheckInEventHandler
    participant IDP     as IdempotencyStore
    participant TIG     as TigerTMS (SOAP)
    participant PMSCB   as PMS Callback
    participant ES      as Elasticsearch

    PMS->>EP: POST /events/checkin { EventEnvelope }
    EP->>ES: Log "CHECKIN_RECEIVED"
    EP->>MQ: Publish(payload bytes, headers)<br/>x-hotel-id, x-event-id, x-correlation-id, x-attempt=0
    EP-->>PMS: 200 { status: "QUEUED", eventId, correlationId }

    Note over MQ,WK: Async — tách biệt ingestion và processing
    MQ->>WK: Deliver ConsumedMessage
    WK->>ORC: ProcessAsync(msg, ct)
    ORC->>ORC: Validate headers (hotelId, eventId, eventType)
    ORC->>HDL: HandleAsync(EventContext, ct)

    HDL->>IDP: SeenRecently(hotelId, eventId)?
    alt Duplicate
        IDP-->>HDL: true
        HDL->>ES: Log WARN "duplicate, bỏ qua"
        HDL->>MQ: ACK (không xử lý lại)
    else Chưa thấy
        IDP-->>HDL: false
        HDL->>HDL: Parse CheckInPayload (JSON)
        HDL->>HDL: TigerSoapBuilder.BuildCheckInInnerXml()
        HDL->>ES: Log "bắt đầu gọi Tiger"
        HDL->>TIG: SendCheckInAsync(innerXml) [SOAP/XML]
        TIG-->>HDL: TigerResponse { IsSuccess, RawResponse }
        HDL->>ES: Log TimedAsync + TigerResponse

        alt Tiger lỗi
            HDL->>ES: Log WARN "Tiger failed → retry"
            HDL->>MQ: PublishToRetry (routing key theo attempt)
            HDL->>MQ: ACK message gốc
        else Tiger thành công
            HDL->>PMSCB: NotifyAsync(PmsCallbackRequest) [HTTP POST]
            PMSCB-->>HDL: ok / fail
            HDL->>ES: Log TimedAsync + PMS status

            alt PMS Callback lỗi
                HDL->>ES: Log WARN "callback failed → retry"
                HDL->>MQ: PublishToRetry
                HDL->>MQ: ACK message gốc
            else Toàn bộ thành công
                HDL->>IDP: MarkSeen(hotelId, eventId, TTL=6h)
                HDL->>ES: Log INFO "xử lý thành công"
                HDL->>MQ: ACK ✅
            end
        end
    end
```

---

## 4. Vị trí xử lý Idempotency

```mermaid
flowchart LR
    A["Message đến từ Queue"] --> B{"SeenRecently?\nIIdempotencyStore"}
    B -->|"✅ Đã thấy\n(duplicate)"| C["Log WARN\n→ ACK ngay\nkhông gọi Tiger"]
    B -->|"❌ Chưa thấy"| D["Xử lý bình thường\nTiger → PMS → ..."]
    D -->|"Thành công hoàn toàn"| E["MarkSeen\n(TTL 6 giờ)"]
    E --> F["ACK"]

    style B fill:#fed7aa,stroke:#ea580c
    style C fill:#fecaca,stroke:#dc2626
    style E fill:#dcfce7,stroke:#16a34a
    style F fill:#dcfce7,stroke:#16a34a
```

| Bước | Nơi thực hiện | Chi tiết |
|---|---|---|
| **Kiểm tra trùng** | `CheckInEventHandler.HandleAsync()` — đầu hàm | `_idempo.SeenRecently(hotelId, eventId)` trước mọi tác vụ I/O |
| **Lưu đã xử lý** | `CheckInEventHandler.HandleAsync()` — cuối hàm (happy path) | `_idempo.MarkSeen(hotelId, eventId, TimeSpan.FromHours(6))` chỉ sau khi Tiger **và** PMS callback đều thành công |
| **Storage** | `MemoryIdempotencyStore` | `ConcurrentDictionary<string, DateTime>` — in-process, reset khi restart |

> ⚠️ **Lưu ý vận hành:** `MemoryIdempotencyStore` chỉ hoạt động trong 1 process. Nếu triển khai multi-instance, cần thay bằng Redis-backed store để idempotency hoạt động chính xác.

---

## 5. Cấu trúc Dependency Injection (ServiceExtensions)

```mermaid
graph LR
    subgraph AddAppOptions
        O1["RabbitOptions\n← appsettings.RabbitMq"]
        O2["TigerOptions\n← appsettings.TigerTms"]
        O3["PmsCallbackOptions\n← appsettings.PmsCallback"]
        O4["RetryPolicyOptions\n← appsettings.RetryPolicy"]
    end

    subgraph AddAppHttpClients
        H1["HttpClient: TigerTms\n(timeout từ TigerOptions)"]
        H2["HttpClient: PmsCallback\n(timeout từ PmsCallbackOptions)"]
        H3["HttpClient: Elastic\n(timeout 5s cứng)"]
    end

    subgraph AddAppInfrastructure
        I1["RabbitConnectionFactory\nSingleton"]
        I2["RabbitTopology  Singleton"]
        I3["RabbitPublisher → IIntegrationQueue\nSingleton"]
        I4["RabbitConsumer → IQueueConsumer\nSingleton"]
        I5["ElasticLogger → IElasticLogger\nSingleton"]
        I6["TigerClient → ITigerClient\nSingleton"]
        I7["PmsCallbackClient → IPmsCallbackClient\nSingleton"]
        I8["MemoryIdempotencyStore → IIdempotencyStore\nSingleton"]
    end

    subgraph AddAppServices
        S1["RetryRouter  Singleton"]
        S2["CheckInEventHandler → IEventHandler\nSingleton"]
        S3["EventHandlerRegistry  Singleton"]
        S4["MessageOrchestrator  Singleton"]
        S5["QueueWorker\nHostedService"]
    end

    AddAppOptions --> AddAppInfrastructure
    AddAppHttpClients --> AddAppInfrastructure
    AddAppInfrastructure --> AddAppServices
```

---

*Tài liệu được tự động tổng hợp từ source code — cập nhật khi có thay đổi kiến trúc.*
