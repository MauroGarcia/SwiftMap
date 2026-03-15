namespace SwiftMap;

/// <summary>
/// Declares that this type can be mapped to the specified destination type.
/// Convention-based matching is applied automatically.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class MapToAttribute(Type destinationType) : Attribute
{
    public Type DestinationType { get; } = destinationType;
}

/// <summary>
/// Declares that this type can be mapped from the specified source type.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class MapFromAttribute(Type sourceType) : Attribute
{
    public Type SourceType { get; } = sourceType;
}

/// <summary>
/// Ignores a property during mapping.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class IgnoreMapAttribute : Attribute;

/// <summary>
/// Maps a destination property from a specific source property by name.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MapPropertyAttribute(string sourcePropertyName) : Attribute
{
    public string SourcePropertyName { get; } = sourcePropertyName;
}
