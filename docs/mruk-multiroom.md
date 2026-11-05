# How MRUK exposes multiple rooms, and how to tell which one you are in

Notes for milestone 4. Everything below was read out of MR Utility Kit v207 / v205
(`Core/Scripts/MRUK.cs`, `MRUK.Shared.cs`, `MRUKRoom.cs`, `MRUKAnchor.cs`) rather than from the
docs site, so it describes what the code actually does.

## The data model

Space Setup writes **one room anchor per room** to the device. MRUK loads all of them.

```
MRUK (singleton MonoBehaviour)
└── Rooms : List<MRUKRoom>            // one entry per scanned room
    └── MRUKRoom                      // a GameObject named "Room - <uuid>"
        ├── Anchor : OVRAnchor        // .Uuid is the stable room identity
        ├── Anchors : List<MRUKAnchor>        // every anchor in this room
        ├── WallAnchors / FloorAnchors / CeilingAnchors
        ├── GlobalMeshAnchor
        └── RoomMeshData              // vertices + per-face semantic labels
            └── MRUKAnchor            // child GameObject per anchor
                ├── Label : SceneLabels   // WALL_FACE, FLOOR, DOOR_FRAME, TABLE, ...
                ├── PlaneRect : Rect?     // planar anchors (walls, floor, doors)
                ├── VolumeBounds : Bounds? // volumetric anchors (furniture)
                └── ParentAnchor          // e.g. a door frame's wall
```

Two details that matter in practice:

- Room GameObjects are created **at the scene root**, not parented under the `MRUK` object. The
  `MRUK` GameObject's own transform is therefore irrelevant to alignment — rooms and anchors are
  posed in world space (`MRUK.Shared.cs`, `UpdateRoomProperties` / `SetTransform`).
- Anchors *are* parented under their room, so anything you attach to an anchor moves with the
  room. `MRUKAnchor` logs an error if third-party code moves its transform; re-skin the renderer
  instead of touching the transform.

### Loading

`MRUK.SceneSettings` controls it:

| Field | Meaning |
| --- | --- |
| `DataSource` | `Device` (what we use), `Prefab`, `DeviceWithPrefabFallback`, `Json`, `DeviceWithJsonFallback` |
| `RoomIndex` | `-1` loads **every** room; `>= 0` loads only that one |
| `LoadSceneOnStartup` | load during `MRUK.Awake` |

`DeviceWithPrefabFallback` is worth switching to while iterating in the editor: it keeps using
real device data on the headset but falls back to a prefab room on the desktop, so you can test
without deploying.

### Events

```csharp
MRUK.Instance.RegisterSceneLoadedCallback(...);   // all rooms available
MRUK.Instance.RegisterRoomCreatedCallback(...);   // one room appeared
MRUK.Instance.RegisterRoomUpdatedCallback(...);   // room geometry changed
MRUK.Instance.RegisterRoomRemovedCallback(...);
```

Rooms can appear *after* scene load (re-running Space Setup, or walking into a room that
localizes late), so anything that decorates anchors has to handle late arrivals. That is why
`SceneLabelColorizer` re-scans on a timer instead of only reacting to `SceneLoadedEvent`.

Per-room filtering for the built-in helpers is `MRUK.RoomFilter`: `None`, `CurrentRoomOnly`,
`AllRooms`. `EffectMesh.SpawnOnStart` uses it — ours is set to `AllRooms`.

## Detecting the current room

Two independent answers, and the difference between them is the interesting signal.

**1. Ask MRUK.** `MRUK.Instance.GetCurrentRoom()` returns the `MRUKRoom` for the headset's
current position. It is answered by MRUK's native library from the headset pose, not by Unity
geometry. Caveats from the source: if the headset has never been in a valid room it returns the
first room in the list, and it returns `null` when no rooms are loaded. So `GetCurrentRoom()`
being non-null is *not* proof that you are inside that room.

