# Retro Video Game Exchange — L3: Kafka Notification System

## Overview

This project adds a **scalable, asynchronous email notification system** on top of the L1 REST API.  
Instead of sending emails synchronously on the API thread (which would make endpoints feel slow), the API publishes lightweight JSON messages to a **Kafka topic**.  A separate **NotificationWorker** process consumes those messages and handles the actual email delivery — keeping the API fast and responsive.

---

## Architecture

```
   Client
     │
     ▼
  nginx:8080  (round-robin load balancer)
  ├── api1:8080  ─┐
  └── api2:8080  ─┤── publish JSON → Kafka topic "notifications"
                  │                           │
              (shared SQLite)                 ▼
                               notification-worker (consumer)
                                 logs / sends email
```

### Docker services (`docker-compose.yml`)

| Service | Image / Build | Purpose |
|---|---|---|
| `api1` | `L1-.../Dockerfile` | REST API instance 1 — Kafka producer |
| `api2` | `L1-.../Dockerfile` | REST API instance 2 — Kafka producer |
| `nginx` | `nginx/Dockerfile` | Reverse proxy, load-balances api1/api2 |
| `zookeeper` | `confluentinc/cp-zookeeper:7.6.1` | Kafka coordination (required by Kafka 7.x) |
| `kafka` | `confluentinc/cp-kafka:7.6.1` | Message broker |
| `notification-worker` | `NotificationWorker/Dockerfile` | Kafka consumer — sends emails |

---

## Kafka Topic

| Property | Value |
|---|---|
| **Topic name** | `notifications` |
| **Partitions** | 1 (single broker development setup) |
| **Replication factor** | 1 |
| **Message key** | `null` (round-robin partition assignment) |
| **Message value** | UTF-8 JSON (see format below) |
| **Consumer group** | `notification-worker` |
| **Auto offset reset** | `Earliest` (starts from oldest unread message on first run) |
| **Auto commit** | `true` (offsets committed after each `Consume()` call) |

---

## Kafka Message Format

Every message on the `notifications` topic is a UTF-8 JSON object matching the `NotificationMessage` record:

```json
{
  "EventType":     "UserPasswordChanged",
  "ToEmail":       "alice@example.com",
  "Subject":       "Password changed",
  "Body":          "Your password was updated successfully.",
  "OccurredAtUtc": "2026-01-30T22:13:52Z"
}
```

### Fields

| Field | Type | Description |
|---|---|---|
| `EventType` | `string` | Identifies the triggering event (see table below) |
| `ToEmail` | `string` | Recipient's email address |
| `Subject` | `string` | Email subject line |
| `Body` | `string` | Plain-text email body |
| `OccurredAtUtc` | `DateTime` (ISO 8601) | UTC timestamp of the event |

### Known EventType values

| EventType | Trigger | Recipients |
|---|---|---|
| `UserPasswordChanged` | `PATCH /api/users/{id}/password` | The affected user |
| `TradeOfferCreated` | `POST /api/tradeoffers` | Requester **and** game owner |
| `TradeOfferAccepted` | `PATCH /api/tradeoffers/{id}/respond` (Accepted) | Requester **and** game owner |
| `TradeOfferRejected` | `PATCH /api/tradeoffers/{id}/respond` (Rejected) | Requester **and** game owner |

---

## Kafka Producer — API side

### Location
`L1-Retro_Video_Game_Exchange_Hypermedia_Rest_API/Notifications/`

### Files

| File | Purpose |
|---|---|
| `NotificationMessage.cs` | C# record defining the JSON payload shape |
| `INotificationProducer.cs` | Interface — decouples controllers from Kafka |
| `KafkaNotificationProducer.cs` | Concrete Confluent.Kafka implementation |

### How the producer is registered

In `Program.cs`:

```csharp
// Singleton so the underlying TCP connection to kafka:9092 is reused
// across all HTTP requests instead of being created per-request.
builder.Services.AddSingleton<INotificationProducer, KafkaNotificationProducer>();
```

### Configuration (`appsettings.json` / environment variables)

| Key | Environment variable | Default | Description |
|---|---|---|---|
| `Kafka:BootstrapServers` | `Kafka__BootstrapServers` | `kafka:9092` | Kafka broker address |
| `Kafka:Topic` | `Kafka__Topic` | `notifications` | Topic to publish to |

### How a message is produced (example — password change)

```csharp
// In UsersController.ChangePassword():
await _notificationProducer.PublishAsync(new NotificationMessage(
    EventType:     "UserPasswordChanged",
    ToEmail:       authenticatedUser.Email,
    Subject:       "Password changed",
    Body:          "Your password was updated successfully.",
    OccurredAtUtc: DateTime.UtcNow));
```

