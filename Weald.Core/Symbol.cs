using System.Runtime.CompilerServices;

namespace Weald.Core;

public sealed class Symbol : IEquatable<Symbol>
{
    public static readonly Symbol Undefined = new();

    public bool Equals(Symbol? other) => ReferenceEquals(this, other);

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public static bool operator ==(Symbol? left, Symbol? right) => Equals(left, right);
    public static bool operator !=(Symbol? left, Symbol? right) => !Equals(left, right);
}
