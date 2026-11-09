# MR-ExitReady

Mixed-reality room understanding on Quest 3, built on the Meta XR SDK and MR Utility Kit
(MRUK). The app turns on passthrough, loads the rooms the user scanned with Space Setup, and
draws every scene anchor as a translucent, colour-coded debug mesh so you can see what MRUK
thinks the room looks like.

---

## Environment

Pin these — mismatched versions are the single most common cause of a broken build.

| Thing | Version | Where it is recorded |
| --- | --- | --- |
| Unity | **6000.3.25f1** (Unity 6.3) | `ProjectSettings/ProjectVersion.txt` |
| Meta XR SDK (All) | **207.0.0** | `Packages/manifest.json` |
| MR Utility Kit | **207.0.1** | `Packages/manifest.json` |
| Render pipeline | URP 17.3.0 | `Packages/manifest.json` |
| Build target | Android / Quest 3 | `ProjectSettings/ProjectSettings.asset` |
| Horizon OS target SDK | 207 | `Assets/Plugins/Android/AndroidManifest.xml` |

Unity needs **Android Build Support** (plus the SDK/NDK and OpenJDK modules). Install the
matching editor version through Unity Hub rather than "whatever is newest" — MRUK's
`package.json` declares a minimum editor version and the Meta packages are compiled against it.

### Headset setup, once per device

1. Enable Developer Mode for the headset in the **Meta Horizon** phone app
   (Devices → your headset → Headset settings → Developer mode).
2. Install **Meta Quest Developer Hub** (MQDH) on the dev machine and pair the headset over USB,
   then enable wireless ADB if you want to build without a cable.
3. On the headset, run **Settings → Physical Space → Space Setup** and scan the rooms. Nothing in
   this project renders until there is scene data on the device.

---

## Build and deploy

```
File → Build Settings            # Platform: Android, scene list: Assets/Scenes/SampleScene.unity
Build and Run                    # with the headset connected and Developer Mode on
```

The already-correct settings worth not breaking:

- `OVRManager.Tracking Origin Type` = **Floor Level**. MRUK's scene data is in floor-level stage
  space; Eye Level puts every debug mesh at the wrong height.
- `OVRManager.Request Scene Permission On Startup` = on, and the Android manifest holds
  `com.oculus.permission.USE_SCENE` + `USE_ANCHOR_API`.
- The headset cameras clear to **solid colour `(0,0,0,0)`** with post-processing **off**. URP
  post-processing destroys the alpha channel the passthrough compositor needs, so the view goes
  black if you turn it on.
- OpenXR feature `MetaXRFeature` is enabled for Android.

---

## Scene layout — `Assets/Scenes/SampleScene.unity`

| Object | What it does |
| --- | --- |
| `[BuildingBlock] Camera Rig` | `OVRCameraRig` + `OVRManager`. Floor-level tracking origin. |
| `[BuildingBlock] Passthrough` | `OVRPassthroughLayer`, the passthrough underlay. |
| `MRUK` | The `MRUK` singleton, plus `StartupRoomScan`, which opens Space Setup at launch and then loads every room. MRUK's own *Load Scene On Startup* is **off** so the two don't race. |
| `FireDrill` | `DrillGameManager`, which runs the v1 fire drill (see below). Everything it shows is built at runtime. |
| `[BuildingBlock] EffectMesh` | Generates a mesh per scene anchor. One of these, not two. |
| `MR Debug Tools` | `SceneLabelColorizer` + `RoomDebugHud` (see below). Both start hidden in v1. |
| `[BuildingBlock] Scene Debugger` | MRUK's own debug menu, plus `DebugPanelPlacer`. |
| `Cube` | The plain floating cube from milestone 2, 1.5 m ahead at 1.2 m. |

Inactive leftovers (`Main Camera`, `Canvas`, `EventSystem`, `Interaction Manager`,
`[BuildingBlock] Cube`, and the disabled `A3TalkingCube` / `Controller Buttons Mapper`) are from
an earlier assignment. They are switched off, not deleted, so that history stays readable.

---

## v1: the room-exit fire drill

The app scans your room, starts a fire on your own furniture, and scores how you get out.

1. **Scan.** Space Setup opens at launch. Scan the room, including its doors, then confirm.
2. **Briefing.** A head-following panel explains the controls. Press **A** to start.
3. **Drill (90 s).** After a 3-second countdown, a fire burns on the furniture nearest one
   door, and that door is *hot*. Smoke banks down from the ceiling. The panel shows the timer,
   what to do next and a checklist.
4. **Debrief.** You get a readiness score out of 100, a grade, each point earned or lost, and up
   to two tips. Press **A** to run it again.

| Do this | How |
| --- | --- |
| Pull the fire alarm | Controller on the red box next to the safe exit, then **trigger** |
| Check a door | Hold a controller on the door for 1 s, pressing nothing. A strong buzz and red frame mean HOT; a soft buzz and blue frame mean cool |
| Pick up the extinguisher | **Grip** near it, and keep holding grip to carry it |
| Spray | **Trigger** while holding it; aim at the base of the flames. 10 s of charge |
| Leave | **Grip** with a hand on a door. Through a cool door you escape; through the hot door the drill fails |
| Stay out of the smoke | Crouch. The view goes dark while your head is inside the smoke layer |

Scoring (weights are in the `DrillGameManager` Inspector): escape +40, alarm +15 (+5 if within
10 s), door checked +15, speed bonus up to +15, small fire put out +10. Penalties: hot door −30,
up to −15 for time standing in smoke, −10 for spraying a large fire for more than 8 s instead of
leaving.

