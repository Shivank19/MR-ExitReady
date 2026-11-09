// Copyright (c) MR-ExitReady contributors.

using System;
using System.Collections.Generic;
using System.Text;
using Meta.XR.MRUtilityKit;
using MRExitReady.Common;
using UnityEngine;

namespace MRExitReady.Debugging
{
    /// <summary>
    /// A lazily head-following panel that reports what MRUK currently knows: how many rooms
    /// loaded, which room the headset is in, how that compares with a geometric containment
    /// test, the anchor census for the current room, and the distance to the nearest wall and
    /// door. The whole panel is built from code so there is nothing to wire up in the Inspector.
    ///
    /// The <c>MRUK room</c> / <c>Geometry</c> rows are the interesting ones when walking between
    /// rooms: <see cref="MRUK.GetCurrentRoom"/> is answered by MRUK's native library, while the
    /// <c>Geometry</c> row is a plain <see cref="MRUKRoom.IsPositionInRoom"/> sweep over every
    /// loaded room. In a doorway the two disagree, and each transition is also written to the
    /// Unity log so it shows up in <c>adb logcat</c> with a timestamp.
    /// </summary>
    public class RoomDebugHud : MonoBehaviour
    {
        [Header("Placement")]
        [Tooltip("Metres in front of the headset.")]
        [SerializeField] private float distance = 0.85f;

        [Tooltip("Metres above (+) or below (-) eye height.")]
        [SerializeField] private float verticalOffset = -0.3f;

        [Tooltip("Degrees of head yaw tolerated before the panel drifts back into view.")]
        [SerializeField] private float deadZoneAngle = 30f;

        [Tooltip("How quickly the panel catches up once it is outside the dead zone.")]
        [SerializeField] private float followSpeed = 3f;

        [Header("Panel")]
        [Tooltip("Panel size in millimetres (the canvas is scaled to metres).")]
        [SerializeField] private Vector2 panelSize = new Vector2(640f, 760f);

        [SerializeField] private float textSize = 20f;

        [Tooltip("Toggle this panel by clicking the left thumbstick.")]
        [SerializeField] private bool allowVisibilityToggle = true;

        [Tooltip("Start with the panel hidden (the fire drill HUD is the main panel).")]
        [SerializeField] private bool startHidden;

        [Tooltip("How many of the latest room-scan log lines to show at the bottom of the panel.")]
        [SerializeField] private int scanLogLines = 8;

        [Header("Logging")]
        [Tooltip("Write a log line whenever the detected room changes.")]
        [SerializeField] private bool logRoomTransitions = true;

        private HeadFollowPanel _panel;
        private readonly StringBuilder _sb = new();
        private readonly Dictionary<Guid, int> _roomIndices = new();

        private Guid _lastNativeRoom = Guid.Empty;
        private int _transitions;
        private float _nextRefresh;

        private void Awake()
        {
            _panel = new HeadFollowPanel(transform, panelSize, textSize, new Color(0.03f, 0.05f, 0.08f, 0.78f))
            {
                Distance = distance,
                VerticalOffset = verticalOffset,
                DeadZoneAngle = deadZoneAngle,
                FollowSpeed = followSpeed,
            };
            _panel.Text.text = "MRUK debug HUD starting...";
            _panel.Visible = !startHidden;
        }

