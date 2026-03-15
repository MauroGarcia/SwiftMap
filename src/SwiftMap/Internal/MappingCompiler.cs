using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace SwiftMap.Internal;

/// <summary>
/// Compiles strongly-typed mapping delegates from expression trees.
/// Each delegate is compiled once and cached — zero reflection at mapping time.
/// </summary>
internal static class MappingCompiler
{
    /// <summary>
    /// Builds a compiled Func&lt;object, object&gt; that maps source to a new destination instance.
    /// </summary>
    internal static Func<object, MapperContext, object> CompileMapping(
        Type sourceType,
        Type destType,
        TypeMapConfig? config,
        MapperContext context)
    {
        var sourceParam = Expression.Parameter(typeof(object), "src");
        var contextParam = Expression.Parameter(typeof(MapperContext), "ctx");
        var typedSource = Expression.Variable(sourceType, "typedSrc");

        var body = new List<Expression>
        {
            Expression.Assign(typedSource, Expression.Convert(sourceParam, sourceType))
        };

        var destExpr = BuildDestinationExpression(sourceType, destType, typedSource, config, contextParam);
        var resultVar = Expression.Variable(destType, "result");
        body.Add(Expression.Assign(resultVar, destExpr));

        // Property assignments for settable properties
        var assignments = BuildPropertyAssignments(sourceType, destType, typedSource, resultVar, config, contextParam);
        body.AddRange(assignments);

        // AfterMap hook
        if (config?.AfterMapAction != null)
        {
            var afterMapField = Expression.Constant(config.AfterMapAction);
            body.Add(Expression.Invoke(afterMapField,
                Expression.Convert(typedSource, typeof(object)),
                Expression.Convert(resultVar, typeof(object))));
        }

        body.Add(Expression.Convert(resultVar, typeof(object)));

        var block = Expression.Block(
            [typedSource, resultVar],
            body);

        var lambda = Expression.Lambda<Func<object, MapperContext, object>>(block, sourceParam, contextParam);
        return lambda.Compile();
    }

    /// <summary>
    /// Builds a compiled action for mapping into an existing destination instance.
    /// </summary>
    internal static Action<object, object, MapperContext> CompileMappingInto(
        Type sourceType,
        Type destType,
        TypeMapConfig? config,
        MapperContext context)
    {
        var sourceParam = Expression.Parameter(typeof(object), "src");
        var destParam = Expression.Parameter(typeof(object), "dest");
        var contextParam = Expression.Parameter(typeof(MapperContext), "ctx");
        var typedSource = Expression.Variable(sourceType, "typedSrc");
        var typedDest = Expression.Variable(destType, "typedDest");

        var body = new List<Expression>
        {
            Expression.Assign(typedSource, Expression.Convert(sourceParam, sourceType)),
            Expression.Assign(typedDest, Expression.Convert(destParam, destType))
        };

        var assignments = BuildPropertyAssignments(sourceType, destType, typedSource, typedDest, config, contextParam);
        body.AddRange(assignments);

        if (!body.Any(e => e.NodeType == ExpressionType.Assign && e != body[0] && e != body[1]))
            body.Add(Expression.Empty());

        var block = Expression.Block([typedSource, typedDest], body);
        var lambda = Expression.Lambda<Action<object, object, MapperContext>>(block, sourceParam, destParam, contextParam);
        return lambda.Compile();
    }

