using Hellfire.Sim;
using UnityEngine;

namespace Hellfire.Presentation
{
    /// <summary>
    /// The player layer (GDD §4 step 6), holding the §1 identity line: you
    /// author doctrine BEFORE the run — sliders lock at launch — then you watch,
    /// with 2–3 blunt swarm-level interrupts and nothing else. Post-run, the §2
    /// contract: every loss names the doctrine parameter responsible.
    /// IMGUI on purpose: zero assets, fully procedural, scope cut to what keeps
    /// the experiment honest.
    /// </summary>
    [RequireComponent(typeof(SimDriver))]
    public sealed class CommanderUI : MonoBehaviour
    {
        private SimDriver _driver;
        private float _autonomy = 0.5f;
        private float _risk = 0.5f;
        private float _comms = 0.5f;
        private float _abortAt = 0.5f;
        private float _reserve = 0.25f;
        private string _seedText = "42";
        private bool _usedAbort, _usedFallBack, _usedCommit;
        private int _peakJamExposure;

        private void Awake()
        {
            _driver = GetComponent<SimDriver>();
        }

        private void OnGUI()
        {
            // IMGUI event, not Input.*: works under either input backend, and in
            // a fullscreen player build ESC is the expected way out.
            var ev = Event.current;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
            {
                Application.Quit();
            }

            GUILayout.BeginArea(new Rect(12, 12, 320, Screen.height - 24), GUI.skin.box);
            GUILayout.Label("<b>PROJECT HELLFIRE</b> — swarm transit through an air-defense belt",
                new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
            GUILayout.Label("ESC quits", new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(1f, 1f, 1f, 0.45f) },
            });

            if (!_driver.Running) DrawAuthoring();
            else DrawRun();

            GUILayout.EndArea();
        }

