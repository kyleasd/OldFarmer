using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Drives grandpa's woodcutting as a finite-state machine:
///
///   Orbiting → (trees found) → MovingToX → MovingToY → Chopping → Cooldown → NextTarget → ...
///
/// While chopping, the axe sprite orbits around grandpa and instantly fells any
/// tree/stump it touches. Horizontal-first movement creates an L-shaped
/// approach path.
/// </summary>
internal sealed class GrandpaWoodcutterBehavior
{
    // ── tunables ──────────────────────────────────────────────────
    private const float MoveSpeed          = 8f;    // pixels/tick
    private const float ArrivalThreshold   = 8f;    // pixels; "close enough"
    private const int   CooldownTicks      = 15;    // ~0.25 s pause between passes
    private const int   ChopDurationTicks  = 180;   // ~3 s of spinning axe per target (6+ rotations)
    private const float AxeOrbitRadius     = 56f;   // pixels (~0.875 tiles)
    private const float AxeOrbitSpeed      = 0.22f; // radians/tick
    private const float LeashRadius        = 640f;  // 10 tiles; abandon if player wanders too far

    // ── state ─────────────────────────────────────────────────────
    private enum State
    {
        Orbiting,
        MovingToX,      // horizontal leg of L-shaped approach
        MovingToY,      // vertical leg
        Chopping,       // axe spinning, destroying on contact
        Cooldown,
        NextTarget,
    }

    private State _state = State.Orbiting;
    private bool _wasOnFarm;

    private Queue<Vector2> _targetQueue = new();
    private Vector2 _targetTile;
    private Vector2 _targetWorldPos;
    private int _cooldownTick;
    private int _chopTick;
    private int _scanLogCooldown;
    private bool _didFirstScanLog;

    // ── axe orbit ─────────────────────────────────────────────────
    private float _axeAngle;
    private Vector2 _axeWorldPos;
    private IMonitor? _monitor;

    // ── public output ─────────────────────────────────────────────

    /// <summary>Current world-pixel position of the grandpa sprite.</summary>
    public Vector2 WorldPosition { get; private set; }

    /// <summary>True while grandpa is actively working (not in Orbiting state).</summary>
    public bool IsChopping => _state != State.Orbiting;

    /// <summary>Current world-pixel position of the orbiting axe.</summary>
    public Vector2 AxeWorldPosition => _axeWorldPos;

    /// <summary>Current rotation angle of the axe (radians).</summary>
    public float AxeAngle => _axeAngle;

    public void SetMonitor(IMonitor monitor)
    {
        _monitor = monitor;
    }

    // ── public control ────────────────────────────────────────────
    /// <summary>
    /// Force-reset the entire state machine back to Orbiting.
    /// Called by <see cref="WoodcuttingModule.Disable"/> so that disabling
    /// the module mid-action does not leave grandpa in a stuck state.
    /// </summary>
    public void Reset()
    {
        _state        = State.Orbiting;
        _targetQueue?.Clear();
        _chopTick     = 0;
        _cooldownTick = 0;
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
            {
                TransitionTo(State.Orbiting);
                _monitor?.Log("[Woodcutter] SAN <= 0, stopping work", LogLevel.Info);
            }
            return;
        }

        // NOTE: we do NOT sync WorldPosition with the orbit position here,
        // because the orbit is computed in screen space and WorldPosition is in
        // world space.  The leash check below treats "Orbiting" as
        // "grandpa is near the player" — which is always true.

        // Only work on the outdoor farm (not FarmHouse interior etc.)
        if (!loc.IsFarm || !loc.IsOutdoors)
        {
            _wasOnFarm = false;
            if (_state != State.Orbiting)
                TransitionTo(State.Orbiting);
            return;
        }

        if (!_wasOnFarm)
        {
            _wasOnFarm = true;
            _monitor?.Log($"[Woodcutting] Entered outdoor farm '{loc.Name}' — scanning for trees...", LogLevel.Info);
        }

        // Leash check: too far from player → move back at variable speed
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


        // Always advance the axe angle for smooth visual continuity,
        // but only record world position when we're in chopping state.
        _axeAngle += AxeOrbitSpeed;
        if (_axeAngle >= MathHelper.TwoPi)
            _axeAngle -= MathHelper.TwoPi;

        if (_state == State.Chopping || _state == State.MovingToX || _state == State.MovingToY)
        {
            _axeWorldPos = WorldPosition + new Vector2(
                (float)Math.Cos(_axeAngle) * AxeOrbitRadius,
                (float)Math.Sin(_axeAngle) * AxeOrbitRadius);
        }

