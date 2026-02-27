# Phân Tích Kiến Trúc — Service Integration TigerTMS

> **Mục tiêu tài liệu:** Phân tích mẫu kiến trúc đang sử dụng, đánh giá điểm mạnh/yếu,
> đề xuất cải tiến, và cung cấp sơ đồ kiến trúc phân tầng chuẩn cho dự án .NET Minimal API.

---

## 1. Nhận diện mẫu kiến trúc (Architectural Pattern)

Dự án hiện tại kết hợp **3 mẫu kiến trúc chính**:

```mermaid
mindmap
  root((Service Integration\nTigerTMS))
    Clean Architecture
      Core/Abstractions = Domain boundary
      Infrastructure = implementation detail
      Endpoints = delivery mechanism
    Event-Driven Architecture
      RabbitMQ broker
      Async fire-and-forget
      Retry ladder pattern
    Hexagonal Architecture
      Ports = IEventHandler, ITigerClient, IPmsCallbackClient, IIdempotencyStore
      Adapters = TigerClient, PmsCallbackClient, RabbitConsumer, MemoryIdempotencyStore
```

| Mẫu kiến trúc | Biểu hiện trong code | Mục đích |
|---|---|---|
| **Clean Architecture** | `Core/Abstractions/` chứa interfaces; `Infrastructure/` chứa implementation | Tách business rule khỏi detail kỹ thuật |
| **Hexagonal (Ports & Adapters)** | `IEventHandler`, `ITigerClient`, `IPmsCallbackClient` = Ports; các class Infrastructure = Adapters | Cho phép thay thế implementation không ảnh hưởng nghiệp vụ |
| **Event-Driven + CQRS-lite** | `EventHandlerRegistry` + `MessageOrchestrator` + `IEventHandler` | Mỗi loại event có handler riêng, dễ mở rộng |
| **Outbox-style Retry** | Retry queues `10s → 1m → 5m → 30m → Dead` | Đảm bảo at-least-once delivery, chịu lỗi tạm thời |
| **Idempotency Pattern** | `IIdempotencyStore.SeenRecently()` + `MarkSeen()` | Tránh xử lý trùng khi retry |

---

## 2. Sơ đồ kiến trúc phân tầng hiện tại (As-Is)

