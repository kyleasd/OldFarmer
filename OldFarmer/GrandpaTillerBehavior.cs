using Microsoft.Xna.Framework;
using StardewValley;

namespace OldFarmer;

/// <summary>
/// Controls grandpa's tilling behaviour as a finite-state machine.
/// Only activates when the player is standing on a Farm location.
///
/// Each "pass" grandpa picks one center point within the player's 8x8 scan area,
/// moves to it, charges, then tills a 9x9 area around that center.
/// Because one pass covers a 9x9 block, only a handful of passes are needed
/// to cover the entire scan area.
/// </summary>
internal sealed class GrandpaTillerBehavior
{
    // ── tunables ──────────────────────────────────────────────────
    private const int ChargeDurationTicks  = 30;   // ~1.3 s at 60 UPS
    private const int CooldownDurationTicks = 30;  // ~0.5 s pause between passes
    private const float MoveSpeedNear      = 5f;   // pixels/tick when close (≤ NearThreshold)
    private const float MoveSpeedFar       = 18f;  // pixels/tick when far (≥ FarThreshold)
    private const float NearThreshold      = 64f;  // 1 tile away → slow down
    private const float FarThreshold       = 192f; // 3 tiles away → full speed
    private const float ArrivalThreshold   = 6f;   // pixels; "close enough"
    private const float LeashRadius        = 640f; // 10 tiles; abandon task and follow player if exceeded

    // Shake amplitude grows from 0 → Max over the charge window
    private const float MaxShakeAmplitudeX = 4f;
    private const float MaxShakeAmplitudeY = 2f;
    private const float ShakeFrequency     = 20f;  // sin-wave cycles over [0,1] progress

    // ── state ─────────────────────────────────────────────────────
    private enum State
    {
        Orbiting,
        MovingToTarget,
        ChargingHoe,
        TillingTile,
        Cooldown,
        NextTarget,
    }

    private State currentState = State.Orbiting;
    private bool wasOnFarm = false;

    // Charging state exposed for highlight rendering
    private int currentStage;
    private bool isCharging;
    private Vector2 chargeCenterTile;

    // Shared claim manager — prevents multiple grandpas from
    // picking the same center tile at the same time.
    private SharedTargetManager? _targetMgr;
    private Vector2? _lastClaimedCenter;

    private Vector2 targetTile;       // center tile for the current pass
    private Vector2 targetWorldPos;   // pixel center of targetTile

    private int chargeTick;
    private int cooldownTick;

    // ── public output ─────────────────────────────────────────────
    /// <summary>Current world-pixel position of the grandpa sprite.</summary>
    public Vector2 WorldPosition { get; private set; }

    /// <summary>Screen-space shake offset — non-zero only during ChargingHoe.</summary>
    public Vector2 DrawShakeOffset { get; private set; }

    /// <summary>True while grandpa is actively tilling (not in Orbiting state).</summary>
    public bool IsTilling => currentState != State.Orbiting;

    /// <summary>True while grandpa is charging the hoe.</summary>
    public bool IsCharging => isCharging;

    /// <summary>The center tile of the current charge/till action.</summary>
    public Vector2 ChargeCenterTile => chargeCenterTile;

    /// <summary>Current charge stage (0=none, 1=3×3, 2=5×5, 3=7×7, 4=9×9).</summary>
    public int CurrentStage => currentStage;

    /// <summary>
    /// Inject the shared claimed-centers set so this grandpa can avoid
    /// picking the same target as other grandpas.
    /// </summary>
    public void SetTargetManager(SharedTargetManager mgr) => _targetMgr = mgr;

    private void ClaimCenter(Vector2 center)
    {
        ReleaseClaim();
        _lastClaimedCenter = center;
        _targetMgr?.TryClaim(center);
    }

    private void ReleaseClaim()
    {
        if (_targetMgr != null && _lastClaimedCenter.HasValue)
        {
            _targetMgr.Release(_lastClaimedCenter.Value);
            _lastClaimedCenter = null;
        }
    }

    // ── public control ────────────────────────────────────────────
    /// <summary>
    /// Force-reset the entire state machine back to Orbiting.
    /// Called by <see cref="TillingModule.Disable"/> so that disabling
    /// the module mid-action does not leave grandpa in a stuck state.
    /// </summary>
    public void Reset()
    {
        ReleaseClaim();
        currentState  = State.Orbiting;
        DrawShakeOffset = Vector2.Zero;
        isCharging      = false;
        currentStage    = 0;
        chargeTick      = 0;
        cooldownTick    = 0;
        // Sync WorldPosition so grandpa re-appears near the player
        if (Game1.player != null)
            WorldPosition = Game1.player.getStandingPosition();
    }