        switch (_state)
        {
            case State.Orbiting:    TickOrbiting(loc, player);   break;
            case State.MovingToX:   TickMovingToX();              break;
            case State.MovingToY:   TickMovingToY();              break;
            case State.Chopping:    TickChopping(loc, player);    break;
            case State.Cooldown:    TickCooldown();               break;
            case State.NextTarget:  TickNextTarget(loc, player);  break;
        }
    }

    // ── per-state ticks ───────────────────────────────────────────

    private void TickOrbiting(GameLocation loc, Farmer player)
    {
        // WorldPosition is synced to the orbit position by Update()
        // when in Orbiting state — no teleport needed.

        var targets = WoodcutterScanner.GetChoppableTiles(loc, player);

        // Diagnostic: log scan results on first scan and every 120 ticks
        _scanLogCooldown = (_scanLogCooldown + 1) % 120;
        if (!_didFirstScanLog || _scanLogCooldown == 0)
        {
            _didFirstScanLog = true;
            var (total, choppable) = WoodcutterScanner.CountTrees(loc, player);
            _monitor?.Log($"[Woodcutting] Scan: {total} total trees, {choppable} choppable (stage≥5 or stump). Targets returned: {targets.Count}. PlayerTile=({player.Tile.X},{player.Tile.Y}) Loc='{loc.Name}'", LogLevel.Info);
        }

        if (targets.Count == 0)
            return;

        // Sort nearest-first
        var playerPos = player.getStandingPosition();
        targets.Sort((a, b) =>
            Vector2.Distance(TileCenter(a), playerPos)
                .CompareTo(Vector2.Distance(TileCenter(b), playerPos)));

        _targetQueue = new Queue<Vector2>(targets);
        _targetTile = _targetQueue.Dequeue();
        _targetWorldPos = TileCenter(_targetTile);

        _monitor?.Log($"[Woodcutting] Transitioning Orbiting→MovingToX, target=({_targetTile.X:F0},{_targetTile.Y:F0})", LogLevel.Info);
        TransitionTo(State.MovingToX);
    }

    // Horizontal leg: move only along X until aligned.
    private void TickMovingToX()
    {
        float dx = _targetWorldPos.X - WorldPosition.X;
        if (Math.Abs(dx) <= ArrivalThreshold)
        {
            WorldPosition = new Vector2(_targetWorldPos.X, WorldPosition.Y);
            TransitionTo(State.MovingToY);
            return;
        }

        float step = Math.Min(Math.Abs(dx), MoveSpeed) * Math.Sign(dx);
        WorldPosition = new Vector2(WorldPosition.X + step, WorldPosition.Y);
    }

    // Vertical leg: move only along Y after X is aligned.
    private void TickMovingToY()
    {
        float dy = _targetWorldPos.Y - WorldPosition.Y;
        if (Math.Abs(dy) <= ArrivalThreshold)
        {
            WorldPosition = _targetWorldPos;
            _monitor?.Log($"[Woodcutting] Arrived at target, MovingToY→Chopping", LogLevel.Info);
            TransitionTo(State.Chopping);
            return;
        }

        float step = Math.Min(Math.Abs(dy), MoveSpeed) * Math.Sign(dy);
        WorldPosition = new Vector2(WorldPosition.X, WorldPosition.Y + step);
    }

    private void TickChopping(GameLocation loc, Farmer player)
    {
        _chopTick++;

        // Try to destroy trees at the axe's current position
        // Returns the number of trees actually destroyed this tick
        int treesDestroyed = WoodcutterExecutor.ChopAtPosition(loc, player, _axeWorldPos);

        // Consume SAN per tree destroyed (8 SAN per tree)
        if (treesDestroyed > 0 && Game1.player != null)
            SanManager.AddSan(Game1.player, -8f * treesDestroyed);

        if (_chopTick >= ChopDurationTicks)
        {
            _chopTick = 0;
            _cooldownTick = 0;
            TransitionTo(State.Cooldown);
        }
    }

    private void TickCooldown()
    {
        _cooldownTick++;
        if (_cooldownTick >= CooldownTicks)
            TransitionTo(State.NextTarget);
    }

    private void TickNextTarget(GameLocation loc, Farmer player)
    {
        // Try remaining targets in the queue; re-validate each one
        while (_targetQueue.Count > 0)
        {
            var candidate = _targetQueue.Dequeue();
            if (IsStillChoppable(loc, candidate))
            {
                _targetTile = candidate;
                _targetWorldPos = TileCenter(candidate);
                TransitionTo(State.MovingToX);
                return;
            }
        }

        // Queue exhausted — re-scan next tick
        TransitionTo(State.Orbiting);
    }

    // ── helpers ───────────────────────────────────────────────────

    private void TransitionTo(State next)
    {

        // Sync WorldPosition to player when leaving Orbiting state
        if (_state == State.Orbiting && next != State.Orbiting && Game1.player != null)
            WorldPosition = Game1.player.getStandingPosition();
        _state = next;

        // On enter Orbiting: clear work queue so we don't re-pick a far-away target
        if (next == State.Orbiting)
            _targetQueue?.Clear();
    }

    /// <summary>
    /// Returns true if the tile still has a choppable tree or stump on it.
    /// </summary>
    private static bool IsStillChoppable(GameLocation loc, Vector2 tile)
    {
        if (!loc.isTileOnMap(tile))
            return false;

        // Check terrain features
        if (loc.terrainFeatures.TryGetValue(tile, out var feature)
            && feature is Tree tree)
        {
            return tree.growthStage.Value >= 5 || tree.stump.Value;
        }

        return false;
    }

    private static Vector2 TileCenter(Vector2 tile) =>
        new(tile.X * 64f + 32f, tile.Y * 64f + 32f);
}