```mermaid
graph TB
    subgraph EXT["EXTERNAL SYSTEMS"]
        direction LR
        PMS_SRC(["🏨 PMS Source\nHTTP Client"])
        TIGER(["🐯 TigerTMS\nSOAP/XML"])
        PMS_CB(["🔔 PMS Callback\nHTTP POST"])
        ES(["📊 Elasticsearch\nAudit Logs"])
        MQ_BROKER(["🐰 RabbitMQ Broker\nAMQP"])
    end

    subgraph SINGLE_PROJECT["📦 ServiceIntegration.csproj  ← Single Project (monolith)"]
        direction TB

        subgraph L1["① Presentation — Endpoints/"]
            EP1["CheckInEndpoints\nPOST /events/checkin"]
            EP2["PmsEndpoints\nGET /health · POST /pms/callback"]
        end

        subgraph L2["② Application — Core/Services/ + Core/Abstractions/"]
            direction TB
            ORCH["MessageOrchestrator"]
            REGISTRY["EventHandlerRegistry"]
            HANDLER["CheckInEventHandler"]
            RETRY_R["RetryRouter"]
            subgraph PORTS["Ports (Interfaces)"]
                direction LR
                I1["IEventHandler"]
                I2["ITigerClient"]
                I3["IPmsCallbackClient"]
                I4["IIdempotencyStore"]
                I5["IIntegrationQueue"]
                I6["IElasticLogger"]
            end
        end

        subgraph L3["③ Infrastructure — Infrastructure/"]
            direction LR
            subgraph RMQ["RabbitMq/"]
                RCF["RabbitConnectionFactory"]
                RTOP["RabbitTopology"]
                RPUB["RabbitPublisher"]
                RCONS["RabbitConsumer"]
            end
            subgraph TIGER_I["TigerTms/"]
                TC["TigerClient"]
                TSB["TigerSoapBuilder\n⚠️ static class"]
            end
            subgraph PMS_I["Pms/"]
                PCC["PmsCallbackClient"]
            end
            subgraph EL_I["Elastic/"]
                ELC["ElasticLogger"]
                ELENTRY["ElasticLogEntry"]
            end
            subgraph IDMP["Idempotency/"]
                MIS["MemoryIdempotencyStore\n⚠️ in-memory only"]
            end
            subgraph CFG["Configuration/"]
                OPT["Options.cs\n⚠️ trong Infrastructure"]
            end
            subgraph WRK["Workers/"]
                QW["QueueWorker\nBackgroundService"]
            end
        end

        subgraph L4["④ Composition Root — Extensions/ + Program.cs"]
            SE["ServiceExtensions\nAddAppOptions()\nAddAppHttpClients()\nAddAppInfrastructure()\nAddAppServices()"]
            PROG["Program.cs\nMinimal API bootstrap"]
        end
    end

    PMS_SRC -->|"HTTP POST JSON"| EP1
    EP1 -->|"IIntegrationQueue.PublishAsync"| RPUB
    RPUB -->|"AMQP publish"| MQ_BROKER
    MQ_BROKER -->|"AMQP consume"| RCONS
    RCONS --> QW --> ORCH --> REGISTRY --> HANDLER
    HANDLER -->|"ITigerClient"| TC -->|"SOAP/XML"| TIGER
    HANDLER -->|"IPmsCallbackClient"| PCC -->|"HTTP POST"| PMS_CB
    HANDLER -->|"IElasticLogger"| ELC -->|"HTTP Bulk"| ES
    HANDLER -->|"IIdempotencyStore"| MIS

    classDef ext     fill:#dbeafe,stroke:#3b82f6,color:#1e3a5f,font-weight:bold
    classDef present fill:#dcfce7,stroke:#16a34a,color:#14532d
    classDef app     fill:#fef9c3,stroke:#ca8a04,color:#713f12
    classDef infra   fill:#f3e8ff,stroke:#9333ea,color:#3b0764
    classDef warn    fill:#fecaca,stroke:#dc2626,color:#7f1d1d
    classDef comp    fill:#e0f2fe,stroke:#0284c7,color:#0c4a6e

    class PMS_SRC,TIGER,PMS_CB,ES,MQ_BROKER ext
    class EP1,EP2 present
    class ORCH,REGISTRY,HANDLER,RETRY_R,I1,I2,I3,I4,I5,I6 app
    class RCF,RTOP,RPUB,RCONS,TC,PCC,ELC,ELENTRY,MIS,QW infra
    class TSB,MIS,OPT warn
    class SE,PROG comp
```

---

## 3. Phân tích Separation of Concerns

### 3.1 Ranh giới tích hợp bên ngoài (External Integration Boundary)

```mermaid
graph LR
    subgraph INTERNAL["🔵 INTERNAL — Pure Business Logic"]
        direction TB
        ORCH2["MessageOrchestrator\n(không biết RabbitMQ)"]
        HDL2["CheckInEventHandler\n(không biết HTTP/SOAP)"]
        RETRY2["RetryRouter\n(chỉ biết attempt number)"]
    end

    subgraph BOUNDARY["🟡 BOUNDARY — Ports (Interface)"]
        direction TB
        P1["ITigerClient\n«interface»"]
        P2["IPmsCallbackClient\n«interface»"]
        P3["IIdempotencyStore\n«interface»"]
        P4["IIntegrationQueue\n«interface»"]
        P5["IQueueConsumer\n«interface»"]
        P6["IElasticLogger\n«interface»"]
    end

    subgraph EXTERNAL_ADAPT["🔴 EXTERNAL — Adapters (Infrastructure)"]
        direction TB
        A1["TigerClient\nHTTP + SOAP builder"]
        A2["PmsCallbackClient\nHTTP POST"]
        A3["MemoryIdempotencyStore\nConcurrentDictionary"]
        A4["RabbitPublisher\nAMQP publish"]
        A5["RabbitConsumer\nAMQP subscribe"]
        A6["ElasticLogger\nHTTP bulk"]
    end

    subgraph EXT2["🌐 EXTERNAL SYSTEMS"]
        T["TigerTMS\n(SOAP/XML)"]
        P["PMS System\n(HTTP)"]
        R["RabbitMQ\n(AMQP)"]
        E["Elasticsearch\n(HTTP)"]
    end

    HDL2 --> P1 & P2 & P3
    ORCH2 --> P4 & P5
    P1 --- A1 --> T
    P2 --- A2 --> P
    P3 --- A3
    P4 --- A4 --> R
    P5 --- A5 --> R
    P6 --- A6 --> E
    HDL2 --> P6

    classDef internal fill:#dbeafe,stroke:#1d4ed8
    classDef boundary fill:#fef9c3,stroke:#b45309
    classDef adapter  fill:#f3e8ff,stroke:#7c3aed
    classDef extSys   fill:#dcfce7,stroke:#15803d

    class ORCH2,HDL2,RETRY2 internal
    class P1,P2,P3,P4,P5,P6 boundary
    class A1,A2,A3,A4,A5,A6 adapter
    class T,P,R,E extSys
```