    private static Expression BuildDestinationExpression(
        Type sourceType,
        Type destType,
        Expression typedSource,
        TypeMapConfig? config,
        Expression contextParam)
    {
        // Custom constructor
        if (config?.CustomConstructor is LambdaExpression ctor)
        {
            return new ParameterReplacer(ctor.Parameters[0], typedSource).Visit(ctor.Body);
        }

        // Record / constructor with parameters — match by name
        var constructors = destType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var bestCtor = constructors
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault(c => c.GetParameters().Length > 0 && CanMapAllParameters(sourceType, c, config));

        if (bestCtor != null)
        {
            var ctorParams = bestCtor.GetParameters();
            var args = new Expression[ctorParams.Length];

            for (int i = 0; i < ctorParams.Length; i++)
            {
                var param = ctorParams[i];
                var sourceProp = FindSourceProperty(sourceType, param.Name!, config);

                if (sourceProp != null)
                {
                    Expression value = BuildPropertyAccess(typedSource, sourceProp, config);
                    args[i] = ConvertIfNeeded(value, param.ParameterType, contextParam);
                }
                else if (param.HasDefaultValue)
                {
                    args[i] = Expression.Constant(param.DefaultValue, param.ParameterType);
                }
                else
                {
                    args[i] = Expression.Default(param.ParameterType);
                }
            }

            return Expression.New(bestCtor, args);
        }

        // Parameterless constructor
        var defaultCtor = destType.GetConstructor(Type.EmptyTypes);
        if (defaultCtor != null)
            return Expression.New(defaultCtor);

        // Struct with no explicit constructor
        if (destType.IsValueType)
            return Expression.New(destType);

        throw new InvalidOperationException(
            $"SwiftMap: No suitable constructor found for '{destType.Name}'. " +
            $"Provide a parameterless constructor, a matching record constructor, or use ConstructUsing().");
    }

    private static bool CanMapAllParameters(Type sourceType, ConstructorInfo ctor, TypeMapConfig? config)
    {
        foreach (var param in ctor.GetParameters())
        {
            if (param.HasDefaultValue) continue;
            var sourceProp = FindSourceProperty(sourceType, param.Name!, config);
            if (sourceProp == null) return false;
        }
        return true;
    }

    private static List<Expression> BuildPropertyAssignments(
        Type sourceType,
        Type destType,
        Expression typedSource,
        Expression typedDest,
        TypeMapConfig? config,
        Expression contextParam)
    {
        var assignments = new List<Expression>();
        var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite || p.SetMethod is { } set && set.IsPublic);

