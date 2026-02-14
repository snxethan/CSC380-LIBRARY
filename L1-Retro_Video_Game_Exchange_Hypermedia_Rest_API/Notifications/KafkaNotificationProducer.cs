using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Notifications
{
    public sealed class KafkaNotificationProducer : INotificationProducer, IDisposable
    {
        private readonly IProducer<Null, string> _producer;
        private readonly string _topic;
        private readonly ILogger<KafkaNotificationProducer> _logger;

        public KafkaNotificationProducer(IConfiguration configuration, ILogger<KafkaNotificationProducer> logger)
        {
            _logger = logger;
            var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "kafka:9092";
            _topic = configuration["Kafka:Topic"] ?? "notifications";

            var config = new ProducerConfig
            {
                BootstrapServers = bootstrapServers
            };

            _producer = new ProducerBuilder<Null, string>(config).Build();
        }

        public async Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = JsonSerializer.Serialize(message);
                await _producer.ProduceAsync(_topic, new Message<Null, string> { Value = payload }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish notification message.");
            }
        }

        public void Dispose()
        {
            _producer.Flush(TimeSpan.FromSeconds(2));
            _producer.Dispose();
        }
    }
}
