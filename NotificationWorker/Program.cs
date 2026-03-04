// ============================================================================
// NotificationWorker — Kafka Email Consumer
// ============================================================================
// This process is the *consumer* side of the notification pipeline.
//
// Architecture overview:
//
//   REST API (producer)                     NotificationWorker (consumer)
//   ┌─────────────────────────┐             ┌──────────────────────────────┐
//   │ KafkaNotificationProducer│─── JSON ──▶│  Consume loop (this file)    │
//   │ PublishAsync(...)        │   message  │  Deserialise NotificationMsg  │
//   └─────────────────────────┘             │  Log ToEmail / Subject / Body │
//           Kafka topic: "notifications"    └──────────────────────────────┘
//
// Why a separate process?
//   Sending emails via SMTP is slow.  Doing it on the API request thread
//   would make every password-change or trade-offer endpoint feel laggy.
//   By publishing to Kafka the API responds immediately; this worker handles
//   the slow email work in its own Docker container without blocking the API.
//
// Configuration (environment variables / appsettings):
//   Kafka__BootstrapServers  — comma-separated host:port list (default: kafka:9092)
//   Kafka__Topic             — topic to subscribe to          (default: notifications)
//   Kafka__GroupId           — consumer group name            (default: notification-worker)
//
// Consumer group:
//   Using a named consumer group means:
//   1. Offsets are committed back to Kafka so messages are not re-processed
//      after a restart (EnableAutoCommit = true).
//   2. If multiple worker instances were run they would share the load
//      (each partition processed by only one instance at a time).
//
// Message format (NotificationMessage JSON):
//   {
//     "EventType":     "UserPasswordChanged" | "TradeOfferCreated" |
//                      "TradeOfferAccepted"  | "TradeOfferRejected",
//     "ToEmail":       "recipient@example.com",
//     "Subject":       "Human-readable email subject",
//     "Body":          "Plain-text email body",
//     "OccurredAtUtc": "2026-01-30T22:13:52Z"
//   }
// ============================================================================

using Confluent.Kafka;
using System.Text.Json;

// ── Kafka connection settings ─────────────────────────────────────────────
// Read from environment variables (set in docker-compose.yml) with sensible
// fallback defaults so the worker can also be run locally without Docker.
var bootstrapServers = Environment.GetEnvironmentVariable("Kafka__BootstrapServers") ?? "kafka:9092";
var topic            = Environment.GetEnvironmentVariable("Kafka__Topic")             ?? "notifications";
var groupId          = Environment.GetEnvironmentVariable("Kafka__GroupId")           ?? "notification-worker";

// ── Build Confluent.Kafka consumer ───────────────────────────────────────
// ConsumerConfig key settings:
//   BootstrapServers — address of the Kafka broker(s).
//   GroupId          — consumer group; Kafka tracks per-group offsets so
//                      restarting the worker resumes from where it left off.
//   AutoOffsetReset  — Earliest: if this group has never consumed from the
//                      topic before, start from the very first message.
//   EnableAutoCommit — true: offsets are committed automatically after the
//                      message is returned by Consume(), which is sufficient
//                      for at-least-once email delivery.
var config = new ConsumerConfig
{
    BootstrapServers = bootstrapServers,
    GroupId          = groupId,
    AutoOffsetReset  = AutoOffsetReset.Earliest,
    EnableAutoCommit = true
};

// Key type is Ignore because the API publishes with a null key.
// Value type is string because the payload is UTF-8 JSON.
using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();

// Subscribe to the notifications topic.  Kafka will assign partitions to
// this consumer once it joins the group.
consumer.Subscribe(topic);

// ── Graceful shutdown via Ctrl+C / SIGTERM ────────────────────────────────
// Docker sends SIGTERM when the container is stopped.  CancellationTokenSource
// lets the consume loop exit cleanly rather than being abruptly killed.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;   // prevent the default Ctrl+C process termination
    cts.Cancel();      // signal the consume loop to stop
};

Console.WriteLine($"Listening for notifications on '{topic}'...");

try
{
    // ── Main consume loop ─────────────────────────────────────────────────
    // Blocks on Consume() until a message arrives or the token is cancelled.
    while (!cts.IsCancellationRequested)
    {
        try
        {
            // Consume() returns the next available message.
            // Passing the cancellation token allows clean shutdown.
            var result = consumer.Consume(cts.Token);

            // Guard: skip tombstone records (null value) if any appear.
            if (result?.Message?.Value == null)
                continue;

            // ── Deserialise the JSON payload ──────────────────────────────
            // NotificationMessage is defined at the bottom of this file.
            // It matches the record type used by KafkaNotificationProducer
            // in the API project.
            var message = JsonSerializer.Deserialize<NotificationMessage>(result.Message.Value);
            if (message == null)
                continue;

            // ── Simulate email sending ────────────────────────────────────
            // In a production system this is where you would call an SMTP
            // client (e.g. MailKit / SendGrid / AWS SES).  For this course
            // assignment we log the email details to stdout to prove the
            // end-to-end pipeline works without requiring a real mail server.
            Console.WriteLine($"Sending email to {message.ToEmail}");
            Console.WriteLine($"Subject: {message.Subject}");
            Console.WriteLine($"Body: {message.Body}");
        }
        catch (ConsumeException ex)
        {
            // Log Kafka-level errors (e.g. broker unreachable) and continue
            // the loop — transient errors should not crash the worker.
            Console.WriteLine($"Kafka consume error: {ex.Error.Reason}");
        }
    }
}
catch (OperationCanceledException)
{
    // Expected when cts.Cancel() is called; exit the loop cleanly.
}
finally
{
    // Close() commits the current offsets and leaves the consumer group
    // cleanly so Kafka can immediately reassign partitions if needed.
    consumer.Close();
}

// ============================================================================
// NotificationMessage — local copy of the API's record type
// ============================================================================
// This record must match the JSON shape produced by the API's
// KafkaNotificationProducer.PublishAsync() method exactly.
// Both sides use System.Text.Json with default (PascalCase) property names.
// ============================================================================

/// <summary>
/// JSON-deserialisable representation of a notification event consumed from
/// the <c>notifications</c> Kafka topic.
/// </summary>
/// <param name="EventType">
/// Identifies the event: <c>UserPasswordChanged</c>, <c>TradeOfferCreated</c>,
/// <c>TradeOfferAccepted</c>, or <c>TradeOfferRejected</c>.
/// </param>
/// <param name="ToEmail">Recipient email address.</param>
/// <param name="Subject">Email subject line.</param>
/// <param name="Body">Plain-text email body.</param>
/// <param name="OccurredAtUtc">UTC timestamp of the triggering event.</param>
public record NotificationMessage(
    string EventType,
    string ToEmail,
    string Subject,
    string Body,
    DateTime OccurredAtUtc);