If the scan has no doors, a virtual exit is placed on the far wall. With no furniture, the fire
burns on the floor.

**Live logs:** `adb logcat -s Unity | grep -E "ExitReady-(Scan|Drill)"` streams the scan and every
drill event (phase changes, alarm, door checks, smoke in/out, score).

The props are placeholders made from primitives, and the fire, smoke and sounds are generated in
code ([ProceduralAssets.cs](Assets/Script/Drill/ProceduralAssets.cs)). Swap in real models
without touching the gameplay code. Fire is not occluded by real furniture yet (Environment Depth
is not set up).

---

## In-headset controls

| Input | Effect |
| --- | --- |
| **A / X** | Fire drill: start / try again |
| Right thumbstick **click** | Show/hide all debug meshes |
| **B / Y** | Re-summon MRUK's Scene Debugger menu in front of you |
| Left thumbstick **click** | Show/hide the room-info HUD |
| **A / X** | Click, inside the Scene Debugger menu (owned by `OVRInputModule`) |

---

## Our code

### `Assets/Script/Drill/`: the fire drill

| Script | Role |
| --- | --- |
| [DrillGameManager.cs](Assets/Script/Drill/DrillGameManager.cs) | Match flow: wait for scan → briefing → countdown → timed drill → debrief. |
| [IDrillScenario.cs](Assets/Script/Drill/IDrillScenario.cs) | The contract new scenarios implement, plus `ScenarioSettings` tuning. |
| [RoomExitScenario.cs](Assets/Script/Drill/RoomExitScenario.cs) | v1 scenario: decides from the scan where the fire, the hot and safe doors, the alarm and the extinguisher go; drives instructions and the checklist. |
| [FireHazard.cs](Assets/Script/Drill/FireHazard.cs), [SmokeLayer.cs](Assets/Script/Drill/SmokeLayer.cs) | Growing fire (particles, light, crackle) and a descending smoke layer with an in-smoke view tint. |
| [FireAlarmStation.cs](Assets/Script/Drill/FireAlarmStation.cs), [FireExtinguisher.cs](Assets/Script/Drill/FireExtinguisher.cs), [ExitDoor.cs](Assets/Script/Drill/ExitDoor.cs) | The interactable tools: pull station with siren and strobe, carry-and-spray extinguisher, doors with the hot-door check. |
| [DrillScorer.cs](Assets/Script/Drill/DrillScorer.cs), [DrillHud.cs](Assets/Script/Drill/DrillHud.cs), [DrillLog.cs](Assets/Script/Drill/DrillLog.cs) | Readiness score and debrief, the player panel, and the live log. |

### `Assets/Script/Common/`

`HeadLocator` (headset/controller lookup), `HeadFollowPanel` (the lazy-follow text panel used
by both HUDs) and `TaggedLog` (the ring buffer behind `ScanLog` and `DrillLog`).

### `Assets/Script/MRUK/`: room understanding and debug tools

| Script | Why it exists |
| --- | --- |
| [SceneLabelColorizer.cs](Assets/Script/MRUK/SceneLabelColorizer.cs) | `EffectMesh` can only apply one material to every anchor. This re-skins each generated mesh with a translucent colour per semantic label, so walls / floor / ceiling / doors / windows / furniture are each distinguishable with passthrough still visible behind them. |
| [RoomDebugHud.cs](Assets/Script/MRUK/RoomDebugHud.cs) | A head-following panel showing room count, which room MRUK thinks you are in, the geometric containment answer for comparison, the anchor census, nearest wall/door, and world-lock state. Built from code, nothing to wire up. |
| [StartupRoomScan.cs](Assets/Script/MRUK/StartupRoomScan.cs) | Runs Space Setup at launch, loads the result into MRUK, logs every step (`ScanLog`), and raises `ScanFinished` for the drill. |
| [DebugPanelPlacer.cs](Assets/Script/MRUK/DebugPanelPlacer.cs) | MRUK's `SceneDebugger` places its canvas in front of you but never sets its rotation, so the menu ends up facing world +Z — edge-on unless you happened to be facing +Z at launch. This sets position *and* a yaw-only rotation, and lets you re-summon the panel. |

Everything else under `Assets/Script/` (`A3TalkingCube`, `AIGatewayHelpers`,
`TemplateInteraction`) belongs to an earlier assignment and is not part of this project.

---

## Milestones

| # | Goal | State |
| --- | --- | --- |
| 1 | Unity + Meta XR SDK + Android target, Developer Mode, MQDH | done |
| 2 | Passthrough with a floating cube, deployed to the headset | done |
| 3 | Space Setup scan loaded through MRUK, rendered as translucent debug meshes | done — see [docs/scene-fixes.md](docs/scene-fixes.md) for what was broken |
| 4 | Multiple connected rooms, current-room detection, doorway drift | tooling in place, measurements pending — see [docs/mruk-multiroom.md](docs/mruk-multiroom.md) |
| 5 | v1 skeleton: one scenario (room exit), timer, tools, readiness score | built on `feature/v1`, needs on-headset testing — see [docs/v1-pitch.md](docs/v1-pitch.md) |

---

## Security note

A live UMN AI Gateway key was committed in `SampleScene.unity` (on the `A3TalkingCube`
component) and is still present in this repository's history, so **it must be treated as
compromised and rotated**. The value has been removed from the current scene; removing it from
history needs a rewrite (`git filter-repo`) and a force-push coordinated with everyone who has a
clone. Do not paste keys into serialized Inspector fields.
