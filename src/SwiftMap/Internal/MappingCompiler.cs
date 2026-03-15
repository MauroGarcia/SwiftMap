using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using FastExpressionCompiler;

namespace SwiftMap.Internal;

/// <summary>
/// Compiles strongly-typed mapping delegates from expression trees.
/// Each delegate is compiled once and cached — zero reflection at mapping time.
/// Nested mappings are inlined directly into the parent expression tree (Mapster pattern),
/// eliminating per-call delegate invocation and closure allocations.
/// Collections use for-loops instead of LINQ Select/ToList (zero iterator overhead).
/// </summary>
internal static class MappingCompiler
{
    /// <summary>
    /// Resolves a TypeMapConfig for a given source→dest pair.
    /// Used to look up configs for nested/collection element types during inline compilation.
    /// </summary>
    internal delegate TypeMapConfig? ConfigResolver(Type sourceType, Type destType);

    private const int MaxInlineDepth = 8;

    // ── Static reflection caches (populated once per type-pair, reused forever) ──────────────

    /// <summary>Cache for implicit operator existence. Key: (from.TypeHandle, to.TypeHandle)</summary>
    private static readonly ConcurrentDictionary<(RuntimeTypeHandle From, RuntimeTypeHandle To), bool>
        _implicitConversionCache = new();

    /// <summary>Cache for explicit operator existence.</summary>
    private static readonly ConcurrentDictionary<(RuntimeTypeHandle From, RuntimeTypeHandle To), bool>
        _explicitConversionCache = new();

    /// <summary>Cache for Type.GetProperty(name, IgnoreCase) lookups. Key: (type.TypeHandle, name)</summary>
    private static readonly ConcurrentDictionary<(RuntimeTypeHandle Type, string Name), PropertyInfo?>
        _propCache = new();

    /// <summary>Cache for collection element type. Key: type.TypeHandle</summary>
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, Type?>
        _elementTypeCache = new();

    /// <summary>
    /// Cache for IList&lt;T&gt; interface + Count property per type.
    /// Value: (countProp, indexerProp, ifaceType) — null ifaceType means not found.
    /// </summary>
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, (PropertyInfo? Count, PropertyInfo? Indexer, Type? Iface)>
        _collectionInfoCache = new();

    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a compiled Func&lt;object, object&gt; that maps source to a new destination instance.
    /// No MapperContext — all nested mappings are inlined at compile time.
    /// </summary>
    internal static Func<object, object> CompileMapping(
        Type sourceType,
        Type destType,
        ConfigResolver configResolver)
    {
        var sourceParam = Expression.Parameter(typeof(object), "src");
        var typedSource = Expression.Variable(sourceType, "typedSrc");
        var config = configResolver(sourceType, destType);

        var body = new List<Expression>
        {
            Expression.Assign(typedSource, Expression.Convert(sourceParam, sourceType))
        };

        var destExpr = BuildDestinationExpression(sourceType, destType, typedSource, config, configResolver, 0);
        var resultVar = Expression.Variable(destType, "result");
        body.Add(Expression.Assign(resultVar, destExpr));

        // Property assignments for settable properties
        var assignments = BuildPropertyAssignments(sourceType, destType, typedSource, resultVar, config, configResolver, 0);
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

        var lambda = Expression.Lambda<Func<object, object>>(block, sourceParam);
        return lambda.CompileFast(ifFastFailedReturnNull: true) ?? lambda.Compile();
    }