**2. Test the geometry yourself.** `room.IsPositionInRoom(worldPosition, testVerticalBounds)`
does a point-in-floor-outline test (also native, but purely geometric). Sweeping it over
`MRUK.Instance.Rooms` gives a second opinion:

```csharp
MRUKRoom ByGeometry(Vector3 p)
{
    foreach (var room in MRUK.Instance.Rooms)
        if (room.IsPositionInRoom(p))
            return room;
    return null;   // in a doorway, or outside every scanned room
}
```

`RoomDebugHud` shows both rows side by side and flags when they disagree. In a doorway the
geometric test typically returns `null` (you are in neither floor outline) while
`GetCurrentRoom()` still names one of them — and which one it names is what flips as you cross.

## Why things drift near doorways

This is the mechanism, and it is worth understanding before blaming tracking.

`MRUK.EnableWorldLock` is **on by default**, and it is not a passive setting. Every frame MRUK
asks its native library for a world-lock offset and then **overwrites the pose of
`OVRCameraRig/TrackingSpace`** with it (`MRUK.cs`, in `Update`). The point is to keep the
localized room's anchors sitting exactly on the real walls. The consequence is that the
*virtual* world — including anything parented under `TrackingSpace`, and your apparent position
relative to world-space objects like our `Cube` — shifts whenever that offset changes.

Walking through a doorway is exactly when it changes: the lock re-targets from one room's anchor
to the next, and the correction lands as a visible jump. `MRUK.IsWorldLockActive` goes false when
localization is lost (the documented example is standby in one room, resume in another), and
content can be metres off until the headset re-localizes.

Levers if the jump is too ugly:

- `MRUK.TrackingSpaceOffset` — the only supported way to translate/rotate `TrackingSpace` while
  world lock is on. Setting `trackingSpace.position` directly just gets overwritten, plus a
  warning in the log.
- `MRUK.SetCustomWorldLockAnchor(anchor, poseOffset)` — lock to one `OVRSpatialAnchor` of your
  choosing instead of MRUK's computed default. Useful if you want a single room to stay rock
  solid at the cost of the others.
- `EnableWorldLock = false` — raw tracking-space poses, no per-frame correction. Rooms will
  slowly drift out of alignment with reality, but nothing jumps.

## Measurement procedure

What to actually do on the headset, and what to write down.

1. Run Space Setup and scan **2–3 connected rooms** in one session, walking through the doorways
   so the headset keeps a continuous map. Scanning rooms in separate sessions is a known source
   of inter-room misalignment.
2. Launch the app. Confirm the HUD shows `Rooms loaded: 3` and `World lock: active`.
3. Stand in the middle of room A, note `Trk offset`. Walk slowly to the middle of room B through
   the doorway, pausing *in* the doorway. Record:
   - `Trk offset` before, inside the doorway, and after — the delta is the drift.
   - whether `MRUK room` and `Geometry` disagree in the doorway, and for how far.
   - the transition count, and whether it flickers (several transitions for one crossing means
     the lock is oscillating between the two rooms).
   - visually: how far the translucent wall meshes sit from the real walls, in both rooms, before
     and after the crossing.
4. Repeat in the other direction, and again after putting the headset into standby in one room
   and resuming in another — that is the case where `IsWorldLockActive` is expected to go false.
5. Compare against `EnableWorldLock = false` on the `MRUK` component. Drift becomes gradual
   instead of a jump; note which failure mode is more usable for this project.

Doorways themselves are worth a look while you are there. `DOOR_FRAME` anchors are planar and
parented to their wall, and MRUK also emits `INVISIBLE_WALL_FACE` anchors for openings between
rooms. We deliberately exclude `INVISIBLE_WALL_FACE` from `EffectMesh.Labels` so doorways read as
open — add bit 15 (`+32768`) back to that mask if you want to see them, and they will show up
magenta per the colouriser's palette.

### Findings

> Fill in after the walkthrough. Keep the numbers.

| Date | Rooms | Drift A→B (m) | Doorway ambiguity | World lock lost? | Notes |
| --- | --- | --- | --- | --- | --- |
| | | | | | |
