// Copyright (c) MR-ExitReady contributors.

using System;
using System.Collections.Generic;
using System.Text;
using Meta.XR.MRUtilityKit;
using MRExitReady.Common;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace MRExitReady.Debugging
{
    /// <summary>
    /// Small in-memory log shared between <see cref="StartupRoomScan"/> and <see cref="RoomDebugHud"/>.
    /// Every line is also written to the Unity log with a <c>[ExitReady-Scan]</c> tag, so
    /// <c>adb logcat -s Unity | grep ExitReady-Scan</c> gives a live stream of the scan.
    /// </summary>
    public static class ScanLog
    {
        private static readonly TaggedLog Log = new("ExitReady-Scan");

        public static IReadOnlyList<string> Recent => Log.Recent;

        public static void Info(string message) => Log.Info(message);
        public static void Warn(string message) => Log.Warn(message);
        public static void Error(string message) => Log.Error(message);
    }

    /// <summary>
    /// Runs the room scan (Meta Space Setup) as the first thing the app does, then loads the
    /// result into MRUK and logs everything it finds.
    ///
    /// Sequence: scene permission -> Space Setup (system UI; the app is paused while it is open)
    /// -> <see cref="MRUK.LoadSceneFromDevice"/> -> per-room summary. MRUK's own
    /// <c>LoadSceneOnStartup</c> must be off in the scene, otherwise it would load (and possibly
    /// request capture) in parallel with this flow.
    ///
    /// In the Editor there is no Space Setup, so only the load step runs.
    /// </summary>
    public class StartupRoomScan : MonoBehaviour
    {
        [Tooltip("Open Space Setup every launch. Off (normal): load the stored room first and only open Space Setup if that finds nothing.")]
        [SerializeField] private bool alwaysScan = false;

        [Tooltip("Seconds to wait for the user to answer the scene permission dialog.")]
        [SerializeField] private float permissionTimeout = 60f;

        [Tooltip("Seconds to wait after the app regains focus before the first load, so tracking can settle.")]
        [SerializeField] private float settleSeconds = 1f;

        [Tooltip("Load attempts before giving up on transient failures.")]
        [SerializeField] private int maxLoadAttempts = 5;

        [Tooltip("Base delay between load attempts; attempt n waits n-1 times this.")]
        [SerializeField] private float retryDelaySeconds = 1.5f;

        private bool _running;

        /// <summary>
        /// Raised when a scan attempt ends. The value is the MRUK load result, or null if the
        /// scan could not get as far as loading (no MRUK, permission denied, exception).
        /// </summary>
        public static event Action<MRUK.LoadDeviceResult?> ScanFinished;

        /// <summary>Result of the most recent finished scan, for listeners that subscribe late.</summary>
        public static MRUK.LoadDeviceResult? LastResult { get; private set; }

        /// <summary>True once at least one scan attempt has finished.</summary>
        public static bool HasFinished { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ScanFinished = null;
            LastResult = null;
            HasFinished = false;
        }

        private async void Start()
        {
            await Run(alwaysScan);
        }

        /// <summary>Opens Space Setup to scan a room, then loads it (e.g. from a UI button or the context menu).</summary>
        [ContextMenu("Run room scan")]
        public System.Threading.Tasks.Task RunScan() => Run(true);

        /// <summary>Loads the stored rooms again without reopening Space Setup.</summary>
        [ContextMenu("Reload rooms")]
        public System.Threading.Tasks.Task ReloadRooms() => Run(false);

        private async System.Threading.Tasks.Task Run(bool openSpaceSetup)
        {
            if (_running)
            {
                ScanLog.Warn("Scan already in progress.");
                return;
            }

            _running = true;
            MRUK.LoadDeviceResult? finalResult = null;
            try
            {
                var mruk = MRUK.Instance;
                if (mruk == null)
                {
                    ScanLog.Error("No MRUK instance in the scene; cannot scan.");
                    return;
                }

                mruk.SceneLoadedEvent.RemoveListener(OnSceneLoaded);
                mruk.RoomCreatedEvent.RemoveListener(OnRoomCreated);
                mruk.RoomUpdatedEvent.RemoveListener(OnRoomUpdated);
                mruk.RoomRemovedEvent.RemoveListener(OnRoomRemoved);
                mruk.SceneLoadedEvent.AddListener(OnSceneLoaded);
                mruk.RoomCreatedEvent.AddListener(OnRoomCreated);
                mruk.RoomUpdatedEvent.AddListener(OnRoomUpdated);
                mruk.RoomRemovedEvent.AddListener(OnRoomRemoved);

                ScanLog.Info("Room scan starting.");

                if (!await EnsureScenePermission())
                {
                    ScanLog.Error("Scene permission denied; cannot scan or load rooms.");
                    return;
                }

                var openedSetup = false;
                if (openSpaceSetup)
                {
                    openedSetup = await OpenSpaceSetup();
                }

                LogDiagnostics();
                var result = await LoadWithRetries(mruk);

                // Like MRUK's own flow, but also for a generic Failure: some headsets answer a
                // query with no stored space with an error instead of "no rooms found", and MRUK
                // only opens Space Setup for the latter. Scan once, then try loading again.
                if (result != MRUK.LoadDeviceResult.Success && !openedSetup && NeedsScan(result))
                {
                    ScanLog.Info($"No usable room ({result}); opening Space Setup so you can scan one.");
                    if (await OpenSpaceSetup())
                    {
                        openedSetup = true;
                        result = await LoadWithRetries(mruk);
                    }
                }

                finalResult = result;

                if (result == MRUK.LoadDeviceResult.Success)
                {
                    LogAllRooms(mruk);
                }
                else
                {
                    ScanLog.Error($"Could not load the room: {result}. {Explain(result)}");
                }
            }
            catch (Exception e)
            {
                ScanLog.Error($"Room scan failed: {e}");
            }
            finally
            {
                _running = false;
                LastResult = finalResult;
                HasFinished = true;
                ScanFinished?.Invoke(finalResult);
            }
        }

        private void OnDestroy()
        {
            var mruk = MRUK.Instance;
            if (mruk == null)
            {
                return;
            }

            mruk.SceneLoadedEvent.RemoveListener(OnSceneLoaded);
            mruk.RoomCreatedEvent.RemoveListener(OnRoomCreated);
            mruk.RoomUpdatedEvent.RemoveListener(OnRoomUpdated);
            mruk.RoomRemovedEvent.RemoveListener(OnRoomRemoved);
        }

        private static void LogDiagnostics()
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            ScanLog.Info($"Diagnostics: OVRPlugin.initialized={OVRPlugin.initialized}, " +
                         $"xrSession={(OVRPlugin.GetNativeOpenXRSession() != 0 ? "ok" : "MISSING")}, " +
                         $"scenePermission={Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission)}, " +
                         $"hasInputFocus={OVRManager.hasInputFocus}, appFocused={Application.isFocused}.");
#endif
        }

        /// <summary>Opens Meta Space Setup (system UI). Returns false if it was cancelled or is unavailable.</summary>
        private static async System.Threading.Tasks.Task<bool> OpenSpaceSetup()
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            ScanLog.Info("Opening Space Setup - scan your room, then confirm to return to the app.");
            var captured = await OVRScene.RequestSpaceSetup();
            ScanLog.Info(captured
                ? "Space Setup finished."
                : "Space Setup was cancelled or failed.");
            return captured;
#else
            await System.Threading.Tasks.Task.CompletedTask;
            return false;
#endif
        }

        private async System.Threading.Tasks.Task<MRUK.LoadDeviceResult> LoadWithRetries(MRUK mruk)
        {
            var result = MRUK.LoadDeviceResult.Failure;
            for (var attempt = 1; attempt <= maxLoadAttempts; attempt++)
            {
                await WaitForFocus();
                await WaitSeconds(attempt == 1 ? settleSeconds : retryDelaySeconds * (attempt - 1));

                ScanLog.Info($"Loading scene from device (attempt {attempt}/{maxLoadAttempts})...");
                result = await mruk.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false,
                    removeMissingRooms: true, sceneModel: MRUK.SceneModel.V1);
                ScanLog.Info($"LoadSceneFromDevice -> {result} ({(int)result})");

                if (result == MRUK.LoadDeviceResult.Success || !IsRetryable(result) || attempt == maxLoadAttempts)
                {
                    break;
                }

                ScanLog.Warn($"{Explain(result)} Retrying...");
            }

            return result;
        }

        /// <summary>Results where scanning (again) is the sensible next step.</summary>
        private static bool NeedsScan(MRUK.LoadDeviceResult result) =>
            result == MRUK.LoadDeviceResult.Failure || result == MRUK.LoadDeviceResult.NoRoomsFound;

        private static bool IsRetryable(MRUK.LoadDeviceResult result) => result switch
        {
            MRUK.LoadDeviceResult.NotInitialized => true,
            MRUK.LoadDeviceResult.DiscoveryOngoing => true,
            MRUK.LoadDeviceResult.FailureInsufficientResources => true,
            MRUK.LoadDeviceResult.FailureInsufficientView => true,
            MRUK.LoadDeviceResult.FailureRateLimited => true,
            _ => false,
        };

        private static string Explain(MRUK.LoadDeviceResult result) => result switch
        {
            MRUK.LoadDeviceResult.NoScenePermission or MRUK.LoadDeviceResult.FailurePermissionInsufficient =>
                "Scene permission is missing: Settings > Apps > this app > Permissions > Spatial data.",
            MRUK.LoadDeviceResult.NoRoomsFound => "No room is stored. Finish Space Setup, including walls and doors.",
            MRUK.LoadDeviceResult.NotInitialized => "MRUK/OpenXR is not ready yet (or running in the Editor without Link).",
            MRUK.LoadDeviceResult.DiscoveryOngoing => "Another scene load is still running.",
            MRUK.LoadDeviceResult.FailureInsufficientView => "Look around the room so the headset can relocalize.",
            MRUK.LoadDeviceResult.FailureTooDark => "The room is too dark for tracking. Turn on a light.",
            MRUK.LoadDeviceResult.FailureTooBright => "The room is too bright for tracking.",
            MRUK.LoadDeviceResult.FailureRateLimited => "The system is rate limiting scene queries.",
            MRUK.LoadDeviceResult.StorageAtCapacity => "Spatial data storage is full. Delete old spaces in Settings.",
            _ => "The headset refused the room query. Scan the room in Space Setup; if it keeps failing, a Stationary boundary is a known cause (use Roomscale: Settings > Physical Space > Boundary).",
        };

        /// <summary>Waits until the app (and the XR session) has focus again after a system UI.</summary>
        private static async System.Threading.Tasks.Task WaitForFocus()
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            var logged = false;
            var deadline = Time.realtimeSinceStartup + 30f;
            while (!(Application.isFocused && OVRManager.hasInputFocus) && Time.realtimeSinceStartup < deadline)
            {
                if (!logged)
                {
                    ScanLog.Info("Waiting for the app to regain focus...");
                    logged = true;
                }

                await System.Threading.Tasks.Task.Yield();
            }
