namespace TabletopCore.Engine;

/// <summary>
/// PCG32 (Permuted Congruential Generator, O'Neill 2014). Implemented here
/// rather than using System.Random because the platform generator's algorithm
/// is not guaranteed stable across .NET versions, and this engine promises
/// identical shuffle sequences forever for a given seed (replay / audit).
/// The full generator state is two ulongs, so it serializes into a snapshot
/// trivially via <see cref="State"/> / <see cref="Increment"/> / <see cref="Restore"/>.
/// </summary>
public sealed class Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private readonly ulong _increment;

    public Pcg32(ulong seed, ulong sequence = 0)
    {
        _increment = (sequence << 1) | 1UL;
        _state = 0;
        NextUInt();
        _state = unchecked(_state + seed);
        NextUInt();
    }

    private Pcg32(ulong state, ulong increment, bool restored)
    {
        _ = restored;
        _state = state;
        _increment = increment;
    }

    public ulong State => _state;
    public ulong Increment => _increment;

    public static Pcg32 Restore(ulong state, ulong increment)
    {
        if ((increment & 1UL) == 0)
            throw new ArgumentException("A PCG32 increment is always odd.", nameof(increment));
        return new Pcg32(state, increment, restored: true);
    }

    public uint NextUInt()
    {
        ulong oldState = _state;
        _state = unchecked(oldState * Multiplier + _increment);
        uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        int rot = (int)(oldState >> 59);
        return (xorShifted >> rot) | (xorShifted << (-rot & 31));
    }

    /// <summary>Unbiased bounded generation via rejection sampling.</summary>
    public uint NextUInt(uint boundExclusive)
    {
        if (boundExclusive == 0)
            throw new ArgumentOutOfRangeException(nameof(boundExclusive), "Bound must be positive.");
        uint threshold = unchecked(0u - boundExclusive) % boundExclusive;
        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold)
                return r % boundExclusive;
        }
    }

    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Bound must be positive.");
        return (int)NextUInt((uint)maxExclusive);
    }
}