        private void Update()
        {
            if (allowVisibilityToggle && OVRInput.GetDown(OVRInput.RawButton.LThumbstick))
            {
                _panel.Visible = !_panel.Visible;
            }

            _panel.Follow();

            // Four refreshes a second is plenty and keeps the string building off the hot path.
            if (Time.unscaledTime < _nextRefresh)
            {
                return;
            }

            _nextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        private void Refresh()
        {
            _sb.Clear();
            _sb.AppendLine("<b>MRUK scene debug</b>");

            var mruk = MRUK.Instance;
            if (mruk == null)
            {
                _sb.AppendLine("<color=#ff6b6b>No MRUK instance in the scene.</color>");
                AppendScanLog();
                _panel.Text.text = _sb.ToString();
                return;
            }

            var rooms = mruk.Rooms;
            RebuildRoomIndices(rooms);

            _sb.AppendLine($"Rooms loaded: <b>{rooms.Count}</b>");

            if (rooms.Count == 0)
            {
                _sb.AppendLine("<color=#ffd166>Waiting for scene data. Run Space Setup on the headset, then relaunch.</color>");
                AppendScanLog();
                _panel.Text.text = _sb.ToString();
                return;
            }

            var nativeRoom = mruk.GetCurrentRoom();
            var headPosition = HeadLocator.Head != null ? HeadLocator.Head.position : Vector3.zero;
            var geometricRoom = FindRoomByGeometry(rooms, headPosition);

            TrackTransition(nativeRoom);

            _sb.AppendLine($"MRUK room:   {Describe(nativeRoom)}");
            _sb.AppendLine($"Geometry:    {Describe(geometricRoom)}");

            if (nativeRoom != geometricRoom)
            {
                _sb.AppendLine("<color=#ffd166>  ^ the two disagree - you are probably in a doorway.</color>");
            }

            _sb.AppendLine($"Transitions: {_transitions}");
            _sb.AppendLine($"Head:        ({headPosition.x:F2}, {headPosition.y:F2}, {headPosition.z:F2})");

            // World lock is the thing that drifts. MRUK rewrites the TrackingSpace pose every
            // frame so the localized room's anchors line up with the real room, so watching the
            // tracking-space offset is the most direct read on "how much did everything shift".
            if (!mruk.EnableWorldLock)
            {
                _sb.AppendLine("World lock: <color=#ffd166>disabled</color>");
            }
            else
            {
                _sb.AppendLine(mruk.IsWorldLockActive
                    ? "World lock: <color=#8ce99a>active</color>"
                    : "World lock: <color=#ff6b6b>lost localization</color>");
            }

            var rig = HeadLocator.Rig;
            var trackingSpace = rig != null ? rig.trackingSpace : null;
            if (trackingSpace != null)
            {
                var offset = trackingSpace.position;
                _sb.AppendLine($"Trk offset:  ({offset.x:F3}, {offset.y:F3}, {offset.z:F3})  yaw {trackingSpace.eulerAngles.y:F1}deg");
            }

            if (nativeRoom != null)
            {
                AppendAnchorCensus(nativeRoom);
                AppendNearestSurfaces(nativeRoom, headPosition);
            }

            AppendScanLog();

            _sb.AppendLine();
            _sb.AppendLine("<size=80%>R-stick click: meshes on/off   B/Y: recentre menu   L-stick click: this panel</size>");

            _panel.Text.text = _sb.ToString();
        }

        private void AppendScanLog()
        {
            var lines = ScanLog.Recent;
            if (lines.Count == 0)
            {
                return;
            }

            _sb.AppendLine();
            _sb.AppendLine("<b>Scan log</b>");
            _sb.Append("<size=75%>");
            for (var i = Mathf.Max(0, lines.Count - scanLogLines); i < lines.Count; i++)
            {
                // The panel does not wrap; the full line is always in logcat.
                var line = lines[i];
                _sb.AppendLine(line.Length > 80 ? line.Substring(0, 79) + "…" : line);
            }

            _sb.AppendLine("</size>");
        }

        private void AppendAnchorCensus(MRUKRoom room)
        {
            int walls = 0, floors = 0, ceilings = 0, doors = 0, windows = 0, furniture = 0, other = 0;

            var anchors = room.Anchors;
            for (var i = 0; i < anchors.Count; i++)
            {
                var label = anchors[i].Label;
                if ((label & (MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE |
                              MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE)) != 0)
                {
                    walls++;
                }
                else if ((label & MRUKAnchor.SceneLabels.FLOOR) != 0)
                {
                    floors++;
                }
                else if ((label & MRUKAnchor.SceneLabels.CEILING) != 0)
                {
                    ceilings++;
                }
                else if ((label & MRUKAnchor.SceneLabels.DOOR_FRAME) != 0)
                {
                    doors++;
                }
                else if ((label & MRUKAnchor.SceneLabels.WINDOW_FRAME) != 0)
                {
                    windows++;
                }
                else if ((label & (MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH |
                                   MRUKAnchor.SceneLabels.BED | MRUKAnchor.SceneLabels.STORAGE |
                                   MRUKAnchor.SceneLabels.SCREEN | MRUKAnchor.SceneLabels.LAMP |
                                   MRUKAnchor.SceneLabels.PLANT | MRUKAnchor.SceneLabels.WALL_ART)) != 0)
                {
                    furniture++;
                }
                else
                {
                    other++;
                }
            }

            _sb.AppendLine($"Anchors:     {anchors.Count}  (wall {walls}, floor {floors}, ceiling {ceilings},");
            _sb.AppendLine($"             door {doors}, window {windows}, furniture {furniture}, other {other})");
        }

        private void AppendNearestSurfaces(MRUKRoom room, Vector3 headPosition)
        {
            var wallDistance = room.TryGetClosestSurfacePosition(
                headPosition, out _, out _,
                new LabelFilter(MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE));

            var doorDistance = room.TryGetClosestSurfacePosition(
                headPosition, out _, out var doorAnchor,
                new LabelFilter(MRUKAnchor.SceneLabels.DOOR_FRAME));

            _sb.AppendLine($"Nearest wall: {Metres(wallDistance)}");
            _sb.AppendLine(doorAnchor != null
                ? $"Nearest door: {Metres(doorDistance)}"
                : "Nearest door: none in this room");
        }

        private void TrackTransition(MRUKRoom nativeRoom)
        {
            var uuid = nativeRoom != null ? nativeRoom.Anchor.Uuid : Guid.Empty;
            if (uuid == _lastNativeRoom)
            {
                return;
            }

            if (_lastNativeRoom != Guid.Empty || uuid != Guid.Empty)
            {
                _transitions++;
                if (logRoomTransitions)
                {
                    Debug.Log($"[RoomDebugHud] t={Time.unscaledTime:F2}s room {Short(_lastNativeRoom)} -> {Short(uuid)} " +
                              $"(transition #{_transitions})");
                }
            }

            _lastNativeRoom = uuid;
        }

        private void RebuildRoomIndices(IReadOnlyList<MRUKRoom> rooms)
        {
            if (_roomIndices.Count == rooms.Count)
            {
                return;
            }

            _roomIndices.Clear();
            for (var i = 0; i < rooms.Count; i++)
            {
                if (rooms[i] != null)
                {
                    _roomIndices[rooms[i].Anchor.Uuid] = i;
                }
            }
        }

        private MRUKRoom FindRoomByGeometry(IReadOnlyList<MRUKRoom> rooms, Vector3 position)
        {
            for (var i = 0; i < rooms.Count; i++)
            {
                if (rooms[i] != null && rooms[i].IsPositionInRoom(position))
                {
                    return rooms[i];
                }
            }

            return null;
        }

        private string Describe(MRUKRoom room)
        {
            if (room == null)
            {
                return "<color=#ffd166>none</color>";
            }

            var uuid = room.Anchor.Uuid;
            var index = _roomIndices.TryGetValue(uuid, out var i) ? i : -1;
            return $"#{index}  {Short(uuid)}";
        }

        private static string Short(Guid uuid)
        {
            return uuid == Guid.Empty ? "-" : uuid.ToString("N").Substring(0, 8);
        }

        private static string Metres(float distance)
        {
            return float.IsPositiveInfinity(distance) || distance <= 0f ? "n/a" : $"{distance:F2} m";
        }
    }
}
