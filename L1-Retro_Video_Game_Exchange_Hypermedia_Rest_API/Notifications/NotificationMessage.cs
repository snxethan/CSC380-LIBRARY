namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Notifications
{
    public record NotificationMessage(
        string EventType,
        string ToEmail,
        string Subject,
        string Body,
        DateTime OccurredAtUtc,
        string CorrelationId,
        int? UserId,
        int? OfferId);
}
