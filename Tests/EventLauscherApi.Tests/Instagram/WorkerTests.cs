using EventLauscherApi.Services.Instagram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EventLauscherApi.Tests.Instagram;

public sealed class WorkerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnabledWorkersResolveScopedProcessorAndStopPromptly(bool fetch)
    {
        var called = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        var inbox = new Mock<IInstagramWebhookInboxProcessor>();
        var source = new Mock<IInstagramSourceFetchProcessor>();
        inbox.Setup(x => x.ProcessReceivedAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => { called.TrySetResult(ct); return Task.FromResult(1); });
        source.Setup(x => x.ProcessPendingFetchAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => { called.TrySetResult(ct); return Task.FromResult(1); });
        services.AddScoped(_ => inbox.Object);
        services.AddScoped(_ => source.Object);
        using var provider = services.BuildServiceProvider();
        using var worker = Create(fetch, provider, true);
        await worker.StartAsync(default);
        var processorToken = await called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(processorToken.CanBeCanceled);
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(processorToken.IsCancellationRequested);
        if (fetch) source.Verify(x => x.ProcessPendingFetchAsync(It.IsAny<CancellationToken>()), Times.Once);
        else inbox.Verify(x => x.ProcessReceivedAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FetchWorkerDoesNotResolveProcessorWhenDisabled()
    {
        // No processor registration: resolving it would fail the worker.
        using var provider = new ServiceCollection().BuildServiceProvider();
        var logger = new Mock<ILogger<InstagramSourceFetchWorker>>();
        using var worker = new InstagramSourceFetchWorker(provider.GetRequiredService<IServiceScopeFactory>(), Config(false), logger.Object);
        await worker.StartAsync(default);
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(logger.Invocations, i => i.Method.Name == "Log" && i.Arguments[0] is LogLevel.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkersLogProcessorErrorsAndKeepRunningUntilStopped(bool fetch)
    {
        var errorLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inbox = new Mock<IInstagramWebhookInboxProcessor>();
        var source = new Mock<IInstagramSourceFetchProcessor>();
        inbox.Setup(x => x.ProcessReceivedAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("processor failed"));
        source.Setup(x => x.ProcessPendingFetchAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("processor failed"));
        var services = new ServiceCollection();
        services.AddScoped(_ => inbox.Object);
        services.AddScoped(_ => source.Object);
        using var provider = services.BuildServiceProvider();
        using BackgroundService worker = fetch
            ? new InstagramSourceFetchWorker(provider.GetRequiredService<IServiceScopeFactory>(), Config(true), new SignalLogger<InstagramSourceFetchWorker>(errorLogged))
            : new InstagramWebhookInboxWorker(provider.GetRequiredService<IServiceScopeFactory>(), new SignalLogger<InstagramWebhookInboxWorker>(errorLogged));
        await worker.StartAsync(default);
        await errorLogged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(worker.ExecuteTask!.IsCompleted);
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static BackgroundService Create(bool fetch, ServiceProvider provider, bool enabled) => fetch
        ? new InstagramSourceFetchWorker(provider.GetRequiredService<IServiceScopeFactory>(), Config(enabled), NullLogger<InstagramSourceFetchWorker>.Instance)
        : new InstagramWebhookInboxWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<InstagramWebhookInboxWorker>.Instance);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopCancelsAnInFlightProcessorWithoutLoggingAnError(bool fetch)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> Process(CancellationToken ct)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 0;
        }
        var inbox = new Mock<IInstagramWebhookInboxProcessor>();
        var source = new Mock<IInstagramSourceFetchProcessor>();
        inbox.Setup(x => x.ProcessReceivedAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => Process(ct));
        source.Setup(x => x.ProcessPendingFetchAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => Process(ct));
        var services = new ServiceCollection();
        services.AddScoped(_ => inbox.Object);
        services.AddScoped(_ => source.Object);
        using var provider = services.BuildServiceProvider();
        var inboxLogger = new Mock<ILogger<InstagramWebhookInboxWorker>>();
        var sourceLogger = new Mock<ILogger<InstagramSourceFetchWorker>>();
        using BackgroundService worker = fetch
            ? new InstagramSourceFetchWorker(provider.GetRequiredService<IServiceScopeFactory>(), Config(true), sourceLogger.Object)
            : new InstagramWebhookInboxWorker(provider.GetRequiredService<IServiceScopeFactory>(), inboxLogger.Object);
        await worker.StartAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(inboxLogger.Invocations.Concat(sourceLogger.Invocations),
            i => i.Method.Name == "Log" && i.Arguments[0] is LogLevel.Error);
    }

    private static IConfiguration Config(bool enabled) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Instagram:EnableFetching"] = enabled.ToString() }).Build();

    private sealed class SignalLogger<T>(TaskCompletionSource signal) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            if (level == LogLevel.Error) signal.TrySetResult();
        }
    }
}
