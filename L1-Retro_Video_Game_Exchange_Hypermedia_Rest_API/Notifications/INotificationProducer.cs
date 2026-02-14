using System.Threading;
using System.Threading.Tasks;

namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Notifications
{
    public interface INotificationProducer
    {
        Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default);
    }
}
