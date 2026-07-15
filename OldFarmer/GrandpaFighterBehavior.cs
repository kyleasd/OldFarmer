using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Monsters;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Drives grandpa's combat behaviour as a finite-state machine.
///
///   Orbiting → MovingToTarget → Attacking → Cooldown → NextTarget → …
///
/// During <see cref="State.Attacking"/>, grandpa teleports-er, dashes to
/// random cardinal positions around the monster (up/down/left/right),
/// dealing damage each time he arrives at a new position.
///
/// Unlike farming behaviours, combat is NOT restricted to the farm —
/// grandpa will fight monsters anywhere (mines, wilderness, etc.).
/// While fighting, the orbiter tints grandpa red via <see cref="IsFighting"/>.
/// </summary>
internal sealed class GrandpaFighterBehavior
{
    // ── tunables ──────────────────────────────────────────────────
    private const int   AttackCooldownTicks  = 10;    // ~0.17 s between hits
    private const int   CooldownDurationTicks= 8;     // ~0.13 s pause after a kill
    private const int   MaxAttackTicks       = 120;   // safety: give up after ~2 s on one target
    private const float AttackRange          = 256f;  // pixels; stop & attack when within this (4 tiles)
    private const float HitRange             = 192f;  // pixels; deal damage when within this (3 tiles)
    private const float StrafeRadius         = 128f;  // pixels; distance from monster when strafing (2 tiles)
    private const float MoveSpeedNear        = 5f;    // pixels/tick when close
    private const float MoveSpeedFar         = 18f;   // pixels/tick when far
    private const float NearThreshold        = 64f;   // 1 tile
    private const float FarThreshold         = 192f;  // 3 tiles
    private const float ArrivalThreshold     = 6f;    // pixels "close enough"
    private const float LeashRadius          = 704f;  // 11 tiles — slightly beyond scan to allow chase

    // ── state ─────────────────────────────────────────────────────
    private enum State
    {
        Orbiting,
        MovingToTarget,
        Attacking,
        Cooldown,
        NextTarget,
    }

    private State _state = State.Orbiting;
    private Monster? _target;
    private int _attackTick;
    private int _cooldownTick;
    private int _hitTick;
    private IMonitor? _monitor;

    // Shared claim manager — prefer unclaimed monsters, but will join claimed ones if needed
    private SharedTargetManager? _targetMgr;
    private Vector2? _claimedMonsterTile;

    // Strafing: random cardinal position around the monster
    private Vector2 _strafeTarget;
    private static readonly Random _rng = new();

    // ── public output ─────────────────────────────────────────────

    /// <summary>Current world-pixel position of the grandpa sprite.</summary>
    public Vector2 WorldPosition { get; private set; }

    /// <summary>Screen-space shake offset during attacks.</summary>
    public Vector2 DrawShakeOffset { get; private set; }

    /// <summary>True while grandpa is actively fighting (not Orbiting). Used for red tint.</summary>
    public bool IsFighting => _state != State.Orbiting;

    public void SetMonitor(IMonitor monitor) => _monitor = monitor;

    public void SetTargetManager(SharedTargetManager mgr) => _targetMgr = mgr;

    // ── public control ────────────────────────────────────────────

    /// <summary>
    /// Force-reset the state machine back to Orbiting.
    /// Called by <see cref="CombatModule.Disable"/>.
    /// </summary>
    public void Reset()
    {
        ReleaseClaim();
        _state       = State.Orbiting;
        _target      = null;
        _attackTick  = 0;
        _cooldownTick= 0;
        _hitTick     = 0;
        DrawShakeOffset = Vector2.Zero;
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

        // ── SAN check: stop fighting if SAN <= 0 ─────────────────
        if (SanManager.GetSan(player) <= 0)
        {
            if (_state != State.Orbiting)
            {
                TransitionTo(State.Orbiting);
            }
            return;
        }

        // Leash check: if grandpa or target is too far from the player, abort
        if (_state != State.Orbiting && _target != null)
        {
            float distToPlayer = Vector2.Distance(WorldPosition, player.getStandingPosition());
            float targetDistToPlayer = Vector2.Distance(
                _target.getStandingPosition(), player.getStandingPosition());

            if (distToPlayer > LeashRadius || targetDistToPlayer > LeashRadius)
            {
                TransitionTo(State.Orbiting);
            }
        }
        else if (_state != State.Orbiting && _target is null)
        {
            TransitionTo(State.Orbiting);
        }

        switch (_state)
        {
            case State.Orbiting:       TickOrbiting(loc, player);    break;
            case State.MovingToTarget: TickMovingToTarget();          break;
            case State.Attacking:      TickAttacking(loc, player);    break;
            case State.Cooldown:       TickCooldown();                break;
            case State.NextTarget:     TickNextTarget(loc, player);   break;
        }
    }

