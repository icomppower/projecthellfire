using Hellfire.Sim;
using UnityEngine;

namespace Hellfire.Presentation
{
    /// <summary>
    /// The GDD §4 step-2 promise, delivered at step 4: doctrine as a
    /// ScriptableObject for the editor workflow, converting to the plain-C#
    /// Doctrine the sim core consumes. The asset is created procedurally by the
    /// scene bootstrap — never hand-authored YAML.
    /// </summary>
    [CreateAssetMenu(fileName = "Doctrine", menuName = "Hellfire/Doctrine")]
    public sealed class DoctrineAsset : ScriptableObject
    {
        [Header("Autonomy dial (0 centralized … 1 decentralized)")]
        [Range(0f, 1f)] public float autonomy = 0.5f;

        [Header("Engagement")]
        [Range(0f, 1f)] public float riskTolerance = 0.5f;
        public float sensorRange = 60f;

        [Header("Comms posture (0 chatty … 1 silent)")]
        [Range(0f, 1f)] public float commsDiscipline = 0.5f;

        [Header("Loss threshold")]
        [Range(0f, 1f)] public float abortLossFraction = 0.5f;

        [Header("Formation (measured inert pre step-6 — see experiment log)")]
        [Range(0f, 1f)] public float cohesion = 0.5f;

        [Header("Envelope")]
        public float maxSpeed = 30f;
        public float neighborRadius = 12f;
        public float crowdDampPerNeighbor = 0.02f;
        public float jitterAccel = 2.0f;

        public Doctrine ToDoctrine() => new Doctrine
        {
            Autonomy = autonomy,
            RiskTolerance = riskTolerance,
            SensorRange = sensorRange,
            CommsDiscipline = commsDiscipline,
            AbortLossFraction = abortLossFraction,
            Cohesion = cohesion,
            MaxSpeed = maxSpeed,
            NeighborRadius = neighborRadius,
            CrowdDampPerNeighbor = crowdDampPerNeighbor,
            JitterAccel = jitterAccel,
        };
    }
}
