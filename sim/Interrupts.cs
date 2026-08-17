using System;

namespace Hellfire.Sim
{
    /// <summary>
    /// The GDD §1 exception, kept blunt by construction: 2–3 commander
    /// interrupts per run, doctrine-level only — never a per-unit order. Each
    /// entry is tick-stamped, so a run is fully replayable from
    /// (seed, doctrine, plan). If these ever grow granular, the identity is
    /// drifting and they should be cut back, not extended.
    /// </summary>
    public enum InterruptType : byte
    {
        /// <summary>Force the abort latch now: mission off, everyone home.</summary>
        Abort = 1,
        /// <summary>Temporary recall for RecallDuration ticks; the mission
        /// resumes when it expires. Costs time and a second band crossing.</summary>
        FallBack = 2,
        /// <summary>Launch the doctrine-held reserve (ReserveFraction).</summary>
        CommitReserve = 3,
    }

    public readonly struct InterruptOrder
    {
        public readonly int Tick;
        public readonly InterruptType Type;

        public InterruptOrder(int tick, InterruptType type)
        {
            Tick = tick;
            Type = type;
        }
    }

    /// <summary>Immutable, validated schedule: at most 3 orders per run.</summary>
    public sealed class InterruptPlan
    {
        public const int MaxOrders = 3;
        private readonly InterruptOrder[] _orders;

        public static readonly InterruptPlan None = new InterruptPlan(Array.Empty<InterruptOrder>());

        public InterruptPlan(InterruptOrder[] orders)
        {
            if (orders.Length > MaxOrders)
                throw new ArgumentException($"at most {MaxOrders} interrupts per run", nameof(orders));
            _orders = orders;
        }

        /// <summary>Applies every order stamped for the state's CURRENT tick —
        /// call once per tick, before Simulation.Tick.</summary>
        public void ApplyDue(SimState state)
        {
            for (int i = 0; i < _orders.Length; i++)
            {
                if (_orders[i].Tick == state.Tick) Simulation.ApplyInterrupt(state, _orders[i].Type);
            }
        }
    }
}
