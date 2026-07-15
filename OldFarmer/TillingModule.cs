using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Self-contained module that drives grandpa's tilling behaviour.
/// Supports multiple grandpas — each gets its own behavior instance.
///
/// Call <see cref="AddGrandpa"/> when a new grandpa is summoned,
/// <see cref="RemoveAllGrandpas"/> when all grandpas are dismissed.
/// Call <see cref="Enable"/> / <see cref="Disable"/> to control whether
/// tilling is active (based on the player's held tool).
/// </summary>
internal sealed class TillingModule
{
    private readonly List<(GrandpaTillerBehavior behavior, GrandpaSpiritOrbiter orbiter)> _entries = new();
    private readonly SharedTargetManager _targetMgr = new();
    private IMonitor? _monitor;

    public void SetMonitor(IMonitor monitor) => _monitor = monitor;

    /// <summary>
    /// Register a new grandpa. Creates a fresh behavior instance bound to
    /// the given orbiter.
    /// </summary>
    public void AddGrandpa(GrandpaSpiritOrbiter orbiter)
    {
        var behavior = new GrandpaTillerBehavior();
        behavior.SetTargetManager(_targetMgr);
        _entries.Add((behavior, orbiter));
    }

    /// <summary>Remove all grandpa entries and reset orbiter state.</summary>
    public void RemoveAllGrandpas()
    {
        foreach (var (behavior, orbiter) in _entries)
        {
            behavior.Reset();
            orbiter.TillingWorldPosition = null;
            orbiter.DrawShakeOffset      = Vector2.Zero;
        }
        _entries.Clear();
        _targetMgr.Clear();
    }

    // ── public API ────────────────────────────────────────────────

    public bool IsEnabled { get; private set; }

    public void Enable()  => IsEnabled = true;

    public void Disable()
    {
        IsEnabled = false;
        foreach (var (behavior, orbiter) in _entries)
        {
            behavior.Reset();
            orbiter.TillingWorldPosition = null;
            orbiter.DrawShakeOffset      = Vector2.Zero;
        }
        _targetMgr.Clear();
    }

    // ── game loop hooks ───────────────────────────────────────────

    public void Update()
    {
        if (!IsEnabled)
            return;

        foreach (var (behavior, orbiter) in _entries)
        {
            // Skip grandpas that are fading out or destroyed
            if (orbiter.IsFadingOut() || orbiter.IsDestroyed)
                continue;

            behavior.Update();

            // Sync orbiter draw state
            if (behavior.IsTilling)
            {
                orbiter.TillingWorldPosition = behavior.WorldPosition;
                orbiter.DrawShakeOffset      = behavior.DrawShakeOffset;
            }
            else
            {
                orbiter.TillingWorldPosition = null;
                orbiter.DrawShakeOffset      = Vector2.Zero;
            }
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!IsEnabled)
            return;

        foreach (var (behavior, _) in _entries)
        {
            if (behavior.IsCharging)
                HighlightDrawer.Draw(spriteBatch, behavior.ChargeCenterTile, behavior.CurrentStage);
        }
    }
}
