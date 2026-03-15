namespace SwiftMap.Internal;

/// <summary>
/// Provides context for nested mapping operations during compiled expression execution.
/// </summary>
public sealed class MapperContext(IMapper mapper)
{
    public T Map<T>(object source) => mapper.Map<T>(source);
}