#endif
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private static async System.Threading.Tasks.Task WaitSeconds(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                await System.Threading.Tasks.Task.Yield();
            }
        }

        private async System.Threading.Tasks.Task<bool> EnsureScenePermission()
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            var permission = OVRPermissionsRequester.ScenePermission;
            if (Permission.HasUserAuthorizedPermission(permission))
            {
                return true;
            }

            ScanLog.Info("Requesting scene permission...");
            Permission.RequestUserPermission(permission);

            var deadline = Time.realtimeSinceStartup + permissionTimeout;
            while (!Permission.HasUserAuthorizedPermission(permission))
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    return false;
                }

                await System.Threading.Tasks.Task.Yield();
            }

            ScanLog.Info("Scene permission granted.");
#endif
            await System.Threading.Tasks.Task.CompletedTask;
            return true;
        }

        private static void OnSceneLoaded() => ScanLog.Info($"MRUK scene loaded: {MRUK.Instance.Rooms.Count} room(s).");
        private static void OnRoomCreated(MRUKRoom room) => ScanLog.Info($"Room created: {Describe(room)}");
        private static void OnRoomUpdated(MRUKRoom room) => ScanLog.Info($"Room updated: {Describe(room)}");
        private static void OnRoomRemoved(MRUKRoom room) => ScanLog.Info($"Room removed: {Describe(room)}");

        private static void LogAllRooms(MRUK mruk)
        {
            var rooms = mruk.Rooms;
            ScanLog.Info($"Scan complete: {rooms.Count} room(s).");
            for (var i = 0; i < rooms.Count; i++)
            {
                ScanLog.Info($"  room #{i}: {Describe(rooms[i])}");
            }
        }

        private static string Describe(MRUKRoom room)
        {
            if (room == null)
            {
                return "(null)";
            }

            var counts = new SortedDictionary<string, int>();
            foreach (var anchor in room.Anchors)
            {
                var key = anchor.Label.ToString();
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }

            var sb = new StringBuilder();
            sb.Append(room.Anchor.Uuid.ToString("N").Substring(0, 8));
            sb.Append($" anchors={room.Anchors.Count}");
            foreach (var kv in counts)
            {
                sb.Append($" {kv.Key}={kv.Value}");
            }

            return sb.ToString();
        }
    }
}
