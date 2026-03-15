namespace SwiftMap;

/// <summary>
/// Marks a partial class as a source-generated mapper (Mapperly-style).
/// Decorate a <c>partial class</c> with this attribute and declare
/// <c>public partial TDest Map(TSource source)</c> methods — the generator
/// will emit zero-overhead, compile-time assignment bodies for each method.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MapperAttribute : Attribute { }

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
