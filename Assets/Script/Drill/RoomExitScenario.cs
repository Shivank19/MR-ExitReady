// Copyright (c) MR-ExitReady contributors.

using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using MRExitReady.Common;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// v1 scenario, "Get out of this room": a fire starts on a piece of the user's own
    /// furniture next to one of the doors, smoke banks down from the ceiling, and the user has to
    /// raise the alarm, check the doors, stay low and leave through the safe exit.
    ///
    /// Layout rules (all from the scan):
    /// <list type="bullet">
    /// <item>2+ doors: the fire goes on the furniture closest to a door; that door is hot.</item>
    /// <item>1 door: it is the safe exit; the fire goes on the furniture farthest from it.</item>
    /// <item>0 doors: a virtual exit is placed on the wall farthest from the user.</item>
    /// <item>No furniture: the fire burns on the floor.</item>
    /// </list>
    /// </summary>
    public class RoomExitScenario : IDrillScenario
    {
        public string Title => "Room exit drill";

        public string Briefing =>
            "<b>A fire is about to start in this room.</b>\n" +
            "Get out safely before time runs out.\n\n" +
            "<b>Trigger</b>  pull the fire alarm, spray the extinguisher\n" +
            "<b>Grip</b>      pick up the extinguisher, open a door\n" +
            "<b>Check a door</b>  hold a controller on it for a second,\n" +
            "                    without pressing anything\n\n" +
            "Smoke rises. Crouch to stay below it.";

        public string Instruction { get; private set; } = "";
        public string Warning { get; private set; }
        public IReadOnlyList<Objective> Objectives => _objectives;
        public bool IsFinished { get; private set; }

        private readonly List<Objective> _objectives = new();
        private readonly Objective _alarmObjective = new("Raise the alarm");
        private readonly Objective _checkObjective = new("Check a door before opening it");
        private readonly Objective _smokeObjective = new("Stay below the smoke");
        private readonly Objective _exitObjective = new("Get out through a safe exit");

        private DrillContext _ctx;
        private Transform _root;
        private FireHazard _fire;
        private SmokeLayer _smoke;
        private FireAlarmStation _alarm;
        private FireExtinguisher _extinguisher;
        private HeatCues _heat;
        private Atmosphere _atmosphere;
        private readonly List<ExitDoor> _doors = new();
        private Vector3 _startHead;
        private float _floorY;
        private float _standingHeight;
        private bool _wasInSmoke;

        public RoomExitScenario()
        {
            _objectives.Add(_alarmObjective);
            _objectives.Add(_checkObjective);
            _objectives.Add(_smokeObjective);
            _objectives.Add(_exitObjective);
        }

        public bool Setup(DrillContext context, out string error)
        {
            _ctx = context;
            var room = context.Room;
            if (room == null)
            {
                error = "No scanned room. Run Space Setup first.";
                return false;
            }

            _root = new GameObject("RoomExitScenario").transform;
            _root.SetParent(context.Root, false);

            var floorY = RoomGeometry.FloorY(room);
            var ceilingY = RoomGeometry.CeilingY(room, floorY);
            var roomCenter = RoomGeometry.Center(room);
            var head = HeadLocator.Head != null ? HeadLocator.Head.position : roomCenter;
            _startHead = head;
            _floorY = floorY;
            // Head height when the drill starts, as the user's standing height (for crouch tracking).
            _standingHeight = Mathf.Clamp(head.y - floorY, 1.2f, 2.1f);

            // ── Exits ──────────────────────────────────────────────────────────
            var doorAnchors = RoomGeometry.AnchorsWith(room, MRUKAnchor.SceneLabels.DOOR_FRAME);
            var exits = new List<(string name, Vector3 center, Vector3 normal, float width, float height, bool isVirtual)>();
            for (var i = 0; i < doorAnchors.Count; i++)
            {
                var a = doorAnchors[i];
                var size = a.PlaneRect.HasValue ? a.PlaneRect.Value.size : new Vector2(0.9f, 2.0f);
                exits.Add(($"door {i + 1}", a.GetAnchorCenter(), RoomGeometry.InwardNormal(room, a), size.x, size.y, false));
            }

            if (exits.Count == 0)
            {
                var wall = FarthestWall(room, head);
                if (wall == null)
                {
                    error = "This room has no walls or doors in its scan. Re-run Space Setup.";
                    Object.Destroy(_root.gameObject);
                    return false;
                }

                var wallCenter = wall.GetAnchorCenter();
                var center = new Vector3(wallCenter.x, floorY + 1.0f, wallCenter.z);
                exits.Add(("exit", center, RoomGeometry.InwardNormal(room, wall), 0.9f, 2.0f, true));
                DrillLog.Warn("No door in the scan; using a virtual exit on the far wall. Add doors in Space Setup for the full drill.");
            }

            // ── Fire placement ─────────────────────────────────────────────────
            var furniture = RoomGeometry.AnchorsWith(room, RoomGeometry.Furniture);
            var hotIndex = -1;
            Vector3 firePosition;
            float fireRadius;
            string fireOn;

            if (exits.Count >= 2)
            {
                // The furniture closest to any door; that door becomes the hot one.
                var best = float.MaxValue;
                MRUKAnchor bestAnchor = null;
                for (var d = 0; d < exits.Count; d++)
                {
                    foreach (var f in furniture)
                    {
                        var dist = RoomGeometry.FlatDistance(RoomGeometry.TopCenter(f), exits[d].center);
                        if (dist < best)
                        {
                            best = dist;
                            bestAnchor = f;
                            hotIndex = d;
                        }
                    }
                }

                if (bestAnchor != null)
                {
                    PlaceOnFurniture(bestAnchor, out firePosition, out fireRadius, out fireOn);
                }
                else
                {
                    hotIndex = FarthestFrom(exits, head);
                    var e = exits[hotIndex];
                    firePosition = new Vector3(e.center.x, floorY, e.center.z) + e.normal * 1.0f;
                    fireRadius = 0.35f;
                    fireOn = "floor";
                }
            }
            else
            {
                // One exit: keep it clear. Burn the furniture farthest from it, or a far corner.
                var exitCenter = exits[0].center;
                MRUKAnchor farthest = null;
                var bestDistance = -1f;
                foreach (var f in furniture)
                {
                    var dist = RoomGeometry.FlatDistance(RoomGeometry.TopCenter(f), exitCenter);
                    if (dist > bestDistance)
                    {
                        bestDistance = dist;
                        farthest = f;
                    }
                }

                if (farthest != null && bestDistance > 1.5f)
                {
                    PlaceOnFurniture(farthest, out firePosition, out fireRadius, out fireOn);
                }
                else
                {
                    firePosition = RoomGeometry.FarthestCorner(room, exitCenter, floorY, 0.7f);
                    fireRadius = 0.35f;
                    fireOn = "floor";
                }
            }

            _fire = FireHazard.Create(_root, context.Assets, firePosition, fireRadius,
                context.Settings.fireStartIntensity, context.Settings.fireGrowthPerSecond);
            _fire.Extinguished += () => context.Scorer.FireExtinguished(_fire.IntensityAtFirstSpray);
            _heat = HeatCues.Create(_root, context.Assets, _fire);

            // ── Doors ──────────────────────────────────────────────────────────
            var safeIndex = -1;
            for (var i = 0; i < exits.Count; i++)
            {
                var e = exits[i];
                var door = ExitDoor.Create(_root, context.Assets, e.name, e.center, e.normal, e.width, e.height,
                    i == hotIndex, e.isVirtual, HandIsBusy);
                door.OnChecked += OnDoorChecked;
                door.OnOpened += OnDoorOpened;
                _doors.Add(door);
                if (i != hotIndex && safeIndex < 0)
                {
                    safeIndex = i;
                }
            }

            // ── Alarm: beside the safe exit, at shoulder height ───────────────
            var safe = exits[safeIndex];
            var right = Vector3.Cross(Vector3.up, safe.normal).normalized;
            var alarmPos = safe.center + right * (Mathf.Clamp(safe.width, 0.6f, 2.5f) * 0.5f + 0.35f);
            alarmPos.y = floorY + 1.2f;
            alarmPos += safe.normal * 0.01f;
            _alarm = FireAlarmStation.Create(_root, context.Assets, alarmPos, safe.normal);
            _alarm.OnPulled += OnAlarmPulled;

            // ── Extinguisher: on the floor by the wall nearest the user ───────
            var extPos = ExtinguisherSpot(room, head, roomCenter, floorY, firePosition);
            _extinguisher = FireExtinguisher.Create(_root, context.Assets, extPos, floorY,
                context.Settings.extinguisherSeconds, _fire, context.Scorer);

            // ── Smoke ──────────────────────────────────────────────────────────
            _smoke = SmokeLayer.Create(_root, context.Assets, room.GetRoomBounds(), floorY, ceilingY,
                context.Settings.smokeMinHeight, context.Settings.smokeDescendSeconds);

            _atmosphere = Atmosphere.Create(_root, _fire, _alarm, _smoke);

            DrillLog.Info($"Scenario ready: {exits.Count} exit(s), hot = {(hotIndex >= 0 ? exits[hotIndex].name : "none")}, " +
                          $"safe = {safe.name}, fire on {fireOn}, floor {floorY:F2} m, ceiling {ceilingY:F2} m.");
            error = null;
            return true;
        }

        public void Tick(float elapsed, float remaining)
        {
            if (IsFinished)
            {
                return;
            }

            var scorer = _ctx.Scorer;

            // Walking counts as reacting.
            if (HeadLocator.Head != null &&
                RoomGeometry.FlatDistance(HeadLocator.Head.position, _startHead) > 0.5f)
            {
                scorer.MarkReaction("moved", "Started moving");
            }

            if (HeadLocator.Head != null)
            {
                var headPos = HeadLocator.Head.position;
                scorer.TrackFrame(headPos, headPos.y - _floorY, _standingHeight, _heat.FireDistance, _heat.Heat,
                    Time.deltaTime);
            }

            if (_extinguisher.IsHeld)
            {
                scorer.ExtinguisherPickedUp();
            }

            if (_smoke.HeadInSmoke)
            {
                scorer.AddSmokeTime(Time.deltaTime);
                if (!_wasInSmoke)
                {
                    scorer.SmokeEntered();
                    DrillLog.Info($"Head entered smoke (layer at {_smoke.BottomY:F2} m).");
                }
            }
            else if (_wasInSmoke)
            {
                DrillLog.Info("Head left smoke.");
            }

            _wasInSmoke = _smoke.HeadInSmoke;
            _smokeObjective.Failed = scorer.SmokeSeconds >= 3f;
            _smokeObjective.Done = !_smokeObjective.Failed && _smoke.IsLow;

            // Instructions escalate with what the user has (not) done yet.
            if (!_alarm.Pulled)
            {
                Instruction = "Fire! Pull the red alarm by the exit.";
            }
            else if (!scorer.CheckedAnyDoor)
            {
                Instruction = "Check a door: hold a controller on it.";
            }
            else
            {
                Instruction = "Open a cool door (grip) and get out.";
            }

            if (remaining < 15f)
            {
                Instruction = "LEAVE NOW. " + Instruction;
            }

            Warning = _heat.Heat >= 0.65f ? "Too close to the fire! Back away."
                : _smoke.HeadInSmoke ? "You're in the smoke. Crouch down!"
                : _extinguisher.IsSpraying && _fire.Intensity > 0.7f ? "This fire is too big to fight. Get out!"
                : null;
        }

        public void Freeze()
        {
            foreach (var door in _doors)
            {
                door.Lock();
            }

            if (_alarm != null)
            {
                _alarm.Silence();
            }

            if (_fire != null)
            {
                _fire.enabled = false;
            }

            if (_smoke != null)
            {
                _smoke.enabled = false;
            }

            if (_extinguisher != null)
            {
                _extinguisher.enabled = false;
            }

            if (_heat != null)
            {
                _heat.enabled = false;
            }

            if (_atmosphere != null)
            {
                _atmosphere.enabled = false;
            }

            ControllerInput.StopAll();
        }

        public void Teardown()
        {
            Freeze();

            // The extinguisher lives under the controller while held, outside the root.
            if (_extinguisher != null)
            {
                Object.Destroy(_extinguisher.gameObject);
            }

            if (_smoke != null)
            {
                Object.Destroy(_smoke.gameObject); // also removes the head tint quad
            }

            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
            }

            _doors.Clear();
        }

        // ── Events ──────────────────────────────────────────────────────────────

        private void OnAlarmPulled()
        {
            _ctx.Scorer.AlarmPulledEvent();
            _alarmObjective.Done = true;
        }

        private void OnDoorChecked(ExitDoor door)
        {
            _ctx.Scorer.DoorChecked(door.DisplayName, door.IsHot);
            _checkObjective.Done = true;
        }

        private void OnDoorOpened(ExitDoor door)
        {
            if (IsFinished)
            {
                return;
            }

            _ctx.Scorer.Exited(door.DisplayName, door.IsHot, door.Checked);
            if (!door.Checked)
            {
                _checkObjective.Failed = !_checkObjective.Done;
            }

            _exitObjective.Done = !door.IsHot;
            _exitObjective.Failed = door.IsHot;
            IsFinished = true;
        }

        private bool HandIsBusy(OVRInput.Controller hand) =>
            _extinguisher != null && _extinguisher.IsHeld && ControllerInput.Grip(hand) &&
            _extinguisher.transform.parent == ControllerInput.Anchor(hand);

        // ── Layout helpers ─────────────────────────────────────────────────────

        private static void PlaceOnFurniture(MRUKAnchor anchor, out Vector3 position, out float radius, out string label)
        {
            var bounds = RoomGeometry.WorldBounds(anchor);
            position = RoomGeometry.TopCenter(anchor);
            radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.8f;
            label = anchor.Label.ToString();
        }

        private static int FarthestFrom(
            List<(string name, Vector3 center, Vector3 normal, float width, float height, bool isVirtual)> exits,
            Vector3 point)
        {
            var index = 0;
            var best = -1f;
            for (var i = 0; i < exits.Count; i++)
            {
                var d = RoomGeometry.FlatDistance(exits[i].center, point);
                if (d > best)
                {
                    best = d;
                    index = i;
                }
            }

            return index;
        }

        private static MRUKAnchor FarthestWall(MRUKRoom room, Vector3 from)
        {
            MRUKAnchor best = null;
            var bestDistance = -1f;
            foreach (var wall in RoomGeometry.AnchorsWith(room, RoomGeometry.Walls))
            {
                var d = RoomGeometry.FlatDistance(wall.GetAnchorCenter(), from);
                if (d > bestDistance)
                {
                    bestDistance = d;
                    best = wall;
                }
            }

            return best;
        }

        private static Vector3 ExtinguisherSpot(MRUKRoom room, Vector3 head, Vector3 roomCenter, float floorY, Vector3 fire)
        {
            Vector3 spot;
            var distance = room.TryGetClosestSurfacePosition(head, out var surface, out var wall, out var normal,
                new LabelFilter(RoomGeometry.Walls));
            if (wall != null && !float.IsPositiveInfinity(distance))
            {
                var inward = RoomGeometry.Inward(roomCenter, surface, normal);
                spot = surface + inward * 0.3f;
            }
            else
            {
                spot = head + HeadLocator.FlatForward * 0.8f;
            }

            // Never start the extinguisher right next to the fire.
            if (RoomGeometry.FlatDistance(spot, fire) < 1.2f)
            {
                var away = head - fire;
                away.y = 0f;
                spot = head + (away.sqrMagnitude > 1e-4f ? away.normalized : HeadLocator.FlatForward) * 0.6f;
            }

            spot.y = floorY;
            return spot;
        }
    }
}
