using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Models
{
    public enum TradeOfferStatus
    {
        Pending,
        Accepted,
        Rejected
    }

    public class TradeOffer
    {
        public int Id { get; set; }

        public int RequestedGameId { get; set; }

        [ForeignKey(nameof(RequestedGameId))]
        [InverseProperty(nameof(Game.RequestedInOffers))]
        public Game RequestedGame { get; set; } = default!;

        public int OfferedGameId { get; set; }

        [ForeignKey(nameof(OfferedGameId))]
        [InverseProperty(nameof(Game.OfferedInOffers))]
        public Game OfferedGame { get; set; } = default!;

        public int RequesterUserId { get; set; }
        public User RequesterUser { get; set; } = default!;

        public int OwnerUserId { get; set; }
        public User OwnerUser { get; set; } = default!;

        public TradeOfferStatus Status { get; set; } = TradeOfferStatus.Pending;
        public DateTime CreatedAtUtc { get; set; }
    }
}