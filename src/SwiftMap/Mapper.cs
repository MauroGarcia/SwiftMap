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
    /// Lazily map an async stream without materializing the full result set.
    /// The compiled mapping delegate is resolved once and reused for the entire enumeration.
    /// </summary>
    public IAsyncEnumerable<TDestination> MapAsync<TSource, TDestination>(
        IAsyncEnumerable<TSource> source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var mapping = _config.GetOrCompileMapping(typeof(TSource), typeof(TDestination));
        return MapAsyncIterator<TSource, TDestination>(source, mapping, cancellationToken);
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

    /// <summary>
    /// Apply non-null fields from <paramref name="source"/> onto <paramref name="destination"/>.
    /// Fields that are null (reference types) or have no value (Nullable&lt;T&gt;) in the source
    /// are silently ignored, preserving the existing value on the destination.
    /// Non-nullable value types are always assigned.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Patch<TSource, TDest>(TSource source, TDest destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var patchDelegate = _config.GetOrCompilePatch(typeof(TSource), typeof(TDest));
        patchDelegate(source!, destination!);
    }

    /// <summary>
    /// Apply non-null fields using an inline configuration override.
    /// The <paramref name="configure"/> action is applied on top of any existing registration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Patch<TSource, TDest>(TSource source, TDest destination, Action<TypeMapConfig<TSource, TDest>> configure)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var config = new TypeMapConfig<TSource, TDest>();
        configure(config);
        var patchDelegate = _config.GetOrCompilePatch(typeof(TSource), typeof(TDest), config);
        patchDelegate(source!, destination!);
    }

    private static async IAsyncEnumerable<TDestination> MapAsyncIterator<TSource, TDestination>(
        IAsyncEnumerable<TSource> source,
        Func<object, object> mapping,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return (TDestination)mapping(item!);
        }
    }
}
