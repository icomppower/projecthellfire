using System;
using Hellfire.Sim;
using UnityEngine;

namespace Hellfire.Presentation
{
    /// <summary>
    /// Player-build render oracle. Rounds 10–11 proved the headless gate ladder
    /// cannot see render truth (shader stripping, stale layouts) — this closes
    /// part of that gap: launched with `--shot /path.png`, the built player
    /// captures its own framebuffer after a settle period and quits, giving the
    /// CLI a readable ground-truth image of what the player actually renders.
    /// `--autolaunch` starts a default-doctrine run first so combat is visible.
    /// Inert without the flag; never active in normal play.
    /// </summary>
    [RequireComponent(typeof(SimDriver))]
    public sealed class DebugShot : MonoBehaviour
    {
        private string _path;
        private bool _autolaunch;
        private int _captureFrame = 300;
        private bool _done;

        private void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--shot" && i + 1 < args.Length) _path = args[i + 1];
                if (args[i] == "--shot-frame" && i + 1 < args.Length
                    && int.TryParse(args[i + 1], out int f)) _captureFrame = f;
                if (args[i] == "--autolaunch") _autolaunch = true;
            }
            if (_path == null) enabled = false;
        }

        private void Update()
        {
            if (_done) return;
            if (_autolaunch && Time.frameCount == 30)
            {
                GetComponent<SimDriver>().LaunchWith(Doctrine.Default, 42UL);
            }
            if (Time.frameCount >= _captureFrame)
            {
                _done = true;
                ScreenCapture.CaptureScreenshot(_path);
                // CaptureScreenshot completes at end of frame; quit shortly after.
                Invoke(nameof(Quit), 1.5f);
            }
        }

        private void Quit() => Application.Quit();
    }
}