### 3.2 TigerTMS SOAP Integration Boundary (Chi tiết)

```mermaid
sequenceDiagram
    box Internal (Application Layer)
        participant H as CheckInEventHandler
    end
    box Boundary (Port)
        participant I as ITigerClient
    end
    box Infrastructure (Adapter)
        participant TC as TigerClient
        participant SB as TigerSoapBuilder
        participant HC as HttpClient "TigerTms"
    end
    box External
        participant TIG as TigerTMS Server\n(SOAP/XML)
    end

    H->>I: SendCheckInAsync(innerXml, ct)
    Note over H,I: Handler chỉ biết interface,<br/>KHÔNG biết SOAP/HTTP
    I->>TC: (concrete impl)
    TC->>SB: WrapCheckIn(EscapeInnerXml(innerXml))
    Note over TC,SB: Build SOAP envelope,<br/>escape XML entities
    TC->>HC: POST {endpoint}\nContent-Type: text/xml\nSOAPAction: {action}
    HC->>TIG: HTTP POST (SOAP XML body)
    TIG-->>HC: HTTP 200 + XML response
    HC-->>TC: raw string
    TC->>TC: parse: contains "SUCCESS"?
    TC-->>I: TigerResult(IsSuccess, RawResponse)
    I-->>H: TigerResult
    Note over H: Handler quyết định ACK / Retry<br/>dựa trên IsSuccess
```

---

## 4. Đánh giá điểm mạnh / điểm yếu

### ✅ Điểm mạnh

| Điểm mạnh | Lý do |
|---|---|
| **Interface-first (Ports)** | Toàn bộ external dependency được abstract qua interface → dễ test, dễ thay thế |
| **Async fire-and-forget** | Endpoint trả về ngay lập tức, processing hoàn toàn tách biệt → high throughput |
| **Retry ladder có cấu trúc** | `10s → 1m → 5m → 30m → Dead` với `x-attempt` header → resilient, không mất message |
| **Idempotency built-in** | Kiểm tra trước mọi I/O, mark sau khi toàn bộ thành công → safe at-least-once |
| **Structured audit logging** | `ElasticLogEntry` + `TimedAsync` → latency tracking + full audit trail |
| **Mock mode** | `TigerOptions.Enabled=false` → test không cần external system |
| **Graceful startup** | RabbitMQ lỗi không crash service → endpoint vẫn hoạt động |

### ⚠️ Điểm yếu & Rủi ro

| Vấn đề | Mức độ | Mô tả |
|---|---|---|
| **Monolithic single project** | 🔴 Cao | Tất cả layers trong 1 `.csproj` → không enforce dependency rule ở build time |
| **`RabbitPublisher` leak vào Application** | 🔴 Cao | `CheckInEventHandler` inject trực tiếp `RabbitPublisher` (concrete) thay vì `IIntegrationQueue` cho retry |
| **`MemoryIdempotencyStore`** | 🔴 Cao | Chỉ hoạt động 1 instance, mất data khi restart → sai với multi-instance deployment |
| **`TigerSoapBuilder` static class** | 🟡 Trung bình | Static không mockable → khó test `TigerClient` đơn vị |
| **Options trong Infrastructure** | 🟡 Trung bình | `Options.cs` đặt trong `Infrastructure/Configuration/` → Core không tham chiếu được trực tiếp |
| **Không có validation middleware** | 🟡 Trung bình | Validation logic viết thẳng trong endpoint handler |
| **Không có health check có cấu trúc** | 🟡 Trung bình | Chỉ có `GET /health` tĩnh, không kiểm tra Rabbit/Elastic connectivity |
| **Không có OpenTelemetry/Metrics** | 🟢 Thấp | Thiếu traces phân tán để debug khi tích hợp nhiều service |
| **SOAP parsing bằng string.Contains** | 🟡 Trung bình | `raw.Contains("SUCCESS")` — brittle, không parse XML structure đúng nghĩa |

