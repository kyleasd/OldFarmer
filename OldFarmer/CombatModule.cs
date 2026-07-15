using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Self-contained module that drives grandpa's combat behaviour.
/// Supports multiple grandpas — each gets its own behavior instance.
///
/// Unlike tool-based modules, combat is triggered reactively by
/// <see cref="MonsterScanner.HasMonstersNearby"/> — not by the player's held tool.
/// It takes the highest rendering priority in the orbiter.
/// </summary>
internal sealed class CombatModule
{
    private readonly List<(GrandpaFighterBehavior behavior, GrandpaSpiritOrbiter orbiter)> _entries = new();
    private readonly SharedTargetManager _targetMgr = new();
    private IMonitor? _monitor;

    public void SetMonitor(IMonitor monitor) => _monitor = monitor;

    public void AddGrandpa(GrandpaSpiritOrbiter orbiter)
    {
        var behavior = new GrandpaFighterBehavior();
        if (_monitor != null)
            behavior.SetMonitor(_monitor);
        behavior.SetTargetManager(_targetMgr);
        _entries.Add((behavior, orbiter));
    }

    public void RemoveAllGrandpas()
    {
        foreach (var (behavior, orbiter) in _entries)
        {
            behavior.Reset();
            orbiter.CombatWorldPosition = null;
            orbiter.IsInCombatMode      = false;
            orbiter.DrawShakeOffset     = Vector2.Zero;
        }
        _entries.Clear();
        _targetMgr.Clear();
    }

    // ── public API ────────────────────────────────────────────────

    public bool IsEnabled { get; private set; }

    public void Enable() => IsEnabled = true;

    public void Disable()
    {
        IsEnabled = false;
        foreach (var (behavior, orbiter) in _entries)
        {
            behavior.Reset();
            orbiter.CombatWorldPosition = null;
            orbiter.IsInCombatMode      = false;
            orbiter.DrawShakeOffset     = Vector2.Zero;
        }
        _targetMgr.Clear();
    }

    // ── game loop hooks ───────────────────────────────────────────

    public void Update()
    {
        if (!IsEnabled) return;

        foreach (var (behavior, orbiter) in _entries)
        {
            // Grandpa is red whenever the combat module is enabled (monsters nearby),
            // even while orbiting — this gives the player a visual warning.
            orbiter.IsInCombatMode = true;

            if (orbiter.IsFadingOut() || orbiter.IsDestroyed)
                continue;

            behavior.Update();

            if (behavior.IsFighting)
            {
                orbiter.CombatWorldPosition = behavior.WorldPosition;
                orbiter.DrawShakeOffset     = behavior.DrawShakeOffset;
            }
            else
            {
                orbiter.CombatWorldPosition = null;
                orbiter.DrawShakeOffset     = Vector2.Zero;
            }
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        // Combat has no extra sprite overlay — grandpa himself is the weapon.
    }
}
