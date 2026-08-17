using System.Text;
using Hellfire.Sim;
using UnityEngine;

namespace Hellfire.Presentation
{
    /// <summary>
    /// Screen-space designations over the 3D scene (step 7: "no label no
    /// nothing" — every emplacement now says what it is): SAM-n on threat
    /// emplacements, EW-n on jammer sites, the delivery zone and launch line,
    /// plus the kill feed (top right) naming attacker, victim and the §2 death
    /// cause. IMGUI + WorldToScreenPoint so it works identically in the editor
    /// and the player build, under any camera.
    /// </summary>
    [RequireComponent(typeof(SimDriver))]
    public sealed class TacticalLabels : MonoBehaviour
    {
        public EngagementRenderer engagement;
        public int feedLines = 7;

        private SimDriver _driver;
        private Camera _cam;
        private GUIStyle _label;
        private GUIStyle _feed;
        private readonly StringBuilder _sb = new StringBuilder(96);

        private static readonly Color ThreatColor = new Color(1f, 0.85f, 0.8f);
        private static readonly Color ThreatHotColor = new Color(1f, 1f, 0.5f);
        private static readonly Color JammerColor = new Color(0.8f, 0.55f, 1f);
        private static readonly Color ObjectiveColor = new Color(0.5f, 1f, 0.6f);
        private static readonly Color FeedColor = new Color(1f, 0.75f, 0.55f);

        private void Awake()
        {
            _driver = GetComponent<SimDriver>();
        }

        private void OnGUI()
        {
            if (_cam == null) _cam = Camera.main;
            var sim = _driver.Sim;
            if (_cam == null || sim == null) return;
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                };
                _feed = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleRight,
                };
            }

            var sc = sim.Scenario;
            for (int t = 0; t < sc.ThreatCount; t++)
            {
                bool hot = engagement != null && engagement.enabled
                    && _driver.Running && IsEngaged(t);
                WorldLabel(new Vector3(sc.ThreatX[t], 10f, sc.ThreatY[t]),
                    $"SAM-{t + 1}", hot ? ThreatHotColor : ThreatColor);
            }
            for (int j = 0; j < sc.JammerCount; j++)
            {
                WorldLabel(new Vector3(sc.JammerX[j], 12f, sc.JammerY[j]),
                    $"EW-{j + 1}", JammerColor);
            }
            WorldLabel(new Vector3(Scenario.ObjectiveX, 6f, Scenario.ObjectiveY),
                "DELIVERY ZONE", ObjectiveColor);
            WorldLabel(new Vector3(60f, 4f, Scenario.SpawnBandHeight),
                "LAUNCH LINE", new Color(0.5f, 0.8f, 1f));

            DrawFeed();
        }

        private bool IsEngaged(int threat)
        {
            // Engagement state lives frame-to-frame in the renderer; the cheap
            // proxy here is whether any under-fire agent is nearest this threat.
            // Simpler and equally readable: recompute range check directly.
            var state = _driver.State;
            var sc = _driver.Sim.Scenario;
            float r = Scenario.EngageRadius(_driver.ActiveDoctrine.CommsDiscipline);
            float r2 = r * r;
            for (int i = 0; i < state.AgentCount; i++)
            {
                if (state.Status[i] != (byte)AgentStatus.Active) continue;
                float dx = state.PosX[i] - sc.ThreatX[threat];
                float dy = state.PosY[i] - sc.ThreatY[threat];
                if (dx * dx + dy * dy <= r2) return true;
            }
            return false;
        }

        private void WorldLabel(Vector3 world, string text, Color color)
        {
            var sp = _cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;
            var rect = new Rect(sp.x - 70f, Screen.height - sp.y - 10f, 140f, 20f);
            // Cheap outline: dark shadow one pixel off, then the colored text.
            var shadow = rect; shadow.x += 1f; shadow.y += 1f;
            _label.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
            GUI.Label(shadow, text, _label);
            _label.normal.textColor = color;
            GUI.Label(rect, text, _label);
        }

        private void DrawFeed()
        {
            if (engagement == null || engagement.Feed.Count == 0) return;
            var feed = engagement.Feed;
            int shown = Mathf.Min(feedLines, feed.Count);
            float y = 14f;
            for (int k = feed.Count - 1; k >= feed.Count - shown; k--)
            {
                var e = feed[k];
                _sb.Length = 0;
                _sb.Append("T+").Append(e.SimSeconds.ToString("F1")).Append("s  ");
                _sb.Append(e.ThreatIndex >= 0 ? $"SAM-{e.ThreatIndex + 1}" : "AD FIRE");
                _sb.Append("  ✕  UAV-").Append(e.AgentIndex + 1);
                _sb.Append("  — ").Append(Cause(e.Flags));
                float age = feed.Count - 1 - k;
                var rect = new Rect(Screen.width - 470f, y, 456f, 18f);
                var shadow = rect; shadow.x += 1f; shadow.y += 1f;
                _feed.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
                GUI.Label(shadow, _sb.ToString(), _feed);
                var c = FeedColor;
                c.a = Mathf.Lerp(1f, 0.4f, age / Mathf.Max(1, shown - 1));
                _feed.normal.textColor = c;
                GUI.Label(rect, _sb.ToString(), _feed);
                y += 19f;
            }
        }

        /// <summary>§2 in one clause: the same attribution vocabulary as the
        /// post-run diagnosis panel, per kill, live.</summary>
        private static string Cause(DeathFlag flags)
        {
            bool jammed = (flags & DeathFlag.Jammed) != 0;
            if ((flags & DeathFlag.UnknownThreat) != 0)
            {
                return jammed ? "blind while jammed" : "never saw it";
            }
            if ((flags & DeathFlag.PressedKnown) != 0)
            {
                return (flags & DeathFlag.Detected) != 0
                    ? "pressed a known zone, detected"
                    : "pressed a known zone";
            }
            if ((flags & DeathFlag.Detected) != 0) return "detected (comms)";
            return "downed";
        }
    }
}
