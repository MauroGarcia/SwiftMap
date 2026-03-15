using System.Runtime.CompilerServices;

namespace SwiftMap.Internal;

/// <summary>
/// Key for the compiled-delegate cache.
/// Stores RuntimeTypeHandle (an IntPtr to the CLR method table) instead of Type references,
/// making GetHashCode a direct IntPtr hash — one arithmetic op vs a virtual dispatch chain.
/// AggressiveInlining on hot-path members eliminates call overhead in tight mapping loops.
/// </summary>
internal readonly struct TypePair : IEquatable<TypePair>
{
    private readonly RuntimeTypeHandle _src;
    private readonly RuntimeTypeHandle _dst;

    // Preserve the original property surface so all callers compile unchanged.
    public Type Source      => Type.GetTypeFromHandle(_src)!;
    public Type Destination => Type.GetTypeFromHandle(_dst)!;

    public TypePair(Type source, Type destination)
    {
        _src = source.TypeHandle;
        _dst = destination.TypeHandle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(TypePair other) =>
        _src.Equals(other._src) && _dst.Equals(other._dst);

    public override bool Equals(object? obj) =>
        obj is TypePair other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() =>
        HashCode.Combine(_src.Value.GetHashCode(), _dst.Value.GetHashCode());
}
