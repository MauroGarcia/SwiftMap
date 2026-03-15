using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SwiftMap.SourceGenerator.Models;

namespace SwiftMap.SourceGenerator.Pipeline;

internal static class MapperModelExtractor
{
    private const string IgnoreMapFqn = "SwiftMap.IgnoreMapAttribute";
    private const string MapPropertyFqn = "SwiftMap.MapPropertyAttribute";

    public static MapperClassModel? Extract(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
            return null;

        ct.ThrowIfCancellationRequested();

        var model = new MapperClassModel
        {
            ClassName = classSymbol.Name,
            Namespace = classSymbol.ContainingNamespace?.IsGlobalNamespace == true
                ? ""
                : classSymbol.ContainingNamespace?.ToDisplayString() ?? ""
        };

        foreach (var member in classSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (member is not IMethodSymbol method) continue;
            if (!method.IsPartialDefinition) continue;
            if (method.ReturnsVoid) continue;
            if (method.Parameters.Length != 1) continue;

            var src = method.Parameters[0].Type as INamedTypeSymbol;
            var dst = method.ReturnType as INamedTypeSymbol;
            if (src == null || dst == null) continue;

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var methodModel = BuildMethodModel(method, src, dst, visited, ct);
            if (methodModel != null)
                model.Methods.Add(methodModel);
        }

        return model.Methods.Count > 0 ? model : null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Internal helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static MappingMethodModel? BuildMethodModel(
        IMethodSymbol method,
        INamedTypeSymbol src,
        INamedTypeSymbol dst,
        HashSet<string> visited,
        CancellationToken ct)
    {
        var key = $"{src.ToDisplayString()}→{dst.ToDisplayString()}";
        if (!visited.Add(key))
            return null; // circular reference guard

        var dstFqn = ToFqn(dst);
        var srcFqn = ToFqn(src);

        bool isRecord = IsRecord(dst);

        var methodModel = new MappingMethodModel
        {
            MethodName = method.Name,
            ParamName = method.Parameters[0].Name,
            SourceFqn = srcFqn,
            DestFqn = dstFqn,
            DestIsRecord = isRecord,
        };

        methodModel.Properties = BuildProperties(src, dst, method.Parameters[0].Name, visited, ct);

        visited.Remove(key);
        return methodModel;
    }

    private static List<MappedPropertyModel> BuildProperties(
        INamedTypeSymbol src,
        INamedTypeSymbol dst,
        string paramName,
        HashSet<string> visited,
        CancellationToken ct)
    {
        var result = new List<MappedPropertyModel>();

        // Collect source properties as a lookup (case-insensitive)
        var srcProps = new Dictionary<string, IPropertySymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in src.GetMembers())
            if (m is IPropertySymbol p && p.GetMethod != null && !p.IsIndexer)
                srcProps[p.Name] = p;

        // Determine destination properties (or constructor params for records)
        foreach (var (dstProp, ctorParamName) in GetWritableDestinationProperties(dst))
        {
            ct.ThrowIfCancellationRequested();

            // [IgnoreMap] on destination
            if (HasAttribute(dstProp, IgnoreMapFqn))
                continue;

            // Determine source member name (may be overridden by [MapProperty])
            string srcName = GetMapPropertyOverride(dstProp) ?? dstProp.Name;

            if (!srcProps.TryGetValue(srcName, out var srcProp))
                continue; // no matching source property — skip (SW0002 diagnostic would go here)

            // [IgnoreMap] on source
            if (HasAttribute(srcProp, IgnoreMapFqn))
                continue;

            var prop = BuildPropertyModel(
                dstProp, srcProp, paramName, visited, ct);
            prop.CtorParamName = ctorParamName;

            result.Add(prop);
        }

        return result;
    }

