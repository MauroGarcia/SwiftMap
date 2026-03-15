namespace SwiftMap;

/// <summary>
/// Maps objects from one type to another using compiled conventions.
/// </summary>
public interface IMapper
{
    TDestination Map<TDestination>(object source);
    TDestination Map<TSource, TDestination>(TSource source);
    TDestination Map<TSource, TDestination>(TSource source, TDestination destination);
    object Map(object source, Type sourceType, Type destinationType);

    /// <summary>
    /// Apply non-null fields from <paramref name="source"/> onto <paramref name="destination"/>.
    /// Fields that are null in <paramref name="source"/> are silently ignored, preserving the
    /// existing value on <paramref name="destination"/>.
    /// </summary>
    void Patch<TSource, TDest>(TSource source, TDest destination);

    /// <summary>
    /// Apply non-null fields from <paramref name="source"/> onto <paramref name="destination"/>
    /// using an inline configuration (e.g. custom behavior overrides).
    /// </summary>
    void Patch<TSource, TDest>(TSource source, TDest destination, Action<TypeMapConfig<TSource, TDest>> configure);
}
