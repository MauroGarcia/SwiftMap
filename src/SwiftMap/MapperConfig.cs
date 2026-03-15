using System.Collections.Concurrent;
using System.Reflection;
using SwiftMap.Internal;

namespace SwiftMap;

/// <summary>
/// Holds all mapping configurations and compiled delegates.
/// Thread-safe and immutable after Build().
/// Uses TryGetValue fast-path + static lambda pattern (MessagePack/NServiceBus)
/// for zero-allocation delegate lookups on the hot path.
/// </summary>
public sealed class MapperConfig
{
    private readonly Dictionary<TypePair, TypeMapConfig> _typeMapConfigs = [];
    private readonly ConcurrentDictionary<TypePair, Func<object, object>> _compiledMaps = [];
    private readonly ConcurrentDictionary<TypePair, Action<object, object>> _compiledMapsInto = [];
    private readonly MappingCompiler.ConfigResolver _configResolver;
    private bool _isBuilt;

    public MapperConfig()
    {
        _configResolver = ResolveConfig;
    }

    /// <summary>
    /// Add a profile containing mapping configurations.
    /// </summary>
    public MapperConfig AddProfile<TProfile>() where TProfile : MapProfile, new()
        => AddProfile(new TProfile());

    /// <summary>
    /// Add a profile instance.
    /// </summary>
    public MapperConfig AddProfile(MapProfile profile)
    {
        ThrowIfBuilt();
        foreach (var map in profile.TypeMaps)
        {
            var pair = new TypePair(map.SourceType, map.DestinationType);
            _typeMapConfigs[pair] = map;

            if (map.ReverseMapEnabled)
            {
                var reversePair = new TypePair(map.DestinationType, map.SourceType);
                if (!_typeMapConfigs.ContainsKey(reversePair))
                    _typeMapConfigs[reversePair] = CreateReverseConfig(map);
            }
        }
        return this;
    }

    /// <summary>
    /// Add a single mapping using fluent configuration.
    /// </summary>
    public MapperConfig CreateMap<TSource, TDestination>(
        Action<TypeMapConfig<TSource, TDestination>>? configure = null)
    {
        ThrowIfBuilt();
        var config = new TypeMapConfig<TSource, TDestination>();
        configure?.Invoke(config);
        var pair = new TypePair(typeof(TSource), typeof(TDestination));
        _typeMapConfigs[pair] = config;

        if (config.ReverseMapEnabled)
        {
            var reversePair = new TypePair(typeof(TDestination), typeof(TSource));
            if (!_typeMapConfigs.ContainsKey(reversePair))
                _typeMapConfigs[reversePair] = CreateReverseConfig(config);
        }

        return this;
    }

    /// <summary>
    /// Scan assemblies for types decorated with [MapTo] or [MapFrom] attributes.
    /// </summary>
    public MapperConfig AddMapsFromAssembly(Assembly assembly)
    {
        ThrowIfBuilt();
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var attr in type.GetCustomAttributes<MapToAttribute>())
            {
                var pair = new TypePair(type, attr.DestinationType);
                if (!_typeMapConfigs.ContainsKey(pair))
                    _typeMapConfigs[pair] = CreateDefaultConfig(type, attr.DestinationType);
            }

            foreach (var attr in type.GetCustomAttributes<MapFromAttribute>())
            {
                var pair = new TypePair(attr.SourceType, type);
                if (!_typeMapConfigs.ContainsKey(pair))
                    _typeMapConfigs[pair] = CreateDefaultConfig(attr.SourceType, type);
            }
        }
        return this;
    }

    /// <summary>
    /// Scan assemblies for all MapProfile subclasses and register them.
    /// </summary>
    public MapperConfig AddProfilesFromAssembly(Assembly assembly)
    {
        ThrowIfBuilt();
        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.IsAbstract || !type.IsSubclassOf(typeof(MapProfile))) continue;
            var profile = (MapProfile)Activator.CreateInstance(type)!;
            AddProfile(profile);
        }
        return this;
    }

    /// <summary>
    /// Freeze configuration and pre-compile all mappings.
    /// </summary>
    public MapperConfig Build()
    {
        _isBuilt = true;
        return this;
    }

    /// <summary>
    /// Zero-allocation on cache hit: TryGetValue fast-path avoids closure/delegate creation.
    /// On cache miss: static lambda + TArg overload (ConcurrentDictionary pattern from .NET 9).
    /// </summary>
    internal Func<object, object> GetOrCompileMapping(Type sourceType, Type destType)
    {
        var pair = new TypePair(sourceType, destType);

        // Fast path — zero allocation (no closure, no delegate, no display class)
        if (_compiledMaps.TryGetValue(pair, out var cached))
            return cached;

        // Slow path — static lambda avoids closure via TArg overload
        return _compiledMaps.GetOrAdd(pair,
            static (p, resolver) => MappingCompiler.CompileMapping(p.Source, p.Destination, resolver),
            _configResolver);
    }

    /// <summary>
    /// Zero-allocation cache lookup for map-into operations.
    /// </summary>
    internal Action<object, object> GetOrCompileMappingInto(Type sourceType, Type destType)
    {
        var pair = new TypePair(sourceType, destType);

        if (_compiledMapsInto.TryGetValue(pair, out var cached))
            return cached;

        return _compiledMapsInto.GetOrAdd(pair,
            static (p, resolver) => MappingCompiler.CompileMappingInto(p.Source, p.Destination, resolver),
            _configResolver);
    }

    internal bool HasMapping(Type sourceType, Type destType)
        => _typeMapConfigs.ContainsKey(new TypePair(sourceType, destType));

    private TypeMapConfig? ResolveConfig(Type sourceType, Type destType)
    {
        _typeMapConfigs.TryGetValue(new TypePair(sourceType, destType), out var config);
        return config;
    }

    private static TypeMapConfig CreateDefaultConfig(Type source, Type dest)
    {
        var configType = typeof(TypeMapConfig<,>).MakeGenericType(source, dest);
        return (TypeMapConfig)Activator.CreateInstance(configType)!;
    }

    private static TypeMapConfig CreateReverseConfig(TypeMapConfig original)
        => CreateDefaultConfig(original.DestinationType, original.SourceType);

    private void ThrowIfBuilt()
    {
        if (_isBuilt)
            throw new InvalidOperationException("Cannot modify MapperConfig after Build() has been called.");
    }
}
