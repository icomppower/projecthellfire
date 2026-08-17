namespace Hellfire.Sim
{
    /// <summary>
    /// Order-independent deterministic RNG (Battlecmo pattern): every draw is a pure
    /// hash of (seed, tick, tags). No stream state, so unrelated draws cannot perturb
    /// each other and adding a draw site never shifts existing ones.
    /// </summary>
    public static class DetHash
    {
        // SplitMix64 finalizer — full-avalanche 64-bit mix.
        private static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static ulong Hash(ulong seed, ulong tick, ulong tagA, ulong tagB = 0)
        {
            ulong h = Mix(seed + 0x9E3779B97F4A7C15UL);
            h = Mix(h ^ Mix(tick + 0xD1B54A32D192ED03UL));
            h = Mix(h ^ Mix(tagA + 0x8CB92BA72F3D8DD7UL));
            h = Mix(h ^ Mix(tagB + 0xEB44ACCAB455D165UL));
            return h;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public static float Float01(ulong seed, ulong tick, ulong tagA, ulong tagB = 0)
        {
            // Top 24 bits → exactly representable in float, uniform in [0,1).
            return (Hash(seed, tick, tagA, tagB) >> 40) * (1.0f / 16777216.0f);
        }

        /// <summary>Uniform in [-1, 1).</summary>
        public static float FloatSigned(ulong seed, ulong tick, ulong tagA, ulong tagB = 0)
        {
            return Float01(seed, tick, tagA, tagB) * 2.0f - 1.0f;
        }
    }
}
