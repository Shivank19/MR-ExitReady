# ExitReady v1: the story

## Use case

**"Can you get out of this room?"** One person, one room they know, one fire. Everything else
(more scenarios, multi-room routes, other tools) builds on this loop.

## What we have

ExitReady turns the room you are standing in into a fire drill. The headset scans the real
room, then a fire breaks out on your own furniture, next to one of your own doors. Smoke banks
down from the ceiling. An alarm, an extinguisher and the doors are where you'd expect them. You
have 90 seconds, and the headset watches what you actually do. At the end you get a readiness
score and the two things to do differently next time.

## Why it matters

- **People don't act the way they were told.** Under pressure they freeze, go for the
  extinguisher when they should leave, or open the door with fire behind it. A poster or a video
  never shows you how you will behave. This does.
- **Generic training ignores the real building.** A drill in a stock 3D office doesn't teach you
  that *your* nearest exit is behind *your* couch. Because the scenario is built from your scan,
  what you practise is your actual way out.
- **Organisations need evidence, not attendance.** Employers, schools, landlords and insurers
  can record that someone sat through fire training, but not that they're ready. ExitReady
  produces a score per person: did they raise the alarm, check the door, stay low, and get out
  in time.
- **It runs anywhere, with no setup.** There is no smoke machine or building closure, and no
  instructor has to be in the room. Put the headset on, scan, and the drill is ready.

## The 30-second demo

1. Put the headset on. The room scan is already done.
2. "Press A to start." 3, 2, 1. The couch is on fire, and you can hear it.
3. The siren starts when you pull the alarm by the door. Smoke is coming down, so you crouch.
4. You put a hand on the door behind the fire and the controller jolts: **HOT**. You go to the
   other door, which is cool, and open it.
5. **EXIT READY, 92/100.** Then the one thing you could have done better.

## What is placeholder in v1

The fire, smoke, alarm siren and extinguisher spray are generated in code, and the props are made
from primitives. Fire is not yet hidden behind real objects. The scoring weights are first
guesses to tune with real users.

## Next, toward the full simulator

- More scenarios through `IDrillScenario`: kitchen grease fire (don't use water), smoke-filled
  corridor, blocked exit (axe / alternate route), assisting someone else.
- More building equipment: fire blanket, hose reel, emergency lighting, refuge points.
- Multi-room evacuation routes across connected scanned rooms (milestone 4 tooling).
- Per-person history and exportable reports for the organisation buying it.
