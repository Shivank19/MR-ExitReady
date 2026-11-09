// Copyright (c) MR-ExitReady contributors.

using System;
using System.Collections.Generic;
using System.Text;
using Meta.XR.MRUtilityKit;
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
        private const int Capacity = 40;
        private static readonly List<string> Lines = new();

        public static event Action<string> LineAdded;

        public static IReadOnlyList<string> Recent => Lines;

        public static void Info(string message) => Write(message, LogType.Log);
        public static void Warn(string message) => Write(message, LogType.Warning);
        public static void Error(string message) => Write(message, LogType.Error);

        private static void Write(string message, LogType type)
        {
            var line = $"{Time.realtimeSinceStartup:F1}s {message}";
            if (Lines.Count >= Capacity)
            {
                Lines.RemoveAt(0);
            }

            Lines.Add(line);

            var tagged = $"[ExitReady-Scan] {line}";
            switch (type)
            {
                case LogType.Warning: Debug.LogWarning(tagged); break;
                case LogType.Error: Debug.LogError(tagged); break;
                default: Debug.Log(tagged); break;
            }

            LineAdded?.Invoke(line);
        }
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
        [Tooltip("Open Space Setup every launch. Turn off to only scan when no room is stored on the device.")]
        [SerializeField] private bool alwaysScan = true;

        [Tooltip("Seconds to wait for the user to answer the scene permission dialog.")]
        [SerializeField] private float permissionTimeout = 60f;

        private bool _running;

        private async void Start()
        {
            await RunScan();
        }

        /// <summary>Runs the scan flow again (e.g. from a UI button or the component context menu).</summary>
        [ContextMenu("Run room scan")]
        public async System.Threading.Tasks.Task RunScan()
        {
            if (_running)
            {
                ScanLog.Warn("Scan already in progress.");
                return;
            }

            _running = true;
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

#if !UNITY_EDITOR && UNITY_ANDROID
                if (alwaysScan)
                {
                    ScanLog.Info("Opening Space Setup - scan your rooms, then confirm to return to the app.");
                    var captured = await OVRScene.RequestSpaceSetup();
                    ScanLog.Info(captured
                        ? "Space Setup finished."
                        : "Space Setup was cancelled or failed; loading whatever is already stored.");
                }
#endif

                ScanLog.Info("Loading scene from device...");
                var result = await mruk.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: !alwaysScan, removeMissingRooms: true);
                ScanLog.Info($"LoadSceneFromDevice -> {result}");

                if (result == MRUK.LoadDeviceResult.Success)
                {
                    LogAllRooms(mruk);
                }
                else if (result == MRUK.LoadDeviceResult.NoRoomsFound)
                {
                    ScanLog.Warn("No rooms found. Complete Space Setup (Settings > Environment Setup) and relaunch.");
                }
            }
            catch (Exception e)
            {
                ScanLog.Error($"Room scan failed: {e}");
            }
            finally
            {
                _running = false;
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
