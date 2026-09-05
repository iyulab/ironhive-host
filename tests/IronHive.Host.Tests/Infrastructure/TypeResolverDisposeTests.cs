using AwesomeAssertions;
using IronHive.Cli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// Regression coverage for TypeResolver.Dispose() against containers holding a service that
/// implements only IAsyncDisposable (e.g. McpPluginManager) — the exact shape that previously made
/// the CLI print an InvalidOperationException on every command's exit.
/// </summary>
public class TypeResolverDisposeTests
{
    private sealed class AsyncOnlyDisposable : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenContainerHasAsyncOnlyDisposableService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AsyncOnlyDisposable>();
        var provider = services.BuildServiceProvider();
        var instance = provider.GetRequiredService<AsyncOnlyDisposable>();

        var resolver = new TypeResolver(provider);

        var act = () => resolver.Dispose();

        act.Should().NotThrow();
        instance.Disposed.Should().BeTrue();
    }

    [Fact]
    public void Dispose_DisposesContainer_WhenAllServicesAreSyncDisposable()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var resolver = new TypeResolver(provider);

        var act = () => resolver.Dispose();

        act.Should().NotThrow();
    }
}
