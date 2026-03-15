using System.Runtime.CompilerServices;

namespace SwiftMap;

/// <summary>
/// The main mapper. Lightweight, thread-safe, and backed by compiled expression trees.
/// All nested mappings are inlined at compile time — zero per-call overhead beyond the destination objects.
/// </summary>
public sealed class Mapper : IMapper
{
    private readonly MapperConfig _config;

    public Mapper(MapperConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Create a mapper with inline configuration.
    /// </summary>
    public static Mapper Create(Action<MapperConfig> configure)
    {
        var config = new MapperConfig();
        configure(config);
        config.Build();
        return new Mapper(config);
    }

    /// <summary>
    /// Map from a boxed source (runtime type resolution path).
    /// </summary>
    public TDestination Map<TDestination>(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var mapping = _config.GetOrCompileMapping(source.GetType(), typeof(TDestination));
        return (TDestination)mapping(source);
    }

    /// <summary>
    /// Map using compile-time type knowledge.
    /// AggressiveInlining eliminates the call frame overhead in tight mapping loops.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TDestination Map<TSource, TDestination>(TSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var mapping = _config.GetOrCompileMapping(typeof(TSource), typeof(TDestination));
        return (TDestination)mapping(source!);
    }

    /// <summary>
    /// Map into an existing destination instance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var mapping = _config.GetOrCompileMappingInto(typeof(TSource), typeof(TDestination));
        mapping(source, destination);
        return destination;
    }

    /// <summary>
    /// Map with fully runtime-resolved types.
    /// </summary>
    public object Map(object source, Type sourceType, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(source);
        var mapping = _config.GetOrCompileMapping(sourceType, destinationType);
        return mapping(source);
    }
}