    // ── main tick ─────────────────────────────────────────────────
    public void Update()
    {
        if (Game1.player is null || Game1.currentLocation is null)
            return;

        Farmer player   = Game1.player;
        GameLocation loc = Game1.currentLocation;

        // ── SAN check: stop working if SAN <= 0 ──────────────────
        if (SanManager.GetSan(player) <= 0)
        {
            if (currentState != State.Orbiting)
            {
                TransitionTo(State.Orbiting);
                Game1.addHUDMessage(new HUDMessage("爷爷累了，需要休息...", 2));
            }
            return; // Don't start new work when SAN <= 0
        }

        // Sync WorldPosition with the orbit position while orbiting
        // so the transition to MovingToTarget has no teleport.
        // NOTE: orbit is computed in screen space, so we can't perfectly sync
        // WorldPosition. Instead we just approximate: during Orbiting,
        // grandpa is always near the player, so we use the player position
        // as the effective WorldPosition for leash-distance calculations.
        // (The actual drawn position is handled by GrandpaSpiritOrbiter.)
        if (currentState == State.Orbiting)
        {
            // Don't modify WorldPosition during orbiting —
            // the distance-to-player check below treats Orbiting as "near player".
        }

        if (!loc.IsFarm || !loc.IsOutdoors)
        {
            wasOnFarm = false;
            if (currentState != State.Orbiting)
                TransitionTo(State.Orbiting);
            DrawShakeOffset = Vector2.Zero;
            return;
        }

        if (!wasOnFarm)
            wasOnFarm = true;

        // Leash check 1: grandpa is too far from the player
        // Leash check 2: the current target is too far from the player
        //   (player may have walked away after the target was picked)
        if (currentState != State.Orbiting)
        {
            float distToPlayer = Vector2.Distance(WorldPosition, player.getStandingPosition());
            float targetDistToPlayer = Vector2.Distance(
                TileCenter(targetTile), player.getStandingPosition());

            if (distToPlayer > LeashRadius || targetDistToPlayer > LeashRadius)
            {
                TransitionTo(State.Orbiting);
            }
        }


        switch (currentState)
        {
            case State.Orbiting:       TickOrbiting(loc, player);       break;
            case State.MovingToTarget: TickMovingToTarget();             break;
            case State.ChargingHoe:    TickChargingHoe();                break;
            case State.TillingTile:    TickTillingTile(loc, player);     break;
            case State.Cooldown:       TickCooldown();                   break;
            case State.NextTarget:     TickNextTarget(loc, player);      break;
        }
    }

    // ── per-state tick methods ────────────────────────────────────

    private void TickOrbiting(GameLocation loc, Farmer player)
    {
        DrawShakeOffset = Vector2.Zero;

        if (TryPickNextTarget(loc, player))
            TransitionTo(State.MovingToTarget);
    }

    private void TickMovingToTarget()
    {
        DrawShakeOffset = Vector2.Zero;
        MoveToward(targetWorldPos);

        if (Vector2.Distance(WorldPosition, targetWorldPos) <= ArrivalThreshold)
        {
            WorldPosition = targetWorldPos;
            TransitionTo(State.ChargingHoe);
        }
    }

    private void TickChargingHoe()
    {
        // Grandpa winds up with a visual shake.
        // We compute the charge stage so the highlight drawer can render the grid.
        chargeTick++;
        float t  = chargeTick / (float)ChargeDurationTicks;
        float sx = (float)Math.Sin(t * Math.PI * ShakeFrequency) * (t * MaxShakeAmplitudeX);
        float sy = (float)Math.Cos(t * Math.PI * ShakeFrequency) * (t * MaxShakeAmplitudeY);
        DrawShakeOffset = new Vector2(sx, sy);

        // Always show full iridium charge range immediately (9×9)
        currentStage = 4;
        chargeCenterTile = targetTile;

        if (chargeTick >= ChargeDurationTicks)
        {
            DrawShakeOffset = Vector2.Zero;
            currentStage = 0;   // hide highlight before the state flips
            isCharging = false;
            TransitionTo(State.TillingTile);
        }
    }

    private void TickTillingTile(GameLocation loc, Farmer player)
    {
        DrawShakeOffset = Vector2.Zero;
        // Till the full 9x9 area around the current center point
        TillingExecutor.TillArea(loc, player, targetTile);

        // Consume SAN for tilling a 9x9 area
        if (Game1.player != null)
            SanManager.AddSan(Game1.player, -5f);

        cooldownTick = 0;
        TransitionTo(State.Cooldown);
    }

    private void TickCooldown()
    {
        cooldownTick++;
        if (cooldownTick >= CooldownDurationTicks)
            TransitionTo(State.NextTarget);
    }

    private void TickNextTarget(GameLocation loc, Farmer player)
    {
        if (TryPickNextTarget(loc, player))
            TransitionTo(State.MovingToTarget);
        else
            TransitionTo(State.Orbiting);
    }