    // ── per-state ticks ───────────────────────────────────────────

    private void TickOrbiting(GameLocation loc, Farmer player)
    {
        DrawShakeOffset = Vector2.Zero;

        var monsters = MonsterScanner.GetNearbyMonsters(loc, player);
        if (monsters.Count == 0)
            return;

        // Prefer an unclaimed monster (spread out), but fall back to claimed ones (focus fire)
        var picked = PickBestTarget(monsters);
        if (picked is null)
            return;

        _target = picked;
        ClaimTarget(picked);
        TransitionTo(State.MovingToTarget);
    }

    private void TickMovingToTarget()
    {
        DrawShakeOffset = Vector2.Zero;

        // Target may have died or been removed
        if (_target is null || _target.Health <= 0 || _target.currentLocation is null)
        {
            TransitionTo(State.NextTarget);
            return;
        }

        var targetPos = _target.getStandingPosition();
        MoveToward(targetPos);

        float dist = Vector2.Distance(WorldPosition, targetPos);
        if (dist <= AttackRange)
        {
            _attackTick = 0;
            _hitTick    = AttackCooldownTicks; // ready to hit immediately
            PickNewStrafeTarget(targetPos);
            TransitionTo(State.Attacking);
        }
    }

    private void TickAttacking(GameLocation loc, Farmer player)
    {
        _attackTick++;

        // Target died or disappeared — move on
        if (_target is null || _target.Health <= 0 || _target.currentLocation is null)
        {
            DrawShakeOffset = Vector2.Zero;
            TransitionTo(State.Cooldown);
            return;
        }

        // Safety timeout — don't get stuck on one monster
        if (_attackTick >= MaxAttackTicks)
        {
            DrawShakeOffset = Vector2.Zero;
            TransitionTo(State.Cooldown);
            return;
        }

        var monsterPos = _target.getStandingPosition();
        float distToMonster = Vector2.Distance(WorldPosition, monsterPos);

        // If the monster moved too far away, chase it again
        if (distToMonster > AttackRange * 1.5f)
        {
            DrawShakeOffset = Vector2.Zero;
            TransitionTo(State.MovingToTarget);
            return;
        }

        // ── Deal damage based on proximity to monster ───────────
        // Grandpa hits the monster whenever within HitRange and cooldown is ready,
        // regardless of strafe position. This makes the attack feel responsive.
        _hitTick++;
        if (_hitTick >= AttackCooldownTicks && distToMonster <= HitRange)
        {
            _hitTick = 0;
            DealDamage(loc, player, _target, monsterPos);
        }

        // ── Strafe: move toward the current cardinal target ──────
        float distToStrafe = Vector2.Distance(WorldPosition, _strafeTarget);

        if (distToStrafe <= ArrivalThreshold)
        {
            // Arrived — pick a new random cardinal position around the monster
            PickNewStrafeTarget(monsterPos);
        }
        else
        {
            // Move toward the strafe target
            MoveToward(_strafeTarget);
        }

        // Light shake while strafing
        float sx = (float)Math.Sin(_attackTick * 18f) * 2f;
        float sy = (float)Math.Cos(_attackTick * 18f) * 1f;
        DrawShakeOffset = new Vector2(sx, sy);
    }

    private void TickCooldown()
    {
        _cooldownTick++;
        DrawShakeOffset = Vector2.Zero;
        if (_cooldownTick >= CooldownDurationTicks)
            TransitionTo(State.NextTarget);
    }

    private void TickNextTarget(GameLocation loc, Farmer player)
    {
        ReleaseClaim();

        var monsters = MonsterScanner.GetNearbyMonsters(loc, player);
        if (monsters.Count > 0)
        {
            var picked = PickBestTarget(monsters);
            if (picked != null)
            {
                _target = picked;
                ClaimTarget(picked);
                _attackTick = 0;
                _hitTick    = AttackCooldownTicks;
                TransitionTo(State.MovingToTarget);
                return;
            }
        }

        TransitionTo(State.Orbiting);
    }

