using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Models
{
    /// <summary>
    /// Lifecycle states of a <see cref="TradeOffer"/>.
    /// Stored as a string in the database (see <c>ExchangeDbContext.OnModelCreating</c>).
    /// </summary>
    public enum TradeOfferStatus
    {
        /// <summary>The offer has been created and is awaiting the game owner's response.</summary>
        Pending,

        /// <summary>The game owner accepted the trade; game ownership has been swapped.</summary>
        Accepted,

        /// <summary>The game owner declined the trade proposal.</summary>
        Rejected
    }

    /// <summary>
    /// Represents a game-trade offer between two users.
    ///
    /// <para>
    /// The <em>requester</em> proposes to swap <see cref="OfferedGame"/>
    /// (which they own) for <see cref="RequestedGame"/> (owned by
    /// <see cref="OwnerUser"/>).  The owner then accepts or rejects via
    /// <c>PATCH /api/tradeoffers/{id}/respond</c>.
    /// </para>
    ///
    /// <para>
    /// Each status transition publishes Kafka notification messages to both
    /// parties via <c>TradeOffersController.NotifyTradeOfferAsync</c>.
    /// </para>
    /// </summary>
    public class TradeOffer
    {
        public int Id { get; set; }

        // ── Requested game (owned by OwnerUser) ───────────────────────────
        public int RequestedGameId { get; set; }

        [ForeignKey(nameof(RequestedGameId))]
        [InverseProperty(nameof(Game.RequestedInOffers))]
        public Game RequestedGame { get; set; } = default!;

        // ── Offered game (owned by RequesterUser) ─────────────────────────
        public int OfferedGameId { get; set; }

        [ForeignKey(nameof(OfferedGameId))]
        [InverseProperty(nameof(Game.OfferedInOffers))]
        public Game OfferedGame { get; set; } = default!;

        // ── Participants ──────────────────────────────────────────────────
        /// <summary>The user who initiated the trade offer.</summary>
        public int RequesterUserId { get; set; }
        public User RequesterUser { get; set; } = default!;

        /// <summary>The user who owns the requested game.</summary>
        public int OwnerUserId { get; set; }
        public User OwnerUser { get; set; } = default!;

        // ── Lifecycle ─────────────────────────────────────────────────────
        /// <summary>Current state of the offer (Pending / Accepted / Rejected).</summary>
        public TradeOfferStatus Status { get; set; } = TradeOfferStatus.Pending;

        /// <summary>UTC timestamp when the offer was created.</summary>
        public DateTime CreatedAtUtc { get; set; }
    }
}