---

## 5. Kiến trúc đề xuất cải tiến (To-Be)

### 5.1 Phân tách dự án (Multi-project solution)

```mermaid
graph TB
    subgraph SOL["📂 Solution: ServiceIntegration.sln"]
        direction TB

        subgraph CORE_P["📦 ServiceIntegration.Core\n(không dependency bên ngoài)"]
            direction LR
            ABS["Abstractions/\nIEventHandler\nITigerClient\nIPmsCallbackClient\nIIdempotencyStore\nIIntegrationQueue\nIQueueConsumer\nIElasticLogger"]
            CONTRACTS["Contracts/\nCheckInPayload\nEventEnvelope\nPmsCallbackRequest\nTigerResult"]
            SERVICES["Services/\nMessageOrchestrator\nCheckInEventHandler\nEventHandlerRegistry\nRetryRouter"]
            OPTS_CORE["Options/\n✅ RabbitOptions\nTigerOptions\nPmsCallbackOptions\nRetryPolicyOptions"]
        end

        subgraph INFRA_P["📦 ServiceIntegration.Infrastructure\n(depends on Core)"]
            direction LR
            RMQ2["RabbitMq/\nRabbitConnectionFactory\nRabbitTopology\nRabbitPublisher\nRabbitConsumer"]
            TIGER2["TigerTms/\n✅ ITigerSoapBuilder (interface)\nTigerClient\nTigerSoapBuilder"]
            PMS2["Pms/\nPmsCallbackClient"]
            ELS2["Elastic/\nElasticLogger\nElasticLogEntry"]
            IDMP2["Idempotency/\nMemoryIdempotencyStore\n✅ RedisIdempotencyStore"]
            WRK2["Workers/\nQueueWorker"]
        end

        subgraph API_P["📦 ServiceIntegration.Api\n(depends on Core + Infrastructure)"]
            direction LR
            EP3["Endpoints/\nCheckInEndpoints\nPmsEndpoints"]
            EXT2["Extensions/\nServiceExtensions"]
            PRG["Program.cs"]
            HC["HealthChecks/\n✅ RabbitHealthCheck\nElasticHealthCheck"]
        end

        subgraph TEST_P["📦 ServiceIntegration.Tests\n(test projects)"]
            direction LR
            UT["UnitTests/\nCheckInEventHandlerTests\nRetryRouterTests\nTigerSoapBuilderTests"]
            IT["IntegrationTests/\nCheckInEndpointTests"]
        end
    end

    CORE_P -->|"referenced by"| INFRA_P
    CORE_P -->|"referenced by"| API_P
    INFRA_P -->|"referenced by"| API_P
    CORE_P -->|"referenced by"| TEST_P
    INFRA_P -->|"referenced by"| TEST_P

    classDef core  fill:#fef9c3,stroke:#ca8a04,color:#713f12,font-weight:bold
    classDef infra fill:#f3e8ff,stroke:#9333ea,color:#3b0764,font-weight:bold
    classDef api   fill:#dcfce7,stroke:#16a34a,color:#14532d,font-weight:bold
    classDef test  fill:#e0f2fe,stroke:#0284c7,color:#0c4a6e,font-weight:bold

    class CORE_P,ABS,CONTRACTS,SERVICES,OPTS_CORE core
    class INFRA_P,RMQ2,TIGER2,PMS2,ELS2,IDMP2,WRK2 infra
    class API_P,EP3,EXT2,PRG,HC api
    class TEST_P,UT,IT test
```

### 5.2 Dependency Rule (hướng phụ thuộc)

