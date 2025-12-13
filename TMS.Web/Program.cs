using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Blazored.LocalStorage;
using Blazored.Toast;
using TMS.Web;
using TMS.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ================================
// API BASE URL Configuration
// ================================
// Try to get from environment variable first (Docker), then from config
var apiBaseUrl = Environment.GetEnvironmentVariable("ApiBaseUrl");
if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    apiBaseUrl = builder.Configuration["ApiBaseUrl"];
}

if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    // Fallback for development
    apiBaseUrl = builder.HostEnvironment.IsDevelopment()
        ? "https://localhost:7130/"
        : "http://localhost:5000/";
}

Console.WriteLine($"API Base URL: {apiBaseUrl}");

// ================================
// Services
// ================================

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddBlazoredToast();

// Authentication & Authorization
builder.Services.AddScoped<AuthenticationStateProvider, AuthStateProvider>();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthMessageHandler>();

// HttpClient with JWT handler
builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<AuthMessageHandler>();
    handler.InnerHandler = new HttpClientHandler();

    return new HttpClient(handler)
    {
        BaseAddress = new Uri(apiBaseUrl)
    };
});

// App Services
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<AuthService>();

await builder.Build().RunAsync();