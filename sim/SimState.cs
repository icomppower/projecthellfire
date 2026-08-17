using System;

namespace Hellfire.Sim
{
    /// <summary>
    /// Struct-of-arrays agent storage — flat float arrays, no per-agent objects.
    /// This exact layout is what the step-5 DOTS port is measured against (H1).
    /// </summary>
    public sealed class SimState
    {
        public int Tick;
        public readonly int AgentCount;
        public readonly float[] PosX;
        public readonly float[] PosY;
        public readonly float[] VelX;
        public readonly float[] VelY;

        public SimState(int agentCount)
        {
            if (agentCount <= 0) throw new ArgumentOutOfRangeException(nameof(agentCount));
            AgentCount = agentCount;
            PosX = new float[agentCount];
            PosY = new float[agentCount];
            VelX = new float[agentCount];
            VelY = new float[agentCount];
        }

        public SimState Clone()
        {
            var c = new SimState(AgentCount) { Tick = Tick };
            Array.Copy(PosX, c.PosX, AgentCount);
            Array.Copy(PosY, c.PosY, AgentCount);
            Array.Copy(VelX, c.VelX, AgentCount);
            Array.Copy(VelY, c.VelY, AgentCount);
            return c;
        }

        /// <summary>
        /// FNV-1a 64 over the exact bit patterns of every field. Byte-identical replay
        /// is defined as equality of this hash — the step-1 gate and the H2 measurement.
        /// </summary>
        public ulong StateHash()
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong h = offset;
            h = FnvInt(h, Tick, prime);
            h = FnvInt(h, AgentCount, prime);
            h = FnvArray(h, PosX, prime);
            h = FnvArray(h, PosY, prime);
            h = FnvArray(h, VelX, prime);
            h = FnvArray(h, VelY, prime);
            return h;
        }

        private static ulong FnvInt(ulong h, int value, ulong prime)
        {
            unchecked
            {
                uint v = (uint)value;
                for (int b = 0; b < 4; b++) { h = (h ^ ((v >> (b * 8)) & 0xFF)) * prime; }
                return h;
            }
        }

        private static ulong FnvArray(ulong h, float[] arr, ulong prime)
        {
            unchecked
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    uint v = (uint)BitConverter.SingleToInt32Bits(arr[i]);
                    for (int b = 0; b < 4; b++) { h = (h ^ ((v >> (b * 8)) & 0xFF)) * prime; }
                }
                return h;
            }
        }
    }
}
