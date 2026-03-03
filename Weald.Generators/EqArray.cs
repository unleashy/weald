using System.Collections;
using System.Collections.Immutable;

namespace Weald.Generators;

internal readonly struct EqArray<T>(ImmutableArray<T> items)
    : IEquatable<EqArray<T>>,
      IEnumerable<T>
    where T : IEquatable<T>
{
    private ReadOnlySpan<T> AsSpan() => items.AsSpan();

    public bool Equals(EqArray<T> other) => AsSpan().SequenceEqual(other.AsSpan());

    public override int GetHashCode() => items.Aggregate(17, (h, x) => h * 31 + x.GetHashCode());

    public ImmutableArray<T>.Enumerator GetEnumerator() => items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>) items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable) items).GetEnumerator();
}

internal static class ImmutableArrayExtensions
{
    public static EqArray<T> ToEquatable<T>(this ImmutableArray<T> array)
        where T : IEquatable<T> =>
        new(array);

    public static EqArray<T> DrainToEquatable<T>(this ImmutableArray<T>.Builder builder)
        where T : IEquatable<T> =>
        new(builder.DrainToImmutable());
}

internal sealed class EqImmutableArrayComparer<T> : IEqualityComparer<ImmutableArray<T>>
{
    public bool Equals(ImmutableArray<T> left, ImmutableArray<T> right)
    {
        if (left.Length != right.Length) {
            return false;
        }

        for (var i = 0; i < left.Length; i++) {
            var areEqual = left[i] is {} leftElem
                ? leftElem.Equals(right[i])
                : right[i] is null;

            if (!areEqual) {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(ImmutableArray<T> obj)
    {
        return Enumerable.Aggregate(obj, 0, (current, item) => (current, item).GetHashCode());
    }
}