`KafkaNotificationProducer.PublishAsync` serialises the record to JSON and calls `IProducer<Null, string>.ProduceAsync()`. Errors are caught and logged as warnings — a notification failure never causes the API to return an error because the database transaction has already committed.

### Trade-offer notifications

`TradeOffersController` calls the private `NotifyTradeOfferAsync` helper after every state transition. The helper publishes **two** separate messages (one per user) so each party gets their own email:

```csharp
private async Task NotifyTradeOfferAsync(TradeOffer offer, string eventType, string subject, string body)
{
    // one message for the requester
    await _notificationProducer.PublishAsync(new NotificationMessage(eventType, requester.Email, ...));
    // one message for the game owner
    await _notificationProducer.PublishAsync(new NotificationMessage(eventType, owner.Email, ...));
}
```

---

## Kafka Consumer — NotificationWorker

### Location
`NotificationWorker/`

### Files

| File | Purpose |
|---|---|
| `Program.cs` | Top-level consumer loop — reads from Kafka and logs email details |
| `NotificationWorker.csproj` | .NET 10 console project with `Confluent.Kafka` dependency |
| `Dockerfile` | Multi-stage Docker image using `dotnet/runtime:10.0` |

### How the consumer works (`NotificationWorker/Program.cs`)

1. **Configure** — reads broker address, topic, and group ID from environment variables.
2. **Build consumer** — `ConsumerBuilder<Ignore, string>` with:
   - `AutoOffsetReset = Earliest` — start from the oldest message on first run.
   - `EnableAutoCommit = true` — offsets are committed automatically after `Consume()`.
3. **Subscribe** — `consumer.Subscribe("notifications")`.
4. **Consume loop** — blocks on `consumer.Consume(cancellationToken)`, deserialises JSON into `NotificationMessage`, and logs the email fields.  
   In production, replace the `Console.WriteLine` calls with an SMTP client (e.g. MailKit, SendGrid, AWS SES).
5. **Graceful shutdown** — `Console.CancelKeyPress` (Ctrl+C / Docker SIGTERM) triggers the `CancellationTokenSource`, which exits the loop cleanly and calls `consumer.Close()` to commit offsets and leave the consumer group.

### Configuration

| Environment variable | Default | Description |
|---|---|---|
| `Kafka__BootstrapServers` | `kafka:9092` | Kafka broker address |
| `Kafka__Topic` | `notifications` | Topic to consume from |
| `Kafka__GroupId` | `notification-worker` | Consumer group ID |

---

## Running the System

```bash
docker-compose up --build
```

The API will be available at `http://localhost:8080` (through nginx).  
Direct instance access: `http://localhost:8081` (api1), `http://localhost:8082` (api2).

To see notification messages as they are consumed, watch the worker logs:

```bash
docker-compose logs -f notification-worker
```

---

## API Endpoints That Trigger Notifications

### Change password
```
PATCH /api/users/{id}/password
Authorization: Basic <base64(email:password)>
Content-Type: application/json

{ "CurrentPassword": "old", "NewPassword": "new" }
```
→ Publishes `UserPasswordChanged` for the user.

### Create trade offer
```
POST /api/tradeoffers
Authorization: Basic <base64(email:password)>
Content-Type: application/json

{ "RequestedGameId": 2, "OfferedGameId": 1 }
```
→ Publishes `TradeOfferCreated` for both the requester and the game owner.

### Respond to trade offer
```
PATCH /api/tradeoffers/{id}/respond
Authorization: Basic <base64(email:password)>
Content-Type: application/json

{ "Decision": "Accepted" }   // or "Rejected"
```
→ Publishes `TradeOfferAccepted` or `TradeOfferRejected` for both parties.

---

## Key Design Decisions

| Decision | Rationale |
|---|---|
| Single `notifications` topic | Simple; the `EventType` field differentiates event kinds without needing per-event topics. |
| `INotificationProducer` interface | Controllers depend on the interface, not the concrete class, making unit testing easy (inject a mock). |
| Singleton producer | The Confluent.Kafka `IProducer` is thread-safe and expensive to create; sharing one instance per app lifetime is best practice. |
| Errors swallowed in `PublishAsync` | A notification failure must not cause the API to return a 5xx — the DB write has already succeeded. |
| `consumer.Close()` on shutdown | Commits current offsets and gracefully leaves the consumer group so Kafka can immediately reassign partitions. |
| `AutoOffsetReset = Earliest` | Ensures the worker processes messages that arrived while it was down, rather than skipping them. |
