using System.Security.Claims;
using System.Text.Json;
using EventLauscherApi.Controllers;
using EventLauscherApi.Data;
using EventLauscherApi.Models;
using EventLauscherApi.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventLauscherApi.Tests.Infrastructure;

internal sealed class TestEnvironment : IDisposable
{
    public const string Password = "testpass123";
    public const string JwtKey = "test-only-signing-key-with-at-least-32-bytes-123456";
    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;
    private readonly string _database = Guid.NewGuid().ToString();
    public EventContext Db => _scope.ServiceProvider.GetRequiredService<EventContext>();
    public UserManager<AppUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    public RoleManager<AppRole> Roles => _scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
    public RecordingEmailService Mail { get; } = new();
    public IConfiguration Configuration { get; } = CreateConfiguration();
    public JwtService Jwt => new(Configuration, Db);
    public Guid UserId { get; } = Guid.NewGuid();

    public TestEnvironment()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(Configuration);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddDbContext<EventContext>(o => o.UseInMemoryDatabase(_database));
        services.AddIdentityCore<AppUser>(o =>
        {
            o.User.RequireUniqueEmail = true;
            o.SignIn.RequireConfirmedEmail = true;
            o.Password.RequiredLength = 8;
            o.Password.RequireDigit = true;
            o.Password.RequireLowercase = true;
            o.Password.RequireUppercase = false;
            o.Password.RequireNonAlphanumeric = false;
            o.Lockout.MaxFailedAccessAttempts = 5;
        }).AddRoles<AppRole>().AddEntityFrameworkStores<EventContext>()
            .AddSignInManager().AddDefaultTokenProviders();
        _services = services.BuildServiceProvider();
        _scope = _services.CreateScope();
        Db.Users.Add(new AppUser
        {
            Id = UserId, UserName = "tester", NormalizedUserName = "TESTER",
            Email = "tester@example.com", NormalizedEmail = "TESTER@EXAMPLE.COM",
            SecurityStamp = Guid.NewGuid().ToString()
        });
        Db.SaveChanges();
    }

    public static IConfiguration CreateConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Jwt:Key"] = JwtKey, ["Jwt:Issuer"] = "eventlauscher-tests",
            ["Jwt:Audience"] = "eventlauscher-tests", ["Jwt:AccessTokenMinutes"] = "15",
            ["Jwt:RefreshTokenDays"] = "7", ["PublicApiBaseUrl"] = "https://api.example.test",
            ["Frontend:BaseUrl"] = "https://app.example.test",
            ["Instagram:WebhookVerifyToken"] = "test-verify-token"
        }).Build();

    public T Attach<T>(T controller, Guid? userId = null, params string[] roles) where T : ControllerBase
    {
        var id = userId ?? UserId;
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id.ToString()), new("sub", id.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
        };
        controller.Request.Scheme = "https";
        controller.Request.Host = new HostString("api.example.test");
        return controller;
    }

    public EventsController Events(Guid? userId = null, params string[] roles) => Attach(new EventsController(Db), userId, roles);
    public ReviewerEventsController Reviewer(Guid? userId = null) => Attach(new ReviewerEventsController(Db, Users), userId, "Reviewer");
    public PublicController Public() => Attach(new PublicController(Db, Users));
    public AuthController Auth()
    {
        var controller = Attach(new AuthController(Users, Roles,
            _scope.ServiceProvider.GetRequiredService<SignInManager<AppUser>>(), Jwt, Mail, Configuration));
        _scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = controller.HttpContext;
        return controller;
    }

    public EventContext Reload() => new(new DbContextOptionsBuilder<EventContext>().UseInMemoryDatabase(_database).Options);

    public async Task<Event> AddEvent(EventStatus status = EventStatus.Draft, Guid? owner = null, string? date = "2026-11-15")
    {
        var ev = new Event { Title = "Test event", UploadUserId = owner ?? UserId, Status = status, Date = date };
        Db.Events.Add(ev);
        await Db.SaveChangesAsync();
        return ev;
    }

    public async Task<AppUser> AddUser(string username = "alice", bool confirmed = true, params string[] roles)
    {
        var user = new AppUser { UserName = username, Email = $"{username}@example.com", EmailConfirmed = confirmed };
        Assert.True((await Users.CreateAsync(user, Password)).Succeeded);
        foreach (var role in roles)
        {
            if (!await Roles.RoleExistsAsync(role))
                Assert.True((await Roles.CreateAsync(new AppRole { Name = role })).Succeeded);
            Assert.True((await Users.AddToRoleAsync(user, role)).Succeeded);
        }
        return user;
    }

    public static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    public static JsonElement Ok(IActionResult result) => Json(Assert.IsType<OkObjectResult>(result).Value);

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
    }
}

internal sealed class RecordingEmailService : IEmailService
{
    public List<(string To, string Subject, string Html)> Messages { get; } = new();
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Messages.Add((to, subject, htmlBody));
        return Task.CompletedTask;
    }
}
