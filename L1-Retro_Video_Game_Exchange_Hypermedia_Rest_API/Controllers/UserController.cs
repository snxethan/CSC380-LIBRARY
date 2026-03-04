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
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly ExchangeDbContext _db;

        /// <summary>
        /// Kafka notification producer injected via DI.
        /// Used by <see cref="ChangePassword"/> to publish a
        /// <c>UserPasswordChanged</c> event after a successful password update.
        /// </summary>
        private readonly INotificationProducer _notificationProducer;

        public UsersController(ExchangeDbContext db, INotificationProducer notificationProducer)
        {
            _db = db;
            _notificationProducer = notificationProducer;
        }


        // GET api/users
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
        {
            var users = await _db.Users.ToListAsync();
            var result = users.Select(ToUserDto);
            return Ok(result);
        }


        // POST api/users  (register)
        [HttpPost]
        public async Task<ActionResult<UserDto>> CreateUser(UserCreateDto dto)
        {
            // basic validation
            if (string.IsNullOrWhiteSpace(dto.Name) ||
                string.IsNullOrWhiteSpace(dto.Email) ||
                string.IsNullOrWhiteSpace(dto.Password) ||
                string.IsNullOrWhiteSpace(dto.StreetAddress))
            {
                return BadRequest(new { error = "Name, email, password, and street address are required." });
            }

            // enforce unique email
            var emailExists = await _db.Users.AnyAsync(u => u.Email == dto.Email);
            if (emailExists)
            {
                return Conflict(new { error = "Email is already registered." });
            }

            var user = new User
            {
                Name = dto.Name.Trim(),
                Email = dto.Email.Trim(),
                Password = dto.Password,
                StreetAddress = dto.StreetAddress.Trim()
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var userDto = ToUserDto(user);
            return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, userDto);
        }

        // GET api/users/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<UserDto>> GetUserById(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound(new { error = "User not found." });
            }

            return Ok(ToUserDto(user));
        }

        // PATCH api/users/{id}/password
        // Requires Basic authentication (email:password).
        // After a successful password change, publishes a Kafka notification
        // so the user receives a confirmation email via the NotificationWorker.
        [HttpPatch("{id:int}/password")]
        public async Task<IActionResult> ChangePassword(int id, UserPasswordUpdateDto dto)
        {
            // Verify the caller is authenticated and is the same user.
            var authenticatedUser = await AuthenticateAsync();
            if (authenticatedUser == null)
                return UnauthorizedResult();

            if (authenticatedUser.Id != id)
                return StatusCode(403, new { error = "Forbidden." });

            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest(new { error = "New password is required." });

            // Verify the current password before allowing the change.
            if (!string.Equals(authenticatedUser.Password, dto.CurrentPassword, StringComparison.Ordinal))
                return BadRequest(new { error = "Current password is incorrect." });

            // Persist the new password.
            authenticatedUser.Password = dto.NewPassword;
            await _db.SaveChangesAsync();

            // ── Kafka notification ────────────────────────────────────────
            // Publish a UserPasswordChanged event to the "notifications" topic.
            // KafkaNotificationProducer serialises this to JSON and sends it
            // to Kafka.  The NotificationWorker container consumes it and logs
            // (or emails) the notification to the user.
            //
            // This call is intentionally fire-and-forget: if Kafka is
            // temporarily unavailable the DB change still succeeds and the
            // API returns 204 — the notification is simply dropped.
            await _notificationProducer.PublishAsync(new NotificationMessage(
                EventType:     "UserPasswordChanged",
                ToEmail:       authenticatedUser.Email,
                Subject:       "Password changed",
                Body:          "Your password was updated successfully.",
                OccurredAtUtc: DateTime.UtcNow));

            return NoContent();
        }

        // PATCH api/users/{id}
        [HttpPatch("{id:int}")]
        public async Task<ActionResult<UserDto>> UpdateUser(int id, UserUpdateDto dto)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound(new { error = "User not found." });
            }

            // email cannot be changed
            if (!string.IsNullOrWhiteSpace(dto.Name))
            {
                user.Name = dto.Name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(dto.StreetAddress))
            {
                user.StreetAddress = dto.StreetAddress.Trim();
            }

            await _db.SaveChangesAsync();

            return Ok(ToUserDto(user));
        }

        // DELETE api/users/{id}
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _db.Users
                .Include(u => u.Games)
                .SingleOrDefaultAsync(u => u.Id == id);

            if (user == null)
            {
                return NotFound(new { error = "User not found." });
            }

            _db.Users.Remove(user);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // helper: map entity -> DTO with HATEOAS links
        private UserDto ToUserDto(User user)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var links = new List<LinkDto>
            {
                new("self",   $"{baseUrl}/api/users/{user.Id}", "GET"),
                new("update", $"{baseUrl}/api/users/{user.Id}", "PATCH"),
                new("delete", $"{baseUrl}/api/users/{user.Id}", "DELETE"),
                new("games",  $"{baseUrl}/api/users/{user.Id}/games", "GET")
            };

            return new UserDto(
                user.Id,
                user.Name,
                user.Email,
                user.StreetAddress,
                links
            );
        }

        // Reads the Basic Authorization header and returns the matching User,
        // or null if the credentials are missing or incorrect.
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
            Response.Headers["WWW-Authenticate"] = "Basic realm=\"users\"";
            return Unauthorized(new { error = "Basic authentication required." });
        }
    }

    // DTO types for Users

    public record UserCreateDto(string Name, string Email, string Password, string StreetAddress);

    // Only fields allowed to change after registration
    public record UserUpdateDto(string? Name, string? StreetAddress);

    /// <summary>
    /// Payload for <c>PATCH /api/users/{id}/password</c>.
    /// Both fields are required; the current password is verified server-side
    /// before the change is committed and the Kafka notification is published.
    /// </summary>
    public record UserPasswordUpdateDto(string CurrentPassword, string NewPassword);

    public record UserDto(
        int Id,
        string Name,
        string Email,
        string StreetAddress,
        List<LinkDto> Links);
}
