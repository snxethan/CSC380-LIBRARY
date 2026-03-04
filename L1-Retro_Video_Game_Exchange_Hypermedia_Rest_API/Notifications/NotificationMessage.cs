namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Notifications
{
    /// <summary>
    /// Represents a single email-notification event that the REST API publishes
    /// to the <c>notifications</c> Kafka topic.
    ///
    /// <para>
    /// The <see cref="NotificationWorker"/> service consumes messages with this
    /// shape from Kafka and uses the fields to compose and send an email.
    /// Because this record is shared (conceptually) between the producer (API)
    /// and the consumer (NotificationWorker), both sides must agree on the same
    /// JSON field names and types.
    /// </para>
    ///
    /// <para><b>JSON wire format example (password-change event):</b></para>
    /// <code>
    /// {
    ///   "EventType":      "UserPasswordChanged",
    ///   "ToEmail":        "alice@example.com",
    ///   "Subject":        "Password changed",
    ///   "Body":           "Your password was updated successfully.",
    ///   "OccurredAtUtc":  "2026-01-30T22:13:52Z"
    /// }
    /// </code>
    /// </summary>
    /// <param name="EventType">
    /// A short string that identifies what happened.
    /// Known values:
    /// <list type="bullet">
    ///   <item><c>UserPasswordChanged</c> — user changed their password</item>
    ///   <item><c>TradeOfferCreated</c>   — a new trade offer was made</item>
    ///   <item><c>TradeOfferAccepted</c>  — a trade offer was accepted</item>
    ///   <item><c>TradeOfferRejected</c>  — a trade offer was rejected</item>
    /// </list>
    /// The consumer uses this field for logging; the <see cref="Subject"/> and
    /// <see cref="Body"/> fields already contain human-readable text.
    /// </param>
    /// <param name="ToEmail">
    /// Recipient's email address.  For trade-offer events the API calls
    /// <see cref="INotificationProducer.PublishAsync"/> once per affected user,
    /// so each Kafka message targets exactly one recipient.
    /// </param>
    /// <param name="Subject">Email subject line.</param>
    /// <param name="Body">Plain-text email body.</param>
    /// <param name="OccurredAtUtc">
    /// UTC timestamp of when the triggering event occurred.
    /// Stored in the message so the consumer can include it in the email or
    /// use it for idempotency / deduplication checks in the future.
    /// </param>
    public record NotificationMessage(
        string EventType,
        string ToEmail,
        string Subject,
        string Body,
        DateTime OccurredAtUtc);
}
