using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace OldFarmer;

/// <summary>
/// Controls grandpa's scything behaviour as a finite-state machine.
/// Mirrors the tilling behaviour: grandpa moves to a 9×9 block center,
/// charges with a visual shake, then harvests all crops/grass in a 9×9
/// area instantly. He then moves to the next block and repeats.
///
///   Orbiting → MovingToTarget → ChargingScythe → HarvestingArea → Cooldown → NextTarget → …
/// </summary>
internal sealed class GrandpaScytherBehavior
{
    // ── tunables ──────────────────────────────────────────────────
    private const int   ChargeDurationTicks   = 25;   // ~0.4 s wind-up
    private const int   CooldownDurationTicks = 20;   // ~0.33 s pause between passes
    private const float MoveSpeedNear         = 5f;   // pixels/tick when close
    private const float MoveSpeedFar          = 18f;  // pixels/tick when far
    private const float NearThreshold         = 64f;  // 1 tile
    private const float FarThreshold          = 192f; // 3 tiles
    private const float ArrivalThreshold      = 6f;   // pixels "close enough"
    private const float LeashRadius           = 640f; // 10 tiles leash

    // Shake constants (same feel as hoe)
    private const float MaxShakeX    = 4f;
    private const float MaxShakeY    = 2f;
    private const float ShakeFreq    = 20f;

    // ── state ─────────────────────────────────────────────────────
    private enum State
    {
        Orbiting,
        MovingToTarget,
        ChargingScythe,
        HarvestingArea,
        Cooldown,
        NextTarget,
    }

    private State _state = State.Orbiting;
    private bool  _wasOnFarm;

    private SharedTargetManager? _targetMgr;
    private Vector2? _lastClaimedCenter;

    private Vector2 _targetTile;
    private Vector2 _targetWorldPos;
    private int _chargeTick;
    private int _cooldownTick;

    // ── public output ─────────────────────────────────────────────

    /// <summary>Current world-pixel position of the grandpa sprite.</summary>
    public Vector2 WorldPosition { get; private set; }

    /// <summary>Screen-space shake offset (non-zero only during ChargingScythe).</summary>
    public Vector2 DrawShakeOffset { get; private set; }

    /// <summary>True while grandpa is actively working (not Orbiting).</summary>
    public bool IsScything => _state != State.Orbiting;

    /// <summary>True while grandpa is doing the charge wind-up.</summary>
    public bool IsCharging => _state == State.ChargingScythe;

    /// <summary>Center tile of the current charge/harvest pass.</summary>
    public Vector2 ChargeCenterTile => _targetTile;

    /// <summary>Current charge stage (always 4 = full 9×9).</summary>
    public int CurrentStage => _state == State.ChargingScythe ? 4 : 0;

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
    /// Called by <see cref="ScythingModule.Disable"/> so that disabling
    /// the module mid-action does not leave grandpa in a stuck state.
    /// </summary>
    public void Reset()
    {
        ReleaseClaim();
        _state         = State.Orbiting;
        DrawShakeOffset = Vector2.Zero;
        _chargeTick    = 0;
        _cooldownTick  = 0;
        // Sync WorldPosition so grandpa re-appears near the player
        if (Game1.player != null)
            WorldPosition = Game1.player.getStandingPosition();
    }

    // ── main tick ─────────────────────────────────────────────────

    public void Update()
    {
        if (Game1.player is null || Game1.currentLocation is null)
            return;

        var player = Game1.player;
        var loc    = Game1.currentLocation;

        // ── SAN check: stop working if SAN <= 0 ──────────────────
        if (SanManager.GetSan(player) <= 0)
        {
            if (_state != State.Orbiting)
                TransitionTo(State.Orbiting);
            return;
        }

        // NOTE: we do NOT sync WorldPosition with the orbit position here,
        // because the orbit is computed in screen space and WorldPosition is in
        // world space.  The leash check below treats "Orbiting" as
        // "grandpa is near the player" — which is always true.

        if (!loc.IsFarm || !loc.IsOutdoors)
        {
            _wasOnFarm = false;
            if (_state != State.Orbiting)
                TransitionTo(State.Orbiting);
            DrawShakeOffset = Vector2.Zero;
            return;
        }

        if (!_wasOnFarm)
        {
            _wasOnFarm = true;
        }

        // Leash check:
        //   1) grandpa is too far from the player
        //   2) the current target is too far from the player
        if (_state != State.Orbiting)
        {
            float distToPlayer    = Vector2.Distance(WorldPosition, player.getStandingPosition());
            float targetDistToPlayer = Vector2.Distance(
                TileCenter(_targetTile), player.getStandingPosition());

            if (distToPlayer > LeashRadius || targetDistToPlayer > LeashRadius)
            {
                TransitionTo(State.Orbiting);
            }
        }


        switch (_state)
        {
            case State.Orbiting:       TickOrbiting(loc, player);   break;
            case State.MovingToTarget: TickMovingToTarget();         break;
            case State.ChargingScythe: TickChargingScythe();         break;
            case State.HarvestingArea: TickHarvestingArea(loc, player); break;
            case State.Cooldown:       TickCooldown();               break;
            case State.NextTarget:     TickNextTarget(loc, player);  break;
        }
    }

    // ── per-state ticks ───────────────────────────────────────────

    private void TickOrbiting(GameLocation loc, Farmer player)
    {
        DrawShakeOffset = Vector2.Zero;

        if (TryPickNextTarget(loc, player))
            TransitionTo(State.MovingToTarget);
    }

