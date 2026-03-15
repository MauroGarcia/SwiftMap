using System.Collections.Generic;

namespace SwiftMap.SourceGenerator.Models;

/// <summary>Represents one <c>public partial TDest Map(TSource source)</c> method.</summary>
internal sealed class MappingMethodModel
{
    /// <summary>Method name (usually "Map").</summary>
    public string MethodName { get; set; } = "";

    /// <summary>Parameter name (usually "source").</summary>
    public string ParamName { get; set; } = "";

    /// <summary>Fully-qualified source type (global:: prefix).</summary>
    public string SourceFqn { get; set; } = "";

    /// <summary>Fully-qualified destination type (global:: prefix).</summary>
    public string DestFqn { get; set; } = "";

    /// <summary>Whether the destination type uses a primary constructor (record / ctor-only).</summary>
    public bool DestIsRecord { get; set; }

    /// <summary>Properties (or constructor params) to map.</summary>
    public List<MappedPropertyModel> Properties { get; set; } = new();
}