```mermaid
flowchart LR
    API["ServiceIntegration.Api"]
    INFRA["ServiceIntegration.Infrastructure"]
    CORE["ServiceIntegration.Core"]
    EXT_SYS["External Systems\n(RabbitMQ, TigerTMS, etc.)"]

    API -->|"depends on"| INFRA
    API -->|"depends on"| CORE
    INFRA -->|"depends on"| CORE
    INFRA -->|"depends on"| EXT_SYS
    CORE -.->|"NO dependency ✅"| INFRA
    CORE -.->|"NO dependency ✅"| API
    CORE -.->|"NO dependency ✅"| EXT_SYS

    note["✅ Core không biết gì về Infrastructure\n     Dependency chỉ đi từ ngoài vào trong"]

    style CORE fill:#fef9c3,stroke:#ca8a04,font-weight:bold
    style INFRA fill:#f3e8ff,stroke:#9333ea
    style API fill:#dcfce7,stroke:#16a34a
    style EXT_SYS fill:#dbeafe,stroke:#3b82f6
```

### 5.3 Cải tiến cụ thể theo từng vấn đề

```mermaid
graph LR
    subgraph FIX1["Fix 1: Tách RabbitPublisher khỏi Handler"]
        OLD1["CheckInEventHandler\ninject RabbitPublisher ❌"]
        NEW1["CheckInEventHandler\ninject IRetryQueue ✅\n(interface riêng cho retry)"]
    end

    subgraph FIX2["Fix 2: Idempotency Store cho multi-instance"]
        OLD2["MemoryIdempotencyStore\n(in-memory) ❌"]
        NEW2["RedisIdempotencyStore\n(IDistributedCache) ✅"]
    end

    subgraph FIX3["Fix 3: SOAP Builder làm interface"]
        OLD3["TigerSoapBuilder\n(static class) ❌"]
        NEW3["ITigerSoapBuilder\n+ TigerSoapBuilder (impl) ✅"]
    end

    subgraph FIX4["Fix 4: Validation tách biệt"]
        OLD4["Validation trong Endpoint ❌\nif (string.IsNullWhiteSpace...)"]
        NEW4["IValidator<EventEnvelope> ✅\n(FluentValidation)\nhoặc endpoint filter"]
    end

    subgraph FIX5["Fix 5: Health Checks"]
        OLD5["GET /health → always 200 ❌"]
        NEW5["AddHealthChecks()\n.AddRabbitMQ()\n.AddUrlGroup(elasticUri)\n→ /health/ready · /health/live ✅"]
    end

    OLD1 --> NEW1
    OLD2 --> NEW2
    OLD3 --> NEW3
    OLD4 --> NEW4
    OLD5 --> NEW5

    classDef old fill:#fecaca,stroke:#dc2626
    classDef new fill:#dcfce7,stroke:#16a34a

    class OLD1,OLD2,OLD3,OLD4,OLD5 old
    class NEW1,NEW2,NEW3,NEW4,NEW5 new
```

---

## 6. Cấu trúc folder đề xuất (Improved Structure)

