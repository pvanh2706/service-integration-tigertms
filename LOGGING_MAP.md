# Bản đồ Logging — ServiceIntegration.TigerTMS

> Cập nhật: 2026-02-27  
> Ký hiệu: ✅ đã có · ❌ thiếu · ⚠️ có nhưng chưa đủ

---

## 1. `QueueWorker`

| # | Điểm ghi log | Trạng thái | Lý do |
|---|-------------|-----------|-------|
| 1 | `ExecuteAsync` — service start | ✅ ILogger | Xác nhận service đã khởi động |
| 2 | `StopAsync` — graceful shutdown | ✅ ILogger | Xác nhận dừng sạch |
| 3 | Exception trong callback `onMessage` | ❌ thiếu | Nếu `ProcessAsync` throw, RabbitMQ event handler nuốt exception — message bị block hoặc leak mà không có trace |

---

## 2. `MessageOrchestrator`

| # | Điểm ghi log | Trạng thái | Lý do |
|---|-------------|-----------|-------|
| 4 | Missing required headers → ACK | ✅ ILogger | Poison message phải được ghi nhận |
| 5 | No handler for `eventType` → ACK | ✅ ILogger | Event type không được hỗ trợ |
| 6 | Missing headers — **Elastic entry** | ❌ thiếu ES | Chỉ có ILogger, không có entry ES → mất khả năng trace qua Kibana |
| 7 | No handler — **Elastic entry** | ❌ thiếu ES | Tương tự #6 |

---

## 3. `CheckInEventHandler`

| # | Điểm ghi log | Trạng thái | Lý do |
|---|-------------|-----------|-------|
| 8 | DUPLICATE idempotency hit | ✅ ILogger + ES | Phát hiện event replay |
| 9 | Bắt đầu xử lý (`attempt`, `hotelId`, `eventId`) | ✅ ILogger + ES | Baseline trace cho mỗi message |
| 10 | Payload không hợp lệ → DEAD | ✅ ILogger + ES | Data error, cần audit |
| 11 | Thiếu `wsuserkey` → retry | ✅ ILogger + ES | Config/header error |
| 12 | Raw body khi parse payload fail | ❌ thiếu | Không biết raw bytes là gì → khó debug dữ liệu đầu vào sai |
| 13 | Kết quả `BuildCheckInInnerXml` (DEBUG) | ❌ thiếu | Nếu SOAP gửi đi sai format không có gì để so sánh |
| 14 | Gọi Tiger TMS (`started_at`, `duration_ms`, response) | ✅ ES (`TimedAsync`) | Performance monitoring + audit |
| 15 | Tiger failed → retry | ✅ ILogger + ES | Lỗi phía Tiger TMS |
| 16 | HTTP status code Tiger response khi fail | ❌ thiếu | `TigerClient` không log status code HTTP — mất thông tin phân biệt 4xx/5xx/timeout |
| 17 | Gọi callback PMS (`started_at`, `duration_ms`) | ✅ ES (`TimedAsync`) | Performance monitoring |
| 18 | PMS callback failed → retry | ✅ ILogger + ES | Lỗi phía PMS |
| 19 | PMS HTTP status code + response body khi fail | ❌ thiếu ES | `PmsCallbackClient` chỉ có ILogger, không có ES entry — mất trace |
| 20 | Xử lý thành công → ACK | ✅ ILogger + ES | Happy path audit |
| 21 | Unhandled exception → DEAD (catch ngoài cùng) | ✅ ILogger + ES | Safety net — mọi exception đều được ghi nhận |
| 22 | `Republish` publish confirm fail | ❌ thiếu | Nếu `WaitForConfirms` timeout trong Republish, exception bay ra catch ngoài nhưng không rõ đã publish được chưa |

---

## 4. `TigerClient`

| # | Điểm ghi log | Trạng thái | Lý do |
|---|-------------|-----------|-------|
| 23 | Mock mode (`Enabled=false`) | ✅ ILogger | Dev/test awareness |
| 24 | HTTP request timeout / `TaskCanceledException` | ❌ thiếu | Exception bay ra caller không có log trong `TigerClient` — khó biết timeout hay business error |
| 25 | HTTP response status code khi thất bại (4xx/5xx) | ❌ thiếu | Hiện chỉ check `raw.Contains("SUCCESS")`, không log HTTP status riêng |
| 26 | Raw response đầy đủ khi Tiger fail (DEBUG) | ⚠️ một phần | `FailureReason` bị truncate 300 chars — nên log full raw ở DEBUG level |

