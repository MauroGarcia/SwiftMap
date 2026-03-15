namespace SwiftMap;

/// <summary>
/// Base class for organizing mapping configurations.
/// </summary>
public abstract class MapProfile
{
    internal List<TypeMapConfig> TypeMaps { get; } = [];

    /// <summary>
    /// Creates a mapping configuration from TSource to TDestination.
    /// </summary>
    protected TypeMapConfig<TSource, TDestination> CreateMap<TSource, TDestination>()
    {
        var config = new TypeMapConfig<TSource, TDestination>();
        TypeMaps.Add(config);
        return config;
    }
}
