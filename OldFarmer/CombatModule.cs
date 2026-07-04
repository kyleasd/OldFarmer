using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Self-contained module that drives grandpa's combat behaviour.
///
/// When enabled (monsters nearby):
///   1. Ticks <see cref="GrandpaFighterBehavior"/> every game update.
///   2. Feeds the combat position / shake back into <see cref="GrandpaSpiritOrbiter"/>.
///   3. The orbiter tints grandpa red while <see cref="IsEnabled"/> is active.
///
/// Unlike tool-based modules, combat is triggered reactively by
/// <see cref="MonsterScanner.HasMonstersNearby"/> — not by the player's held tool.
/// It takes the highest rendering priority in the orbiter.
/// </summary>
internal sealed class CombatModule
{
    private readonly GrandpaFighterBehavior _fighter;
    private readonly GrandpaSpiritOrbiter    _orbiter;

    public CombatModule(GrandpaSpiritOrbiter orbiter)
    {
        _orbiter = orbiter;
        _fighter = new GrandpaFighterBehavior();
    }

    public void SetMonitor(IMonitor monitor)
    {
        _fighter.SetMonitor(monitor);
    }

    // ── public API ────────────────────────────────────────────────

    public bool IsEnabled { get; private set; }

    public void Enable() => IsEnabled = true;

    public void Disable()
    {
        IsEnabled = false;
        _fighter.Reset();
        _orbiter.CombatWorldPosition = null;
        _orbiter.IsInCombatMode      = false;
        _orbiter.DrawShakeOffset     = Vector2.Zero;
    }

    // ── game loop hooks ───────────────────────────────────────────

    public void Update()
    {
        if (!IsEnabled) return;

        // Grandpa is red whenever the combat module is enabled (monsters nearby),
        // even while orbiting — this gives the player a visual warning.
        _orbiter.IsInCombatMode = true;

        _fighter.Update();

        if (_fighter.IsFighting)
        {
            _orbiter.CombatWorldPosition = _fighter.WorldPosition;
            _orbiter.DrawShakeOffset     = _fighter.DrawShakeOffset;
        }
        else
        {
            _orbiter.CombatWorldPosition = null;
            _orbiter.DrawShakeOffset     = Vector2.Zero;
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        // Combat has no extra sprite overlay — grandpa himself is the weapon.
        // The red tint is handled by the orbiter via IsInCombatMode.
    }
}
