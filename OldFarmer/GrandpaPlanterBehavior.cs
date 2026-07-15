using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using System.Collections.Generic;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Controls grandpa's planting behaviour as a finite-state machine.
/// Only activates when the player is standing on a Farm location
/// and is holding seeds.
/// </summary>
internal sealed class GrandpaPlanterBehavior
{
    private readonly IMonitor _monitor;
    private int _stateChangeFrame = 0;

    // ── tunables ──────────────────────────────────────────────────
    private const int PlantingDurationTicks = 18; // ~0.3 s planting animation
    private const int CooldownDurationTicks  = 10; // ~0.17 s pause between passes
    private const float MoveSpeedNear        = 5f;
    private const float MoveSpeedFar         = 18f;
    private const float NearThreshold        = 64f;
    private const float FarThreshold         = 192f;
    private const float ArrivalThreshold     = 6f;
    private const float LeashRadius          = 640f;

    // ── state ─────────────────────────────────────────────────────
    private enum State
    {
        Orbiting,
        MovingToTarget,
        PlantingTile,
        Cooldown,
        NextTarget,
    }

    private State currentState = State.Orbiting;
    private bool wasOnFarm = false;

    // Public output
    public Vector2 WorldPosition { get; private set; }
    public Vector2 DrawShakeOffset { get; private set; }

    /// <summary>True while grandpa is actively planting.</summary>
    public bool IsPlanting => currentState != State.Orbiting;

    private SharedTargetManager? _targetMgr;
    private Vector2? _lastClaimedCenter;

    // Queue of center points — replaced by TryPickNextTarget + SharedTargetManager
    private Vector2 targetTile;
    private Vector2 targetWorldPos;
    private int plantingTick;
    private int cooldownTick;

    public GrandpaPlanterBehavior(IMonitor monitor)
    {
        _monitor = monitor;
    }

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
    /// </summary>
    public void Reset()
    {
        ReleaseClaim();
        var prev = currentState;
        currentState   = State.Orbiting;
        DrawShakeOffset = Vector2.Zero;
        plantingTick   = 0;
        cooldownTick   = 0;
        if (Game1.player != null)
            WorldPosition = Game1.player.getStandingPosition();
        _monitor?.Log($"[Planter] Reset (was {prev})", LogLevel.Trace);
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
                _monitor?.Log("[Planter] SAN <= 0, stopping work", LogLevel.Info);
            }
            return;
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

        // Leash check
        if (currentState != State.Orbiting)
        {
            float distToPlayer    = Vector2.Distance(WorldPosition, player.getStandingPosition());
            float targetDistToPlayer = Vector2.Distance(
                TileCenter(targetTile), player.getStandingPosition());

            if (distToPlayer > LeashRadius || targetDistToPlayer > LeashRadius)
            {
                _monitor?.Log($"[Planter] Leash exceeded: distToPlayer={distToPlayer:F0}, targetDist={targetDistToPlayer:F0}", LogLevel.Trace);
                TransitionTo(State.Orbiting);
            }
        }

