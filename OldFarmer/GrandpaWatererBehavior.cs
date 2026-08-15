using Microsoft.Xna.Framework;
using StardewValley;
using System.Collections.Generic;

namespace OldFarmer;

/// <summary>
/// Controls grandpa's watering behaviour as a finite-state machine.
/// Only activates when the player is standing on a Farm location
/// and is holding a watering can.
/// </summary>
internal sealed class GrandpaWatererBehavior
{
    // ── tunables ──────────────────────────────────────────────────
    private const int PrepareDurationTicks = 20;  // ~0.33 s at 60 UPS
    private const int WateringDurationTicks = 18;  // ~0.3 s watering animation
    private const int CooldownDurationTicks = 10;  // ~0.17 s pause between passes
    private const float MoveSpeedNear      = 5f;
    private const float MoveSpeedFar       = 18f;
    private const float NearThreshold      = 64f;
    private const float FarThreshold       = 192f;
    private const float ArrivalThreshold   = 6f;
    private const float LeashRadius        = 640f;

    // ── state ─────────────────────────────────────────────────────
    private enum State
    {
        Orbiting,
        MovingToTarget,
        Preparing,
        WateringTile,
        Cooldown,
        NextTarget,
    }

    private State currentState = State.Orbiting;
    private bool wasOnFarm = false;

    // Preparing state
    private int currentStage;
    private bool isPreparing;
    private Vector2 prepareCenterTile;

    // Public output
    public Vector2 WorldPosition { get; private set; }
    public Vector2 DrawShakeOffset { get; private set; }

    /// <summary>True while grandpa is actively watering.</summary>
    public bool IsWatering => currentState != State.Orbiting;

    /// <summary>True while grandpa is preparing to water.</summary>
    public bool IsPreparing => isPreparing;

    public Vector2 PrepareCenterTile => prepareCenterTile;
    public int CurrentStage => currentStage;

    private SharedTargetManager? _targetMgr;
    private Vector2? _lastClaimedCenter;

    // Queue of center points — replaced by TryPickNextTarget + SharedTargetManager
    private Vector2 targetTile;
    private Vector2 targetWorldPos;
    private int prepareTick;
    private int wateringTick;
    private int cooldownTick;

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
    /// Called by <see cref="WateringModule.Disable"/> so that disabling
    /// the module mid-action does not leave grandpa in a stuck state.
    /// </summary>
    public void Reset()
    {
        ReleaseClaim();
        currentState   = State.Orbiting;
        DrawShakeOffset = Vector2.Zero;
        isPreparing   = false;
        currentStage   = 0;
        prepareTick   = 0;
        wateringTick   = 0;
        cooldownTick   = 0;
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
                TransitionTo(State.Orbiting);
            return;
        }

        // NOTE: we do NOT sync WorldPosition with the orbit position here,
        // because the orbit is computed in screen space and WorldPosition is in
        // world space.  The leash check below treats "Orbiting" as
        // "grandpa is near the player" — which is always true.

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

        // Leash check:
        //   1) grandpa is too far from the player
        //   2) the current target is too far from the player
        if (currentState != State.Orbiting)
        {
            float distToPlayer    = Vector2.Distance(WorldPosition, player.getStandingPosition());
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
            case State.Preparing:      TickPreparing();                  break;
            case State.WateringTile:   TickWateringTile(loc, player);   break;
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
            TransitionTo(State.Preparing);
        }
    }

    private void TickPreparing()
    {
        // Noticeable shake while grandpa aims the watering can
        prepareTick++;
        float t  = prepareTick / (float)PrepareDurationTicks;
        float sx = (float)System.Math.Sin(t * System.Math.PI * 4f) * (t * 3f);
        float sy = (float)System.Math.Cos(t * System.Math.PI * 3f) * (t * 2f);
        DrawShakeOffset = new Vector2(sx, sy);

        currentStage = 4;
        prepareCenterTile = targetTile;

        if (prepareTick >= PrepareDurationTicks)
        {
            DrawShakeOffset = Vector2.Zero;
            currentStage   = 0;
            isPreparing   = false;
            wateringTick   = 0;
            TransitionTo(State.WateringTile);
        }
    }

    private void TickWateringTile(GameLocation loc, Farmer player)
    {
        // Hold the watering pose for a short duration so the animation is visible
        wateringTick++;
        if (wateringTick < WateringDurationTicks)
            return;

        DrawShakeOffset = Vector2.Zero;
        WateringExecutor.WaterArea(loc, targetTile);

        // Consume SAN for watering a 9x9 area
        if (Game1.player != null)
            SanManager.AddSan(Game1.player, -3f);

        wateringTick = 0;
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

    private bool TryPickNextTarget(GameLocation loc, Farmer player)
    {
        var unwatered = TileScanner.GetUnwateredTiles(loc, player);
        if (unwatered.Count == 0)
            return false;

        var centers = BuildCenterPoints(player.Tile, unwatered);

        if (_targetMgr != null)
            centers.RemoveAll(c => _targetMgr.IsClaimed(c));

        centers.RemoveAll(c => !TileScanner.HasUnwateredInBlock(loc, c));

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

    private static List<Vector2> BuildCenterPoints(Vector2 playerTile, List<Vector2> unwatered)
    {
        var unwateredSet = new HashSet<Vector2>(unwatered);
        const int halfStride = 4;
        var centers = new List<Vector2>();
        var covered = new HashSet<Vector2>();

        foreach (var tile in unwatered)
        {
            if (covered.Contains(tile))
                continue;

            int cx = (int)tile.X;
            int cy = (int)tile.Y;

            bool blockHasUnwatered = false;
            for (int dx = -halfStride; dx <= halfStride; dx++)
            {
                for (int dy = -halfStride; dy <= halfStride; dy++)
                {
                    var t = new Vector2(cx + dx, cy + dy);
                    if (unwateredSet.Contains(t))
                        blockHasUnwatered = true;
                    covered.Add(t);
                }
            }

            if (blockHasUnwatered)
                centers.Add(new Vector2(cx, cy));
        }

        return centers;
    }

    private void TransitionTo(State next)
    {
        if (currentState == State.Preparing)
            isPreparing = false;


        // Sync WorldPosition to player when leaving Orbiting state
        if (currentState == State.Orbiting && next != State.Orbiting && Game1.player != null)
            WorldPosition = Game1.player.getStandingPosition();
        currentState = next;

        // On enter Orbiting: clear work queue so we don't re-pick a far-away target
        if (next == State.Orbiting)
            ReleaseClaim();

        if (next == State.Preparing)
        {
            isPreparing = true;
            prepareCenterTile = targetTile;
            prepareTick  = 0;
            currentStage = 4;
        }
        else if (next == State.WateringTile)
        {
            // Play the watering sound — DoFunction doesn't play it; the game
            // plays it in the normal tool-use flow which we're bypassing.
            if (Game1.player != null && Game1.currentLocation != null)
                Game1.currentLocation.playSound("wateringCan");

            wateringTick = 0;
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
