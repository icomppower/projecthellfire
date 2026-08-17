using System;

namespace Hellfire.Sim
{
    public enum AgentStatus : byte
    {
        Active = 0,
        Dead = 1,
        Completed = 2, // reached the objective; latched
        Safe = 3,      // aborted and made it back to the spawn band
    }

    /// <summary>
    /// Struct-of-arrays agent storage — flat arrays, no per-agent objects.
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
        public readonly byte[] Status;
        /// <summary>Swarm-level mission-abort latch (doctrine loss threshold).</summary>
        public bool Aborted;

        public SimState(int agentCount)
        {
            if (agentCount <= 0) throw new ArgumentOutOfRangeException(nameof(agentCount));
            AgentCount = agentCount;
            PosX = new float[agentCount];
            PosY = new float[agentCount];
            VelX = new float[agentCount];
            VelY = new float[agentCount];
            Status = new byte[agentCount];
        }

        public SimState Clone()
        {
            var c = new SimState(AgentCount) { Tick = Tick, Aborted = Aborted };
            Array.Copy(PosX, c.PosX, AgentCount);
            Array.Copy(PosY, c.PosY, AgentCount);
            Array.Copy(VelX, c.VelX, AgentCount);
            Array.Copy(VelY, c.VelY, AgentCount);
            Array.Copy(Status, c.Status, AgentCount);
            return c;
        }

        public int CountStatus(AgentStatus s)
        {
            byte b = (byte)s;
            int n = 0;
            for (int i = 0; i < AgentCount; i++) { if (Status[i] == b) n++; }
            return n;
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
            h = FnvInt(h, Aborted ? 1 : 0, prime);
            h = FnvArray(h, PosX, prime);
            h = FnvArray(h, PosY, prime);
            h = FnvArray(h, VelX, prime);
            h = FnvArray(h, VelY, prime);
            unchecked
            {
                for (int i = 0; i < Status.Length; i++) { h = (h ^ Status[i]) * prime; }
            }
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
