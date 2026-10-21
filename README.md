# Scoot Foma

A trick scooter simulator in Unity where the player controls the **rider's body parts** with a gamepad, and tricks emerge **from physics**, not from animations.

Left stick: hands and handlebars. Right stick: torso. Triggers and bumpers: legs and grip. A tailwhip happens because a foot kicked the deck, not because a "tailwhip" button was pressed. The main value of the project is well-thought-out controls.

## Idea

In existing scooter games, tricks are triggered by button combinations or animations. Fully physical control of body parts exists only for skateboarding (Skater XL). Here:

- one control, one body part; the state (riding, air, slide) changes how physics responds, not what a button means;
- tricks emerge on their own: combos (whip + barspin, 360 + whip) work if the individual parts work;
- internal forces only: in the air you can't "spin up" with a stick, rotation is gathered at takeoff;
- assist is a separate layer, with sliders and no animations.

## Technology

Unity 6 (6000.6), the new Input System, PhysX at **240 Hz**. The rider and the scooter are two separate `ArticulationBody` trees (13 and 4 links), and muscles are joint drives. A hand grip goes through a light intermediate hand body on a `Joint`; feet stand on the deck by contact with a soft, force-limited adhesion.

## Architecture

Layers exchange frame structs and know nothing about each other:
**input → intent → assist → physics → sensing/recognition.** Recognition never moves anything. A single orchestrator (`SimulationLoop`) steps the physics, and visuals are interpolated separately.

```
Assets/_Project/
  Scripts/Core/      pure logic with no Unity objects: input filters, flick, latch, intent, IK, angular momentum
  Scripts/Runtime/   MonoBehaviour wrappers: gamepad, orchestrator, scooter and rider rigs, debug HUD
  Scripts/Editor/    scene builders (Scoot Foma menu)
  Tests/             EditMode tests for the core
  Settings/          coefficients live in assets, not in code
```

Details and results of the technical physics checks are in [docs/spikes.md](docs/spikes.md).

## Status

| Stage | Status |
| --- | --- |
| 0. Foundation: physics settings, structure, tests | done |
| 0.5. Technical spikes: grips, wheels, angular momentum, input | done |
| 1. Input and intent: filters, flick, modifier latch, input recording and replay, HUD | done |
| 2. Scooter without a rider | mostly done at spike level, to be moved into production code |
| 3. Rider on motors: balance, crouch, carving, manual | next |
| 4. Air: ollie, landing, 180 / 360 / flip | |
| 5. First-version tricks: tailwhip, barspin | |
| 6. Slide, grind, bail | |

The first version is a small set of tricks done well, on a single test park.

## Running

1. Open the project in Unity 6000.6.
2. Menu **Scoot Foma → Build Input Lab**, then open `Assets/_Project/Scenes/InputLab.unity`. The screen shows what the sticks do and where the body-part targets go. **F5** records input, **F1** hides the HUD.
3. Physics spikes: **Scoot Foma → Spikes → Build All Spike Scenes**.
4. Tests: Window → General → Test Runner → EditMode.

## Plans

Once the controls and physics are ready, the system is planned to be used for **machine learning**: an agent (ML-Agents) gets exactly the same controls as the player and has to learn tricks on its own, a tailwhip for example, with no prepared animations. This is both a check that tricks really emerge from physics and a basis for NPC riders.

Also in the idea backlog: flight breakdown in replay (center of mass, moments, contact forces), haptic feedback through gamepad vibration, a spot editor, and second-wave tricks (fingerwhip, kickless, DS, bartwist, bry).
