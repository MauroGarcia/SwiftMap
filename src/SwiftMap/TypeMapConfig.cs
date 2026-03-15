using System.Linq.Expressions;

namespace SwiftMap;

/// <summary>
/// Controls how null/default source values are handled during a Patch operation.
/// </summary>
public enum PatchBehavior
{
    /// <summary>
    /// Skip source fields that are null (reference types and Nullable&lt;T&gt;).
    /// Non-nullable value types are always assigned.
    /// </summary>
    SkipNullFields,

    /// <summary>
    /// Skip source fields that equal default(T) — includes 0, false, Guid.Empty, null, etc.
    /// </summary>
    SkipDefaultFields
}

/// <summary>
/// Non-generic base for storing type map configurations.
/// </summary>
public abstract class TypeMapConfig
{
    public Type SourceType { get; protected init; } = null!;
    public Type DestinationType { get; protected init; } = null!;
    // Ordinal is safe here: keys are always inserted and looked up using the exact CLR property name
    // (member.Member.Name / destProp.Name), so case-insensitive comparison is unnecessary overhead.
    internal Dictionary<string, MemberMapConfig> MemberConfigs { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> IgnoredMembers { get; } = new(StringComparer.Ordinal);
    internal LambdaExpression? CustomConstructor { get; set; }
    internal Func<object, object, object>? AfterMapAction { get; set; }
    internal bool ReverseMapEnabled { get; set; }
    internal PatchBehavior? PatchMode { get; set; }
}

/// <summary>
/// Fluent configuration for a specific source-to-destination mapping.
/// </summary>
public sealed class TypeMapConfig<TSource, TDestination> : TypeMapConfig
{
    public TypeMapConfig()
    {
        SourceType = typeof(TSource);
        DestinationType = typeof(TDestination);
    }

    /// <summary>
    /// Configure a specific destination member.
    /// </summary>
    public TypeMapConfig<TSource, TDestination> ForMember<TMember>(
        Expression<Func<TDestination, TMember>> destinationMember,
        Action<MemberMapExpression<TSource, TDestination, TMember>> options)
    {
        var memberName = GetMemberName(destinationMember);
        var expression = new MemberMapExpression<TSource, TDestination, TMember>();
        options(expression);

        MemberConfigs[memberName] = expression.Config;
        return this;
    }

    /// <summary>
    /// Ignore a destination member during mapping.
    /// </summary>
    public TypeMapConfig<TSource, TDestination> Ignore<TMember>(
        Expression<Func<TDestination, TMember>> destinationMember)
    {
        IgnoredMembers.Add(GetMemberName(destinationMember));
        return this;
    }

    /// <summary>
    /// Provide a custom constructor expression for the destination.
    /// </summary>
    public TypeMapConfig<TSource, TDestination> ConstructUsing(
        Expression<Func<TSource, TDestination>> constructor)
    {
        CustomConstructor = constructor;
        return this;
    }

    /// <summary>
    /// Execute an action after mapping.
    /// </summary>
    public TypeMapConfig<TSource, TDestination> AfterMap(Action<TSource, TDestination> action)
    {
        AfterMapAction = (src, dest) =>
        {
            action((TSource)src, (TDestination)dest);
            return dest;
        };
        return this;
    }

    /// <summary>
    /// Create a reverse mapping (TDestination -> TSource).
    /// </summary>
    public TypeMapConfig<TSource, TDestination> ReverseMap()
    {
        ReverseMapEnabled = true;
        return this;
    }

    /// <summary>
    /// Mark this map as a patch map, controlling how null/default source values are handled.
    /// When used with <see cref="IMapper.Patch{TSource,TDest}(TSource,TDest)"/>, only non-null (or non-default)
    /// source fields are applied to the destination.
    /// </summary>
    public TypeMapConfig<TSource, TDestination> AsPatch(PatchBehavior behavior = PatchBehavior.SkipNullFields)
    {
        PatchMode = behavior;
        return this;
    }

    private static string GetMemberName<TMember>(Expression<Func<TDestination, TMember>> expression)
    {
        if (expression.Body is MemberExpression member)
            return member.Member.Name;

        throw new ArgumentException("Expression must be a simple member access (e.g., x => x.Name).");
    }
}

/// <summary>
/// Configuration for a single member mapping.
/// </summary>
public sealed class MemberMapConfig
{
    internal LambdaExpression? MapFromExpression { get; set; }
    internal LambdaExpression? ConditionExpression { get; set; }
    internal object? NullSubstitute { get; set; }
    internal bool HasNullSubstitute { get; set; }
}

/// <summary>
/// Fluent API for configuring individual member mappings.
/// </summary>
public sealed class MemberMapExpression<TSource, TDestination, TMember>
{
    internal MemberMapConfig Config { get; } = new();

    /// <summary>
    /// Map this member from a custom source expression.
    /// </summary>
    public MemberMapExpression<TSource, TDestination, TMember> MapFrom<TSourceMember>(
        Expression<Func<TSource, TSourceMember>> source)
    {
        Config.MapFromExpression = source;
        return this;
    }

    /// <summary>
    /// Only map this member when the condition is met.
    /// </summary>
    public MemberMapExpression<TSource, TDestination, TMember> Condition(
        Expression<Func<TSource, bool>> condition)
    {
        Config.ConditionExpression = condition;
        return this;
    }

    /// <summary>
    /// Use this value when the source is null.
    /// </summary>
    public MemberMapExpression<TSource, TDestination, TMember> NullSubstitute(TMember value)
    {
        Config.NullSubstitute = value;
        Config.HasNullSubstitute = true;
        return this;
    }
}
