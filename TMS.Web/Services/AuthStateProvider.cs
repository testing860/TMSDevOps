using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace TMS.Web.Services
{
    public class AuthStateProvider : AuthenticationStateProvider
    {
        private readonly ILocalStorageService _localStorage;
        private const string TokenKey = "tmsAuthToken";

        public AuthStateProvider(ILocalStorageService localStorage)
        {
            _localStorage = localStorage;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var token = await _localStorage.GetItemAsync<string>(TokenKey);

            if (string.IsNullOrWhiteSpace(token))
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

            token = token.Trim('"');

            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                // Malformed Token -> Anonymous
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var jwtToken = handler.ReadJwtToken(token);

            // Expired Token -> Remove Token and Return Anonymously
            if (jwtToken.ValidTo < DateTime.UtcNow)
            {
                await _localStorage.RemoveItemAsync(TokenKey);
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var principal = CreatePrincipalFromToken(jwtToken);
            return new AuthenticationState(principal);
        }

        public void NotifyUserAuthentication(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return;

            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                return;
            }

            var jwtToken = handler.ReadJwtToken(token);
            var user = CreatePrincipalFromToken(jwtToken);
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
        }

        public void NotifyUserLogout()
        {
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(anonymous)));
        }

        private ClaimsPrincipal CreatePrincipalFromToken(JwtSecurityToken jwtToken)
        {
            // Convert Token Claims into ClaimsPrincipal, Mormalize Role Claims
            var claims = jwtToken.Claims
                .Select(c =>
                {
                    if (string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(c.Type, "roles", StringComparison.OrdinalIgnoreCase))
                    {
                        return new Claim(ClaimTypes.Role, c.Value);
                    }

                    return new Claim(c.Type, c.Value);
                })
                .ToList();

            // Ensure a Name Claim exists
            var nameClaim = claims.FirstOrDefault(c =>
                c.Type == ClaimTypes.Name ||
                string.Equals(c.Type, "name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Type, "email", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Type, "sub", StringComparison.OrdinalIgnoreCase));

            if (nameClaim != null && !claims.Any(c => c.Type == ClaimTypes.Name))
            {
                claims.Add(new Claim(ClaimTypes.Name, nameClaim.Value));
            }

            var identity = new ClaimsIdentity(claims, "jwt");
            return new ClaimsPrincipal(identity);
        }
    }
}