    /// <summary>
    /// Builds a compiled action for mapping into an existing destination instance.
    /// </summary>
    internal static Action<object, object> CompileMappingInto(
        Type sourceType,
        Type destType,
        ConfigResolver configResolver)
    {
        var sourceParam = Expression.Parameter(typeof(object), "src");
        var destParam = Expression.Parameter(typeof(object), "dest");
        var typedSource = Expression.Variable(sourceType, "typedSrc");
        var typedDest = Expression.Variable(destType, "typedDest");
        var config = configResolver(sourceType, destType);

        var body = new List<Expression>
        {
            Expression.Assign(typedSource, Expression.Convert(sourceParam, sourceType)),
            Expression.Assign(typedDest, Expression.Convert(destParam, destType))
        };

        var assignments = BuildPropertyAssignments(sourceType, destType, typedSource, typedDest, config, configResolver, 0);
        body.AddRange(assignments);

        if (!body.Any(e => e.NodeType == ExpressionType.Assign && e != body[0] && e != body[1]))
            body.Add(Expression.Empty());

        var block = Expression.Block([typedSource, typedDest], body);
        var lambda = Expression.Lambda<Action<object, object>>(block, sourceParam, destParam);
        return lambda.CompileFast(ifFastFailedReturnNull: true) ?? lambda.Compile();
    }

    /// <summary>
    /// Builds an inline mapping expression for a nested object.
    /// Instead of calling ctx.Map&lt;T&gt;() (which re-enters the pipeline with closure allocations),
    /// this recursively builds the full mapping expression tree inline.
    /// Pattern from Mapster's ClassAdapter.CreateInlineExpression.
    /// </summary>
    private static Expression BuildInlineMapping(
        Type sourceType,
        Type destType,
        Expression source,
        ConfigResolver configResolver,
        int depth)
    {
        var config = configResolver(sourceType, destType);

        // Local variable to avoid re-evaluating the source expression multiple times
        var localSource = Expression.Variable(sourceType, $"ns{depth}");
        var destExpr = BuildDestinationExpression(sourceType, destType, localSource, config, configResolver, depth);
        var resultVar = Expression.Variable(destType, $"nr{depth}");

        var body = new List<Expression>
        {
            Expression.Assign(localSource, source),
            Expression.Assign(resultVar, destExpr)
        };

        var assignments = BuildPropertyAssignments(sourceType, destType, localSource, resultVar, config, configResolver, depth);
        body.AddRange(assignments);

        // AfterMap hook for nested type
        if (config?.AfterMapAction != null)
        {
            var afterMapField = Expression.Constant(config.AfterMapAction);
            body.Add(Expression.Invoke(afterMapField,
                Expression.Convert(localSource, typeof(object)),
                Expression.Convert(resultVar, typeof(object))));
        }

        body.Add(resultVar);
        return Expression.Block([localSource, resultVar], body);
    }

