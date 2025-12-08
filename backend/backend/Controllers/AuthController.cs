using backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public sealed class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _users;
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly IConfiguration _cfg;

        public AuthController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, IConfiguration cfg)
        {
            _users = users;
            _signIn = signIn;
            _cfg = cfg;
        }

        private static readonly Regex UserNameRule = new(@"^[a-zA-Z0-9_.-]{3,32}$", RegexOptions.Compiled);

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.UserName) || !UserNameRule.IsMatch(dto.UserName) || dto.UserName.Contains('@'))
                return BadRequest(new { message = "Invalid username. 3–32 chars, [a-zA-Z0-9_.-], no '@'." });

            if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
                return BadRequest(new { message = "Password must be at least 6 characters." });

            if (await _users.FindByNameAsync(dto.UserName) is not null)
                return Conflict(new { message = "Username already taken." });

            if (!string.IsNullOrWhiteSpace(dto.Email) && await _users.FindByEmailAsync(dto.Email) is not null)
                return Conflict(new { message = "Email already registered." });

            var u = new ApplicationUser
            {
                UserName = dto.UserName,
                Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant(),
                DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.UserName : dto.DisplayName.Trim()
            };

            var res = await _users.CreateAsync(u, dto.Password);
            if (!res.Succeeded)
            {
                var errors = string.Join(" ", res.Errors.Select(e => e.Description));
                return BadRequest(new { message = errors });
            }

            await _users.AddToRoleAsync(u, "Player");

            var token = GenerateJwt(u);
            return Ok(new
            {
                token,
                user = new { u.Id, u.UserName, u.Email, u.DisplayName }
            });
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.UserOrEmail) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest(new { message = "Username/email and password required." });

            var identifier = dto.UserOrEmail.Trim();
            var u = identifier.Contains('@')
                ? await _users.FindByEmailAsync(identifier.ToLowerInvariant())
                : await _users.FindByNameAsync(identifier);

            if (u is null)
                return Unauthorized(new { message = "Invalid credentials." });

            var pass = await _signIn.CheckPasswordSignInAsync(u, dto.Password, false);
            if (!pass.Succeeded)
                return Unauthorized(new { message = "Invalid credentials." });

            var token = GenerateJwt(u);
            return Ok(new
            {
                token,
                user = new { u.Id, u.UserName, u.Email, u.DisplayName }
            });
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            var u = await _users.GetUserAsync(User);
            if (u is null) return Unauthorized();

            var roles = await _users.GetRolesAsync(u);
            return Ok(new
            {
                u.Id,
                u.UserName,
                u.Email,
                u.DisplayName,
                Roles = roles
            });
        }

        [HttpPut("profile")]
        [Authorize]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var u = await _users.GetUserAsync(User);
            if (u is null) return Unauthorized();

            if (!string.IsNullOrWhiteSpace(dto.DisplayName))
                u.DisplayName = dto.DisplayName.Trim();

            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                var email = dto.Email.Trim().ToLowerInvariant();
                if (email != u.Email)
                {
                    var existing = await _users.FindByEmailAsync(email);
                    if (existing is not null && existing.Id != u.Id)
                        return Conflict(new { message = "Email already in use." });
                    u.Email = email;
                }
            }

            await _users.UpdateAsync(u);
            return Ok(new { u.Id, u.UserName, u.Email, u.DisplayName });
        }

        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword) || string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest(new { message = "Current and new password required." });

            if (dto.NewPassword.Length < 6)
                return BadRequest(new { message = "New password must be at least 6 characters." });

            var u = await _users.GetUserAsync(User);
            if (u is null) return Unauthorized();

            var res = await _users.ChangePasswordAsync(u, dto.CurrentPassword, dto.NewPassword);
            if (!res.Succeeded)
            {
                var errors = string.Join(" ", res.Errors.Select(e => e.Description));
                return BadRequest(new { message = errors });
            }

            return Ok(new { message = "Password changed." });
        }

        [HttpGet("usernames/check")]
        [AllowAnonymous]
        public async Task<IActionResult> CheckUserName([FromQuery] string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Ok(new { available = false, reason = "empty" });

            var trimmed = name.Trim();
            if (!UserNameRule.IsMatch(trimmed) || trimmed.Contains('@'))
                return Ok(new { available = false, reason = "invalid" });

            var exists = await _users.FindByNameAsync(trimmed);
            return Ok(new { available = exists is null });
        }

        [HttpGet("emails/check")]
        [AllowAnonymous]
        public async Task<IActionResult> CheckEmail([FromQuery] string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                return Ok(new { available = false, reason = "invalid" });

            var normalized = email.Trim().ToLowerInvariant();
            var exists = await _users.FindByEmailAsync(normalized);
            return Ok(new { available = exists is null });
        }

        [HttpPost("logout")]
        [Authorize]
        public IActionResult Logout()
        {
            return Ok(new { message = "Logged out. Discard token client-side." });
        }

        private string GenerateJwt(ApplicationUser user)
        {
            var key = _cfg["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key missing");
            var issuer = _cfg["Jwt:Issuer"];
            var audience = _cfg["Jwt:Audience"];

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.UserName ?? user.Id.ToString()),
                new("displayName", user.DisplayName ?? user.UserName ?? string.Empty),
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var creds = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256);

            var jwt = new JwtSecurityToken(
                issuer: string.IsNullOrWhiteSpace(issuer) ? null : issuer,
                audience: string.IsNullOrWhiteSpace(audience) ? null : audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddDays(7),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

        public sealed record RegisterDto(string UserName, string Password, string? Email, string? DisplayName);
        public sealed record LoginDto(string UserOrEmail, string Password);
        public sealed record UpdateProfileDto(string? DisplayName, string? Email);
        public sealed record ChangePasswordDto(string CurrentPassword, string NewPassword);
    }
}