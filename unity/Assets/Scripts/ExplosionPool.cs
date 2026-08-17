using System.Collections.Generic;
using UnityEngine;

namespace Hellfire.Presentation
{
    /// <summary>
    /// Pooled, code-configured ParticleSystem explosions — the H6a comparison
    /// subject on the Unity side. VFX Graph was the GDD §4 callout, but VFX
    /// assets are editor-authored graphs that cannot be built from text; doing
    /// them headlessly would mean hand-writing .vfx YAML, which sits in the
    /// same corruption category as .unity edits. Logged as a deviation:
    /// ParticleSystem now, VFX Graph reserved for an editor-in-the-loop session
    /// (a natural H5 test case).
    /// </summary>
    public sealed class ExplosionPool : MonoBehaviour
    {
        public int poolSize = 32;
        public Color flashColor = new Color(1f, 0.6f, 0.2f);

        private readonly List<ParticleSystem> _pool = new List<ParticleSystem>();
        private int _next;

        private void Awake()
        {
            for (int i = 0; i < poolSize; i++)
            {
                _pool.Add(Build($"explosion-{i}"));
            }
        }

        public void Spawn(Vector3 position)
        {
            if (_pool.Count == 0) return;
            var ps = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            ps.transform.position = position;
            ps.Play();
        }

        private ParticleSystem Build(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 26f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
            main.startColor = flashColor;
            main.maxParticles = 64;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(flashColor, 0.25f),
                    new GradientColorKey(new Color(0.5f, 0.1f, 0.05f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 0.4f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null) renderer.material = new Material(shader);
            return ps;
        }
    }
}