    /// <summary>
    /// Scans for untilled tiles, builds 9×9 center points, filters out
    /// centers claimed by other grandpas, and picks the nearest one.
    /// Returns false if no valid target is available.
    /// </summary>
    private bool TryPickNextTarget(GameLocation loc, Farmer player)
    {
        var untilled = TileScanner.GetUntilledTiles(loc, player);
        if (untilled.Count == 0)
            return false;

        var centers = BuildCenterPoints(player.Tile, untilled);

        // Exclude centers claimed by other grandpas so we spread out
        if (_targetMgr != null)
            centers.RemoveAll(c => _targetMgr.IsClaimed(c));

        // Keep only centers whose 9×9 block still has untilled tiles
        centers.RemoveAll(c => !HasUntilledInBlock(loc, c));

        if (centers.Count == 0)
            return false;

        // Sort nearest-first
        var playerPos = player.getStandingPosition();
        centers.Sort((a, b) =>
            Vector2.Distance(TileCenter(a), playerPos)
                .CompareTo(Vector2.Distance(TileCenter(b), playerPos)));

        targetTile     = centers[0];
        targetWorldPos = TileCenter(targetTile);
        ClaimCenter(targetTile);

        return true;
    }

    // ── helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Builds a list of 9x9 center points that together cover all untilled tiles
    /// within the player's scan area. Uses a HashSet of untilled positions for
    /// fast membership checks.
    /// </summary>
    private static List<Vector2> BuildCenterPoints(Vector2 playerTile, List<Vector2> untilled)
    {
        var untilledSet = new HashSet<Vector2>(untilled);
        const int halfStride = 4;
        var centers = new List<Vector2>();
        var covered = new HashSet<Vector2>();

        foreach (var tile in untilled)
        {
            if (covered.Contains(tile))
                continue;

            // Snap tile to the nearest 9-tile grid aligned to playerTile
            int cx = (int)tile.X;
            int cy = (int)tile.Y;

            // Mark all tiles in the 9x9 block as covered
            bool blockHasUntilled = false;
            for (int dx = -halfStride; dx <= halfStride; dx++)
            {
                for (int dy = -halfStride; dy <= halfStride; dy++)
                {
                    var t = new Vector2(cx + dx, cy + dy);
                    if (untilledSet.Contains(t))
                        blockHasUntilled = true;
                    covered.Add(t);
                }
            }

            if (blockHasUntilled)
                centers.Add(new Vector2(cx, cy));
        }

        return centers;
    }

    /// <summary>Returns true if the 9x9 block around <paramref name="center"/> has any untilled diggable tile.</summary>
    private static bool HasUntilledInBlock(GameLocation loc, Vector2 center)
    {
        const int halfStride = 4;
        for (int dx = -halfStride; dx <= halfStride; dx++)
        {
            for (int dy = -halfStride; dy <= halfStride; dy++)
            {
                var t = new Vector2(center.X + dx, center.Y + dy);
                if (!loc.isTileOnMap(t)) continue;
                if (loc.terrainFeatures.ContainsKey(t)) continue;
                if (loc.objects.ContainsKey(t)) continue;
                if (loc.doesTileHaveProperty((int)t.X, (int)t.Y, "Diggable", "Back") != null)
                    return true;
            }
        }
        return false;
    }

    private void TransitionTo(State next)
    {
        // On exit: clean up current state
        if (currentState == State.ChargingHoe)
            isCharging = false;


        // Sync WorldPosition to player when leaving Orbiting state
        if (currentState == State.Orbiting && next != State.Orbiting && Game1.player != null)
            WorldPosition = Game1.player.getStandingPosition();
        currentState = next;

        // On enter: set up new state
        if (next == State.Orbiting)
        {
            ReleaseClaim();
        }
        else if (next == State.ChargingHoe)
        {
            isCharging = true;
            chargeCenterTile = targetTile;
            chargeTick = 0;     // always reset here, regardless of which state we came from
            currentStage = 4;   // show full iridium range from the very first frame
        }
    }

    private void MoveToward(Vector2 target)
    {
        var dir   = target - WorldPosition;
        float dist = dir.Length();
        if (dist <= ArrivalThreshold)
        {
            WorldPosition = target;
            return;
        }

        // Smoothly interpolate speed: slow near target, fast when far away
        float t = Math.Clamp((dist - NearThreshold) / (FarThreshold - NearThreshold), 0f, 1f);
        float speed = MoveSpeedNear + t * (MoveSpeedFar - MoveSpeedNear);

        if (dist <= speed)
            WorldPosition = target;
        else
            WorldPosition += Vector2.Normalize(dir) * speed;
    }

    private static Vector2 TileCenter(Vector2 tile) =>
        new(tile.X * 64f + 32f, tile.Y * 64f + 32f);
}