    private static MappedPropertyModel BuildPropertyModel(
        IPropertySymbol dstProp,
        IPropertySymbol srcProp,
        string paramName,
        HashSet<string> visited,
        CancellationToken ct)
    {
        var dstType = dstProp.Type;
        var srcType = srcProp.Type;

        var srcExpr = $"{paramName}.{srcProp.Name}";

        var pm = new MappedPropertyModel
        {
            DestName = dstProp.Name,
            SourceExpression = srcExpr,
            DestTypeFqn = ToFqnType(dstType),
            SourceTypeFqn = ToFqnType(srcType),
            DestIsNullable = dstType.NullableAnnotation == NullableAnnotation.Annotated,
            SourceIsNullable = srcType.NullableAnnotation == NullableAnnotation.Annotated,
        };

        // Unwrap nullable for classification
        var dstCore = UnwrapNullable(dstType);
        var srcCore = UnwrapNullable(srcType);

        pm.Kind = Classify(srcCore, dstCore, visited, ct, out var nested);

        if (pm.Kind == MappingKind.Nested && nested != null)
        {
            pm.ElementSourceFqn = ToFqn((INamedTypeSymbol)srcCore);
            pm.ElementDestFqn = ToFqn((INamedTypeSymbol)dstCore);
            pm.NestedProperties = nested;
            pm.NestedIsRecord = IsRecord((INamedTypeSymbol)dstCore);
        }
        else if (pm.Kind == MappingKind.Collection)
        {
            FillCollectionInfo(pm, srcCore, dstCore, paramName, visited, ct);
        }
        else if (pm.Kind == MappingKind.Enum)
        {
            pm.EnumToString = srcCore.TypeKind == TypeKind.Enum
                              && dstCore.SpecialType == SpecialType.System_String;
            pm.StringToEnum = srcCore.SpecialType == SpecialType.System_String
                              && dstCore.TypeKind == TypeKind.Enum;
            pm.EnumDifferentType = srcCore.TypeKind == TypeKind.Enum
                                   && dstCore.TypeKind == TypeKind.Enum
                                   && !SymbolEqualityComparer.Default.Equals(srcCore, dstCore);
        }

        if (srcType.NullableAnnotation == NullableAnnotation.Annotated
            && dstType.NullableAnnotation != NullableAnnotation.Annotated
            && pm.Kind == MappingKind.Direct)
        {
            pm.Kind = MappingKind.NullableUnwrap;
        }

        return pm;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static MappingKind Classify(
        ITypeSymbol src,
        ITypeSymbol dst,
        HashSet<string> visited,
        CancellationToken ct,
        out List<MappedPropertyModel>? nested)
    {
        nested = null;

        // Enum cases
        if (src.TypeKind == TypeKind.Enum && dst.TypeKind == TypeKind.Enum)
            return MappingKind.Enum;
        if (src.TypeKind == TypeKind.Enum && dst.SpecialType == SpecialType.System_String)
            return MappingKind.Enum;
        if (src.SpecialType == SpecialType.System_String && dst.TypeKind == TypeKind.Enum)
            return MappingKind.Enum;

        // Collection (exclude primitives like string which implement IEnumerable<char>)
        if (!IsPrimitive(src) && !IsPrimitive(dst) && IsCollection(src) && IsCollection(dst))
            return MappingKind.Collection;

        // Nested object
        if (src.TypeKind == TypeKind.Class || src.TypeKind == TypeKind.Struct)
        {
            // Exclude primitives / strings / well-known value types
            if (!IsPrimitive(src) && !IsPrimitive(dst)
                && src is INamedTypeSymbol srcNamed
                && dst is INamedTypeSymbol dstNamed)
            {
                var key = $"{srcNamed.ToDisplayString()}→{dstNamed.ToDisplayString()}";
                if (!visited.Contains(key))
                {
                    visited.Add(key);
                    nested = BuildProperties(srcNamed, dstNamed, "__src", visited, ct);
                    visited.Remove(key);
                    if (nested.Count > 0)
                        return MappingKind.Nested;
                    nested = null;
                }
            }
        }

        return MappingKind.Direct;
    }

    private static void FillCollectionInfo(
        MappedPropertyModel pm,
        ITypeSymbol srcCore,
        ITypeSymbol dstCore,
        string paramName,
        HashSet<string> visited,
        CancellationToken ct)
    {
        var srcElem = GetElementType(srcCore);
        var dstElem = GetElementType(dstCore);
        if (srcElem == null || dstElem == null) return;

        pm.ElementSourceFqn = ToFqnType(srcElem);
        pm.ElementDestFqn = ToFqnType(dstElem);

        // If element types are objects, build nested properties
        var srcElemNamed = UnwrapNullable(srcElem) as INamedTypeSymbol;
        var dstElemNamed = UnwrapNullable(dstElem) as INamedTypeSymbol;
        if (srcElemNamed != null && dstElemNamed != null
            && !IsPrimitive(srcElemNamed) && !IsPrimitive(dstElemNamed))
        {
            var key = $"{srcElemNamed.ToDisplayString()}→{dstElemNamed.ToDisplayString()}";
            if (!visited.Contains(key))
            {
                visited.Add(key);
                pm.NestedProperties = BuildProperties(srcElemNamed, dstElemNamed, "__item", visited, ct);
                pm.NestedIsRecord = IsRecord(dstElemNamed);
                pm.ElementDestFqn = ToFqn(dstElemNamed);
                pm.ElementSourceFqn = ToFqn(srcElemNamed);
                visited.Remove(key);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Symbol helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns writable destination properties paired with the constructor parameter name
    /// (non-null only for records, where the ctor param name may differ in casing from the property name).
    /// </summary>
    private static IEnumerable<(IPropertySymbol Prop, string? CtorParamName)> GetWritableDestinationProperties(
        INamedTypeSymbol dst)
    {
        // For records, use the primary constructor parameters in order
        if (IsRecord(dst))
        {
            var ctor = dst.Constructors
                .OrderByDescending(c => c.Parameters.Length)
                .FirstOrDefault();
            if (ctor != null)
            {
                // Return property symbols matching constructor params, keeping the param name
                var propMap = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
                foreach (var m in dst.GetMembers())
                    if (m is IPropertySymbol p) propMap[p.Name] = p;

                foreach (var param in ctor.Parameters)
                {
                    // Find the matching property (same name, case-insensitive)
                    var found = propMap.TryGetValue(param.Name, out var pp) ? pp
                        : propMap.Values.FirstOrDefault(x =>
                            string.Equals(x.Name, param.Name, StringComparison.OrdinalIgnoreCase));
                    if (found != null)
                        yield return (found, param.Name); // preserve actual ctor param name
                }
                yield break;
            }
        }

        // Regular class: writable properties (setter or init-only)
        foreach (var m in dst.GetMembers())
        {
            if (m is IPropertySymbol p
                && !p.IsIndexer
                && !p.IsReadOnly
                && (p.SetMethod != null && p.SetMethod.DeclaredAccessibility == Accessibility.Public))
                yield return (p, null);
        }
    }

    private static bool IsRecord(INamedTypeSymbol type)
        => type.IsRecord;

    private static bool HasAttribute(ISymbol symbol, string fqn)
    {
        foreach (var attr in symbol.GetAttributes())
            if (attr.AttributeClass?.ToDisplayString() == fqn)
                return true;
        return false;
    }

    private static string? GetMapPropertyOverride(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == MapPropertyFqn)
            {
                if (attr.ConstructorArguments.Length > 0
                    && attr.ConstructorArguments[0].Value is string name)
                    return name;
            }
        }
        return null;
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && named.TypeArguments.Length == 1)
            return named.TypeArguments[0];
        return type;
    }

    private static bool IsPrimitive(ITypeSymbol type)
    {
        return type.SpecialType switch
        {
            SpecialType.System_Boolean => true,
            SpecialType.System_Byte => true,
            SpecialType.System_SByte => true,
            SpecialType.System_Int16 => true,
            SpecialType.System_UInt16 => true,
            SpecialType.System_Int32 => true,
            SpecialType.System_UInt32 => true,
            SpecialType.System_Int64 => true,
            SpecialType.System_UInt64 => true,
            SpecialType.System_Single => true,
            SpecialType.System_Double => true,
            SpecialType.System_Decimal => true,
            SpecialType.System_Char => true,
            SpecialType.System_String => true,
            SpecialType.System_Object => true,
            SpecialType.System_DateTime => true,
            _ => false,
        };
    }

    private static bool IsCollection(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol) return true;
        if (type is INamedTypeSymbol named)
        {
            foreach (var iface in named.AllInterfaces)
                if (iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                    return true;
        }
        return false;
    }

    private static ITypeSymbol? GetElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arr) return arr.ElementType;
        if (type is INamedTypeSymbol named)
            foreach (var iface in named.AllInterfaces)
                if (iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                    && iface.TypeArguments.Length == 1)
                    return iface.TypeArguments[0];
        return null;
    }

    /// <summary>global::Namespace.TypeName (no generic arity for now).</summary>
    private static string ToFqn(INamedTypeSymbol type)
        => "global::" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                             .TrimStart("global::".ToCharArray());

    private static string ToFqnType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named) return ToFqn(named);
        if (type is IArrayTypeSymbol arr) return ToFqnType(arr.ElementType) + "[]";
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
