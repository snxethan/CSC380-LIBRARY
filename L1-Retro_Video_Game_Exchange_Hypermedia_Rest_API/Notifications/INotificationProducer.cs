using System.Threading;
using System.Threading.Tasks;

namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Notifications
{
    /// <summary>
    /// Abstraction for publishing <see cref="NotificationMessage"/> events to a
    /// message broker (currently Kafka).
    ///
    /// <para>
    /// Coding against this interface instead of directly against
    /// <see cref="KafkaNotificationProducer"/> achieves two things:
    /// <list type="number">
    ///   <item>
    ///     <b>Testability</b> — unit tests can inject a fake/mock implementation
    ///     that records which messages were published without touching a real
    ///     Kafka cluster.
    ///   </item>
    ///   <item>
    ///     <b>Replaceability</b> — if the project ever switches from Kafka to
    ///     RabbitMQ, SNS, or another broker, only the concrete class needs to
    ///     change; all controllers remain untouched.
    ///   </item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// The concrete implementation (<see cref="KafkaNotificationProducer"/>) is
    /// registered as a <b>singleton</b> in <c>Program.cs</c> so that the
    /// underlying Confluent.Kafka <c>IProducer</c> is shared across all HTTP
    /// requests and is reused efficiently.
    /// </para>
    /// </summary>
    public interface INotificationProducer
    {
        /// <summary>
        /// Publishes a notification event to the message broker asynchronously.
        ///
        /// <para>
        /// Implementations must <b>not</b> throw exceptions that would bubble up
        /// to the API caller — a notification failure must never cause an
        /// HTTP 5xx response after the database transaction has already
        /// committed.  Errors should be caught internally and logged.
        /// </para>
        /// </summary>
        /// <param name="message">
        /// The notification envelope to publish.  See <see cref="NotificationMessage"/>
        /// for a description of each field and the JSON wire format.
        /// </param>
        /// <param name="cancellationToken">
        /// Token that can cancel the publish operation (e.g. on request abort).
        /// </param>
        Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default);
    }
}
