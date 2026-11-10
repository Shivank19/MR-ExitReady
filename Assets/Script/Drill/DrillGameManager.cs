// Copyright (c) MR-ExitReady contributors.

using Meta.XR.MRUtilityKit;
using MRExitReady.Debugging;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// Runs one drill as a match: wait for the room scan, show the briefing, count down, run the
    /// scenario against a timer, then show the readiness score. A restarts from the debrief.
    ///
    /// Put this on one GameObject in the scene and assign the three materials in
    /// Assets/Script/Drill/Materials (the references also make sure their shaders are in the build).
    /// </summary>
    public class DrillGameManager : MonoBehaviour
    {
        private enum Phase
        {
            WaitingForScan,
            Briefing,
            Countdown,
            Active,
            Debrief,
        }

        [Header("Materials (Assets/Script/Drill/Materials)")]
        [SerializeField] private Material particleAdditive;
        [SerializeField] private Material particleAlpha;
        [SerializeField] private Material propMaterial;

        [Header("Match")]
        [Tooltip("Seconds the user has to get out.")]
        [SerializeField] private float timeLimit = 90f;

        [SerializeField] private float countdownSeconds = 3f;

        [Header("Tuning")]
        [SerializeField] private ScenarioSettings scenario = new();
        [SerializeField] private ScoreWeights scoring = new();

        private Phase _phase = Phase.WaitingForScan;
        private ProceduralAssets _assets;
        private DrillHud _hud;
        private Transform _hudRoot;
        private readonly IDrillScenario _preview = new RoomExitScenario();
        private IDrillScenario _scenario;
        private StartupRoomScan _scanner;
        private DrillScorer _scorer;
        private ScoreReport _report;
        private float _phaseStart;
        private string _setupError;
        private int _lastTickSecond = -1;

        /// <summary>Seconds left at which the left controller taps, so the timer can be felt.</summary>
        private static readonly int[] TickSeconds = { 30, 15, 5, 4, 3, 2, 1 };

        private float PhaseTime => Time.time - _phaseStart;

        private void Awake()
        {
            if (particleAdditive == null || particleAlpha == null || propMaterial == null)
            {
                DrillLog.Error("DrillGameManager is missing its materials; assign them from Assets/Script/Drill/Materials.");
                enabled = false;
                return;
            }

            _assets = new ProceduralAssets(particleAdditive, particleAlpha, propMaterial);
            _hudRoot = new GameObject("DrillHud").transform;
            _hudRoot.SetParent(transform, false);
            _hud = new DrillHud(_hudRoot);
            _scanner = FindAnyObjectByType<StartupRoomScan>();
            _hud.ShowMessage("Scanning your room", "Follow the Space Setup prompts.", ScanLog.Recent);
        }

        private void OnDisable()
        {
            ControllerInput.StopAll();
        }

        private void Update()
        {
            ControllerInput.Tick();
            _hud.Follow();

            switch (_phase)
            {
                case Phase.WaitingForScan: UpdateWaitingForScan(); break;
                case Phase.Briefing: UpdateBriefing(); break;
                case Phase.Countdown: UpdateCountdown(); break;
                case Phase.Active: UpdateActive(); break;
                case Phase.Debrief: UpdateDebrief(); break;
            }
        }

        private static bool StartPressed() => OVRInput.GetDown(OVRInput.RawButton.A | OVRInput.RawButton.X);

        // ── Phases ─────────────────────────────────────────────────────────────

        private void UpdateWaitingForScan()
        {
            var mruk = MRUK.Instance;
            var hasRooms = mruk != null && mruk.Rooms.Count > 0;
            var scanDone = _scanner == null || StartupRoomScan.HasFinished;

            if (hasRooms && scanDone)
            {
                Enter(Phase.Briefing);
                return;
            }

            if (scanDone && !hasRooms)
            {
                var result = StartupRoomScan.LastResult.HasValue ? StartupRoomScan.LastResult.Value.ToString() : "not loaded";
                _hud.ShowMessage("Room didn't load",
                    $"Result: <color=#ffd166>{result}</color>\nThe drill can't run without a scanned room.\n" +
                    "If your boundary is <b>Stationary</b>, switch to <b>Roomscale</b>\n" +
                    "(Settings > Physical Space > Boundary), rescan the room\nin Space Setup, then relaunch.\n" +
                    "<color=#8ce99a><b>Press A to scan your room</b></color>",
                    ScanLog.Recent);
                if (StartPressed() && _scanner != null)
                {
                    _ = _scanner.RunScan();
                }

                return;
            }

            _hud.ShowMessage("Scanning your room", "Follow the Space Setup prompts.", ScanLog.Recent);
        }

        private void UpdateBriefing()
        {
            if (StartPressed())
            {
                if (TrySetUpScenario())
                {
                    Enter(Phase.Countdown);
                    return;
                }
            }

            var briefing = StartupRoomScan.UsingFallbackRoom
                ? _preview.Briefing + "\n\n<color=#ffd166>Your room scan couldn't be loaded, so this drill uses a " +
                  "sample room. Its walls and doors aren't your real ones (R-stick shows them).</color>"
                : _preview.Briefing;
            _hud.ShowBriefing(_preview.Title, briefing, _setupError);
        }

        private void UpdateCountdown()
        {
            var left = countdownSeconds - PhaseTime;
            if (left <= 0f)
            {
                _scorer.Begin();
                Enter(Phase.Active);
                return;
            }

            _hud.ShowCountdown(Mathf.CeilToInt(left));
        }

        private void UpdateActive()
        {
            var elapsed = PhaseTime;
            var remaining = Mathf.Max(0f, timeLimit - elapsed);

            _scenario.Tick(elapsed, remaining);
            TimerHaptics(remaining);

            if (_scenario.IsFinished || remaining <= 0f)
            {
                if (!_scenario.IsFinished)
                {
                    _scorer.TimedOut();
                }

                _scenario.Freeze();
                _report = _scorer.Build();
                Enter(Phase.Debrief);
                return;
            }

            _hud.ShowActive(_scenario.Title, remaining, _scenario.Instruction, _scenario.Warning, _scenario.Objectives);
        }

        private void UpdateDebrief()
        {
            _hud.ShowDebrief(_scenario.Title, _report, timeLimit);

            // Short guard so the press that ended the drill can't also restart it.
            if (PhaseTime > 1f && StartPressed())
            {
                _scenario.Teardown();
                _scenario = null;
                if (TrySetUpScenario())
                {
                    Enter(Phase.Countdown);
                }
                else
                {
                    Enter(Phase.Briefing);
                }
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private bool TrySetUpScenario()
        {
            var mruk = MRUK.Instance;
            var room = mruk != null ? mruk.GetCurrentRoom() : null;
            if (room == null && mruk != null && mruk.Rooms.Count > 0)
            {
                room = mruk.Rooms[0];
            }

            _scorer = new DrillScorer(scoring);
            _scenario = new RoomExitScenario();
            var context = new DrillContext
            {
                Root = transform,
                Assets = _assets,
                Scorer = _scorer,
                Room = room,
                Settings = scenario,
            };

            if (_scenario.Setup(context, out _setupError))
            {
                _setupError = null;
                return true;
            }

            DrillLog.Error($"Scenario setup failed: {_setupError}");
            _scenario = null;
            return false;
        }

        private void TimerHaptics(float remaining)
        {
            var second = Mathf.CeilToInt(remaining);
            if (second == _lastTickSecond)
            {
                return;
            }

            _lastTickSecond = second;
            if (System.Array.IndexOf(TickSeconds, second) >= 0)
            {
                ControllerInput.Pulse(OVRInput.Controller.LTouch, second <= 5 ? 0.8f : 0.5f, second <= 5 ? 0.12f : 0.25f);
            }
        }

        private void Enter(Phase phase)
        {
            _lastTickSecond = -1;
            DrillLog.Info($"Phase: {_phase} -> {phase}");
            _phase = phase;
            _phaseStart = Time.time;
        }
    }
}