```
ServiceIntegration.sln
│
├── src/
│   ├── ServiceIntegration.Core/               ← Pure domain, zero external dependency
│   │   ├── Abstractions/
│   │   │   ├── IEventHandler.cs
│   │   │   ├── ITigerClient.cs
│   │   │   ├── IPmsCallbackClient.cs
│   │   │   ├── IIdempotencyStore.cs
│   │   │   ├── IIntegrationQueue.cs
│   │   │   ├── IRetryQueue.cs                 ← ✅ tách khỏi IIntegrationQueue
│   │   │   ├── IQueueConsumer.cs
│   │   │   ├── ITigerSoapBuilder.cs           ← ✅ interface cho SOAP builder
│   │   │   └── IElasticLogger.cs
│   │   ├── Contracts/
│   │   │   ├── CheckInPayload.cs
│   │   │   ├── EventEnvelope.cs
│   │   │   ├── PmsCallbackRequest.cs
│   │   │   └── TigerResult.cs
│   │   ├── Options/                           ← ✅ chuyển từ Infrastructure sang Core
│   │   │   ├── RabbitOptions.cs
│   │   │   ├── TigerOptions.cs
│   │   │   ├── PmsCallbackOptions.cs
│   │   │   ├── ElasticOptions.cs
│   │   │   └── RetryPolicyOptions.cs
│   │   └── Services/
│   │       ├── MessageOrchestrator.cs
│   │       ├── EventHandlerRegistry.cs
│   │       ├── CheckInEventHandler.cs
│   │       └── RetryRouter.cs
│   │
│   ├── ServiceIntegration.Infrastructure/     ← Adapters, I/O, external systems
│   │   ├── RabbitMq/
│   │   │   ├── RabbitConnectionFactory.cs
│   │   │   ├── RabbitTopology.cs
│   │   │   ├── RabbitPublisher.cs             ← implements IIntegrationQueue + IRetryQueue
│   │   │   └── RabbitConsumer.cs
│   │   ├── TigerTms/
│   │   │   ├── TigerClient.cs                 ← implements ITigerClient
│   │   │   └── TigerSoapBuilder.cs            ← implements ITigerSoapBuilder
│   │   ├── Pms/
│   │   │   └── PmsCallbackClient.cs           ← implements IPmsCallbackClient
│   │   ├── Elastic/
│   │   │   ├── ElasticLogger.cs               ← implements IElasticLogger
│   │   │   └── ElasticLogEntry.cs
│   │   ├── Idempotency/
│   │   │   ├── MemoryIdempotencyStore.cs      ← single-instance dev/test
│   │   │   └── RedisIdempotencyStore.cs       ← ✅ production multi-instance
│   │   └── Workers/
│   │       └── QueueWorker.cs
│   │
│   └── ServiceIntegration.Api/                ← Composition root + HTTP surface
│       ├── Endpoints/
│       │   ├── CheckInEndpoints.cs
│       │   └── PmsEndpoints.cs
│       ├── Extensions/
│       │   └── ServiceExtensions.cs
│       ├── HealthChecks/                      ← ✅ mới
│       │   ├── RabbitHealthCheck.cs
│       │   └── ElasticHealthCheck.cs
│       ├── Filters/                           ← ✅ mới — validation endpoint filter
│       │   └── EventEnvelopeValidationFilter.cs
│       └── Program.cs
│
└── tests/
    ├── ServiceIntegration.UnitTests/
    │   ├── CheckInEventHandlerTests.cs
    │   ├── RetryRouterTests.cs
    │   └── TigerSoapBuilderTests.cs
    └── ServiceIntegration.IntegrationTests/
        └── CheckInEndpointTests.cs
```

---

## 7. Lộ trình cải tiến (Improvement Roadmap)

```mermaid
gantt
    title Lộ trình cải tiến kiến trúc
    dateFormat  YYYY-MM-DD
    section Phase 1 — Critical
        Tách IRetryQueue khỏi RabbitPublisher              :p1a, 2026-03-01, 3d
        Di chuyển Options sang Core                        :p1b, 2026-03-01, 1d
        Thêm ITigerSoapBuilder interface                   :p1c, after p1a, 2d
    section Phase 2 — Stability
        Tách multi-project solution                        :p2a, after p1c, 5d
        Implement RedisIdempotencyStore                    :p2b, after p2a, 3d
        Thêm Health Checks (RabbitMQ + Elastic)            :p2c, after p2a, 2d
    section Phase 3 — Quality
        FluentValidation cho EventEnvelope                 :p3a, after p2c, 3d
        Viết Unit Tests (Core layer)                       :p3b, after p2c, 5d
        OpenTelemetry traces                               :p3c, after p3b, 4d
```

### Mức ưu tiên

| # | Cải tiến | Ưu tiên | Lý do |
|---|---|---|---|
| 1 | Tách `IRetryQueue` — đưa reference từ concrete `RabbitPublisher` về interface | 🔴 Ngay | Vi phạm Dependency Inversion trực tiếp |
| 2 | `RedisIdempotencyStore` thay thế `MemoryIdempotencyStore` | 🔴 Ngay | Rủi ro duplicate khi deploy multi-instance hoặc restart |
| 3 | `ITigerSoapBuilder` làm interface | 🟡 Soon | Mở khóa khả năng unit test `TigerClient` không cần HTTP |
| 4 | Di chuyển `Options.cs` vào Core | 🟡 Soon | Core hiện phụ thuộc ngầm vào namespace Infrastructure |
| 5 | Tách multi-project | 🟡 Soon | Enforce dependency rule ở compiler level |
| 6 | Health checks đầy đủ | 🟢 Later | Cần cho monitoring production (k8s readiness/liveness) |
| 7 | Unit tests cho Core layer | 🟢 Later | Core layer hiện 0% test coverage |
| 8 | OpenTelemetry | 🟢 Later | Distributed tracing khi scale |

---

*Tài liệu được phân tích dựa trên source code thực tế — cập nhật khi kiến trúc thay đổi.*
