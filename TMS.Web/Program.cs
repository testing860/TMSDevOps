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

// Load appsettings.json + appsettings.{Environment}.json
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                     .AddJsonFile($"appsettings.{builder.HostEnvironment.Environment}.json", optional: true);

// Blazored Storage & Toasts
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddBlazoredToast();

// Authentication State Provider
builder.Services.AddScoped<AuthenticationStateProvider, AuthStateProvider>();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthMessageHandler>();

// Configure HttpClient with the authorization handler
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? throw new Exception("ApiBaseUrl is not set in appsettings!");
builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<AuthMessageHandler>();
    handler.InnerHandler = new HttpClientHandler();

    return new HttpClient(handler)
    {
        BaseAddress = new Uri(apiBaseUrl)
    };
});

// Register Services
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<AuthService>();

await builder.Build().RunAsync();