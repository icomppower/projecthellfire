using System;

namespace Hellfire.Sim
{
    public enum AgentStatus : byte
    {
        Active = 0,
        Dead = 1,
        Completed = 2, // reached the objective; latched
        Safe = 3,      // aborted and made it back to the spawn band
        Reserve = 4,   // doctrine-held at spawn; launches only on CommitReserve
    }

    /// <summary>
    /// Death-cause flags recorded at the kill tick — the raw material of the §2
    /// diagnosability contract: every loss must map back to a doctrine parameter.
    /// Non-exclusive; a death can carry several.
    /// </summary>
    [Flags]
    public enum DeathFlag : byte
    {
        None = 0,
        /// <summary>Killer was outside the agent's knowledge radius → autonomy /
        /// sensor axis (blind, or network-stripped — see Jammed).</summary>
        UnknownThreat = 1,
        /// <summary>Agent was inside a jammer zone → over-reliance on network
        /// (autonomy too low for the EW environment).</summary>
        Jammed = 2,
        /// <summary>Kill proximity only existed because of the chatty-comms
        /// engage-radius extension → comms discipline axis.</summary>
        Detected = 4,
        /// <summary>Killer was known and the agent was in its kill zone anyway →
        /// risk tolerance (or cohesion dragging the flock through danger).</summary>
        PressedKnown = 8,
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
        public readonly byte[] DeathFlags;
        /// <summary>Tick+1 at which the agent died; 0 = never died.</summary>
        public readonly int[] DeathTick;
        /// <summary>Swarm-level mission-abort latch (doctrine loss threshold).</summary>
        public bool Aborted;
        /// <summary>FallBack interrupt: agents head home while Tick &lt; this.</summary>
        public int RecallUntilTick;

        public SimState(int agentCount)
        {
            if (agentCount <= 0) throw new ArgumentOutOfRangeException(nameof(agentCount));
            AgentCount = agentCount;
            PosX = new float[agentCount];
            PosY = new float[agentCount];
            VelX = new float[agentCount];
            VelY = new float[agentCount];
            Status = new byte[agentCount];
            DeathFlags = new byte[agentCount];
            DeathTick = new int[agentCount];
        }

        public SimState Clone()
        {
            var c = new SimState(AgentCount) { Tick = Tick, Aborted = Aborted, RecallUntilTick = RecallUntilTick };
            Array.Copy(PosX, c.PosX, AgentCount);
            Array.Copy(PosY, c.PosY, AgentCount);
            Array.Copy(VelX, c.VelX, AgentCount);
            Array.Copy(VelY, c.VelY, AgentCount);
            Array.Copy(Status, c.Status, AgentCount);
            Array.Copy(DeathFlags, c.DeathFlags, AgentCount);
            Array.Copy(DeathTick, c.DeathTick, AgentCount);
            return c;
        }

        public int CountStatus(AgentStatus s)
        {
            byte b = (byte)s;
            int n = 0;
            for (int i = 0; i < AgentCount; i++) { if (Status[i] == b) n++; }
            return n;
        }

        public int CountDeathFlag(DeathFlag f)
        {
            byte b = (byte)f;
            int n = 0;
            for (int i = 0; i < AgentCount; i++) { if ((DeathFlags[i] & b) != 0) n++; }
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
            h = FnvInt(h, RecallUntilTick, prime);
            h = FnvArray(h, PosX, prime);
            h = FnvArray(h, PosY, prime);
            h = FnvArray(h, VelX, prime);
            h = FnvArray(h, VelY, prime);
            unchecked
            {
                for (int i = 0; i < Status.Length; i++) { h = (h ^ Status[i]) * prime; }
                for (int i = 0; i < DeathFlags.Length; i++) { h = (h ^ DeathFlags[i]) * prime; }
            }
            for (int i = 0; i < DeathTick.Length; i++) { h = FnvInt(h, DeathTick[i], prime); }
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
