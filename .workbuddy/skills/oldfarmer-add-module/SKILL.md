---
name: oldfarmer-add-module
description: "This skill should be used when adding a new behavior module (mining, fishing, combat, foraging, etc.) to the OldFarmer Stardew Valley mod. It captures the reusable file-and-interface pattern: Scanner / Executor / Behavior (FSM) / Module (Enable, Disable, Update, Draw) / ModEntry integration. Also used when refactoring an existing ad-hoc behavior into this module pattern."
agent_created: true
---

# OldFarmer — Add a Behavior Module

## Overview

Every new grandpa behavior follows the same five-file pattern established by
`TillingModule` and `WoodcuttingModule`.  This skill describes the exact files
to create, their responsibilities, and the wiring required in `ModEntry.cs` and
`GrandpaSpiritOrbiter.cs`.

## File Checklist

For a behaviour called **`Foo`** (e.g. Mining, Fishing) create / modify these
files:

| # | File | Purpose |
|---|------|---------|
| 1 | `FooScanner.cs` | Static class that scans the world for valid targets |
| 2 | `FooExecutor.cs` | Static class that performs one "action" at a position |
| 3 | `GrandpaFooBehavior.cs` | FSM class: Orbiting → Moving → Acting → Cooldown → NextTarget |
| 4 | `FooModule.cs` | Module wrapper: `Enable()` / `Disable()` / `IsEnabled` / `Update()` / `Draw()` |
| 5 | `ModEntry.cs` | Add `readonly FooModule _fooModule` field; call `_fooModule.Update()` and `.Draw()` |
| 6 | `GrandpaSpiritOrbiter.cs` | Add `Vector2? FooWorldPosition` if needed; update `Draw()` priority |

## Step-by-step

### 1. Scanner (`FooScanner.cs`)

```csharp
using Microsoft.Xna.Framework;
using StardewValley;

namespace OldFarmer;

internal static class FooScanner
{
    private const int ScanRadius = 6;  // 13×13 around player

    /// <summary>Returns tile positions with valid targets.</summary>
    public static List<Vector2> GetTargets(GameLocation loc, Farmer player)
    {
        var results = new List<Vector2>();
        var playerTile = player.Tile;
        // ... scan loop ...
        return results;
    }
}
```

Key rules:
- Scan radius matches the behavior's effective range.
- Filter out already-handled targets.
- Return tile positions (not world-pixel positions).

### 2. Executor (`FooExecutor.cs`)

```csharp
internal static class FooExecutor
{
    /// <summary>Perform the action at <paramref name="worldPos"/>. Returns count of affected targets.</summary>
    public static int DoAction(GameLocation loc, Farmer player, Vector2 worldPos)
    {
        // Convert world pixel → tile, run action
        return destroyedCount;
    }
}
```

Key rules:
- Accept world-pixel position, convert to tile internally.
- Respect stamina/safety as much as possible.
- Return a count so the behavior can decide when to move on.

### 3. Behavior FSM (`GrandpaFooBehavior.cs`)

```csharp
internal sealed class GrandpaFooBehavior
{
    private enum State { Orbiting, MovingToX, MovingToY, Acting, Cooldown, NextTarget }

    // Tunables
    private const float MoveSpeed = 8f;
    private const float ArrivalThreshold = 8f;

    // Public output
    public Vector2 WorldPosition { get; private set; }
    public bool IsActive => _state != State.Orbiting;

    public void Update() { /* FSM tick */ }
}
```

FSM flow:

```
Orbiting → (targets found) → MovingToTarget → Acting → Cooldown → NextTarget → ...
```

Key rules:
- Add `_wasOnFarm` / leash-check from `GrandpaTillerBehavior` patterns.
- `WorldPosition` is always a world-pixel position (not tile).
- `IsActive` is true when not in Orbiting.
- Every new state needs a `Tick*` method.

### 4. Module (`FooModule.cs`)

```csharp
internal sealed class FooModule
{
    private readonly GrandpaFooBehavior _behavior = new();
    private readonly GrandpaSpiritOrbiter _orbiter;

    public bool IsEnabled { get; private set; }
    public void Enable() => IsEnabled = true;
    public void Disable()
    {
        IsEnabled = false;
        _orbiter.FooWorldPosition = null;
    }

    public FooModule(GrandpaSpiritOrbiter orbiter) => _orbiter = orbiter;

    public void Update()
    {
        if (!IsEnabled) return;
        _behavior.Update();
        _orbiter.FooWorldPosition = _behavior.IsActive ? _behavior.WorldPosition : null;
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!IsEnabled) return;
        // Any custom rendering for this behavior
    }
}
```

Key rules:
- Constructor takes `GrandpaSpiritOrbiter` to feed position back.
- `Disable()` MUST clear the orbiter's position property.
- `Update()` gate-checks `IsEnabled` before ticking behavior.
- If the orbiter needs a new position slot, add it (step 6).

### 5. ModEntry wiring

```csharp
// field
private readonly FooModule fooModule;

// constructor
fooModule = new FooModule(grandpaSpirit);
// fooModule.Enable();   // ← uncomment to activate

// OnUpdateTicked
fooModule.Update();

// OnRenderedWorld
fooModule.Draw(e.SpriteBatch);
```

### 6. GrandpaSpiritOrbiter (if needed)

If the new behavior needs to override grandpa's draw position (like tilling and
woodcutting do), add a new nullable property and update the `Draw()` method:

```csharp
public Vector2? FooWorldPosition { get; set; }

// In Draw(), add a branch BEFORE the existing ones:
if (FooWorldPosition.HasValue)
{
    var worldPos = FooWorldPosition.Value;
    screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos);
    layerDepth = Math.Max(0.0001f, (worldPos.Y + 32f) / 10000f);
    flip = false;
}
else if // ... existing branches ...
```

Priority order in `Draw()` should be: newest/most-active behavior first.
Currently: `WoodcuttingWorldPosition` > `TillingWorldPosition` > orbit.

## References

See existing implementations for working examples:
- `OldFarmer/TillingModule.cs` + `GrandpaTillerBehavior.cs` + `TileScanner.cs` + `TillingExecutor.cs`
- `OldFarmer/WoodcuttingModule.cs` + `GrandpaWoodcutterBehavior.cs` + `WoodcutterScanner.cs` + `WoodcutterExecutor.cs`
