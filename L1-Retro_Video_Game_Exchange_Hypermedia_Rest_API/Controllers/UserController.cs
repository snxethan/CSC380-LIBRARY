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
    public class UsersController : ControllerBase
    {
        private readonly ExchangeDbContext _db;

        public UsersController(ExchangeDbContext db)
        {
            _db = db;
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
    }

    // DTO types for Users

    public record UserCreateDto(string Name, string Email, string Password, string StreetAddress);

    // Only fields allowed to change after registration
    public record UserUpdateDto(string? Name, string? StreetAddress);

    public record UserDto(
        int Id,
        string Name,
        string Email,
        string StreetAddress,
        List<LinkDto> Links);
}
