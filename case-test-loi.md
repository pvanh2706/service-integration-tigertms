Rất tốt 👍
Dưới đây là tài liệu **“Test case & Expected log”** dựa đúng vào cấu trúc source bạn đang chạy (RabbitMQ-only, retry qua exchange retry, ACK/NACK trong handler).

Tài liệu này viết theo kiểu bạn có thể dùng để demo với leader.

---

# 📘 TEST CASE & EXPECTED LOG

## Hệ thống: ServiceIntegration (RabbitMQ-only)

## Event: CHECKIN

---

# 🔹 Case 1 – Tiger timeout

---

## 🎯 Mục tiêu

Kiểm tra hệ thống xử lý khi TigerTMS không phản hồi (timeout).

---

## 🧪 Cách test

### Cách 1 (đơn giản nhất – simulate timeout)

Trong `TigerClient.cs`, sửa tạm:

```csharp
await Task.Delay(TimeSpan.FromSeconds(30), ct);
throw new TaskCanceledException("Simulated timeout");
```

Hoặc:

* Đổi `Endpoint` sang URL sai.
* Hoặc chặn outbound network tới Tiger.

---

## 🔄 Flow mong đợi

```plaintext
API → Publish → Worker consume
→ SendToTiger → Timeout
→ RetryRouter chọn retry.10s
→ Republish sang retry exchange
→ ACK message cũ
```

---

## 📜 Expected Log

```plaintext
HANDLE CHECKIN hotelId=EZ001 eventId=...
SENT_TO_TIGER ...
Tiger request failed: timeout
Retry route selected: Retry10s (attempt=1)
Republished to retry exchange with routingKey=retry.10s
ACK original message
```

Sau 10s:

```plaintext
HANDLE CHECKIN hotelId=EZ001 eventId=... attempt=1
```

---

## 🧠 Kết quả mong đợi

* Message không mất
* Không block API
* Retry tăng dần
* Sau max attempt → vào dead queue

---

# 🔹 Case 2 – Service crash giữa chừng

---

## 🎯 Mục tiêu

Kiểm tra nếu service bị kill khi đang xử lý message.

---

## 🧪 Cách test

1. Gửi CHECKIN
2. Khi log xuất hiện:

```plaintext
HANDLE CHECKIN ...
```

3. Kill process (`Ctrl+C` hoặc kill PID)

---

## 🔄 Flow mong đợi

Vì consumer dùng:

```csharp
autoAck: false
```

→ Nếu crash trước khi `BasicAck`
→ RabbitMQ sẽ tự động trả message về queue

---

## 📜 Expected Log sau restart

```plaintext
Rabbit consumer started
HANDLE CHECKIN hotelId=EZ001 eventId=... attempt=0
```

---

## 🧠 Kết quả mong đợi

✔ Message không mất
✔ Message được xử lý lại
✔ Không duplicate nếu Tiger có idempotency

---

# 🔹 Case 3 – Callback PMS fail

---

## 🎯 Mục tiêu

Kiểm tra khi Tiger thành công nhưng callback PMS thất bại.

---

## 🧪 Cách test

Trong `Program.cs`, sửa endpoint callback:

```csharp
app.MapPost("/pms/callback", () => Results.StatusCode(500));
```

---

## 🔄 Flow mong đợi

```plaintext
HANDLE CHECKIN
SENT_TO_TIGER
Tiger SUCCESS
CALLBACK FAIL (500)
RetryRouter → retry.10s
Republish
ACK original
```

Sau TTL:

```plaintext
HANDLE CHECKIN attempt=1
```

---

## 📜 Expected Log

```plaintext
Tiger SUCCESS for eventId=...
Callback to PMS failed: 500
Retry route selected: Retry10s
Republished to retry exchange
ACK original message
```

---

## 🧠 Kết quả mong đợi

✔ Tiger không bị gửi lại ngay lập tức
✔ Callback được retry
✔ Không mất trạng thái

---

# 🔹 Case 4 – PMS gửi trùng event

---

## 🎯 Mục tiêu

Kiểm tra idempotency khi cùng `eventId` gửi nhiều lần.

---

## 🧪 Cách test

Gửi 2 lần cùng curl:

```json
{
  "eventId": "DUPLICATE123",
  ...
}
```

---

## 🔄 Flow mong đợi

Trong handler có:

```csharp
if (_dedupe.Exists(eventId)) {
   ACK and return;
}
```

---

## 📜 Expected Log

Lần 1:

```plaintext
HANDLE CHECKIN eventId=DUPLICATE123
SENT_TO_TIGER
DONE CHECKIN -> ACK
```

Lần 2:

```plaintext
HANDLE CHECKIN eventId=DUPLICATE123
Duplicate event detected -> ACK
```

---

## 🧠 Kết quả mong đợi

✔ Tiger không bị gọi lần 2
✔ Message vẫn ACK
✔ Không crash

---

# 📦 Tổng hợp trạng thái message

| Case          | Retry | Dead letter     | Mất message | Duplicate                         |
| ------------- | ----- | --------------- | ----------- | --------------------------------- |
| Tiger timeout | Có    | Có nếu vượt max | Không       | Có thể nếu Tiger không idempotent |
| Service crash | Không | Không           | Không       | Có thể                            |
| Callback fail | Có    | Có              | Không       | Không                             |
| PMS gửi trùng | Không | Không           | Không       | Không                             |

---

# 🧠 Điều leader sẽ hỏi bạn

### “RabbitMQ-only có đảm bảo 100% không?”

Câu trả lời chuẩn:

> RabbitMQ-only đảm bảo at-least-once delivery.
> Không đảm bảo exactly-once nếu không có DB idempotency store.

---

# 🚀 Nếu bạn muốn bước tiếp theo

Mình có thể viết thêm:

* Test case cho retry max attempt → dead queue
* Test case cho Rabbit restart
* Test case cho network partition
* So sánh RabbitMQ-only vs DB-backed architecture
* Viết kịch bản demo 10 phút thuyết trình leader

Bạn muốn đi hướng nào tiếp?
s