    private static Expression BuildDestinationExpression(
        Type sourceType,
        Type destType,
        Expression typedSource,
        TypeMapConfig? config,
        ConfigResolver configResolver,
        int depth)
    {
        // Custom constructor
        if (config?.CustomConstructor is LambdaExpression ctor)
        {
            return new ParameterReplacer(ctor.Parameters[0], typedSource).Visit(ctor.Body);
        }

        // Record / constructor with parameters — match by name.
        // Optimized: single pass with for-loop instead of OrderByDescending + FirstOrDefault (avoids LINQ allocation).
        var constructors = destType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        ConstructorInfo? bestCtor = null;
        int bestParamCount = 0;
        foreach (var candidate in constructors)
        {
            var paramCount = candidate.GetParameters().Length;
            if (paramCount > 0 && paramCount > bestParamCount && CanMapAllParameters(sourceType, candidate, config))
            {
                bestCtor = candidate;
                bestParamCount = paramCount;
            }
        }

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
                    args[i] = ConvertIfNeeded(value, param.ParameterType, configResolver, depth);
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
        ConfigResolver configResolver,
        int depth)
    {
        var assignments = new List<Expression>();
        var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Track which properties were already set via constructor.
        // Optimized: single pass for-loop instead of LINQ + allocation.
        var ctorParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config?.CustomConstructor == null)
        {
            var constructors = destType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            ConstructorInfo? bestCtor = null;
            int bestParamCount = 0;
            foreach (var candidate in constructors)
            {
                var paramCount = candidate.GetParameters().Length;
                if (paramCount > 0 && paramCount > bestParamCount && CanMapAllParameters(sourceType, candidate, config))
                {
                    bestCtor = candidate;
                    bestParamCount = paramCount;
                }
            }

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
                valueExpr = ConvertIfNeeded(valueExpr, destProp.PropertyType, configResolver, depth);
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
                        valueExpr = ConvertIfNeeded(flattenExpr, destProp.PropertyType, configResolver, depth);
                    }
                }

                // Check IgnoreMap on source property
                if (sourceProp?.GetCustomAttribute<IgnoreMapAttribute>() != null)
                    sourceProp = null;

                if (valueExpr == null && sourceProp != null)
                {
                    Expression propAccess = Expression.Property(typedSource, sourceProp);
                    valueExpr = ConvertIfNeeded(propAccess, destProp.PropertyType, configResolver, depth);
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
        var props = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            if (!destPropertyName.StartsWith(prop.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = destPropertyName[prop.Name.Length..];
            Expression access = Expression.Property(source, prop);

            if (remainder.Length == 0)
                return access;

            var nestedProp = prop.PropertyType.GetProperty(remainder,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (nestedProp != null)
            {
                Expression nestedAccess = Expression.Property(access, nestedProp);
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
        if (config != null && config.MemberConfigs.TryGetValue(name, out var mc) && mc.MapFromExpression != null)
            return null;

        return FindSourcePropertyDirect(sourceType, name);
    }

    /// <summary>
    /// Cached wrapper around Type.GetProperty with IgnoreCase flag.
    /// Avoids repeated reflection on the same (type, propertyName) pair during compilation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PropertyInfo? FindSourcePropertyDirect(Type sourceType, string name)
    {
        var key = (sourceType.TypeHandle, name);
        return _propCache.GetOrAdd(key, static k =>
            Type.GetTypeFromHandle(k.Type)!.GetProperty(k.Name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
    }

    /// <summary>
    /// Determines the appropriate conversion expression.
    /// Check order: identical → nullable → enum (cheap) → implicit/explicit operators (expensive) →
    /// collections → nested objects → Convert.ChangeType → hard cast.
    /// Enum checks are ordered BEFORE operator reflection to avoid reflection for common cases.
    /// </summary>
    private static Expression ConvertIfNeeded(Expression source, Type targetType, ConfigResolver configResolver, int depth)
    {
        if (source.Type == targetType)
            return source;

        // ── 1. Nullable<T> ↔ T ──────────────────────────────────────────────────────────────
        var sourceUnderlying = Nullable.GetUnderlyingType(source.Type);
        var targetUnderlying = Nullable.GetUnderlyingType(targetType);

        if (sourceUnderlying != null && targetType == sourceUnderlying)
            return Expression.Property(source, "Value");

        if (targetUnderlying != null && source.Type == targetUnderlying)
            return Expression.Convert(source, targetType);

        // ── 2. Enum conversions (cheap — no reflection) ──────────────────────────────────────
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

        // ── 3. Implicit/explicit operator (expensive — cached reflection) ────────────────────
        if (HasImplicitConversion(source.Type, targetType) || HasExplicitConversion(source.Type, targetType))
            return Expression.Convert(source, targetType);

        // ── 4. Collection mapping ────────────────────────────────────────────────────────────
        var sourceElementType = GetCollectionElementType(source.Type);
        var destElementType = GetCollectionElementType(targetType);

        if (sourceElementType != null && destElementType != null && sourceElementType != destElementType)
        {
            return BuildCollectionMapping(source, sourceElementType, destElementType, targetType, configResolver, depth);
        }

        // ── 5. Nested object mapping — inline (Mapster pattern: CreateInlineExpression) ──────
        if (!source.Type.IsValueType && !targetType.IsValueType
            && source.Type != typeof(string) && targetType != typeof(string))
        {
            if (depth >= MaxInlineDepth)
                throw new InvalidOperationException(
                    $"SwiftMap: Maximum nesting depth ({MaxInlineDepth}) exceeded mapping " +
                    $"'{source.Type.Name}' to '{targetType.Name}'. Possible circular reference.");

            var inlineMapping = BuildInlineMapping(source.Type, targetType, source, configResolver, depth + 1);

            return Expression.Condition(
                Expression.Equal(source, Expression.Constant(null, source.Type)),
                Expression.Default(targetType),
                inlineMapping);
        }

        // ── 6. Convert.ChangeType for IConvertible primitives ────────────────────────────────
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

    /// <summary>
    /// Builds a for-loop based collection mapping (NServiceBus/Mapster pattern).
    /// Eliminates LINQ Select/ToList overhead: no SelectIterator, no closure allocation.
    /// Only allocates the destination collection and its elements.
    /// </summary>
    private static Expression BuildCollectionMapping(
        Expression source,
        Type sourceElementType,
        Type destElementType,
        Type targetCollectionType,
        ConfigResolver configResolver,
        int depth)
    {
        // Check if source supports indexed access (array, List<T>, IList<T>)
        if (TryGetCountAndIndexer(source, sourceElementType, out var countExpr, out var indexerFactory))
        {
            return BuildIndexedCollectionMapping(
                source, sourceElementType, destElementType, targetCollectionType,
                countExpr!, indexerFactory!, configResolver, depth);
        }

        // Fallback for IEnumerable<T> without indexer: use compiled element mapper as constant
        return BuildEnumerableCollectionMapping(
            source, sourceElementType, destElementType, targetCollectionType,
            configResolver, depth);
    }

    /// <summary>
    /// For-loop mapping for indexed collections (array, List&lt;T&gt;, IList&lt;T&gt;).
    /// Generates: var result = new T[count]; for (int i = 0; i &lt; count; i++) result[i] = map(source[i]);
    /// Zero intermediate allocations — only the destination collection.
    /// </summary>
    private static Expression BuildIndexedCollectionMapping(
        Expression source,
        Type sourceElementType,
        Type destElementType,
        Type targetCollectionType,
        Expression countExpr,
        Func<Expression, Expression> indexerFactory,
        ConfigResolver configResolver,
        int depth)
    {
        var i = Expression.Variable(typeof(int), "i");
        var len = Expression.Variable(typeof(int), "len");
        var itemVar = Expression.Variable(sourceElementType, "elem");
        var breakLabel = Expression.Label(typeof(void), "brk");

        // Build inline element mapping
        if (depth >= MaxInlineDepth)
            throw new InvalidOperationException(
                $"SwiftMap: Maximum nesting depth ({MaxInlineDepth}) exceeded mapping " +
                $"collection elements '{sourceElementType.Name}' to '{destElementType.Name}'.");

        var mappedItem = BuildInlineMapping(sourceElementType, destElementType, itemVar, configResolver, depth + 1);

        if (targetCollectionType.IsArray)
        {
            var arrVar = Expression.Variable(targetCollectionType, "arr");
            return Expression.Block(
                [i, len, arrVar, itemVar],
                Expression.Assign(len, countExpr),
                Expression.Assign(arrVar, Expression.NewArrayBounds(destElementType, len)),
                Expression.Assign(i, Expression.Constant(0)),
                Expression.Loop(
                    Expression.IfThenElse(
                        Expression.LessThan(i, len),
                        Expression.Block(
                            Expression.Assign(itemVar, indexerFactory(i)),
                            Expression.Assign(Expression.ArrayAccess(arrVar, i), mappedItem),
                            Expression.PostIncrementAssign(i)
                        ),
                        Expression.Break(breakLabel)
                    ),
                    breakLabel
                ),
                arrVar
            );
        }

        // List<TDest> with pre-allocated capacity
        var listType = typeof(List<>).MakeGenericType(destElementType);
        var listCtor = listType.GetConstructor([typeof(int)])!;
        var addMethod = listType.GetMethod("Add")!;
        var listVar = Expression.Variable(listType, "list");

        Expression result = Expression.Block(
            [i, len, listVar, itemVar],
            Expression.Assign(len, countExpr),
            Expression.Assign(listVar, Expression.New(listCtor, len)),
            Expression.Assign(i, Expression.Constant(0)),
            Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(i, len),
                    Expression.Block(
                        Expression.Assign(itemVar, indexerFactory(i)),
                        Expression.Call(listVar, addMethod, mappedItem),
                        Expression.PostIncrementAssign(i)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            ),
            listVar
        );

        // Cast if target is an interface (IList<T>, ICollection<T>, etc.)
        if (targetCollectionType != listType && targetCollectionType.IsAssignableFrom(listType))
            result = Expression.Convert(result, targetCollectionType);

        return result;
    }

    /// <summary>
    /// Fallback for IEnumerable&lt;T&gt; without indexer.
    /// Uses a pre-compiled element mapper stored as a constant (allocated once at compile time).
    /// </summary>
    private static Expression BuildEnumerableCollectionMapping(
        Expression source,
        Type sourceElementType,
        Type destElementType,
        Type targetCollectionType,
        ConfigResolver configResolver,
        int depth)
    {
        // Compile a standalone element mapper as a constant
        var elementMapper = CompileElementMapper(sourceElementType, destElementType, configResolver, depth);
        var mapperConst = Expression.Constant(elementMapper);

        var helperMethod = typeof(CollectionHelper)
            .GetMethod(nameof(CollectionHelper.MapToList), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(sourceElementType, destElementType);

        Expression result = Expression.Call(helperMethod, source, mapperConst);

        if (targetCollectionType.IsArray)
        {
            var toArrayMethod = typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray))!
                .MakeGenericMethod(destElementType);
            result = Expression.Call(toArrayMethod, result);
        }
        else if (targetCollectionType != typeof(List<>).MakeGenericType(destElementType)
                 && targetCollectionType.IsAssignableFrom(typeof(List<>).MakeGenericType(destElementType)))
        {
            result = Expression.Convert(result, targetCollectionType);
        }

        return result;
    }

    /// <summary>
    /// Compiles a Func&lt;TSource, TDest&gt; for element mapping, used as a constant in expression trees.
    /// Allocated once at compile time — zero per-call cost.
    /// Uses FEC for faster compilation and potentially faster delegates.
    /// </summary>
    private static Delegate CompileElementMapper(
        Type sourceType, Type destType, ConfigResolver configResolver, int depth)
    {
        var funcType = typeof(Func<,>).MakeGenericType(sourceType, destType);
        var param = Expression.Parameter(sourceType, "e");
        var mapping = BuildInlineMapping(sourceType, destType, param, configResolver, depth + 1);
        var lambda = Expression.Lambda(funcType, mapping, param);
        return lambda.CompileFast(ifFastFailedReturnNull: true) ?? lambda.Compile();
    }

    private static bool TryGetCountAndIndexer(
        Expression source, Type elementType,
        out Expression? countExpr,
        out Func<Expression, Expression>? indexerFactory)
    {
        // Array: .Length and arr[i]
        if (source.Type.IsArray)
        {
            countExpr = Expression.ArrayLength(source);
            indexerFactory = idx => Expression.ArrayIndex(source, idx);
            return true;
        }

        // Types with Count property and this[int] indexer (List<T>, IList<T>, etc.)
        var countProp = source.Type.GetProperty("Count", typeof(int));
        if (countProp != null)
        {
            var indexerProp = source.Type.GetProperty("Item", [typeof(int)]);
            if (indexerProp != null)
            {
                countExpr = Expression.Property(source, countProp);
                indexerFactory = idx => Expression.MakeIndex(source, indexerProp, [idx]);
                return true;
            }

            // Check IList<T> interface — cached per type to avoid repeated GetInterfaces() calls
            var info = _collectionInfoCache.GetOrAdd(source.Type.TypeHandle, static handle =>
            {
                var t = Type.GetTypeFromHandle(handle)!;
                var cp = t.GetProperty("Count", typeof(int));
                if (cp == null) return (null, null, null);
                foreach (var iface in t.GetInterfaces())
                {
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IList<>))
                    {
                        var ifaceIndexer = iface.GetProperty("Item");
                        if (ifaceIndexer != null)
                            return (cp, ifaceIndexer, iface);
                    }
                }
                return (cp, null, null);
            });

            if (info.Indexer != null && info.Iface != null)
            {
                countExpr = Expression.Property(source, countProp);
                var castSource = Expression.Convert(source, info.Iface);
                indexerFactory = idx => Expression.MakeIndex(castSource, info.Indexer, [idx]);
                return true;
            }
        }

        countExpr = null;
        indexerFactory = null;
        return false;
    }

    /// <summary>
    /// Returns the element type of a collection type, or null if not a collection.
    /// Cached per type — GetInterfaces() is called at most once per type.
    /// </summary>
    private static Type? GetCollectionElementType(Type type)
    {
        return _elementTypeCache.GetOrAdd(type.TypeHandle, static handle =>
        {
            var t = Type.GetTypeFromHandle(handle)!;

            if (t.IsArray)
                return t.GetElementType();

            if (t.IsGenericType)
            {
                var genDef = t.GetGenericTypeDefinition();
                if (genDef == typeof(List<>) || genDef == typeof(IList<>) ||
                    genDef == typeof(ICollection<>) || genDef == typeof(IEnumerable<>) ||
                    genDef == typeof(IReadOnlyList<>) || genDef == typeof(IReadOnlyCollection<>))
                {
                    return t.GetGenericArguments()[0];
                }
            }

            // Fallback: scan interfaces (early exit on first match)
            foreach (var iface in t.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            }

            return null;
        });
    }

    /// <summary>
    /// Checks for op_Implicit between two types.
    /// Cached: GetMethods() is called at most once per (from, to) pair.
    /// </summary>
    private static bool HasImplicitConversion(Type from, Type to)
    {
        var key = (from.TypeHandle, to.TypeHandle);
        return _implicitConversionCache.GetOrAdd(key, static k =>
        {
            var fromType = Type.GetTypeFromHandle(k.Item1)!;
            var toType = Type.GetTypeFromHandle(k.Item2)!;
            return fromType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                       .Any(m => m.Name == "op_Implicit" && m.ReturnType == toType) ||
                   toType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                       .Any(m => m.Name == "op_Implicit" && m.GetParameters().Length > 0 &&
                                 m.GetParameters()[0].ParameterType == fromType);
        });
    }

    /// <summary>
    /// Checks for op_Explicit between two types.
    /// Cached: GetMethods() is called at most once per (from, to) pair.
    /// </summary>
    private static bool HasExplicitConversion(Type from, Type to)
    {
        var key = (from.TypeHandle, to.TypeHandle);
        return _explicitConversionCache.GetOrAdd(key, static k =>
        {
            var fromType = Type.GetTypeFromHandle(k.Item1)!;
            var toType = Type.GetTypeFromHandle(k.Item2)!;
            return fromType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                       .Any(m => m.Name == "op_Explicit" && m.ReturnType == toType) ||
                   toType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                       .Any(m => m.Name == "op_Explicit" && m.GetParameters().Length > 0 &&
                                 m.GetParameters()[0].ParameterType == fromType);
        });
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

/// <summary>
/// Helper for mapping IEnumerable sources without indexer access.
/// The Func delegate is a pre-compiled constant — zero per-call allocation beyond the List itself.
/// </summary>
internal static class CollectionHelper
{
    internal static List<TDest> MapToList<TSource, TDest>(
        IEnumerable<TSource> source, Func<TSource, TDest> mapper)
    {
        var list = source is ICollection<TSource> col
            ? new List<TDest>(col.Count)
            : new List<TDest>();

        foreach (var item in source)
            list.Add(mapper(item));

        return list;
    }
}