    private void TickMovingToTarget()
    {
        DrawShakeOffset = Vector2.Zero;
        MoveToward(_targetWorldPos);

        if (Vector2.Distance(WorldPosition, _targetWorldPos) <= ArrivalThreshold)
        {
            WorldPosition = _targetWorldPos;
            TransitionTo(State.ChargingScythe);
        }
    }

    private void TickChargingScythe()
    {
        _chargeTick++;
        float t  = _chargeTick / (float)ChargeDurationTicks;
        float sx = (float)Math.Sin(t * Math.PI * ShakeFreq) * (t * MaxShakeX);
        float sy = (float)Math.Cos(t * Math.PI * ShakeFreq) * (t * MaxShakeY);
        DrawShakeOffset = new Vector2(sx, sy);

        if (_chargeTick >= ChargeDurationTicks)
        {
            DrawShakeOffset = Vector2.Zero;
            TransitionTo(State.HarvestingArea);
        }
    }

    private void TickHarvestingArea(GameLocation loc, Farmer player)
    {
        DrawShakeOffset = Vector2.Zero;
        ScythingExecutor.HarvestArea(loc, player, _targetTile);

        // Consume SAN for harvesting a 9x9 area
        if (Game1.player != null)
            SanManager.AddSan(Game1.player, -2f);

        _cooldownTick = 0;
        TransitionTo(State.Cooldown);
    }

    private void TickCooldown()
    {
        _cooldownTick++;
        if (_cooldownTick >= CooldownDurationTicks)
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
        var targets = ScythingScanner.GetAllScytheTargets(loc, player);
        if (targets.Count == 0)
            return false;

        var centers = BuildCenterPoints(targets);

        if (_targetMgr != null)
            centers.RemoveAll(c => _targetMgr.IsClaimed(c));

        centers.RemoveAll(c => !HasTargetInBlock(loc, c));

        if (centers.Count == 0)
            return false;

        var playerPos = player.getStandingPosition();
        centers.Sort((a, b) =>
            Vector2.Distance(TileCenter(a), playerPos)
                .CompareTo(Vector2.Distance(TileCenter(b), playerPos)));

        _targetTile     = centers[0];
        _targetWorldPos = TileCenter(_targetTile);
        ClaimCenter(_targetTile);

        return true;
    }

    // ── helpers ───────────────────────────────────────────────────

    private void TransitionTo(State next)
    {
        if (_state == State.ChargingScythe)
        {
            DrawShakeOffset = Vector2.Zero;
        }


        // Sync WorldPosition to player when leaving Orbiting state
        if (_state == State.Orbiting && next != State.Orbiting && Game1.player != null)
            WorldPosition = Game1.player.getStandingPosition();
        _state = next;

        // On enter Orbiting: clear work queue so we don't re-pick a far-away target
        if (next == State.Orbiting)
            ReleaseClaim();

        if (next == State.ChargingScythe)
            _chargeTick = 0;
    }

    private void MoveToward(Vector2 target)
    {
        var   dir   = target - WorldPosition;
        float dist  = dir.Length();
        if (dist <= ArrivalThreshold)
        {
            WorldPosition = target;
            return;
        }

        float t     = Math.Clamp((dist - NearThreshold) / (FarThreshold - NearThreshold), 0f, 1f);
        float speed = MoveSpeedNear + t * (MoveSpeedFar - MoveSpeedNear);

        WorldPosition = dist <= speed
            ? target
            : WorldPosition + Vector2.Normalize(dir) * speed;
    }

    /// <summary>
    /// Builds a list of 9×9 center points that together cover all target tiles.
    /// Each center tile maps to a unique 9×9 block with no overlap.
    /// </summary>
    private static List<Vector2> BuildCenterPoints(List<Vector2> targets)
    {
        const int halfStride = 4;
        var targetSet = new HashSet<Vector2>(targets);
        var centers   = new List<Vector2>();
        var covered   = new HashSet<Vector2>();

        foreach (var tile in targets)
        {
            if (covered.Contains(tile))
                continue;

            int cx = (int)tile.X;
            int cy = (int)tile.Y;

            bool blockHasTarget = false;
            for (int dx = -halfStride; dx <= halfStride; dx++)
            {
                for (int dy = -halfStride; dy <= halfStride; dy++)
                {
                    var t = new Vector2(cx + dx, cy + dy);
                    if (targetSet.Contains(t)) blockHasTarget = true;
                    covered.Add(t);
                }
            }

            if (blockHasTarget)
                centers.Add(new Vector2(cx, cy));
        }

        return centers;
    }

    /// <summary>Returns true if the 9×9 block around center still has any harvestable tile.</summary>
    private static bool HasTargetInBlock(GameLocation loc, Vector2 center)
    {
        const int halfStride = 4;
        for (int dx = -halfStride; dx <= halfStride; dx++)
        {
            for (int dy = -halfStride; dy <= halfStride; dy++)
            {
                var t = new Vector2(center.X + dx, center.Y + dy);
                if (!loc.isTileOnMap(t)) continue;

                if (loc.terrainFeatures.TryGetValue(t, out var feature))
                {
                    if (feature is Grass)
                        return true;

                    if (feature is HoeDirt dirt
                        && dirt.crop is Crop crop
                        && crop.currentPhase.Value >= crop.phaseDays.Count - 1
                        && !crop.dead.Value)
                        return true;
                }
            }
        }
        return false;
    }

    private static Vector2 TileCenter(Vector2 tile) =>
        new(tile.X * 64f + 32f, tile.Y * 64f + 32f);
}
