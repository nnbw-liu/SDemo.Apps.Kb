namespace SDemo.Kb.Tests;

/// <summary>可推进的时钟，用来测 TTL / 回收站，不真的等 7 天。</summary>
internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset UtcNow { get; private set; } = now;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan by) => UtcNow += by;

    public void Set(DateTimeOffset value) => UtcNow = value;
}
