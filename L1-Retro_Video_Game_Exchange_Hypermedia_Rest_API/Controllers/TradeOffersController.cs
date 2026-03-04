using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Data;
using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.DTOs;
using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Models;
using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Controllers
{
    /// <summary>
    /// Manages game-trade offers between users.
    ///
    /// <para><b>Kafka events produced by this controller</b></para>
    /// <para>
    /// Every state transition that affects two users triggers two Kafka
    /// messages (one per user) via the private
    /// <see cref="NotifyTradeOfferAsync"/> helper, which calls
    /// <see cref="INotificationProducer.PublishAsync"/>.  The
    /// <c>NotificationWorker</c> container consumes these messages from the
    /// <c>notifications</c> topic and sends the corresponding emails.
    /// </para>
    /// <list type="table">
    ///   <listheader>
    ///     <term>Action</term>
    ///     <description>EventType published to Kafka</description>
    ///   </listheader>
    ///   <item>
    ///     <term>POST /api/tradeoffers (offer created)</term>
    ///     <description>
    ///       <c>TradeOfferCreated</c> — sent to both the requester and the
    ///       owner of the requested game.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>PATCH /api/tradeoffers/{id}/respond — Accepted</term>
    ///     <description>
    ///       <c>TradeOfferAccepted</c> — sent to both parties after game
    ///       ownership is swapped in the database.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>PATCH /api/tradeoffers/{id}/respond — Rejected</term>
    ///     <description>
    ///       <c>TradeOfferRejected</c> — sent to both parties.
    ///     </description>
    ///   </item>
    /// </list>
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class TradeOffersController : ControllerBase
    {
        private readonly ExchangeDbContext _db;

        /// <summary>
        /// Kafka notification producer injected via DI (singleton).
        /// Used by <see cref="NotifyTradeOfferAsync"/> to publish notification
        /// messages to the <c>notifications</c> Kafka topic.
        /// </summary>
        private readonly INotificationProducer _notificationProducer;

        public TradeOffersController(ExchangeDbContext db, INotificationProducer notificationProducer)
        {
            _db = db;
            _notificationProducer = notificationProducer;
        }

        // POST api/tradeoffers  (create trade offer)
        // Requires Basic authentication.  The authenticated user becomes the
        // "requester"; they specify which game they want (RequestedGameId) and
        // which of their own games they are offering in exchange (OfferedGameId).
        [HttpPost]
        public async Task<ActionResult<TradeOfferDto>> CreateTradeOffer(TradeOfferCreateDto dto)
        {
            var user = await AuthenticateAsync();
            if (user == null)
                return UnauthorizedResult();

            if (dto.OfferedGameId == dto.RequestedGameId)
                return BadRequest(new { error = "Offered and requested games must be different." });

            var requestedGame = await _db.Games.FindAsync(dto.RequestedGameId);
            if (requestedGame == null)
                return NotFound(new { error = "Requested game not found." });

            if (requestedGame.OwnerId == user.Id)
                return BadRequest(new { error = "Cannot request your own game." });

            var offeredGame = await _db.Games.FindAsync(dto.OfferedGameId);
            if (offeredGame == null)
                return NotFound(new { error = "Offered game not found." });

            if (offeredGame.OwnerId != user.Id)
                return BadRequest(new { error = "You do not own the offered game." });

            var offer = new TradeOffer
            {
                RequestedGameId = requestedGame.Id,
                OfferedGameId   = offeredGame.Id,
                RequesterUserId = user.Id,
                OwnerUserId     = requestedGame.OwnerId,
                Status          = TradeOfferStatus.Pending,
                CreatedAtUtc    = DateTime.UtcNow
            };

            _db.TradeOffers.Add(offer);
            await _db.SaveChangesAsync();

            // ── Kafka: notify both parties that a new offer was created ──
            await NotifyTradeOfferAsync(
                offer,
                eventType: "TradeOfferCreated",
                subject:   "New trade offer created",
                body:      $"Trade offer {offer.Id} was created for requested game {offer.RequestedGameId} and offered game {offer.OfferedGameId}.");

            return CreatedAtAction(nameof(GetTradeOfferById), new { id = offer.Id }, ToTradeOfferDto(offer));
        }

        // GET api/tradeoffers/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<TradeOfferDto>> GetTradeOfferById(int id)
        {
            var user = await AuthenticateAsync();
            if (user == null)
                return UnauthorizedResult();

            var offer = await _db.TradeOffers.FirstOrDefaultAsync(o => o.Id == id);
            if (offer == null)
                return NotFound(new { error = "Trade offer not found." });

            if (offer.RequesterUserId != user.Id && offer.OwnerUserId != user.Id)
                return StatusCode(403, new { error = "Forbidden." });

            return Ok(ToTradeOfferDto(offer));
        }

        // GET api/tradeoffers/incoming
        // Returns all pending offers where the authenticated user is the owner
        // of the requested game (i.e. they need to decide accept/reject).
        [HttpGet("incoming")]
        public async Task<ActionResult<IEnumerable<TradeOfferDto>>> GetIncomingOffers()
        {
            var user = await AuthenticateAsync();
            if (user == null)
                return UnauthorizedResult();

            var offers = await _db.TradeOffers
                .Where(o => o.OwnerUserId == user.Id)
                .ToListAsync();

            return Ok(offers.Select(ToTradeOfferDto));
        }

        // GET api/tradeoffers/outgoing
        // Returns all offers the authenticated user has sent to others.
        [HttpGet("outgoing")]
        public async Task<ActionResult<IEnumerable<TradeOfferDto>>> GetOutgoingOffers()
        {
            var user = await AuthenticateAsync();
            if (user == null)
                return UnauthorizedResult();

            var offers = await _db.TradeOffers
                .Where(o => o.RequesterUserId == user.Id)
                .ToListAsync();

            return Ok(offers.Select(ToTradeOfferDto));
        }

        // PATCH api/tradeoffers/{id}/respond
        // The game owner (offeree) accepts or rejects the pending offer.
        // On acceptance: game ownership is swapped and PreviousOwners incremented.
        // Either outcome publishes a Kafka notification to both parties.
        [HttpPatch("{id:int}/respond")]
        public async Task<ActionResult<TradeOfferDto>> RespondToOffer(int id, TradeOfferRespondDto dto)
        {
            var user = await AuthenticateAsync();
            if (user == null)
                return UnauthorizedResult();

            var offer = await _db.TradeOffers.FindAsync(id);
            if (offer == null)
                return NotFound(new { error = "Trade offer not found." });

            // Only the owner of the requested game may respond.
            if (offer.OwnerUserId != user.Id)
                return StatusCode(403, new { error = "Forbidden." });

            if (offer.Status != TradeOfferStatus.Pending)
                return Conflict(new { error = "Offer has already been responded to." });

            if (!Enum.TryParse<TradeOfferStatus>(dto.Decision, true, out var decision) ||
                decision == TradeOfferStatus.Pending)
            {
                return BadRequest(new { error = "Decision must be Accepted or Rejected." });
            }

            if (decision == TradeOfferStatus.Accepted)
            {
                var requestedGame = await _db.Games.FindAsync(offer.RequestedGameId);
                var offeredGame   = await _db.Games.FindAsync(offer.OfferedGameId);

                if (requestedGame == null || offeredGame == null)
                    return Conflict(new { error = "One or more games no longer exist." });

                // Guard against ownership changes between offer creation and acceptance.
                if (requestedGame.OwnerId != offer.OwnerUserId ||
                    offeredGame.OwnerId   != offer.RequesterUserId)
                {
                    return Conflict(new { error = "Game ownership changed before acceptance." });
                }

                // Swap ownership between the two users.
                requestedGame.OwnerId = offer.RequesterUserId;
                offeredGame.OwnerId   = offer.OwnerUserId;

                // Track how many times each game has changed hands.
                requestedGame.PreviousOwners = (requestedGame.PreviousOwners ?? 0) + 1;
                offeredGame.PreviousOwners   = (offeredGame.PreviousOwners   ?? 0) + 1;

                offer.Status = TradeOfferStatus.Accepted;
            }
            else
            {
                offer.Status = TradeOfferStatus.Rejected;
            }

            await _db.SaveChangesAsync();

            // ── Kafka: notify both parties of the outcome ─────────────────
            if (decision == TradeOfferStatus.Accepted)
            {
                await NotifyTradeOfferAsync(
                    offer,
                    eventType: "TradeOfferAccepted",
                    subject:   "Trade offer accepted",
                    body:      $"Trade offer {offer.Id} was accepted.");
            }
            else
            {
                await NotifyTradeOfferAsync(
                    offer,
                    eventType: "TradeOfferRejected",
                    subject:   "Trade offer rejected",
                    body:      $"Trade offer {offer.Id} was rejected.");
            }

            return Ok(ToTradeOfferDto(offer));
        }

        // ── Authentication helper ─────────────────────────────────────────
        // Reads the Basic Authorization header and returns the matching User,
        // or null if credentials are missing or incorrect.
        private async Task<User?> AuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
                return null;

            var header = authHeader.ToString();
            if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return null;

            var encoded = header["Basic ".Length..].Trim();
            string decoded;

            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            }
            catch
            {
                return null;
            }

            var parts = decoded.Split(':', 2);
            if (parts.Length != 2)
                return null;

            var email    = parts[0];
            var password = parts[1];

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return null;

            return await _db.Users.SingleOrDefaultAsync(u =>
                u.Email == email && u.Password == password);
        }

        private ActionResult UnauthorizedResult()
        {
            Response.Headers["WWW-Authenticate"] = "Basic realm=\"tradeoffers\"";
            return Unauthorized(new { error = "Basic authentication required." });
        }

        // ── HATEOAS helper ────────────────────────────────────────────────
        private TradeOfferDto ToTradeOfferDto(TradeOffer offer)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var links = new List<LinkDto>
            {
                new("self",      $"{baseUrl}/api/tradeoffers/{offer.Id}",         "GET"),
                new("respond",   $"{baseUrl}/api/tradeoffers/{offer.Id}/respond", "PATCH"),
                new("requested", $"{baseUrl}/api/games/{offer.RequestedGameId}",  "GET"),
                new("offered",   $"{baseUrl}/api/games/{offer.OfferedGameId}",    "GET"),
                new("requester", $"{baseUrl}/api/users/{offer.RequesterUserId}",  "GET"),
                new("owner",     $"{baseUrl}/api/users/{offer.OwnerUserId}",      "GET")
            };

            return new TradeOfferDto(
                offer.Id,
                offer.RequestedGameId,
                offer.OfferedGameId,
                offer.RequesterUserId,
                offer.OwnerUserId,
                offer.Status.ToString(),
                offer.CreatedAtUtc,
                links
            );
        }

        /// <summary>
        /// Publishes a <see cref="NotificationMessage"/> to the Kafka
        /// <c>notifications</c> topic for <b>each</b> user involved in the offer.
        ///
        /// <para>
        /// Two separate messages are produced — one addressed to the requester
        /// and one to the game owner — so that each recipient gets their own
        /// personalised email via the <c>NotificationWorker</c>.
        /// </para>
        ///
        /// <para>
        /// The method is called after every state transition
        /// (created / accepted / rejected) with the appropriate
        /// <paramref name="eventType"/>, <paramref name="subject"/>, and
        /// <paramref name="body"/> strings.
        /// </para>
        /// </summary>
        /// <param name="offer">The trade offer whose parties should be notified.</param>
        /// <param name="eventType">
        /// Kafka event type string (<c>TradeOfferCreated</c>,
        /// <c>TradeOfferAccepted</c>, or <c>TradeOfferRejected</c>).
        /// </param>
        /// <param name="subject">Email subject line.</param>
        /// <param name="body">Plain-text email body.</param>
        private async Task NotifyTradeOfferAsync(TradeOffer offer, string eventType, string subject, string body)
        {
            // Look up both users to get their email addresses.
            var requester = await _db.Users.FindAsync(offer.RequesterUserId);
            var owner     = await _db.Users.FindAsync(offer.OwnerUserId);

            var occurredAt = DateTime.UtcNow;

            // Publish a message for the requester (the user who created the offer).
            if (requester != null)
            {
                await _notificationProducer.PublishAsync(new NotificationMessage(
                    eventType,
                    requester.Email,
                    subject,
                    body,
                    occurredAt));
            }

            // Publish a separate message for the owner of the requested game.
            if (owner != null)
            {
                await _notificationProducer.PublishAsync(new NotificationMessage(
                    eventType,
                    owner.Email,
                    subject,
                    body,
                    occurredAt));
            }
        }
    }

    /// <summary>Payload for creating a new trade offer.</summary>
    public record TradeOfferCreateDto(int RequestedGameId, int OfferedGameId);

    /// <summary>
    /// Payload for responding to a trade offer.
    /// <c>Decision</c> must be <c>"Accepted"</c> or <c>"Rejected"</c>
    /// (case-insensitive).
    /// </summary>
    public record TradeOfferRespondDto(string Decision);

    /// <summary>Read model returned by all trade-offer endpoints.</summary>
    public record TradeOfferDto(
        int Id,
        int RequestedGameId,
        int OfferedGameId,
        int RequesterUserId,
        int OwnerUserId,
        string Status,
        DateTime CreatedAtUtc,
        List<LinkDto> Links);
}
