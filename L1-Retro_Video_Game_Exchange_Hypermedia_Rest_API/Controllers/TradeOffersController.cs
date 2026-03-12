using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Data;
using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.DTOs;
using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Models;
using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TradeOffersController : ControllerBase
    {
        private readonly ExchangeDbContext _db;
        private readonly INotificationProducer _notificationProducer;
        private readonly ILogger<TradeOffersController> _logger;

        public TradeOffersController(
            ExchangeDbContext db,
            INotificationProducer notificationProducer,
            ILogger<TradeOffersController> logger)
        {
            _db = db;
            _notificationProducer = notificationProducer;
            _logger = logger;
        }

        // POST api/tradeoffers  (create trade offer)
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
                OfferedGameId = offeredGame.Id,
                RequesterUserId = user.Id,
                OwnerUserId = requestedGame.OwnerId,
                Status = TradeOfferStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.TradeOffers.Add(offer);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Trade offer created {OfferId} requester {RequesterUserId} owner {OwnerUserId} correlation {CorrelationId}.",
                offer.Id,
                offer.RequesterUserId,
                offer.OwnerUserId,
                GetCorrelationId());

            await NotifyTradeOfferAsync(
                offer,
                "TradeOfferCreated",
                "New trade offer created",
                $"Trade offer {offer.Id} was created for requested game {offer.RequestedGameId} and offered game {offer.OfferedGameId}.");

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
        [HttpPatch("{id:int}/respond")]
        public async Task<ActionResult<TradeOfferDto>> RespondToOffer(int id, TradeOfferRespondDto dto)
        {
            var user = await AuthenticateAsync();
            if (user == null)
                return UnauthorizedResult();

            var offer = await _db.TradeOffers.FindAsync(id);
            if (offer == null)
                return NotFound(new { error = "Trade offer not found." });

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
                var offeredGame = await _db.Games.FindAsync(offer.OfferedGameId);

                if (requestedGame == null || offeredGame == null)
                    return Conflict(new { error = "One or more games no longer exist." });

                if (requestedGame.OwnerId != offer.OwnerUserId ||
                    offeredGame.OwnerId != offer.RequesterUserId)
                {
                    return Conflict(new { error = "Game ownership changed before acceptance." });
                }

                requestedGame.OwnerId = offer.RequesterUserId;
                offeredGame.OwnerId = offer.OwnerUserId;

                requestedGame.PreviousOwners = (requestedGame.PreviousOwners ?? 0) + 1;
                offeredGame.PreviousOwners = (offeredGame.PreviousOwners ?? 0) + 1;

                offer.Status = TradeOfferStatus.Accepted;
            }
            else
            {
                offer.Status = TradeOfferStatus.Rejected;
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Trade offer responded {OfferId} status {Status} owner {OwnerUserId} correlation {CorrelationId}.",
                offer.Id,
                offer.Status,
                offer.OwnerUserId,
                GetCorrelationId());

            if (decision == TradeOfferStatus.Accepted)
            {
                await NotifyTradeOfferAsync(
                    offer,
                    "TradeOfferAccepted",
                    "Trade offer accepted",
                    $"Trade offer {offer.Id} was accepted.");
            }
            else
            {
                await NotifyTradeOfferAsync(
                    offer,
                    "TradeOfferRejected",
                    "Trade offer rejected",
                    $"Trade offer {offer.Id} was rejected.");
            }

            return Ok(ToTradeOfferDto(offer));
        }

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

            var email = parts[0];
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

        private TradeOfferDto ToTradeOfferDto(TradeOffer offer)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var links = new List<LinkDto>
            {
                new("self",        $"{baseUrl}/api/tradeoffers/{offer.Id}", "GET"),
                new("respond",     $"{baseUrl}/api/tradeoffers/{offer.Id}/respond", "PATCH"),
                new("requested",   $"{baseUrl}/api/games/{offer.RequestedGameId}", "GET"),
                new("offered",     $"{baseUrl}/api/games/{offer.OfferedGameId}", "GET"),
                new("requester",   $"{baseUrl}/api/users/{offer.RequesterUserId}", "GET"),
                new("owner",       $"{baseUrl}/api/users/{offer.OwnerUserId}", "GET")
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

        private async Task NotifyTradeOfferAsync(TradeOffer offer, string eventType, string subject, string body)
        {
            var requester = await _db.Users.FindAsync(offer.RequesterUserId);
            var owner = await _db.Users.FindAsync(offer.OwnerUserId);

            var occurredAt = DateTime.UtcNow;
            var correlationId = GetCorrelationId();

            if (requester != null)
            {
                _logger.LogInformation(
                    "Publishing trade offer notification {EventType} for user {UserId} offer {OfferId} correlation {CorrelationId}.",
                    eventType,
                    requester.Id,
                    offer.Id,
                    correlationId);
                await _notificationProducer.PublishAsync(new NotificationMessage(
                    eventType,
                    requester.Email,
                    subject,
                    body,
                    occurredAt,
                    correlationId,
                    requester.Id,
                    offer.Id));
            }

            if (owner != null)
            {
                _logger.LogInformation(
                    "Publishing trade offer notification {EventType} for user {UserId} offer {OfferId} correlation {CorrelationId}.",
                    eventType,
                    owner.Id,
                    offer.Id,
                    correlationId);
                await _notificationProducer.PublishAsync(new NotificationMessage(
                    eventType,
                    owner.Email,
                    subject,
                    body,
                    occurredAt,
                    correlationId,
                    owner.Id,
                    offer.Id));
            }
        }

        private string GetCorrelationId()
        {
            if (HttpContext.Items.TryGetValue("CorrelationId", out var value) && value is string correlationId)
            {
                return correlationId;
            }

            return Guid.NewGuid().ToString("N");
        }
    }

    public record TradeOfferCreateDto(int RequestedGameId, int OfferedGameId);

    public record TradeOfferRespondDto(string Decision);

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