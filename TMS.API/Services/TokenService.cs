using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using TMS.API.Data.Entities;

namespace TMS.API.Services
{
    public class TokenService : ITokenService
    {
        private readonly IConfiguration _config;
        private readonly UserManager<AppUser> _userManager;

        public TokenService(IConfiguration config, UserManager<AppUser> userManager)
        {
            _config = config;
            _userManager = userManager;
        }

        public async Task<string> GenerateTokenAsync(AppUser user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var authClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id ?? string.Empty),
        new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
        new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
        new Claim("DisplayName", user.DisplayName ?? string.Empty)
    };

            var roles = await _userManager.GetRolesAsync(user);
            foreach (var role in roles)
            {
                authClaims.Add(new Claim(ClaimTypes.Role, role));
            }

            // Read from ENV first; Fallback to Config
            var keyString = Environment.GetEnvironmentVariable("JWT_KEY")
                ?? _config["Jwt:Key"]
                ?? throw new InvalidOperationException("JWT_KEY is not configured");

            var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER")
                ?? _config["Jwt:Issuer"]
                ?? "TMSAPI";

            var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE")
                ?? _config["Jwt:Audience"]
                ?? "TMSWebClient";

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: authClaims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
