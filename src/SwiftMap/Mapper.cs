using SwiftMap.Internal;

namespace SwiftMap;

/// <summary>
/// The main mapper. Lightweight, thread-safe, and backed by compiled expression trees.
/// </summary>
public sealed class Mapper : IMapper
{
    private readonly MapperConfig _config;
    private readonly MapperContext _context;

    public Mapper(MapperConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _context = new MapperContext(this);
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

    public TDestination Map<TDestination>(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var mapping = _config.GetOrCompileMapping(source.GetType(), typeof(TDestination), _context);
        return (TDestination)mapping(source, _context);
    }

    public TDestination Map<TSource, TDestination>(TSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var mapping = _config.GetOrCompileMapping(typeof(TSource), typeof(TDestination), _context);
        return (TDestination)mapping(source, _context);
    }

    public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var mapping = _config.GetOrCompileMappingInto(typeof(TSource), typeof(TDestination), _context);
        mapping(source, destination, _context);
        return destination;
    }

    public object Map(object source, Type sourceType, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(source);
        var mapping = _config.GetOrCompileMapping(sourceType, destinationType, _context);
        return mapping(source, _context);
    }
}
