namespace Hellfire.Sim
{
    /// <summary>
    /// Step-1 placeholder for the doctrine schema (real schema lands at step 2).
    /// Only the numeric knobs the determinism spine needs to exercise float paths
    /// and the spatial hash — no doctrine semantics yet.
    /// </summary>
    public readonly struct Doctrine
    {
        public readonly float JitterAccel;        // magnitude of hashed-RNG wander accel
        public readonly float NeighborRadius;     // spatial-hash query radius
        public readonly float CrowdDampPerNeighbor; // speed damping per neighbor in radius
        public readonly float MaxSpeed;

        public Doctrine(float jitterAccel, float neighborRadius, float crowdDampPerNeighbor, float maxSpeed)
        {
            JitterAccel = jitterAccel;
            NeighborRadius = neighborRadius;
            CrowdDampPerNeighbor = crowdDampPerNeighbor;
            MaxSpeed = maxSpeed;
        }

        public static Doctrine Default => new Doctrine(
            jitterAccel: 2.0f,
            neighborRadius: 12.0f,
            crowdDampPerNeighbor: 0.02f,
            maxSpeed: 30.0f);
    }
}
