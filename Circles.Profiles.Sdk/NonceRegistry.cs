namespace Circles.Profiles.Sdk;

internal static class NonceRegistry
{
    private const int Window = 4096;
    private static readonly LinkedList<string> _q = new();
    private static readonly HashSet<string> _set = new(StringComparer.Ordinal); // hex strings

    private static readonly object _lock = new();

    public static bool SeenBefore(string nonce)
    {
        if (nonce is null) throw new ArgumentNullException(nameof(nonce));

        lock (_lock)
        {
            if (_set.Contains(nonce)) return true;

            _q.AddFirst(nonce);
            _set.Add(nonce);

            if (_q.Count > Window)
            {
                string old = _q.Last!.Value;
                _q.RemoveLast();
                _set.Remove(old);
            }

            return false;
        }
    }
}