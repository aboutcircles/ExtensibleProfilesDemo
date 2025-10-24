namespace Circles.Profiles.Aggregation;

public readonly record struct TimeWindow(long Start, long End)
{
    public const long FutureSkewSeconds = 30;

    public bool IsValid => Start <= End;

    public bool Contains(long ts)
    {
        long skewedEnd = End + FutureSkewSeconds;
        bool afterStart = ts >= Start;
        bool beforeEnd = ts <= skewedEnd;
        return afterStart && beforeEnd;
    }
}
