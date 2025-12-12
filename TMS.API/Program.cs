using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using TMS.API.Data;
using TMS.API.Data.Entities;
using TMS.API.Services;

// Env Setup
DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Db Connection (Env)
var dbServer = Environment.GetEnvironmentVariable("DB_SERVER") ?? "localhost,1433";
var dbName = Environment.GetEnvironmentVariable("DB_NAME") ?? "TaskManagementSystem";
var dbUser = Environment.GetEnvironmentVariable("DB_USER") ?? "sa";
var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");

if (string.IsNullOrEmpty(dbPassword))
{
    throw new Exception("DB_PASSWORD is not set in .env file!");
}

var connectionString = $"Server={dbServer};Database={dbName};User Id={dbUser};Password={dbPassword};TrustServerCertificate=true;";

builder.Services.AddDbContext<TMSDbContext>(options =>
    options.UseSqlServer(connectionString));

// Identity (AppUser + Roles)
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<TMSDbContext>()
.AddDefaultTokenProviders();

// JWT Tokens (Env)
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY");
if (string.IsNullOrEmpty(jwtKey))
{
    // Generate random JWT key if not set
    Console.WriteLine("WARNING: JWT_KEY not set in .env, generating random one...");
    jwtKey = GenerateSecureKey(64);
    Console.WriteLine($"Generated JWT Key: {jwtKey.Substring(0, 16)}...");
    Console.WriteLine("Add this to your .env file as JWT_KEY=<the_full_key>");
    Console.WriteLine("If you don't save it, existing tokens won't work after restart!");
}

var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "TMSAPI";
var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "TMSWebClient";

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = true;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,

        ValidateAudience = true,
        ValidAudience = audience,

        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey,

        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(2)
    };
});

// Authorization
builder.Services.AddAuthorization();

// Controllers & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "TMS API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter JWT as: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
        {
            new OpenApiSecurityScheme {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Services
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAppTaskService, AppTaskService>();
builder.Services.AddScoped<IUserService, UserService>();

// HttpContextAccessor
builder.Services.AddHttpContextAccessor();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowTMSWeb", policy =>
    {
        policy.WithOrigins(
            "https://localhost:7141",
            "https://localhost:7181",
            "http://10.0.2.15:7130"
        )
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});


var app = builder.Build();

// Middleware Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "TMS API v1"));
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseCors("AllowTMSWeb");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// USER SEEDING USING .ENV
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    try
    {
        var db = services.GetRequiredService<TMSDbContext>();
        db.Database.Migrate();

        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        // Ensure Roles
        string[] roles = { "Admin", "User" };
        foreach (var r in roles)
        {
            if (!roleManager.RoleExistsAsync(r).Result)
                roleManager.CreateAsync(new IdentityRole(r)).Wait();
        }

        // ADMIN SEEDING
        string adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? "admin@tms.com";
        string adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? "ThisIsAPassword123!";
        string adminDisplayName = "Admin";

        var existing = userManager.FindByEmailAsync(adminEmail).Result;
        if (existing == null)
        {
            var admin = new AppUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                DisplayName = adminDisplayName,
                EmailConfirmed = true
            };
            var create = userManager.CreateAsync(admin, adminPassword).Result;
            if (create.Succeeded)
            {
                userManager.AddToRoleAsync(admin, "Admin").Wait();
                Console.WriteLine($"Created admin user: {adminEmail}");
            }
            else
            {
                Console.WriteLine($"Failed creating admin user: {string.Join(", ", create.Errors.Select(e => e.Description))}");
            }
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error while migrating/seeding DB");
    }
}

// Helper function to generate random JWT key
string GenerateSecureKey(int length)
{
    const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()-_=+[]{}|;:,.<>?";
    var random = new Random();
    return new string(Enumerable.Repeat(chars, length)
        .Select(s => s[random.Next(s.Length)]).ToArray());
}

app.Run();
