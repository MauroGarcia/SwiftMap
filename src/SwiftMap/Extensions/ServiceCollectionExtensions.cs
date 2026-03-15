using Microsoft.Extensions.DependencyInjection;

namespace SwiftMap;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add SwiftMap to the DI container with inline configuration.
    /// </summary>
    public static IServiceCollection AddSwiftMap(
        this IServiceCollection services,
        Action<MapperConfig> configure)
    {
        var config = new MapperConfig();
        configure(config);
        config.Build();

        services.AddSingleton(config);
        services.AddSingleton<IMapper, Mapper>();
        return services;
    }

    /// <summary>
    /// Add SwiftMap scanning for profiles in the specified assemblies.
    /// </summary>
    public static IServiceCollection AddSwiftMap(
        this IServiceCollection services,
        params System.Reflection.Assembly[] assemblies)
    {
        var config = new MapperConfig();
        foreach (var assembly in assemblies)
        {
            config.AddProfilesFromAssembly(assembly);
            config.AddMapsFromAssembly(assembly);
        }
        config.Build();

        services.AddSingleton(config);
        services.AddSingleton<IMapper, Mapper>();
        return services;
    }
}