        // Track which properties were already set via constructor
        var ctorParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config?.CustomConstructor == null)
        {
            var constructors = destType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            var bestCtor = constructors
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault(c => c.GetParameters().Length > 0 && CanMapAllParameters(sourceType, c, config));

            if (bestCtor != null)
            {
                foreach (var p in bestCtor.GetParameters())
                    ctorParamNames.Add(p.Name!);
            }
        }

        foreach (var destProp in destProps)
        {
            if (!destProp.CanWrite) continue;
            if (config?.IgnoredMembers.Contains(destProp.Name) == true) continue;
            if (destProp.GetCustomAttribute<IgnoreMapAttribute>() != null) continue;
            if (ctorParamNames.Contains(destProp.Name)) continue;

            // Check for custom member config
            MemberMapConfig? memberConfig = null;
            config?.MemberConfigs.TryGetValue(destProp.Name, out memberConfig);

            Expression? valueExpr = null;

            if (memberConfig?.MapFromExpression is LambdaExpression mapFrom)
            {
                valueExpr = new ParameterReplacer(mapFrom.Parameters[0], typedSource).Visit(mapFrom.Body);
                valueExpr = ConvertIfNeeded(valueExpr, destProp.PropertyType, contextParam);
            }
            else
            {
                // Convention: check for MapPropertyAttribute on dest
                var mapPropAttr = destProp.GetCustomAttribute<MapPropertyAttribute>();
                var sourceName = mapPropAttr?.SourcePropertyName ?? destProp.Name;

                var sourceProp = FindSourcePropertyDirect(sourceType, sourceName);

                // Flattening: try splitting PascalCase (e.g., AddressCity -> Address.City)
                if (sourceProp == null && sourceName == destProp.Name)
                {
                    var flattenExpr = TryBuildFlattenedAccess(sourceType, destProp.Name, typedSource);
                    if (flattenExpr != null)
                    {
                        valueExpr = ConvertIfNeeded(flattenExpr, destProp.PropertyType, contextParam);
                    }
                }

                // Check IgnoreMap on source property
                if (sourceProp?.GetCustomAttribute<IgnoreMapAttribute>() != null)
                    sourceProp = null;

                if (valueExpr == null && sourceProp != null)
                {
                    Expression propAccess = Expression.Property(typedSource, sourceProp);
                    valueExpr = ConvertIfNeeded(propAccess, destProp.PropertyType, contextParam);
                }
            }

            if (valueExpr == null) continue;

            // Null substitute
            if (memberConfig is { HasNullSubstitute: true } && !destProp.PropertyType.IsValueType)
            {
                valueExpr = Expression.Coalesce(valueExpr,
                    Expression.Constant(memberConfig.NullSubstitute, destProp.PropertyType));
            }

            Expression assignment = Expression.Assign(
                Expression.Property(typedDest, destProp),
                valueExpr);

            // Condition
            if (memberConfig?.ConditionExpression is LambdaExpression condition)
            {
                var condBody = new ParameterReplacer(condition.Parameters[0], typedSource).Visit(condition.Body);
                assignment = Expression.IfThen(condBody, assignment);
            }

            assignments.Add(assignment);
        }

        return assignments;
    }

    private static Expression? TryBuildFlattenedAccess(Type sourceType, string destPropertyName, Expression source)
    {
        // Try to split at PascalCase boundaries and navigate the object graph.
        // E.g., "AddressStreetName" -> source.Address.StreetName
        var props = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            if (!destPropertyName.StartsWith(prop.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = destPropertyName[prop.Name.Length..];
            Expression access = Expression.Property(source, prop);

            if (remainder.Length == 0)
                return access;

            // Try to find a property on the nested type
            var nestedProp = prop.PropertyType.GetProperty(remainder,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (nestedProp != null)
            {
                Expression nestedAccess = Expression.Property(access, nestedProp);
                // Null-safe: if parent is null, return default
                if (!prop.PropertyType.IsValueType)
                {
                    return Expression.Condition(
                        Expression.Equal(access, Expression.Constant(null, prop.PropertyType)),
                        Expression.Default(nestedProp.PropertyType),
                        nestedAccess);
                }
                return nestedAccess;
            }

            // Recursive: try deeper flattening
            if (!prop.PropertyType.IsValueType && remainder.Length > 0)
            {
                var deeper = TryBuildFlattenedAccess(prop.PropertyType, remainder, access);
                if (deeper != null)
                {
                    return Expression.Condition(
                        Expression.Equal(access, Expression.Constant(null, prop.PropertyType)),
                        Expression.Default(deeper.Type),
                        deeper);
                }
            }
        }

        return null;
    }

    private static Expression BuildPropertyAccess(Expression source, PropertyInfo prop, TypeMapConfig? config)
    {
        return Expression.Property(source, prop);
    }

    private static PropertyInfo? FindSourceProperty(Type sourceType, string name, TypeMapConfig? config)
    {
        // Check member config for custom source
        if (config != null && config.MemberConfigs.TryGetValue(name, out var mc) && mc.MapFromExpression != null)
            return null; // handled separately

        return FindSourcePropertyDirect(sourceType, name);
    }

    private static PropertyInfo? FindSourcePropertyDirect(Type sourceType, string name)
    {
        return sourceType.GetProperty(name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
    }

    private static Expression ConvertIfNeeded(Expression source, Type targetType, Expression contextParam)
    {
        if (source.Type == targetType)
            return source;

        // Nullable<T> to T or T to Nullable<T>
        var sourceUnderlying = Nullable.GetUnderlyingType(source.Type);
        var targetUnderlying = Nullable.GetUnderlyingType(targetType);

        if (sourceUnderlying != null && targetType == sourceUnderlying)
            return Expression.Property(source, "Value");

        if (targetUnderlying != null && source.Type == targetUnderlying)
            return Expression.Convert(source, targetType);

        // Implicit/explicit conversion
        if (HasImplicitConversion(source.Type, targetType) || HasExplicitConversion(source.Type, targetType))
            return Expression.Convert(source, targetType);

        // Enum conversions
        if (source.Type.IsEnum && targetType == typeof(string))
            return Expression.Call(source, nameof(object.ToString), Type.EmptyTypes);

        if (source.Type == typeof(string) && targetType.IsEnum)
        {
            var parseMethod = typeof(Enum).GetMethod(nameof(Enum.Parse), [typeof(Type), typeof(string), typeof(bool)])!;
            return Expression.Convert(
                Expression.Call(parseMethod,
                    Expression.Constant(targetType),
                    source,
                    Expression.Constant(true)),
                targetType);
        }

        if (source.Type.IsEnum && targetType.IsEnum)
        {
            return Expression.Convert(
                Expression.Convert(source, typeof(int)),
                targetType);
        }

        // Collection mapping: IEnumerable<TSource> -> IEnumerable<TDest> / List<TDest> / TDest[]
        var sourceElementType = GetCollectionElementType(source.Type);
        var destElementType = GetCollectionElementType(targetType);

        if (sourceElementType != null && destElementType != null && sourceElementType != destElementType)
        {
            return BuildCollectionMapping(source, sourceElementType, destElementType, targetType, contextParam);
        }

        // Nested object mapping via context.Map (null-safe)
        if (!source.Type.IsValueType && !targetType.IsValueType
            && source.Type != typeof(string) && targetType != typeof(string))
        {
            var mapMethod = typeof(MapperContext).GetMethod(nameof(MapperContext.Map))!
                .MakeGenericMethod(targetType);
            var mapCall = Expression.Call(contextParam, mapMethod, Expression.Convert(source, typeof(object)));
            // null check: source == null ? default(TDest) : ctx.Map<TDest>(source)
            return Expression.Condition(
                Expression.Equal(source, Expression.Constant(null, source.Type)),
                Expression.Default(targetType),
                mapCall);
        }

        // Last resort: Convert.ChangeType for primitives
        if (IsConvertible(source.Type) && IsConvertible(targetType))
        {
            var changeType = typeof(Convert).GetMethod(nameof(Convert.ChangeType), [typeof(object), typeof(Type)])!;
            return Expression.Convert(
                Expression.Call(changeType,
                    Expression.Convert(source, typeof(object)),
                    Expression.Constant(targetType)),
                targetType);
        }

        return Expression.Convert(source, targetType);
    }

    private static Expression BuildCollectionMapping(
        Expression source,
        Type sourceElementType,
        Type destElementType,
        Type targetCollectionType,
        Expression contextParam)
    {
        // Build: source.Select(x => ctx.Map<TDest>(x)).ToList() or .ToArray()
        var mapMethod = typeof(MapperContext).GetMethod(nameof(MapperContext.Map))!
            .MakeGenericMethod(destElementType);

        var itemParam = Expression.Parameter(sourceElementType, "item");
        var mapCall = Expression.Call(contextParam, mapMethod, Expression.Convert(itemParam, typeof(object)));
        var selectLambda = Expression.Lambda(mapCall, itemParam);

        var enumerableSelect = typeof(Enumerable).GetMethods()
            .First(m => m.Name == "Select" && m.GetParameters().Length == 2)
            .MakeGenericMethod(sourceElementType, destElementType);

        var selectExpr = Expression.Call(enumerableSelect, source, selectLambda);

        // Determine target collection type
        if (targetCollectionType.IsArray)
        {
            var toArray = typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray))!
                .MakeGenericMethod(destElementType);
            return Expression.Call(toArray, selectExpr);
        }

        var toList = typeof(Enumerable).GetMethod(nameof(Enumerable.ToList))!
            .MakeGenericMethod(destElementType);
        return Expression.Call(toList, selectExpr);
    }

    private static Type? GetCollectionElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType)
        {
            var genDef = type.GetGenericTypeDefinition();
            if (genDef == typeof(List<>) || genDef == typeof(IList<>) ||
                genDef == typeof(ICollection<>) || genDef == typeof(IEnumerable<>) ||
                genDef == typeof(IReadOnlyList<>) || genDef == typeof(IReadOnlyCollection<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        // Check implemented interfaces
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return iface.GetGenericArguments()[0];
        }

        return null;
    }

    private static bool HasImplicitConversion(Type from, Type to)
    {
        return from.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(m => m.Name == "op_Implicit" && m.ReturnType == to) ||
               to.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(m => m.Name == "op_Implicit" && m.GetParameters()[0].ParameterType == from);
    }

    private static bool HasExplicitConversion(Type from, Type to)
    {
        return from.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(m => m.Name == "op_Explicit" && m.ReturnType == to) ||
               to.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(m => m.Name == "op_Explicit" && m.GetParameters()[0].ParameterType == from);
    }

    private static bool IsConvertible(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return typeof(IConvertible).IsAssignableFrom(underlying);
    }

    private sealed class ParameterReplacer(ParameterExpression oldParam, Expression newExpr) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == oldParam ? newExpr : base.VisitParameter(node);
    }
}