---

## 5. `PmsCallbackClient`

| # | Điểm ghi log | Trạng thái | Lý do |
|---|-------------|-----------|-------|
| 27 | Mock mode (`Enabled=false`) | ✅ ILogger | Dev/test awareness |
| 28 | HTTP fail → trả `false` | ✅ ILogger (status code) | Cơ bản đủ |
| 29 | Exception (timeout, network error) | ❌ thiếu | `PostAsJsonAsync` throw → exception lan ra `CheckInEventHandler` không có log trong `PmsCallbackClient` — mất context tầng nào bị lỗi |
| 30 | Response body từ PMS khi fail | ❌ thiếu | Chỉ log status code, không log body lỗi PMS trả về |

---

## 6. `RabbitPublisher`

| # | Điểm ghi log | Trạng thái | Lý do |
|---|-------------|-----------|-------|
| 31 | Connection created | ✅ ILogger | Infra event |
| 32 | Connection shutdown | ✅ ILogger | Connectivity alert |
| 33 | Publish confirm fail / timeout trước khi throw | ❌ thiếu | Throw `Exception` không log trước — caller có thể log nhưng mất routing key + exchange context |
| 34 | Reconnect thành công | ❌ thiếu | Không biết được connection recovery đã xảy ra |

---

## 7. `RabbitConsumer`

| # | Điểm ghi log | Trạng thái | Lý do |
|---|-------------|-----------|-------|
| 35 | Consumer started | ✅ ILogger | Infra event |
| 36 | Exception trong `consumer.Received` event (⚠️ **Critical**) | ❌ thiếu | Hoàn toàn không có try/catch trong lambda — nếu `onMessage` throw, RabbitMQ client nuốt exception, message bị block/leak không traceable |
| 37 | `DisposeAsync` close fail | ⚠️ silent catch | `catch { }` trống — nên `_logger.LogWarning` khi `_ch.Close()` hoặc `_conn.Close()` fail |

---

## 8. `ElasticLogger`

| # | Điểm ghi log | Trạng thái | Lý do |
|---|-------------|-----------|-------|
| 38 | POST Elasticsearch thất bại (non-2xx) | ✅ ILogger fallback | Phát hiện ES unavailable |
| 39 | Exception khi POST | ✅ ILogger fallback | Network error |
| 40 | `Enabled=false` — startup warning | ⚠️ thiếu | Nên log 1 lần lúc startup "ElasticLogger disabled" tránh nhầm tưởng log bị mất |

---

## 9. `RabbitTopology`

| # | Điểm ghi log | Trạng thái | Lý do |
|---|-------------|-----------|-------|
| 41 | Topology ensured (exchanges/queues/bindings) | ✅ ILogger | Startup audit |
| 42 | Exception khi `Ensure()` | ❌ thiếu | Nếu broker unreachable lúc startup, exception không có log trước khi crash |

---

## Tóm tắt theo mức độ ưu tiên

| Mức | # | File | Mô tả |
|-----|---|------|-------|
| 🔴 Cao | 36 | `RabbitConsumer` | Không có try/catch trong `Received` event — có thể gây message leak |
| 🔴 Cao | 6, 7 | `MessageOrchestrator` | Thiếu ES log cho poison message — không traceable qua Kibana |
| 🟡 Trung | 24, 29 | `TigerClient`, `PmsCallbackClient` | HTTP timeout/network error không được log tại source |
| 🟡 Trung | 22, 33 | `CheckInEventHandler`, `RabbitPublisher` | Publish confirm fail không có đủ context |
| 🟢 Thấp | 12, 13, 26, 30 | `CheckInEventHandler`, `TigerClient`, `PmsCallbackClient` | Debug/detail logging hữu ích khi investigation |
| 🟢 Thấp | 34, 37, 40, 42 | Infra layer | Startup/shutdown/recovery awareness |