        State prevState = currentState;
        switch (currentState)
        {
            case State.Orbiting:     TickOrbiting(loc, player);     break;
            case State.MovingToTarget: TickMovingToTarget();         break;
            case State.PlantingTile:  TickPlantingTile(loc, player); break;
            case State.Cooldown:      TickCooldown();               break;
            case State.NextTarget:     TickNextTarget(loc, player);  break;
        }
        if (prevState != currentState)
            _monitor?.Log($"[Planter] State: {prevState} → {currentState}", LogLevel.Trace);
    }

    // ── per-state tick methods ────────────────────────────────────

    private void TickOrbiting(GameLocation loc, Farmer player)
    {
        DrawShakeOffset = Vector2.Zero;

        if (!IsHoldingSeeds(player))
            return;

        if (TryPickNextTarget(loc, player))
            TransitionTo(State.MovingToTarget);
    }

    private void TickMovingToTarget()
    {
        DrawShakeOffset = Vector2.Zero;
        MoveToward(targetWorldPos);

        float dist = Vector2.Distance(WorldPosition, targetWorldPos);
        if (dist <= ArrivalThreshold)
        {
            WorldPosition = targetWorldPos;
            _monitor?.Log($"[Planter] Arrived at ({targetTile.X},{targetTile.Y}), dist={dist:F1}", LogLevel.Debug);
            TransitionTo(State.PlantingTile);
        }
    }

    private void TickPlantingTile(GameLocation loc, Farmer player)
    {
        plantingTick++;

        // Small shake while planting
        float t = plantingTick / (float)PlantingDurationTicks;
        DrawShakeOffset = new Vector2(
            (float)System.Math.Sin(t * System.Math.PI * 2f) * 2f,
            (float)System.Math.Abs(System.Math.Sin(t * System.Math.PI * 3f)) * -2f
        );

        if (plantingTick < PlantingDurationTicks)
            return;

        DrawShakeOffset = Vector2.Zero;
        _monitor?.Log($"[Planter] Planting at ({targetTile.X},{targetTile.Y}), player.CurrentItem = {player.CurrentItem?.Name ?? "null"}", LogLevel.Debug);

        // Get seeds from player's held item
        if (player.CurrentItem is StardewValley.Object seedObj && IsSeeds(seedObj))
        {
            int planted = PlantingExecutor.PlantArea(loc, player, targetTile, seedObj);
            _monitor?.Log($"[Planter] PlantArea returned {planted}", LogLevel.Info);
            if (planted > 0 && loc != null)
                loc.playSound("dirtyHit");

            // Consume SAN for planting a 9x9 area (4 SAN per area)
            if (planted > 0 && Game1.player != null)
                SanManager.AddSan(Game1.player, -4f);
        }
        else
        {
            _monitor?.Log($"[Planter] Not holding seeds when trying to plant! CurrentItem = {player.CurrentItem}", LogLevel.Warn);
        }

        plantingTick = 0;
        TransitionTo(State.Cooldown);
    }

    private void TickCooldown()
    {
        cooldownTick++;
        if (cooldownTick >= CooldownDurationTicks)
        {
            cooldownTick = 0;
            TransitionTo(State.NextTarget);
        }
    }

    private void TickNextTarget(GameLocation loc, Farmer player)
    {
        if (!IsHoldingSeeds(player))
        {
            _monitor?.Log("[Planter] Player no longer holding seeds, back to Orbiting", LogLevel.Debug);
            TransitionTo(State.Orbiting);
            return;
        }

        if (TryPickNextTarget(loc, player))
            TransitionTo(State.MovingToTarget);
        else
            TransitionTo(State.Orbiting);
    }

    private bool TryPickNextTarget(GameLocation loc, Farmer player)
    {
        var plantable = PlantingScanner.GetPlantableTiles(loc, player);
        if (plantable.Count == 0)
            return false;

        var centers = BuildCenterPoints(player.Tile, plantable);

        if (_targetMgr != null)
            centers.RemoveAll(c => _targetMgr.IsClaimed(c));

        centers.RemoveAll(c => !PlantingScanner.HasPlantableInBlock(loc, c));

        if (centers.Count == 0)
            return false;

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

    private static bool IsHoldingSeeds(Farmer player)
    {
        return player.CurrentItem is StardewValley.Object obj && IsSeeds(obj);
    }

    private static bool IsSeeds(StardewValley.Object obj)
    {
        // Seeds category = -74 in SDV
        return obj.Category == -74;
    }

    private static List<Vector2> BuildCenterPoints(Vector2 playerTile, List<Vector2> plantable)
    {
        var plantableSet = new HashSet<Vector2>(plantable);
        const int halfStride = 4;
        var centers = new List<Vector2>();
        var covered = new HashSet<Vector2>();

        foreach (var tile in plantable)
        {
            if (covered.Contains(tile))
                continue;

            int cx = (int)tile.X;
            int cy = (int)tile.Y;

            bool blockHasPlantable = false;
            for (int dx = -halfStride; dx <= halfStride; dx++)
            {
                for (int dy = -halfStride; dy <= halfStride; dy++)
                {
                    var t = new Vector2(cx + dx, cy + dy);
                    if (plantableSet.Contains(t))
                        blockHasPlantable = true;
                    covered.Add(t);
                }
            }

            if (blockHasPlantable)
                centers.Add(new Vector2(cx, cy));
        }

        return centers;
    }

    private void TransitionTo(State next)
    {
        if (currentState == next)
            return;

        var prev = currentState;
        if (prev == State.PlantingTile)
            plantingTick = 0;
        if (prev == State.Cooldown)
            cooldownTick = 0;

        // Sync WorldPosition to player when leaving Orbiting state
        if (currentState == State.Orbiting && next != State.Orbiting && Game1.player != null)
            WorldPosition = Game1.player.getStandingPosition();
        currentState = next;

        // On enter Orbiting: clear work queue
        if (next == State.Orbiting)
            ReleaseClaim();

        if (next == State.PlantingTile)
            plantingTick = 0;
        if (next == State.Cooldown)
            cooldownTick = 0;

        _stateChangeFrame = Environment.TickCount;
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

        float t = MathHelper.Clamp((dist - NearThreshold) / (FarThreshold - NearThreshold), 0f, 1f);
        float speed = MoveSpeedNear + t * (MoveSpeedFar - MoveSpeedNear);

        if (dist <= speed)
            WorldPosition = target;
        else
            WorldPosition += Vector2.Normalize(dir) * speed;
    }

    private static Vector2 TileCenter(Vector2 tile) =>
        new(tile.X * 64f + 32f, tile.Y * 64f + 32f);
}
