using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Enchantments;
using StardewValley.Monsters;
using StardewValley.Tools;

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
    private const float HitRange             = 256f;  // pixels; deal damage when within this (same as AttackRange — no first-hit delay)
    private const float StrafeRadius         = 128f;  // pixels; distance from monster when strafing (2 tiles)
    private const float MoveSpeedNear        = 5f;    // pixels/tick when close
    private const float MoveSpeedFar         = 18f;   // pixels/tick when far
    private const float NearThreshold        = 64f;   // 1 tile
    private const float FarThreshold         = 192f;  // 3 tiles
    private const float ArrivalThreshold     = 6f;    // pixels "close enough"
    private const float LeashRadius          = 1024f; // 16 tiles — generous leash so monsters that wander are still chased

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

    // Shared claim manager — prefer unclaimed monsters, but will join claimed ones if needed
    private SharedTargetManager? _targetMgr;
    private Vector2? _claimedMonsterTile;

    // Strafing: random cardinal position around the monster
    private Vector2 _strafeTarget;
    private static readonly Random _rng = new();

    // Cached Bug-Killer sword used to pierce Armored Bug immunity.
    // Lazily created on first use (game content must be loaded first).
    private static MeleeWeapon? _bugKillerWeapon;

    // ── public output ─────────────────────────────────────────────

    /// <summary>Current world-pixel position of the grandpa sprite.</summary>
    public Vector2 WorldPosition { get; private set; }

    /// <summary>Screen-space shake offset during attacks.</summary>
    public Vector2 DrawShakeOffset { get; private set; }

    /// <summary>True while grandpa is actively fighting (not Orbiting). Used for red tint.</summary>
    public bool IsFighting => _state != State.Orbiting;

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

        // Leash check: if the TARGET is too far from the player, abort.
        // We only check the target's distance — grandpa's own distance from
        // the player is irrelevant; he should be free to chase monsters within
        // scan range even if that takes him far from the player's position.
        if (_state != State.Orbiting && _target != null)
        {
            float targetDistToPlayer = Vector2.Distance(
                _target.getStandingPosition(), player.getStandingPosition());

            if (targetDistToPlayer > LeashRadius)
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
            DealDamage(loc, player, _target);
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
    /// <c>damageMonster</c> method, which handles knockback, death animation,
    /// sound effects, loot drops, and damage-number display.
    /// </summary>
    private void DealDamage(GameLocation loc, Farmer player, Monster monster)
    {
        // Damage: 10 + 10 × CombatLevel  (level 0 = 10, level 10 = 110)
        int damage = 10 + 10 * player.CombatLevel;

        // AoE centered on the monster so it's always in range
        var monsterPos = monster.getStandingPosition();
        var area = new Rectangle(
            (int)monsterPos.X - 64, (int)monsterPos.Y - 64,
            128, 128);

        // Mummy (Skull Cavern) has a two-phase death mechanic that a single
        // normal hit cannot finish: dropping HP to 0 without a Crusader-enchanted
        // weapon only crumbles it — HP resets to MaxHealth and it revives 10s
        // later. Downed Mummies are immune to all damage except explosions.
        // Grandpa is self-sufficient here — he doesn't need the player to hold
        // any particular weapon. He knocks the Mummy down with a bomb-style hit
        // (isBomb=true still triggers crumble at HP 0 because the Crusader check
        // requires !isBomb), then immediately follows up with another bomb hit
        // to finish it.
        if (monster is Mummy mummy)
        {
            DealDamageToMummy(loc, player, mummy, damage, area);
            return;
        }

        // Armored Bug (Skull Cavern, isArmoredBug=true) is immune to ALL damage
        // except a non-bomb hit from a Bug-Killer-enchanted MeleeWeapon.
        //
        // Inside Bug.takeDamage (decompiled SDV 1.6):
        //   if (isArmoredBug && (isBomb || !(who.CurrentTool is MeleeWeapon)
        //                          || !meleeWeapon.hasEnchantmentOfType<BugKillerEnchantment>()))
        //       return 0;
        //
        // Grandpa's default isBomb=true — which is the VERY condition that makes
        // Armored Bugs immune. So Armored Bugs must be handled by a dedicated
        // path that swaps in a Bug-Killer weapon and uses isBomb=false.
        if (monster is Bug bug && bug.isArmoredBug.Value)
        {
            DealDamageToArmoredBug(loc, player, bug, damage, area);
            return;
        }

        // CRITICAL: Clear invincibility frames before calling damageMonster.
        //
        // Inside damageMonster (decompiled SDV 1.6, GameLocation.cs line 4647):
        //   if (!monster.IsInvisible && !monster.isInvincible() && (isBomb || ...))
        //
        // isBomb=true bypasses isMonsterDamageApplicable (tool/path checks),
        // but does NOT bypass !monster.isInvincible(). The player's own weapon
        // swings set invincibility frames on the monster (via setInvincibleCountdown),
        // and those frames block ALL damage — including isBomb=true hits — for
        // ~225ms. Grandpa's AttackCooldownTicks (10 ticks ≈ 167ms) is shorter
        // than this window, so without clearing, most of his hits are silently
        // skipped. This was the root cause of "grandpa goes to attack but can't
        // deal damage when the player is far away" — the player's earlier swings
        // left invincibility frames that blocked grandpa's subsequent hits.
        //
        // We pass triggerMonsterInvincibleTimer=false so grandpa's own hits
        // do NOT set new invincibility frames, but we still need to clear any
        // existing frames from the player's attacks.
        monster.invincibleCountdown = 0;

        // isBomb=true makes grandpa's hits completely independent of the
        // player's current tool. Inside damageMonster:
        //   • isMonsterDamageApplicable(who, monster) — returns false when the
        //     player holds a non-weapon (e.g. a pickaxe while mining ore),
        //     causing isBomb=false hits to be silently skipped.
        //   • Bug.isArmoredBug — the armor immunity check is
        //     `isArmoredBug && (isBomb || !(CurrentTool is MeleeWeapon)
        //                        || !hasBugKiller)` — so isBomb=true makes
        //     Armored Bugs IMMUNE. Armored Bugs are handled separately above.
        //
        // Note: hitWithTool(who.CurrentTool) is NOT bypassed by isBomb, but
        // only fires for specific monster+tool combos (e.g. Rock Crab shell
        // + pickaxe) — not a general issue.
        loc.damageMonster(
            area,           // areaOfEffect
            damage,         // minDamage
            damage,         // maxDamage
            true,           // isBomb — bypasses tool-dependent immunity checks
            0.5f,           // knockBackModifier
            0,              // addedPrecision
            0f,             // critChance
            1f,             // critMultiplier
            false,          // triggerMonsterInvincibleTimer — don't set frames
            player          // who
        );

        // Small SAN cost per hit
        SanManager.AddSan(player, -0.5f);

        // If the monster died, consume a bit more SAN
        if (monster.Health <= 0)
            SanManager.AddSan(player, -2f);
    }

    /// <summary>
    /// Grandpa's self-sufficient Mummy kill — no player weapon required.
    ///
    /// Mummy takeDamage rules (SDV 1.6):
    ///   • reviveTimer &gt; 0 (downed): isBomb=false → immune (returns -1);
    ///     isBomb=true → Health=0, runs death animation &amp; drops.
    ///   • reviveTimer == 0 (alive): a hit that drops HP to 0 without a
    ///     Crusader-enchanted weapon → Mummy crumbles (HP reset to MaxHealth,
    ///     reviveTimer = 10000). It is now downed and waiting for a bomb.
    ///     Key: the Crusader check is `!isBomb && who.CurrentTool is MeleeWeapon
    ///     with CrusaderEnchantment` — so isBomb=true always falls through to
    ///     the crumble path, regardless of the player's weapon.
    ///
    /// Grandpa always finishes in (at most) two beats, regardless of what the
    /// player is holding:
    ///   1. If still standing, a bomb-style hit depletes HP and triggers crumble.
    ///      (isBomb=true bypasses isMonsterDamageApplicable, so it works even
    ///      when the player holds a pickaxe/ore — unlike isBomb=false.)
    ///   2. A bomb-style hit on the downed Mummy forces Health=0 via the
    ///      vanilla death path (animation, loot drops, removal all handled by
    ///      the game — we don't touch any fields manually).
    ///
    /// Both beats use isBomb=true and clear invincibility frames beforehand,
    /// so the Mummy takes damage regardless of the player's tool or recent
    /// attack history.
    /// </summary>
    private void DealDamageToMummy(GameLocation loc, Farmer player, Mummy mummy, int damage, Rectangle area)
    {
        // Beat 1: if still standing, knock it down with bomb-style damage.
        // isBomb=true is used here (not false) so that isMonsterDamageApplicable
        // is bypassed — this lets grandpa damage the Mummy even when the player
        // is holding a non-weapon tool (e.g. pickaxe while mining).
        // At HP 0, the Crusader check requires !isBomb, so isBomb=true always
        // falls through to the crumble path: HP=MaxHealth, reviveTimer=10000.
        if (mummy.reviveTimer.Value <= 0)
        {
            mummy.invincibleCountdown = 0;
            loc.damageMonster(
                area, damage, damage,
                true,           // isBomb — bypasses isMonsterDamageApplicable
                0.5f, 0, 0f, 1f,
                false, player);
            SanManager.AddSan(player, -0.5f);
        }

        // Beat 2: if the Mummy is now downed (or was already downed when we
        // picked the target), finish it with a bomb-style hit. Inside takeDamage
        // this forces Health=0 and runs the vanilla death animation + loot drops.
        if (mummy.reviveTimer.Value > 0 && mummy.Health > 0)
        {
            mummy.invincibleCountdown = 0;
            loc.damageMonster(
                area, damage, damage,
                true,           // isBomb — kills a downed Mummy
                0.5f, 0, 0f, 1f,
                false, player);
            SanManager.AddSan(player, -0.5f);
        }

        // Kill SAN surcharge — only the final bomb hit leaves Health at 0.
        if (mummy.Health <= 0)
            SanManager.AddSan(player, -2f);
    }

    /// <summary>
    /// Grandpa's self-sufficient Armored Bug kill — no player weapon required.
    ///
    /// Armored Bugs (Skull Cavern) are immune to everything except a non-bomb
    /// hit from a Bug-Killer-enchanted MeleeWeapon. Grandpa normally uses
    /// isBomb=true, which is exactly what makes Armored Bugs immune — so they
    /// need this dedicated path.
    ///
    /// Strategy: temporarily equip a cached Bug-Killer sword onto the player,
    /// hit with isBomb=false so the armor check passes, and use
    /// isProjectile=true to bypass the line-of-sight check
    /// (isMonsterDamageApplicable) — that way grandpa can hit even when the
    /// player is far away or walls block LOS. The player's real tool is
    /// restored immediately afterwards.
    /// </summary>
    private void DealDamageToArmoredBug(GameLocation loc, Farmer player, Bug bug, int damage, Rectangle area)
    {
        var oldTool = player.CurrentTool;
        player.CurrentTool = GetBugKillerWeapon();

        // Clear any invincibility frames left by the player's own attacks.
        bug.invincibleCountdown = 0;

        // isBomb=false is REQUIRED — isBomb=true makes Armored Bugs immune.
        // isProjectile=true bypasses isMonsterDamageApplicable (LOS check),
        // which would otherwise block the hit when the player is far from the
        // monster or walls are in the way. isProjectile has no other effect
        // inside damageMonster.
        loc.damageMonster(
            area,           // areaOfEffect
            damage,         // minDamage
            damage,         // maxDamage
            false,          // isBomb — MUST be false for Armored Bug
            0.5f,           // knockBackModifier
            0,              // addedPrecision
            0f,             // critChance
            1f,             // critMultiplier
            false,          // triggerMonsterInvincibleTimer
            player,         // who
            true            // isProjectile — bypass LOS check
        );

        // Restore the player's real tool immediately.
        player.CurrentTool = oldTool;

        // Small SAN cost per hit
        SanManager.AddSan(player, -0.5f);

        // If the bug died, consume a bit more SAN
        if (bug.Health <= 0)
            SanManager.AddSan(player, -2f);
    }

    /// <summary>
    /// Returns a cached MeleeWeapon with the Bug Killer enchantment, used to
    /// pierce Armored Bug immunity. Created lazily on first use because
    /// ItemRegistry requires game content to be loaded first.
    /// </summary>
    private static MeleeWeapon GetBugKillerWeapon()
    {
        if (_bugKillerWeapon is null)
        {
            // "(W)0" is the Training Sword — a guaranteed-to-exist MeleeWeapon.
            // The sword's own stats don't matter; grandpa supplies the damage
            // value via damageMonster's min/maxDamage. We only need it to carry
            // the Bug Killer enchantment so Bug.takeDamage's armor check passes.
            _bugKillerWeapon = (MeleeWeapon)ItemRegistry.Create("(W)0");
            _bugKillerWeapon.AddEnchantment(new BugKillerEnchantment());
        }
        return _bugKillerWeapon;
    }

    // ── helpers ───────────────────────────────────────────────────

    /// <summary>
    /// True if <paramref name="monster"/> is a Skull Cavern Mummy that has been
    /// knocked down (<c>reviveTimer &gt; 0</c>) and is awaiting a bomb to finish
    /// it. In this state the Mummy is immune to all non-explosive damage and
    /// will revive at full HP after 10 seconds.
    /// </summary>
    private static bool IsDownedMummy(Monster monster) =>
        monster is Mummy mummy && mummy.reviveTimer.Value > 0;

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
        // Downed Mummies revive after 10s — finish them first while they are
        // helpless. Priority order:
        //   1. unclaimed downed Mummy  (stop the revive, spread out)
        //   2. unclaimed monster       (normal spread-out strategy)
        //   3. any downed Mummy        (focus fire to stop the revive)
        //   4. nearest monster         (focus fire fallback)
        if (_targetMgr is null)
        {
            foreach (var m in monsters)
                if (IsDownedMummy(m)) return m;
            return monsters[0]; // no sharing — just pick nearest
        }

        foreach (var m in monsters)
            if (IsDownedMummy(m) && !_targetMgr.IsClaimed(m.Tile))
                return m;

        // First pass: try unclaimed
        foreach (var m in monsters)
        {
            if (!_targetMgr.IsClaimed(m.Tile))
                return m;
        }

        // All monsters claimed — prefer a downed Mummy, else nearest
        foreach (var m in monsters)
            if (IsDownedMummy(m)) return m;

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
