namespace Circles.Profiles.Sdk;

internal static class NonceRegistry
{
    private const int Window = 4096;
    private static readonly LinkedList<string> Q = new();
    private static readonly HashSet<string> Set = new(StringComparer.Ordinal); // hex strings

    private static readonly Lock Lock = new();

    public static bool SeenBefore(string nonce)
    {
        if (nonce is null) throw new ArgumentNullException(nameof(nonce));

        lock (Lock)
        {
            if (Set.Contains(nonce)) return true;

            Q.AddFirst(nonce);
            Set.Add(nonce);

            if (Q.Count > Window)
            {
                string old = Q.Last!.Value;
                Q.RemoveLast();
                Set.Remove(old);
            }

            return false;
        }
    }
}