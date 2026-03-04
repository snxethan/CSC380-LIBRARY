using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Notifications
{
    /// <summary>
    /// Confluent.Kafka-backed implementation of <see cref="INotificationProducer"/>.
    ///
    /// <para><b>Role in the system</b></para>
    /// <para>
    /// This class is the <em>Kafka producer</em> side of the notification pipeline.
    /// When an API endpoint detects a notification-worthy event it calls
    /// <see cref="PublishAsync"/>, which serialises the payload to JSON and
    /// writes it to the <c>notifications</c> Kafka topic.  The separate
    /// <c>NotificationWorker</c> process (running in its own Docker container)
    /// subscribes to that topic as a <em>consumer</em> and handles the actual
    /// email sending.
    /// </para>
    ///
    /// <para><b>Kafka topic</b></para>
    /// <para>
    /// All events share a single topic whose name is configured via
    /// <c>appsettings.json</c> → <c>Kafka:Topic</c> (default: <c>notifications</c>).
    /// The <c>EventType</c> field inside the JSON message body differentiates
    /// event kinds so the consumer can handle or filter them individually.
    /// </para>
    ///
    /// <para><b>Kafka configuration</b></para>
    /// <list type="table">
    ///   <listheader>
    ///     <term>Config key</term>
    ///     <description>Source / default</description>
    ///   </listheader>
    ///   <item>
    ///     <term><c>Kafka:BootstrapServers</c></term>
    ///     <description>
    ///       <c>appsettings.json</c> or the <c>Kafka__BootstrapServers</c>
    ///       environment variable set in <c>docker-compose.yml</c>.
    ///       Falls back to <c>kafka:9092</c> (the Docker service name).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term><c>Kafka:Topic</c></term>
    ///     <description>
    ///       <c>appsettings.json</c> or the <c>Kafka__Topic</c> env var.
    ///       Falls back to <c>notifications</c>.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para><b>Message key</b></para>
    /// <para>
    /// The message key is set to <see cref="Null"/> (null key) so Kafka
    /// distributes messages across partitions in a round-robin fashion.
    /// </para>
    ///
    /// <para><b>Thread safety</b></para>
    /// <para>
    /// <see cref="IProducer{TKey,TValue}"/> from Confluent.Kafka is thread-safe.
    /// This class is registered as a <b>singleton</b> in <c>Program.cs</c>, so
    /// the same producer instance is shared across all concurrent HTTP requests,
    /// which avoids the overhead of creating a new TCP connection per request.
    /// </para>
    /// </summary>
    public sealed class KafkaNotificationProducer : INotificationProducer, IDisposable
    {
        // ── Fields ─────────────────────────────────────────────────────────

        /// <summary>Confluent.Kafka producer instance (thread-safe, reused for all messages).</summary>
        private readonly IProducer<Null, string> _producer;

        /// <summary>Kafka topic name, read from configuration at startup.</summary>
        private readonly string _topic;

        /// <summary>Logger for publish confirmations and error diagnostics.</summary>
        private readonly ILogger<KafkaNotificationProducer> _logger;

        // ── Constructor ────────────────────────────────────────────────────

        /// <summary>
        /// Builds the Confluent.Kafka producer using configuration values.
        ///
        /// <para>
        /// Called once by ASP.NET's DI container at application startup
        /// (singleton lifetime).  A TCP connection to the Kafka broker is
        /// established lazily on the first <see cref="PublishAsync"/> call.
        /// </para>
        /// </summary>
        /// <param name="configuration">
        /// Application configuration — reads <c>Kafka:BootstrapServers</c>
        /// and <c>Kafka:Topic</c>.
        /// </param>
        /// <param name="logger">Logger injected by DI.</param>
        public KafkaNotificationProducer(IConfiguration configuration, ILogger<KafkaNotificationProducer> logger)
        {
            _logger = logger;

            // Read broker address and topic from config / environment variables.
            var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "kafka:9092";
            _topic = configuration["Kafka:Topic"] ?? "notifications";

            var config = new ProducerConfig
            {
                BootstrapServers = bootstrapServers
                // Default Acks=Leader — acceptable for notifications where
                // occasional loss is tolerable; raise to Acks=All for stricter
                // delivery guarantees if needed.
            };

            _producer = new ProducerBuilder<Null, string>(config).Build();
        }

        // ── INotificationProducer ──────────────────────────────────────────

        /// <summary>
        /// Serialises <paramref name="message"/> to JSON and produces it onto
        /// the configured Kafka topic.
        ///
        /// <para>
        /// The method is fire-and-forget from the caller's perspective: any
        /// Kafka error is caught here, logged as a warning, and swallowed so
        /// that the API never returns an error to the client solely because of
        /// a notification failure.  The business transaction (DB save) has
        /// already committed at this point.
        /// </para>
        ///
        /// <para><b>Kafka message layout</b></para>
        /// <list type="bullet">
        ///   <item><b>Topic</b>: value of <c>Kafka:Topic</c> config key.</item>
        ///   <item><b>Key</b>: <c>null</c> (round-robin partition assignment).</item>
        ///   <item><b>Value</b>: UTF-8 JSON of <see cref="NotificationMessage"/>.</item>
        /// </list>
        /// </summary>
        /// <param name="message">Notification envelope to publish.</param>
        /// <param name="cancellationToken">Cancellation token forwarded to the Kafka client.</param>
        public async Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            try
            {
                // Serialize the notification record to compact JSON.
                var payload = JsonSerializer.Serialize(message);

                // ProduceAsync delivers the message and returns a DeliveryResult
                // once the broker acknowledges receipt (based on configured Acks).
                await _producer.ProduceAsync(
                    _topic,
                    new Message<Null, string> { Value = payload },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // Log but do not rethrow — notification failure must not
                // cause an API error after the DB write has succeeded.
                _logger.LogWarning(ex, "Failed to publish notification message.");
            }
        }

        // ── IDisposable ────────────────────────────────────────────────────

        /// <summary>
        /// Flushes any in-flight messages (up to 2 seconds) and releases the
        /// Kafka producer.  Called by ASP.NET DI when the application shuts down.
        /// </summary>
        public void Dispose()
        {
            // Give any buffered messages a chance to be delivered before shutdown.
            _producer.Flush(TimeSpan.FromSeconds(2));
            _producer.Dispose();
        }
    }
}
