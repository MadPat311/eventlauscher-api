using EventLauscherApi.Data;
using EventLauscherApi.Services;
using EventLauscherApi.Services.Instagram;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Xml.Linq;

namespace EventLauscherApi.Tests.Infrastructure;

internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _database = Guid.NewGuid().ToString();
    public RecordingEmailService Mail { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var pair in TestEnvironment.CreateConfiguration().AsEnumerable())
            if (pair.Value is not null) builder.UseSetting(pair.Key, pair.Value);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<EventContext>>();
            services.AddDbContext<EventContext>(o => o.UseInMemoryDatabase(_database));
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Mail);
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.Configure<KeyManagementOptions>(o => o.XmlRepository = new MemoryKeyRepository());
            // Real workers are tested separately; they must not consume HTTP test data.
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) &&
                         (d.ImplementationType == typeof(InstagramWebhookInboxWorker) ||
                          d.ImplementationType == typeof(InstagramSourceFetchWorker))).ToArray())
                services.Remove(descriptor);
        });
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });

    public async Task SeedAsync(Func<IServiceProvider, Task> seed)
    {
        using var scope = Services.CreateScope();
        await seed(scope.ServiceProvider);
    }

    private sealed class MemoryKeyRepository : IXmlRepository
    {
        private readonly List<XElement> _elements = new();
        public IReadOnlyCollection<XElement> GetAllElements()
        {
            lock (_elements) return _elements.Select(x => new XElement(x)).ToArray();
        }
        public void StoreElement(XElement element, string friendlyName)
        {
            lock (_elements) _elements.Add(new XElement(element));
        }
    }
}
