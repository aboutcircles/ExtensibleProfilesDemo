using Circles.Profiles.Interfaces;

namespace Circles.Profiles.Sdk;

internal class InMemoryNonceRegistry : INonceRegistry
{
    private const int Window = 4096;
    private readonly LinkedList<string> _q = new();
    private readonly HashSet<string> _set = new(StringComparer.Ordinal); // hex strings

    private readonly Lock _lock = new();

    public bool SeenBefore(string nonce)
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