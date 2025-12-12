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

// Determine environment
var env = builder.HostEnvironment.Environment;
var apiBaseUrl = env == "Development"
    ? "https://localhost:7130/"
    : "http://10.0.2.15:7130/api/";


// Blazored Storage & Toasts
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddBlazoredToast();

// Authentication State Provider
builder.Services.AddScoped<AuthenticationStateProvider, AuthStateProvider>();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthMessageHandler>();

// Configure HttpClient with the authorization handler
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
