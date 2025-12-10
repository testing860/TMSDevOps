using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using TMS.Shared.DTOs;

namespace TMS.Web.Services
{
    public class AuthService
    {
        private readonly HttpClient _http;
        private readonly ILocalStorageService _localStorage;
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly NavigationManager _navigation;
        private const string TokenKey = "tmsAuthToken";

        public AuthService(
            HttpClient http,
            ILocalStorageService localStorage,
            AuthenticationStateProvider authStateProvider,
            NavigationManager navigation)
        {
            _http = http;
            _localStorage = localStorage;
            _authStateProvider = authStateProvider;
            _navigation = navigation;
        }

        public async Task<bool> LoginAsync(string emailOrUsername, string password)
        {
            if (string.IsNullOrWhiteSpace(emailOrUsername) || string.IsNullOrWhiteSpace(password))
                return false;

            var request = new AuthRequestDto
            {
                Email = emailOrUsername,
                Password = password
            };

            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsJsonAsync("api/token/login", request);
            }
            catch (HttpRequestException)
            {
                return false;
            }

            if (!response.IsSuccessStatusCode)
                return false;

            var authResponse = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
            if (authResponse == null || string.IsNullOrWhiteSpace(authResponse.AccessToken))
                return false;

            // Storing Token (with no extra quotes)
            await _localStorage.SetItemAsStringAsync(TokenKey, authResponse.AccessToken.Trim('"'));

            // Notify the AuthStateProvider
            if (_authStateProvider is AuthStateProvider customProvider)
            {
                customProvider.NotifyUserAuthentication(authResponse.AccessToken);
            }

            return true;
        }

        public async Task LogoutAsync()
        {
            await _localStorage.RemoveItemAsync(TokenKey);

            if (_authStateProvider is AuthStateProvider customProvider)
                customProvider.NotifyUserLogout();

            _navigation.NavigateTo("/login");
        }

        public async Task<RegisterResultDto> RegisterAsync(RegisterDto registerDto)
        {
            if (registerDto == null) throw new ArgumentNullException(nameof(registerDto));

            var response = await _http.PostAsJsonAsync("api/token/register", registerDto);

            if (!response.IsSuccessStatusCode)
            {
                var errorResult = await response.Content.ReadFromJsonAsync<RegisterResultDto>();
                return errorResult ?? new RegisterResultDto { Success = false, Errors = new[] { "Registration failed" } };
            }

            var authResponse = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
            if (authResponse == null || string.IsNullOrWhiteSpace(authResponse.AccessToken))
            {
                return new RegisterResultDto { Success = false, Errors = new[] { "No token returned after registration" } };
            }

            await _localStorage.SetItemAsStringAsync(TokenKey, authResponse.AccessToken.Trim('"'));

            if (_authStateProvider is AuthStateProvider customProvider)
                customProvider.NotifyUserAuthentication(authResponse.AccessToken);

            return new RegisterResultDto { Success = true };
        }

        public async Task<string?> GetTokenAsync()
        {
            return await _localStorage.GetItemAsync<string>(TokenKey);
        }
    }
}