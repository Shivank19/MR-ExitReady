# What was wrong in the scene, and what changed

A record of the milestone-3 cleanup, so the next person does not undo it. Every change is in
`Assets/Scenes/SampleScene.unity` unless stated otherwise.

## Bugs fixed

### 1. A second, material-less `EffectMesh` was boxing the user in — *removed*

The `MRUK` GameObject carried its own `EffectMesh` component in addition to the
`[BuildingBlock] EffectMesh` object. It was set to `CurrentRoomOnly`, covered floor / ceiling /
wall / door / window / other, and had **no material assigned**. `EffectMesh` only assigns a
material when one is set, so those meshes got Unity's default opaque material: a solid box drawn
over the real room, hiding both passthrough and the translucent meshes from the other
`EffectMesh`. Deleted the duplicate component; the `[BuildingBlock] EffectMesh` object is now the
only one.

### 2. The Scene Debugger menu faced the wrong way — *fixed around*

MRUK's `SceneDebugger.MoveCanvasInFrontOfCamera` runs `SnapCanvasInFrontOfCamera()`, which sets
`transform.position` and nothing else. The rotation stays at identity, so the panel always faces
world +Z. Unless you happen to be looking down +Z when the app starts, it is edge-on or facing
away — which is what "the debugger is misaligned" was.

`SceneDebugger` is a package script and cannot be edited, so instead:
`MoveCanvasInFrontOfCamera` is now **off**, and
[DebugPanelPlacer.cs](../Assets/Script/MRUK/DebugPanelPlacer.cs) on the same GameObject does the
placement properly — position plus a yaw-only rotation, after waiting for head tracking to report
a real pose, and re-triggerable with **B/Y**.

Note that MRUK marks `SceneDebugger` `[Obsolete]` in favour of
**Meta → Tools → Immersive Debugger**. We kept it because it is already wired up, but the
Immersive Debugger is the forward path if it needs more work.

### 3. Debug meshes were not readable as debug meshes — *per-label colouring added*

`EffectMesh` applies a single material to every anchor, and the material in use
(`TransparentSceneAnchor`, MRUK's `HighlightsAndShadows` shader) is a subtle shadow/highlight
effect, not something you can read structure from.
[SceneLabelColorizer.cs](../Assets/Script/MRUK/SceneLabelColorizer.cs) on the new
`MR Debug Tools` object re-skins each generated mesh with a translucent colour per semantic label
(grey walls, blue floor, pale ceiling, orange doors, cyan windows, per-type furniture) at 30%
opacity, so passthrough stays visible behind them. It re-scans every 0.5 s rather than relying on
`SceneLoadedEvent`, because callback order against `EffectMesh` is not guaranteed and rooms can
appear later.

The material it clones is `Assets/Script/MRUK/Materials/MRUK_DebugTranslucent.mat` (URP Unlit,
transparent, double-sided). URP reads its blend state from material properties, so the clone sets
`_Surface` / `_SrcBlend` / `_DstBlend` / `_ZWrite` / `_Cull` and the `_SURFACE_TYPE_TRANSPARENT`
keyword explicitly — setting colour alpha alone does nothing.

### 4. `INVISIBLE_WALL_FACE` was being rendered — *excluded*

`EffectMesh.Labels` was `442367`, which includes bit 15, `INVISIBLE_WALL_FACE`. MRUK emits those
anchors for openings between rooms, so doorways were drawn as surfaces — exactly the geometry you
need to see through for milestone 4. Now `409599` (the same set minus bit 15). Add `32768` back if
you want them; the colouriser draws them magenta.

### 5. `EffectMesh.Colliders` turned on

The Scene Debugger's raycast tools need something to hit. Costs a mesh collider per anchor.

### 6. The talking cube was hijacking startup — *disabled*

`A3TalkingCube` (an earlier assignment) was live on `Cube`. Its `Start()` requests microphone
permission — a modal dialog on every launch — and then logs an error and disables itself because
no API key is set. `[BuildingBlock] Controller Buttons Mapper` existed only to drive it, and its
`UnityEvent`s would have called into a half-initialised component. Both components are now
disabled; the `Cube` keeps its URP Lit material and renders fine as the plain milestone-2 cube.

**A live UMN AI Gateway key was serialized into that component and committed.** It has been
removed from the scene, but it is still in git history (commit `31788e1`) and must be rotated.
See the security note in the [README](../README.md).

### 7. Stray transforms reset

Cosmetic, but they make the Hierarchy lie about where things are:

| Object | Was | Now |
| --- | --- | --- |
| `MRUK` | `(635, 234, 334)` | `(0, 0, 0)` |
| `Directional Light` | `(246, 1144, 1827)` | `(0, 3, 0)` |
| `InputModule` | `(1.99, 5.81, 22.12)` | `(0, 0, 0)` |
| `Cube` | `(0, 1.2, 2.5)` | `(0, 1.2, 1.5)` |

`MRUK`'s position was harmless — rooms are created at the scene root and posed in world space, so
they never inherited it — but it looked like the cause of a misalignment and wasted time. The
directional light's position never mattered (only its rotation does), and `InputModule` is driven
by `rayTransform`, not its own transform. The `Cube` moved from 2.5 m to 1.5 m so it is reliably
inside the room rather than potentially behind a wall.

## Checked and left alone

These were already right, and are the things to re-check first if the build misbehaves:

- `OVRManager.Tracking Origin Type` = Floor Level (`_trackingOriginType: 1`).
- `OVRManager.Request Scene Permission On Startup` = on; `isInsightPassthroughEnabled` = on.
- Android manifest: `USE_SCENE`, `USE_ANCHOR_API`, `PASSTHROUGH` feature, Horizon OS target 207.
- Headset cameras clear to solid `(0,0,0,0)` with `m_RenderPostProcessing: 0`. The only camera
  with post-processing on is the inactive `Main Camera`, which never renders. URP
  post-processing clobbers the alpha the passthrough compositor needs.
- OpenXR Android: `MetaXRFeature` enabled.
- `MRUK.SceneSettings`: `DataSource = Device`, `RoomIndex = -1` (all rooms),
  `LoadSceneOnStartup` on.
- `MRUK.EnableWorldLock` on — see [mruk-multiroom.md](mruk-multiroom.md) for why that is the
  thing to suspect when content drifts.

## Left in place deliberately

Inactive leftovers from the earlier assignment: `Main Camera`, `Canvas` (scale 0), `EventSystem`,
`Interaction Manager`, `[BuildingBlock] Cube`, and `Assets/_Recovery/`. They are disabled, cost
nothing at runtime, and deleting objects out of an 880 KB scene file by hand is more risk than the
tidiness is worth. Delete them from the Unity editor if you want them gone.
