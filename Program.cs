using System.Text;
using System.Threading.RateLimiting;
using EventLauscherApi.Data;
using EventLauscherApi.Models;
using EventLauscherApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;
using EventLauscherApi.Services.Instagram;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// Swagger + Bearer Auth (sauber mit Reference)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Eventlauscher API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Bearer {token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new List<string>()
        }
    });
});

// DbContext
builder.Services.AddDbContext<EventContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity (Guid-basierter User/Role)
builder.Services
    .AddIdentityCore<AppUser>(o =>
    {
        // Sign-In/Email
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedEmail = true;

        // Passwort-Policy (passt zu deinem UI-Validator)
        o.Password.RequiredLength = 8;
        o.Password.RequireDigit = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;

        // Lockout
        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<EventContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Lebensdauer für DataProtection-Tokens (Password-Reset/E-Mail-Confirm)
builder.Services.Configure<DataProtectionTokenProviderOptions>(o =>
{
    o.TokenLifespan = TimeSpan.FromHours(2);
});

// JWT Authentication
var jwt = builder.Configuration.GetSection("Jwt");
var key = new SymmetricSecurityKey(
    Encoding.UTF8.GetBytes(jwt["Key"] ?? throw new InvalidOperationException("Jwt:Key missing"))
);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = key,

            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ClockSkew = TimeSpan.FromSeconds(30),

            // ✅ wichtig: Claims eindeutig machen
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };

        o.SaveToken = true;
    });

// Rollen-Policies
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Reviewer", p => p.RequireRole("Reviewer", "Admin"));
    o.AddPolicy("Organizer", p => p.RequireRole("Organizer", "Admin"));

    // darf sofort veröffentlichen (Organizer/Reviewer/Admin)
    o.AddPolicy("CanPublish", p => p.RequireRole("Organizer", "Reviewer", "Admin"));

    o.AddPolicy("Admin", p => p.RequireRole("Admin"));
});

// CORS (fürs Testen offen; vor Launch enger fassen)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Rate Limiting
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Partition by the connection IP after trusted forwarded headers were applied.
    // Do not read X-Forwarded-For directly: arbitrary clients can forge it.
    o.AddPolicy("auth-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1), PermitLimit = 10,
            QueueLimit = 0, AutoReplenishment = true
        }));
    o.AddPolicy("pwreset", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(5), PermitLimit = 5,
            QueueLimit = 0, AutoReplenishment = true
        }));
});

// App-Services
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

builder.Services.AddScoped<IInstagramWebhookInboxProcessor, InstagramWebhookInboxProcessor>();
builder.Services.AddScoped<IInstagramSourceFetchProcessor, InstagramSourceFetchProcessor>();
builder.Services.AddScoped<IInstagramGraphClient, InstagramGraphClient>();

builder.Services.AddHostedService<InstagramWebhookInboxWorker>();
builder.Services.AddHostedService<InstagramSourceFetchWorker>();

builder.Services.AddHttpClient();

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Reverse proxy support (Nginx/Traefik)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// CORS
app.UseCors("AllowAll");

// 401/403 sichtbar machen
app.UseStatusCodePages();

app.UseHttpsRedirection();

app.UseRateLimiter();

// Wichtig: AuthN vor AuthZ
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Optional: Seed
// await EventLauscherApi.Data.Seed.EnsureSeedAsync(app);

app.Run();

// Entry point for HTTP integration tests using WebApplicationFactory.
public partial class Program { }
