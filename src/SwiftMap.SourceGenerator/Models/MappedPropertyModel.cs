namespace SwiftMap.SourceGenerator.Models;

/// <summary>How the destination property value is produced.</summary>
internal enum MappingKind
{
    /// <summary>Direct assignment: compatible types, no conversion needed.</summary>
    Direct,
    /// <summary>Nullable unwrap: <c>T?</c> → <c>T</c> (uses <c>.Value</c> or cast).</summary>
    NullableUnwrap,
    /// <summary>Enum → enum (same underlying type), enum → string, or string → enum.</summary>
    Enum,
    /// <summary>Nested object — recurse into a new object initializer.</summary>
    Nested,
    /// <summary>Collection (IEnumerable / List / array) of any element kind.</summary>
    Collection,
}

/// <summary>Describes a single destination property and how to populate it.</summary>
internal sealed class MappedPropertyModel
{
    /// <summary>Destination property name (exact CLR casing).</summary>
    public string DestName { get; set; } = "";

    /// <summary>Source accessor expression, e.g. <c>source.FirstName</c>.</summary>
    public string SourceExpression { get; set; } = "";

    /// <summary>Fully-qualified destination property type (global:: prefix).</summary>
    public string DestTypeFqn { get; set; } = "";

    /// <summary>Fully-qualified source property type.</summary>
    public string SourceTypeFqn { get; set; } = "";

    public MappingKind Kind { get; set; }

    // ── Nested / Collection extras ──────────────────────────────────────────

    /// <summary>Nested source type symbol FQN (used for Nested/Collection).</summary>
    public string? ElementSourceFqn { get; set; }

    /// <summary>Nested destination type symbol FQN.</summary>
    public string? ElementDestFqn { get; set; }

    /// <summary>Properties of the nested destination type (for Nested / Collection element).</summary>
    public System.Collections.Generic.List<MappedPropertyModel>? NestedProperties { get; set; }

    /// <summary>Whether the nested destination uses a primary constructor (record).</summary>
    public bool NestedIsRecord { get; set; }

    // ── Enum extras ─────────────────────────────────────────────────────────

    public bool EnumToString { get; set; }
    public bool StringToEnum { get; set; }
    /// <summary>When true the destination enum type differs from source — requires an int cast.</summary>
    public bool EnumDifferentType { get; set; }

    // ── Nullable ─────────────────────────────────────────────────────────────

    /// <summary>True if the destination property type is nullable.</summary>
    public bool DestIsNullable { get; set; }
    /// <summary>True if the source property type is nullable.</summary>
    public bool SourceIsNullable { get; set; }

    // ── Record constructor param ──────────────────────────────────────────────

    /// <summary>
    /// For record constructors: the actual constructor parameter name
    /// (may differ from <see cref="DestName"/> in casing).
    /// Null for regular classes.
    /// </summary>
    public string? CtorParamName { get; set; }
}
