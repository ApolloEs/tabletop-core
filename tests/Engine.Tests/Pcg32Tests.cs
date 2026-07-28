using TabletopCore.Engine;
using Xunit;

namespace TabletopCore.Engine.Tests;

public class Pcg32Tests
{
    [Fact]
    public void MatchesPcgReferenceImplementation()
    {
        // First six outputs of the canonical pcg32 demo, seed 42 / sequence 54.
        var rng = new Pcg32(42, 54);
        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];
        foreach (uint value in expected)
            Assert.Equal(value, rng.NextUInt());
    }

    [Fact]
    public void SameSeedProducesSameSequence()
    {
        var a = new Pcg32(123);
        var b = new Pcg32(123);
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void DifferentSeedsProduceDifferentSequences()
    {
        var a = new Pcg32(1);
        var b = new Pcg32(2);
        var divergence = false;
        for (int i = 0; i < 10 && !divergence; i++)
            divergence = a.NextUInt() != b.NextUInt();
        Assert.True(divergence);
    }

    [Fact]
    public void RestoredStateContinuesTheExactSequence()
    {
        var original = new Pcg32(999, 7);
        for (int i = 0; i < 25; i++)
            original.NextUInt();

        var restored = Pcg32.Restore(original.State, original.Increment);
        for (int i = 0; i < 25; i++)
            Assert.Equal(original.NextUInt(), restored.NextUInt());
    }

    [Fact]
    public void BoundedValuesStayBelowBound()
    {
        var rng = new Pcg32(5);
        for (int i = 0; i < 1000; i++)
            Assert.InRange(rng.NextInt(13), 0, 12);
    }

    [Fact]
    public void NonPositiveBoundIsRejected()
    {
        var rng = new Pcg32(5);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(-3));
    }
}
