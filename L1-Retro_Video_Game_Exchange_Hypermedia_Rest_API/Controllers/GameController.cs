using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Data;
using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.DTOs;
using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GamesController : ControllerBase
    {
        private readonly ExchangeDbContext _db;

        public GamesController(ExchangeDbContext db)
        {
            _db = db;
        }

        // POST api/games  (create game)
        [HttpPost]
        public async Task<ActionResult<GameDto>> CreateGame(GameCreateDto dto)
        {
            // check user exists
            var ownerExists = await _db.Users.AnyAsync(u => u.Id == dto.OwnerId);
            if (!ownerExists)
                return BadRequest(new { error = "Owner user does not exist." });

            var game = new Game
            {
                Name = dto.Name,
                Publisher = dto.Publisher,
                Year = dto.Year,
                System = dto.System,
                Condition = dto.Condition,
                PreviousOwners = dto.PreviousOwners,
                OwnerId = dto.OwnerId
            };

            _db.Games.Add(game);
            await _db.SaveChangesAsync();

            var gameDto = ToGameDto(game);
            return CreatedAtAction(nameof(GetGameById), new { id = game.Id }, gameDto);
        }

        // GET api/games/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<GameDto>> GetGameById(int id)
        {
            var game = await _db.Games.FindAsync(id);
            if (game == null)
            {
                return NotFound(new { error = "Game not found." });
            }

            return Ok(ToGameDto(game));
        }

        // PUT api/games/{id}  (full update)
        [HttpPut("{id:int}")]
        public async Task<ActionResult<GameDto>> ReplaceGame(int id, GameUpdateDto dto)
        {
            var game = await _db.Games.FindAsync(id);
            if (game == null)
            {
                return NotFound(new { error = "Game not found." });
            }

            // full replace of mutable fields
            game.Name = dto.Name.Trim();
            game.Publisher = dto.Publisher.Trim();
            game.Year = dto.Year;
            game.System = dto.System.Trim();
            game.Condition = dto.Condition.Trim();
            game.PreviousOwners = dto.PreviousOwners;
            game.OwnerId = dto.OwnerUserId;

            await _db.SaveChangesAsync();

            return Ok(ToGameDto(game));
        }

        // PATCH api/games/{id}  (partial update)
        [HttpPatch("{id:int}")]
        public async Task<ActionResult<GameDto>> PatchGame(int id, GamePatchDto dto)
        {
            var game = await _db.Games.FindAsync(id);
            if (game == null)
            {
                return NotFound(new { error = "Game not found." });
            }

            if (!string.IsNullOrWhiteSpace(dto.Name))
                game.Name = dto.Name.Trim();

            if (!string.IsNullOrWhiteSpace(dto.Publisher))
                game.Publisher = dto.Publisher.Trim();

            if (dto.Year.HasValue)
                game.Year = dto.Year.Value;

            if (!string.IsNullOrWhiteSpace(dto.System))
                game.System = dto.System.Trim();

            if (!string.IsNullOrWhiteSpace(dto.Condition))
                game.Condition = dto.Condition.Trim();

            if (dto.PreviousOwners.HasValue)
                game.PreviousOwners = dto.PreviousOwners;

            if (dto.OwnerUserId.HasValue)
                game.OwnerId = dto.OwnerUserId.Value;

            await _db.SaveChangesAsync();

            return Ok(ToGameDto(game));
        }

        // DELETE api/games/{id}
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteGame(int id)
        {
            var game = await _db.Games.FindAsync(id);
            if (game == null)
            {
                return NotFound(new { error = "Game not found." });
            }

            _db.Games.Remove(game);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // GET api/games  (search)

        //   /api/games?name=mario
        //   /api/games?system=NES
        //   /api/games?ownerUserId=3
        [HttpGet]
        public async Task<ActionResult<IEnumerable<GameDto>>> SearchGames(
            [FromQuery] string? name,
            [FromQuery] string? system,
            [FromQuery] int? ownerUserId)
        {
            IQueryable<Game> query = _db.Games;

            if (!string.IsNullOrWhiteSpace(name))
                query = query.Where(g => g.Name.Contains(name));

            if (!string.IsNullOrWhiteSpace(system))
                query = query.Where(g => g.System == system);

            if (ownerUserId.HasValue)
                query = query.Where(g => g.OwnerId == ownerUserId.Value);

            var games = await query.ToListAsync();
            var dtos = games.Select(ToGameDto);

            return Ok(dtos);
        }

        // GET api/games/others/{userId}  (games not owned by user)
        // Used by the trade-offer UI to show games the user can request.
        [HttpGet("others/{userId:int}")]
        public async Task<ActionResult<IEnumerable<GameDto>>> GetGamesNotOwnedByUser(int userId)
        {
            var userExists = await _db.Users.AnyAsync(u => u.Id == userId);
            if (!userExists)
                return NotFound(new { error = "User not found." });

            var games = await _db.Games
                .Where(g => g.OwnerId != userId)
                .ToListAsync();

            return Ok(games.Select(ToGameDto));
        }

        // GET api/users/{userId}/games  (games owned by a specific user)
        [HttpGet("/api/users/{userId:int}/games")]
        public async Task<ActionResult<IEnumerable<GameDto>>> GetGamesForUser(int userId)
        {
            var userExists = await _db.Users.AnyAsync(u => u.Id == userId);
            if (!userExists)
            {
                return NotFound(new { error = "User not found." });
            }

            var games = await _db.Games
                .Where(g => g.OwnerId == userId)
                .ToListAsync();

            var dtos = games.Select(ToGameDto);
            return Ok(dtos);
        }

        // helper: map entity to DTO with HATEOAS links
        private GameDto ToGameDto(Game game)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var links = new List<LinkDto>
            {
                new("self",   $"{baseUrl}/api/games/{game.Id}", "GET"),
                new("update", $"{baseUrl}/api/games/{game.Id}", "PATCH"),
                new("replace",$"{baseUrl}/api/games/{game.Id}", "PUT"),
                new("delete", $"{baseUrl}/api/games/{game.Id}", "DELETE"),
                new("owner",  $"{baseUrl}/api/users/{game.OwnerId}", "GET")
            };

            return new GameDto(
                game.Id,
                game.Name,
                game.Publisher,
                game.Year,
                game.System,
                game.Condition,
                game.PreviousOwners,
                game.OwnerId,
                links
            );
        }
    }

    // DTO types for Games

    public record GameCreateDto(
        string Name,
        string Publisher,
        int Year,
        string System,
        string Condition,
        int? PreviousOwners,
        int OwnerId);


    public record GameUpdateDto(
        string Name,
        string Publisher,
        int Year,
        string System,
        string Condition,
        int? PreviousOwners,
        int OwnerUserId);

    public record GamePatchDto(
        string? Name,
        string? Publisher,
        int? Year,
        string? System,
        string? Condition,
        int? PreviousOwners,
        int? OwnerUserId);

    public record GameDto(
        int Id,
        string Name,
        string Publisher,
        int Year,
        string System,
        string Condition,
        int? PreviousOwners,
        int OwnerUserId,
        List<LinkDto> Links);
}