    // ── combat ────────────────────────────────────────────────────

    /// <summary>
    /// Deals damage to the target monster using the game's native
    /// <c>takeDamage</c> method, which handles knockback, death animation,
    /// sound effects, and loot drops.
    /// </summary>
    private void DealDamage(GameLocation loc, Farmer player, Monster monster, Vector2 monsterPos)
    {
        // Damage: 10 + 10 × CombatLevel  (level 0 = 10, level 10 = 110)
        int damage = 10 + 10 * player.CombatLevel;

        // Knockback direction: from grandpa → monster
        var dir = monsterPos - WorldPosition;
        if (dir.LengthSquared() < 0.01f)
            dir = new Vector2(1f, 0f);
        dir = Vector2.Normalize(dir);
        int xKnockback = (int)(dir.X * 80);
        int yKnockback = (int)(dir.Y * 80);

        int healthBefore = monster.Health;

        // Use the game's native damage method
        monster.takeDamage(damage, xKnockback, yKnockback, false, 0, player);

        // Small SAN cost per hit
        SanManager.AddSan(player, -0.5f);

        // If the monster died, consume a bit more SAN and move on
        if (monster.Health <= 0)
        {
            SanManager.AddSan(player, -2f);
            loc.playSound("cowboy_monsterhit");
        }
    }

    // ── helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Picks a random cardinal direction (up/down/left/right) and sets
    /// <see cref="_strafeTarget"/> to that offset from the monster's position.
    /// </summary>
    private void PickNewStrafeTarget(Vector2 monsterPos)
    {
        int dir = _rng.Next(4);
        _strafeTarget = dir switch
        {
            0 => monsterPos + new Vector2(0, -StrafeRadius),  // up
            1 => monsterPos + new Vector2(0,  StrafeRadius),   // down
            2 => monsterPos + new Vector2(-StrafeRadius, 0),   // left
            _ => monsterPos + new Vector2(StrafeRadius,  0),   // right
        };
    }

    private void TransitionTo(State next)
    {
        // Sync WorldPosition to player when leaving Orbiting state
        if (_state == State.Orbiting && next != State.Orbiting && Game1.player != null)
            WorldPosition = Game1.player.getStandingPosition();

        _state = next;

        if (next == State.Orbiting)
        {
            ReleaseClaim();
            _target = null;
            DrawShakeOffset = Vector2.Zero;
        }

        if (next == State.Attacking)
            _attackTick = 0;

        if (next == State.Cooldown)
            _cooldownTick = 0;
    }

    private void MoveToward(Vector2 target)
    {
        var   dir  = target - WorldPosition;
        float dist = dir.Length();
        if (dist <= ArrivalThreshold)
        {
            WorldPosition = target;
            return;
        }

        float t     = Math.Clamp((dist - NearThreshold) / (FarThreshold - NearThreshold), 0f, 1f);
        float speed = MoveSpeedNear + t * (MoveSpeedFar - MoveSpeedNear);

        // Combat movement is 1.4× faster — grandpa is motivated!
        speed *= 1.4f;

        WorldPosition = dist <= speed
            ? target
            : WorldPosition + Vector2.Normalize(dir) * speed;
    }

    // ── shared target claiming (soft strategy: prefer unclaimed, fall back to claimed) ──

    /// <summary>
    /// Picks the best monster from the list.
    /// Strategy: prefer unclaimed monsters first (spread out), then fall back to
    /// any monster (focus fire). The list is already sorted nearest-first by the scanner.
    /// </summary>
    private Monster? PickBestTarget(List<Monster> monsters)
    {
        if (_targetMgr is null)
            return monsters[0]; // no sharing — just pick nearest

        // First pass: try unclaimed
        foreach (var m in monsters)
        {
            if (!_targetMgr.IsClaimed(m.Tile))
                return m;
        }

        // All monsters claimed — join the fight on the nearest
        return monsters[0];
    }

    private void ClaimTarget(Monster monster)
    {
        if (_targetMgr is null) return;
        ReleaseClaim();
        _claimedMonsterTile = monster.Tile;
        _targetMgr.TryClaim(monster.Tile);
    }

    private void ReleaseClaim()
    {
        if (_targetMgr != null && _claimedMonsterTile.HasValue)
        {
            _targetMgr.Release(_claimedMonsterTile.Value);
            _claimedMonsterTile = null;
        }
    }
}
