# ExitReady v1: presentation notes

*Mixed-reality fire-safety training on Meta Quest 3. Your own room becomes the drill.*

---

## 1. One-line pitch

**"Can you get out of this room?"** ExitReady scans the room you are standing in, starts a fire
on your own furniture next to one of your own doors, and scores how you react.

## 2. The problem

- People don't act the way they were told. Under pressure they freeze, grab the extinguisher
  when they should leave, or open a door with fire behind it.
- Posters and videos never show how *you* will behave.
- Generic training uses a stock building. It can't teach you that *your* nearest exit is behind
  *your* couch.
- Organisations can record attendance at fire training, but not readiness.

## 3. What v1 does (the demo flow)

| Step | What the user sees and does |
| --- | --- |
| **1. Scan** | The headset scans the room with Meta Space Setup (walls, doors, furniture). The app loads it through MR Utility Kit (MRUK). |
| **2. Briefing** | A head-following panel explains the controls. **A** starts. |
| **3. Countdown** | 3, 2, 1. |
| **4. Drill (90 s)** | A fire starts on the furniture nearest one door, and that door is *hot*. Smoke banks down from the ceiling and the room darkens as the fire grows. The user raises the alarm, checks the doors, stays low and leaves. The timer, current instruction and checklist sit on a "watch" panel above the left controller, so the view stays clear. |
| **5. Debrief** | A slow-motion outcome beat, then a readiness score out of 100, a grade, a timeline, measurements and up to two tips. **A** runs it again. |

### Interactions

| Action | How |
| --- | --- |
| Pull the fire alarm | Controller on the red pull station by the safe exit (a pulsing red beacon marks it), then trigger. The siren and strobe start, and every strobe flash lights up the room. |
| Check a door | Hold a controller flat on the door for 1 s. Strong buzz and red frame = **HOT**. Soft buzz and blue frame = cool. |
| Pick up the extinguisher | Hold grip near it, and keep holding to carry it. |
| Spray | Trigger while holding it. 10 s of charge. Aim at the base of the flames. |
| Leave | Grip with a hand on a door. A cool door is an escape. The hot door fails the drill. |
| Avoid the smoke | Crouch below the rolling smoke clouds. The view darkens while the head is inside the smoke layer. |
| Keep away from the fire | Getting close brings a red heat vignette, a louder crackle and a heartbeat rumble in both controllers. Staying too close costs points. |

## 4. Feature highlights

### Built from your real room
- Scene data comes from the headset scan: walls, floor, ceiling, doors, windows, furniture.
- **Scenario layout is computed from the scan** and adapts to the room:
  - 2 or more doors: the fire goes on the furniture nearest a door, and that door is hot.
  - 1 door: it is the safe exit, and the fire goes on the furniture farthest from it.
  - No doors: a virtual exit is placed on the far wall.
  - No furniture: the fire burns on the floor.
- **Robust startup:** it loads the stored room first and opens Space Setup if that fails or finds
  no room. If the device room still can't load, it falls back to an MRUK sample room, drawn as a
  furnished virtual room (walls, floor, doors, windows, furniture), and the briefing says so.

### Fire, smoke and atmosphere
- **Fire:** layered particle systems (white-hot core, flame body, tall flame tongues, embers,
  sparks, glow halo, smoke plume), a scorch mark that spreads, two flickering lights, and a
  crackle sound. It grows over time and shrinks when sprayed.
- **Smoke layer:** a volume that descends from the ceiling over the drill, with churning
  clouds and a view tint when the head is inside. Crouching clears it.
- **Atmosphere:** the passthrough image gets darker and washed out as the fire and smoke grow,
  and flashes with the alarm strobe. Only passthrough is adjusted, so the virtual fire keeps its colour.
- **Heat and danger cues:** a red vignette, a louder crackle and a heartbeat rumble in both
  controllers that speeds up as you get closer to the fire.
- **Timer feel:** a controller tap marks the seconds running out near the end.

### Interface
- **Head-following panel** for scan status, briefing and countdown.
- **Wrist "watch" panel** above the left controller during the drill: timer, current
  instruction and checklist, so the view stays clear.
- **Warning strip** that appears only when something urgent happens.
- **Two-column debrief:** score and tips on one side, timeline and measurements on the other.
- **Live event log** for every drill step, readable over `adb logcat`.

