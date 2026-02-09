using JetBrains.Annotations;

namespace Weald.Extensions;

public static class DictionaryExtensions
{
    public static TValue GetValueElse<TKey, TValue>(
        this IDictionary<TKey, TValue> self,
        TKey key,
        [RequireStaticDelegate] Func<TKey, TValue> func
    ) =>
        self.TryGetValue(key, out var value) ? value : self[key] = func(key);
}
