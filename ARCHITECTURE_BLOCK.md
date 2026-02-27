# Sơ Đồ Kiến Trúc Khối — Service Integration TigerTMS

> Sơ đồ mức cao (high-level) thể hiện hai luồng chính của hệ thống tích hợp.

---

## Sơ đồ tổng quan

```mermaid
flowchart TD
    %% ─────────────────────────────────────────
    %% LUỒNG 1 — Check-in  (trái → phải → xuống)
    %% ─────────────────────────────────────────

    CLIENT(["🏨 PMS / Client"])

    subgraph API["API Layer"]
        EP_IN["POST /events/checkin\n(CheckInEndpoints)"]
    end

    subgraph QUEUE["Message Broker"]
        MQ[("🐰 RabbitMQ\nevents.queue")]
        MQ_RETRY[("⏱ Retry Queues\n10s · 1m · 5m · 30m")]
        MQ_DEAD[("💀 Dead Queue")]
    end

    subgraph APP["Application Layer"]
        WORKER["QueueWorker\n(BackgroundService)"]
        ORCH["MessageOrchestrator\nRoute by event-type"]
        IDEMPO{{"🔑 Idempotency\nCheck"}}
        HANDLER["CheckInEventHandler\nBusiness Logic"]
        RETRY["RetryRouter\nDecide next queue"]
    end

    subgraph INFRA["Infrastructure Layer"]
        TIGER_C["TigerClient\nSOAP Builder + HTTP"]
        PMS_C["PmsCallbackClient\nHTTP POST"]
        LOG["ElasticLogger\nAudit + Timing"]
    end

    subgraph EXT["External Systems"]
        TIGER(["🐯 TigerTMS\nSOAP/XML"])
        PMS_SYS(["📋 PMS System\nCallback receiver"])
        ES(["📊 Elasticsearch\nAudit Logs"])
    end

    %% ─────────────────────────────────────────
    %% LUỒNG 1 — Ingress: PMS gửi sự kiện
    %% ─────────────────────────────────────────
    CLIENT      -->|"① POST JSON\nEventEnvelope"| EP_IN
    EP_IN       -->|"② Enqueue\n+ correlationId"| MQ
    MQ          -->|"③ Consume"| WORKER
    WORKER      -->|"④ ProcessAsync"| ORCH
    ORCH        -->|"⑤ Route CHECKIN"| IDEMPO

    IDEMPO      -->|"⑥a Duplicate\n→ ACK / bỏ qua"| SINK1(["🚫 Bỏ qua"])
    IDEMPO      -->|"⑥b Lần đầu\n→ xử lý"| HANDLER

    HANDLER     -->|"⑦ Build SOAP\nSendCheckInAsync"| TIGER_C
    TIGER_C     -->|"⑧ HTTP POST\nSOAP/XML"| TIGER

    TIGER       -->|"⑨ Response\nSUCCESS / FAIL"| TIGER_C
    TIGER_C     -->|"⑩ TigerResult"| HANDLER

    %% ─────────────────────────────────────────
    %% LUỒNG 2 — Callback: thông báo kết quả về PMS
    %% ─────────────────────────────────────────
    HANDLER     -->|"⑪ NotifyAsync\n(Tiger SUCCESS)"| PMS_C
    PMS_C       -->|"⑫ HTTP POST JSON\nPmsCallbackRequest"| PMS_SYS

    %% ─────────────────────────────────────────
    %% Retry path
    %% ─────────────────────────────────────────
    HANDLER     -->|"❌ Tiger / PMS lỗi\n→ Republish"| RETRY
    RETRY       -->|"attempt 0–3\nTTL re-route"| MQ_RETRY
    RETRY       -->|"attempt 4+\nhoặc parse error"| MQ_DEAD
    MQ_RETRY    -.->|"TTL hết hạn\n→ quay lại"| MQ

    %% ─────────────────────────────────────────
    %% Logging cross-cutting
    %% ─────────────────────────────────────────
    HANDLER     -.->|"Log mọi bước\nTimedAsync"| LOG
    EP_IN       -.->|"Log ingress"| LOG
    LOG         -.->|"HTTP Bulk"| ES

    %% ─────────────────────────────────────────
    %% Styles
    %% ─────────────────────────────────────────
    classDef extBox  fill:#dbeafe,stroke:#2563eb,color:#1e3a5f,font-weight:bold
    classDef apiBox  fill:#dcfce7,stroke:#16a34a,color:#14532d,font-weight:bold
    classDef appBox  fill:#fef9c3,stroke:#ca8a04,color:#713f12
    classDef infBox  fill:#f3e8ff,stroke:#9333ea,color:#3b0764
    classDef mqBox   fill:#e0f2fe,stroke:#0284c7,color:#0c4a6e
    classDef sink    fill:#f1f5f9,stroke:#94a3b8,color:#475569

    class CLIENT,TIGER,PMS_SYS,ES extBox
    class EP_IN apiBox
    class WORKER,ORCH,IDEMPO,HANDLER,RETRY appBox
    class TIGER_C,PMS_C,LOG infBox
    class MQ,MQ_RETRY,MQ_DEAD mqBox
    class SINK1 sink
```

