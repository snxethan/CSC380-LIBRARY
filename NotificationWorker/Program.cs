using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Prometheus;
using System.Net;
using System.Net.Mail;
using System.Text.Json;

var bootstrapServers = Environment.GetEnvironmentVariable("Kafka__BootstrapServers") ?? "kafka:9092";
var userTopic = Environment.GetEnvironmentVariable("Kafka__UserTopic") ?? "users";
var offerTopic = Environment.GetEnvironmentVariable("Kafka__OfferTopic") ?? "offers";
var groupId = Environment.GetEnvironmentVariable("Kafka__GroupId") ?? "notification-worker";
var smtpHost = Environment.GetEnvironmentVariable("SMTP__Host");
var smtpPort = Environment.GetEnvironmentVariable("SMTP__Port");
var smtpUser = Environment.GetEnvironmentVariable("SMTP__User");
var smtpPass = Environment.GetEnvironmentVariable("SMTP__Pass");
var smtpFrom = Environment.GetEnvironmentVariable("SMTP__From") ?? smtpUser;
var smtpEnableSsl = Environment.GetEnvironmentVariable("SMTP__EnableSsl") ?? "true";
var smtpEnabled = !string.IsNullOrWhiteSpace(smtpHost)
    && !string.IsNullOrWhiteSpace(smtpUser)
    && !string.IsNullOrWhiteSpace(smtpPass)
    && !string.IsNullOrWhiteSpace(smtpFrom);

var messagesConsumed = Metrics.CreateCounter(
    "notification_worker_messages_consumed_total",
    "Total number of messages consumed by the notification worker.");

var consumeErrors = Metrics.CreateCounter(
    "notification_worker_consume_errors_total",
    "Total number of Kafka consume errors encountered by the notification worker.");

var emailSendErrors = Metrics.CreateCounter(
    "notification_worker_email_send_errors_total",
    "Total number of email send errors encountered by the notification worker.");

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddSimpleConsole(options =>
    {
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    });
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger("NotificationWorker");

using var metricServer = new KestrelMetricServer(port: 8080);
metricServer.Start();

var config = new ConsumerConfig
{
    BootstrapServers = bootstrapServers,
    GroupId = groupId,
    AutoOffsetReset = AutoOffsetReset.Earliest,
    EnableAutoCommit = true
};

using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
var topics = new[] { userTopic, offerTopic };
consumer.Subscribe(topics);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

logger.LogInformation("Listening for notifications on '{Topics}'.", string.Join("', '", topics));

try
{
    while (!cts.IsCancellationRequested)
    {
        try
        {
            var result = consumer.Consume(cts.Token);
            if (result?.Message?.Value == null)
                continue;

            var message = JsonSerializer.Deserialize<NotificationMessage>(result.Message.Value);
            if (message == null)
                continue;

            logger.LogInformation(
                "Notification consumed {EventType} user {UserId} offer {OfferId} correlation {CorrelationId}.",
                message.EventType,
                message.UserId,
                message.OfferId,
                message.CorrelationId);

            if (smtpEnabled)
            {
                try
                {
                    using var client = new SmtpClient(smtpHost!, int.TryParse(smtpPort, out var port) ? port : 587)
                    {
                        EnableSsl = bool.TryParse(smtpEnableSsl, out var enableSsl) ? enableSsl : true,
                        Credentials = new NetworkCredential(smtpUser, smtpPass)
                    };

                    using var mail = new MailMessage(smtpFrom!, message.ToEmail)
                    {
                        Subject = message.Subject,
                        Body = message.Body
                    };

                    await client.SendMailAsync(mail, cts.Token);
                }
                catch (Exception ex)
                {
                    emailSendErrors.Inc();
                    logger.LogWarning(
                        ex,
                        "SMTP send error for correlation {CorrelationId} to {ToEmail}.",
                        message.CorrelationId,
                        message.ToEmail);
                }
            }
            else
            {
                logger.LogInformation(
                    "SMTP not configured; skipping send to {ToEmail} for correlation {CorrelationId}.",
                    message.ToEmail,
                    message.CorrelationId);
                logger.LogInformation("Subject: {Subject}", message.Subject);
                logger.LogInformation("Body: {Body}", message.Body);
            }

            messagesConsumed.Inc();
        }
        catch (ConsumeException ex)
        {
            consumeErrors.Inc();
            logger.LogWarning(ex, "Kafka consume error: {Reason}", ex.Error.Reason);
        }
    }
}
catch (OperationCanceledException)
{
}
finally
{
    consumer.Close();
    metricServer.Stop();
}

public record NotificationMessage(
    string EventType,
    string ToEmail,
    string Subject,
    string Body,
    DateTime OccurredAtUtc,
    string CorrelationId,
    int? UserId,
    int? OfferId);
