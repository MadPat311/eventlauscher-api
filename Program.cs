using System.Text;
using EventLauscherApi.Data;
using EventLauscherApi.Services;
using EventLauscherApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// Swagger + Bearer Auth
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Bearer {token}"
    };
    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { scheme, new List<string>() }
    });
});

// DbContext (dein bestehender ConnectionString-Name)
builder.Services.AddDbContext<EventContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity (Guid-basierter User/Role) + Token-Provider (für E-Mail-Bestätigung etc.)
builder.Services
    .AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedEmail = true;   // E-Mail muss bestätigt sein
        o.Password.RequiredLength = 6;
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<EventContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// JWT Authentication
var jwt = builder.Configuration.GetSection("Jwt");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"] ?? throw new InvalidOperationException("Jwt:Key missing")));
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
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

// Rollen-Policies
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Reviewer", p => p.RequireRole("Reviewer", "Admin"));
    o.AddPolicy("Admin", p => p.RequireRole("Admin"));
});

// CORS (deine bestehende Policy beibehalten)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("auth-login", options =>
    {
        options.Window = TimeSpan.FromMinutes(1);
        options.PermitLimit = 10;         // z. B. 10 Logins/Minute pro IP
        options.QueueLimit = 0;
    });
});

// App-Services (JWT + SMTP via MailKit)
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

// Zeigt 401/403 als Statusseiten (statt 404/Redirects)
app.UseStatusCodePages();

app.UseHttpsRedirection();

app.UseRateLimiter();

// Wichtig: AuthN vor AuthZ
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Optional (nur einbinden, wenn du die Seed-Klasse angelegt hast):
// await EventlauscherApi.Data.Seed.EnsureSeedAsync(app);

app.Run();
