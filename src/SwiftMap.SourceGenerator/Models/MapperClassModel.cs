using System.Collections.Generic;

namespace SwiftMap.SourceGenerator.Models;

/// <summary>Represents a <c>[Mapper]</c> partial class and all its mapping methods.</summary>
internal sealed class MapperClassModel
{
    /// <summary>Simple class name (without namespace), e.g. <c>AppMapper</c>.</summary>
    public string ClassName { get; set; } = "";

    /// <summary>Namespace, or empty string if in global namespace.</summary>
    public string Namespace { get; set; } = "";

    /// <summary>All partial mapping methods declared in this class.</summary>
    public List<MappingMethodModel> Methods { get; set; } = new();
}
