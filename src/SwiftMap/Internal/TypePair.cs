namespace SwiftMap.Internal;

internal readonly record struct TypePair(Type Source, Type Destination)
{
    public override int GetHashCode() => HashCode.Combine(Source, Destination);
}
