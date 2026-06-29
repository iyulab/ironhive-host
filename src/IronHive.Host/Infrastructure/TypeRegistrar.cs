using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace IronHive.Host.Infrastructure;

/// <summary>
/// Type registrar for Spectre.Console.Cli with Microsoft.Extensions.DependencyInjection.
/// </summary>
public sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _services;

    public TypeRegistrar(IServiceCollection services)
    {
        _services = services;
    }

    public void Register(Type service, Type implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        _services.AddSingleton(service, _ => factory());
    }

    public ITypeResolver Build()
    {
        var provider = _services.BuildServiceProvider();

        // Validate critical services early to provide better error messages
        ValidateCriticalServices(provider);

        return new TypeResolver(provider);

        static void ValidateCriticalServices(ServiceProvider provider)
        {
            // Note: IChatClientProvider is not validated here because:
            // 1. It may not be configured (no .env file)
            // 2. User can use --provider local/lmsupply to use local inference
            // 3. IChatClientFactory handles fallback to lmsupply automatically
            var criticalServices = new (string Name, Type Type)[]
            {
                ("IAgentLoopFactory", typeof(IronHive.Agent.Loop.IAgentLoopFactory)),
            };

            foreach (var (name, type) in criticalServices)
            {
                try
                {
                    var service = provider.GetService(type);
                    if (service is null)
                    {
                        throw new InvalidOperationException($"Service '{name}' is not registered.");
                    }
                }
                catch (InvalidOperationException ex)
                {
                    // Re-throw with service context but avoid duplicating the inner message
                    throw new InvalidOperationException(
                        $"Configuration Error: Failed to initialize {name}.\n\n" +
                        $"{ex.Message}",
                        ex);
                }
            }
        }
    }
}

/// <summary>
/// Type resolver for Spectre.Console.Cli.
/// </summary>
public sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider;

    public TypeResolver(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public object? Resolve(Type? type)
    {
        if (type is null)
        {
            return null;
        }

        try
        {
            return _provider.GetService(type);
        }
        catch (InvalidOperationException ex)
        {
            // Re-throw with more context for better error messages
            // This helps surface configuration errors (e.g., missing .env file)
            throw new InvalidOperationException(
                $"Failed to resolve service '{type.Name}'. " +
                $"This may indicate a missing configuration. " +
                $"Ensure .env file exists in the current or parent directory. " +
                $"Inner error: {ex.Message}",
                ex);
        }
    }

    public void Dispose()
    {
        if (_provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