### Scoring: a readiness score out of 100

| Event | Points |
| --- | --- |
| Escaped through a safe exit | +40 |
| Alarm pulled (+5 more within 10 s) | +15 |
| Door checked before opening | +15 |
| Speed bonus | up to +15 |
| Small fire put out | +10 |
| Opened the hot door | −30 |
| Time standing in smoke | up to −15 |
| Time too close to the fire | up to −10 |
| Spraying a large fire for more than 8 s instead of leaving | −10 |

Outcomes: **Escaped**, **Hot door**, **Timed out**. Weights are tunable in the Inspector.

**Measurements recorded for the debrief:** reaction time, time to alarm, time to first door
check, exit time, distance walked, crouch time, smoke time and entries, heat time, closest
distance to the fire, idle time, doors checked, spray time.

## 5. Architecture

```
Scan layer (MRUK)            Drill layer                         Presentation
─────────────────            ─────────────────────────────       ─────────────────────
StartupRoomScan  ──room──►   DrillGameManager  (match flow)      DrillHud (panels)
RoomGeometry (helpers)       ├─ IDrillScenario  (plug-in API)    WristPanel
SceneLabelColorizer          │   └─ RoomExitScenario (v1)        HeatCues / Atmosphere
RoomDebugHud                 │       ├─ FireHazard, SmokeLayer   ProceduralAssets
DebugPanelPlacer             │       ├─ FireAlarmStation         (code-generated visuals
                             │       ├─ FireExtinguisher          and audio)
                             │       └─ ExitDoor (hot / cool)
                             └─ DrillScorer → ScoreReport
```

- **Scenario interface.** `IDrillScenario` is the contract for new scenarios. They plug into
  the existing manager, scoring and UI.
- **Everything is built at runtime** from code, so there are no hand-placed props to keep in sync
  with the room.
- **Stack:** Unity 6000.3, Meta XR SDK 207, MR Utility Kit 207, URP 17, Quest 3 (Horizon OS SDK 207),
  passthrough mixed reality.
- **Size:** about 6,300 lines of C# across 29 scripts in `Assets/Script/`.

## 6. Development progress

| Milestone | State |
| --- | --- |
| Unity, Meta XR SDK, Android target, Developer Mode | done |
| Passthrough with a floating cube on the headset | done |
| Space Setup scan loaded through MRUK, shown as colour-coded debug meshes | done |
| Multi-room tooling (current-room detection, doorway drift) | tooling in place, measurements pending |
| **v1: room-exit drill with timer, tools and readiness score** | **built on `feature/v1`, on-headset tuning ongoing** |

## 7. Honest status (for Q&A)

- The props are placeholders built from primitives. The fire, smoke, siren, spray and room
  textures are generated in code. Real models and audio can be swapped in without touching
  gameplay code.
- On the test headset, the system refuses this app's room queries (MRUK returns a generic failure
  and Space Setup won't open from the app), even though permissions are granted and other apps
  load the room. Until that's solved, demos run in MRUK's sample room, drawn as a virtual room.
  Fire, doors and furniture then don't line up with the real space.
- Fire is not hidden behind real furniture yet (Environment Depth isn't set up).
- The scoring weights are first guesses, to be tuned with real users.
- v1 has one scenario and one room. It has not had user testing.
- A leaked API key from an earlier assignment still sits in git history and must be rotated
  (details in the README security note). Don't show that history on screen.

## 8. Roadmap

1. **User testing** with a handful of people to tune timing, difficulty and scoring.
2. **More scenarios** through `IDrillScenario`: kitchen grease fire (don't use water),
   smoke-filled corridor, blocked exit, helping another person.
3. **More equipment:** fire blanket, hose reel, emergency lighting, refuge points.
4. **Occlusion:** hide fire and smoke behind real furniture with Environment Depth.
5. **Multi-room evacuation routes** across connected scanned rooms.
6. **Reporting:** per-person history and exportable reports for employers, schools and insurers.

## 9. Suggested slide order

1. Title and the one-line pitch
2. The problem (section 2)
3. Live demo or video (section 3)
4. What's under the hood: scan-driven layout (section 4)
5. Scoring and the debrief
6. Architecture (section 5)
7. Status and roadmap (sections 6 to 8)
