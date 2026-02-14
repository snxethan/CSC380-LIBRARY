using Confluent.Kafka;
using System.Text.Json;

var bootstrapServers = Environment.GetEnvironmentVariable("Kafka__BootstrapServers") ?? "kafka:9092";
var topic = Environment.GetEnvironmentVariable("Kafka__Topic") ?? "notifications";
var groupId = Environment.GetEnvironmentVariable("Kafka__GroupId") ?? "notification-worker";

var config = new ConsumerConfig
{
    BootstrapServers = bootstrapServers,
    GroupId = groupId,
    AutoOffsetReset = AutoOffsetReset.Earliest,
    EnableAutoCommit = true
};

using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
consumer.Subscribe(topic);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine($"Listening for notifications on '{topic}'...");

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

            Console.WriteLine($"Sending email to {message.ToEmail}");
            Console.WriteLine($"Subject: {message.Subject}");
            Console.WriteLine($"Body: {message.Body}");
        }
        catch (ConsumeException ex)
        {
            Console.WriteLine($"Kafka consume error: {ex.Error.Reason}");
        }
    }
}
catch (OperationCanceledException)
{
}
finally
{
    consumer.Close();
}

public record NotificationMessage(
    string EventType,
    string ToEmail,
    string Subject,
    string Body,
    DateTime OccurredAtUtc);