        private void DrawAuthoring()
        {
            GUILayout.Label("Author doctrine. It locks at launch — after that you only watch.",
                Wrapped());
            _autonomy = Axis("Autonomy (centralized ↔ decentralized)", _autonomy);
            _risk = Axis("Risk tolerance (evade ↔ press on)", _risk);
            _comms = Axis("Comms discipline (chatty ↔ silent)", _comms);
            _abortAt = Axis("Abort at loss fraction", _abortAt);
            _reserve = Axis("Reserve held back", Mathf.Min(_reserve, 0.4f), 0.4f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed", GUILayout.Width(40));
            _seedText = GUILayout.TextField(_seedText);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            if (GUILayout.Button("LAUNCH", GUILayout.Height(34)))
            {
                if (!ulong.TryParse(_seedText, out ulong seed)) seed = 42;
                _usedAbort = _usedFallBack = _usedCommit = false;
                _peakJamExposure = 0;
                _driver.LaunchWith(new Doctrine
                {
                    Autonomy = _autonomy,
                    RiskTolerance = _risk,
                    CommsDiscipline = _comms,
                    AbortLossFraction = _abortAt,
                    ReserveFraction = _reserve,
                }, seed);
            }
        }

        private void DrawRun()
        {
            var s = _driver.State;
            var sc = _driver.Sim.Scenario;
            int active = s.CountStatus(AgentStatus.Active);
            int dead = s.CountStatus(AgentStatus.Dead);
            int completed = s.CountStatus(AgentStatus.Completed);
            int safe = s.CountStatus(AgentStatus.Safe);
            int reserve = s.CountStatus(AgentStatus.Reserve);
            int jammedNow = s.JammedNowCount;
            if (jammedNow > _peakJamExposure) _peakJamExposure = jammedNow;

            GUILayout.Label($"T+{s.Tick / 60f:F1}s   EW sites this run: {sc.JammerCount}");
            GUILayout.Label($"active {active}   reserve {reserve}   delivered {completed}   dead {dead}   home {safe}");
            GUILayout.Label($"jammed now: {jammedNow}");
            if (s.Aborted) GUILayout.Label("MISSION ABORTED — swarm returning");
            else if (s.Tick < s.RecallUntilTick)
                GUILayout.Label($"FALLING BACK — resumes in {(s.RecallUntilTick - s.Tick) / 60f:F0}s");

            _driver.timeScale = GUILayout.HorizontalSlider(_driver.timeScale, 0f, 8f);
            GUILayout.Label($"time ×{_driver.timeScale:F1}");

            GUILayout.Space(6);
            GUILayout.Label($"Commander interrupts ({InterruptPlan.MaxOrders - _driver.InterruptsUsed} left) — swarm-level only:",
                Wrapped());
            GUI.enabled = !_driver.Finished && !_usedAbort && !s.Aborted;
            if (GUILayout.Button("ABORT — mission off, everyone home"))
                _usedAbort = _driver.TryInterrupt(InterruptType.Abort);
            GUI.enabled = !_driver.Finished && !_usedFallBack && !s.Aborted;
            if (GUILayout.Button($"FALL BACK — recall {Simulation.RecallDurationTicks / 60}s, then resume"))
                _usedFallBack = _driver.TryInterrupt(InterruptType.FallBack);
            GUI.enabled = !_driver.Finished && !_usedCommit && reserve > 0;
            if (GUILayout.Button($"COMMIT RESERVE — launch the held {reserve}"))
                _usedCommit = _driver.TryInterrupt(InterruptType.CommitReserve);
            GUI.enabled = true;

            if (_driver.Finished)
            {
                GUILayout.Space(8);
                GUILayout.Label("<b>Post-run diagnosis (§2: every loss names its doctrine parameter)</b>",
                    new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
                GUILayout.Label($"peak jam exposure: {_peakJamExposure} agents");
                DrawDiagnosis(s);
                GUILayout.Space(6);
                if (GUILayout.Button("NEW RUN", GUILayout.Height(30))) Relaunch();
            }
        }

        private void DrawDiagnosis(SimState s)
        {
            int deaths = s.CountStatus(AgentStatus.Dead);
            if (deaths == 0)
            {
                GUILayout.Label("No losses. Nothing to re-examine.", Wrapped());
                return;
            }
            float inv = 1f / deaths;
            int unknown = s.CountDeathFlag(DeathFlag.UnknownThreat);
            int jammed = s.CountDeathFlag(DeathFlag.Jammed);
            int detected = s.CountDeathFlag(DeathFlag.Detected);
            int pressed = s.CountDeathFlag(DeathFlag.PressedKnown);

            // Same attribution mapping as Scorer.Diagnose, per single run.
            int unknownJammed = Mathf.Min(unknown, jammed);
            int unknownBlind = unknown - unknownJammed;
            if (unknownJammed > 0)
                Line($"Autonomy too low for this EW: {unknownJammed * inv:P0} killed blind while jammed");
            if (unknownBlind > 0)
                Line($"Autonomy/SensorRange: {unknownBlind * inv:P0} killed by threats never seen");
            if (detected > 0)
                Line($"CommsDiscipline too chatty: {detected * inv:P0} killed in the detectability band");
            if (pressed > 0)
                Line($"RiskTolerance: {pressed * inv:P0} pressed known kill zones");
            if (s.Aborted)
                Line("AbortLossFraction: the loss threshold ended this mission");
        }

        private void Relaunch()
        {
            // Back to the authoring panel with doctrine editable again.
            _seedText = (ulong.TryParse(_seedText, out ulong prev) ? prev + 1 : 43UL).ToString();
            var driver = _driver;
            driver.timeScale = 1f;
            // Running resets on next LaunchWith; flip the panel by resetting sim state.
            driver.HaltToAuthoring();
        }

        private static void Line(string text) => GUILayout.Label("• " + text, Wrapped());

        private static GUIStyle Wrapped() => new GUIStyle(GUI.skin.label) { wordWrap = true };

        private static float Axis(string label, float value, float max = 1f)
        {
            GUILayout.Label(label);
            return GUILayout.HorizontalSlider(value, 0f, max);
        }
    }
}
