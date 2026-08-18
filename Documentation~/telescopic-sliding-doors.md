# Telescopic sliding doors (design proposal)

Status: **accepted — decisions below (2026-08-18), implemented in 2.2.0.**

## Decisions

1. **Spans are explicit**: `DoorConfig.rightSlideOpenOffset` sets the right (leading)
   panel's travel; `slideOpenOffset` stays the left panel's travel. Both must point in
   the same direction; the right one must be longer.
2. **`openRightDoorOnly` lives on `DoorConfig`** (shared behavior, like every other
   behavior setting). When ticked, the right door stops exactly where the left door
   sits (the left door never moves).
3. **Both panels slide in the same direction** — the direction is whatever the two
   offsets point at; no extra direction field.

## What it is

A new behavior for **Sliding + Double** doors. Instead of the two panels mirroring away
from the centre (current behavior), both panels slide in the **same direction** and stack
into one pocket, like a telescopic door:

```
wall |                                      [==Left==]      [==Right==]      | wall
     |  <- slide direction                                                   |
     |                                                                       |
open |  [==Left==]                                                           |
     |  [==Right==]   (stacked in the pocket)                                |
```

- The **right door leads**: it starts sliding alone.
- When it reaches the left door's position along the slide axis, **both continue
  together** until the end of the travel (the pocket).
- The two panels therefore have **different sliding spans**: the right door travels
  further than the left one.
- Closing plays the same motion backwards: both panels come back together until the
  left panel reaches its closed position, then the right panel continues alone.
- Optional **bool**: only open the right door — it slides until it stacks onto the
  (stationary) left door and stops there, giving a partial (half) opening.

## Motion model

Let `S_l` = left panel span, `S_r` = right panel span, and `D = S_r − S_l` (the
catch-up distance). The left panel is simply **coupled** to the right one:

```
rightPos = lerp(0, S_r, easedProgress)          // right door owns the timeline
leftPos  = clamp(rightPos − D, 0, S_l)          // left waits, then follows
```

This single formula produces the full behavior in both directions:

- Opening: left stays at 0 while `rightPos < D`, then moves in lockstep, both arrive at
  the pocket at the same time.
- Closing: right comes back alone first; once it is stacked with the left panel
  (`rightPos − D < S_l`) they travel together; left stops at 0 and right finishes alone.
- "Right door only": the right door's target span becomes `D` instead of `S_r`
  (it stops exactly where it stacks onto the left door); `leftPos` stays 0 by the
  same clamp — no special case needed.

The existing cosine ease (`CalculateEasedProgress`) applies to the right door's
timeline; the left door inherits it through the coupling. `animationDuration` covers
the whole sequence, unchanged.

## Touch points

| Piece | Change |
|-------|--------|
| `DoorConfig` | New `slidingStyle` enum: `Mirrored` (default, current behavior) \| `Telescopic`. Span definition + the right-door-only bool per the open questions below. |
| `DoorTransformData` (ECS) | New `SlidingStyle` byte + the second span (float3 or derived scalar) + `RightDoorOnly` byte. Baked from config; subscene rebake required. |
| `DoorAuthoring` baker | Bakes the new fields; converts spans to door-root local space like `slideOpenOffset` today. |
| `DoorAnimationSystem.AnimateSlidingDoor` | Branch on `SlidingStyle`: telescopic uses the coupling formula above; `Mirrored` path untouched. |
| Gizmos (`DrawSlidingDoorGizmos`) | Telescopic: per-panel arrows with their actual spans (same direction, different lengths) instead of mirrored arrows. |
| Validation | `Telescopic` on a non-Double or non-Sliding config → warning in `DoorAuthoringEditor` + setup validator. Spans must satisfy `S_r > S_l > 0`. |
| Tests (`Tests/Editor`) | Pure-math tests for the coupling formula: waits-then-follows, both-arrive-together, closing symmetry, right-door-only clamp. |
| Version | 2.2.0 (new feature). |

## Non-goals

- No change to detection, trigger volumes, locking, or audio.
- No change to rotating doors or single sliding doors.
- Collider pooling: panels already carry their own collider data per panel — unchanged.

## Resolved questions

See the Decisions section at the top — explicit `rightSlideOpenOffset`, bool on
`DoorConfig`, direction given by the offsets themselves.