---

## Giải thích hai luồng chính

### Luồng 1 — Check-in: PMS gửi sự kiện

| Bước | Thành phần | Mô tả |
|:---:|---|---|
| ① | **PMS → CheckInEndpoints** | PMS gửi HTTP POST kèm `EventEnvelope` (JSON) |
| ② | **Endpoint → RabbitMQ** | Endpoint validate input tối thiểu, gán `correlationId`, đẩy vào `events.queue` → trả `200 QUEUED` ngay lập tức |
| ③–④ | **RabbitMQ → QueueWorker** | `QueueWorker` (BackgroundService) liên tục lắng nghe và chuyển message xuống `MessageOrchestrator` |
| ⑤ | **Orchestrator route** | Đọc header `x-event-type`, tra cứu handler phù hợp trong `EventHandlerRegistry` |
| ⑥ | **Idempotency check** | Kiểm tra `(hotelId, eventId)` đã xử lý chưa — nếu trùng thì ACK bỏ qua ngay |
| ⑦–⑨ | **Handler → TigerClient → TigerTMS** | Build SOAP XML, gọi HTTP POST đến TigerTMS, nhận kết quả |
| ⑩ | **TigerResult** | `IsSuccess = true/false` quyết định tiếp tục hay retry |

### Luồng 2 — Callback: thông báo kết quả về PMS

| Bước | Thành phần | Mô tả |
|:---:|---|---|
| ⑪ | **Handler → PmsCallbackClient** | Sau khi Tiger trả SUCCESS, gọi `NotifyAsync` |
| ⑫ | **PmsCallbackClient → PMS** | HTTP POST JSON mang `TigerStatus`, `EventId`, `CorrelationId` về hệ thống PMS gốc |

### Luồng 3 — Retry / Dead-letter

```mermaid
flowchart LR
    ERR(["❌ Lỗi xảy ra\n(Tiger / PMS / Parse)"])
    RR["RetryRouter\nDecide(attempt)"]
    Q1[("retry.10s")]
    Q2[("retry.1m")]
    Q3[("retry.5m")]
    Q4[("retry.30m")]
    QD[("💀 dead.queue")]
    BACK[("events.queue")]

    ERR --> RR
    RR -->|"attempt 0"| Q1
    RR -->|"attempt 1"| Q2
    RR -->|"attempt 2"| Q3
    RR -->|"attempt 3"| Q4
    RR -->|"attempt 4+\nparse error"| QD
    Q1 & Q2 & Q3 & Q4 -->|"TTL hết hạn → re-route"| BACK

    classDef q fill:#e0f2fe,stroke:#0284c7
    classDef dead fill:#fecaca,stroke:#dc2626
    classDef ok fill:#dcfce7,stroke:#16a34a

    class Q1,Q2,Q3,Q4,BACK q
    class QD dead
    class ERR,RR dead
```

> **Nguyên tắc:** Message **không bao giờ bị mất**. Khi lỗi, message được republish vào queue retry với TTL tăng dần. Sau 4 lần thất bại (hoặc lỗi không thể thử lại như parse error), message chuyển vào `dead.queue` để xem xét thủ công.

---

## Ranh giới tích hợp TigerTMS SOAP

```mermaid
flowchart LR
    subgraph INTERNAL["Nội bộ Service"]
        H["CheckInEventHandler"]
        I(["«interface»\nITigerClient"])
    end

    subgraph ADAPTER["Infrastructure Adapter"]
        TC["TigerClient"]
        SB["TigerSoapBuilder\nBuild XML envelope"]
        HC["HttpClient\n'TigerTms'"]
    end

    subgraph EXTERNAL["Hệ thống ngoài"]
        TIG(["🐯 TigerTMS Server\nSOAP/XML over HTTP"])
    end

    H -->|"SendCheckInAsync\n(innerXml)"| I
    I --- TC
    TC --> SB
    SB -->|"WrapCheckIn\nEscapeInnerXml"| TC
    TC -->|"POST text/xml\nSOAPAction header"| HC
    HC -->|"HTTP POST"| TIG
    TIG -->|"XML Response"| HC
    HC -->|"raw string"| TC
    TC -->|"parse SUCCESS?\nTigerResult"| I
    I -->|"TigerResult\n(IsSuccess, RawResponse)"| H

    classDef int  fill:#fef9c3,stroke:#ca8a04
    classDef ada  fill:#f3e8ff,stroke:#9333ea
    classDef ext  fill:#dbeafe,stroke:#2563eb,font-weight:bold

    class H,I int
    class TC,SB,HC ada
    class TIG ext
```

> **Điểm quan trọng:** `CheckInEventHandler` **chỉ biết `ITigerClient`** — không biết gì về SOAP, XML, hay HTTP.
> Toàn bộ chi tiết giao tiếp được đóng gói trong `TigerClient` (adapter) và `TigerSoapBuilder`.
> Đây là ranh giới tách biệt rõ ràng giữa **business logic** và **integration detail**.

---

*Sơ đồ được tổng hợp từ source code thực tế của dự án.*
