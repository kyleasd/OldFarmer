using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OldFarmer;

/// <summary>
/// Self-contained module that drives grandpa's watering behaviour.
/// Supports multiple grandpas — each gets its own behavior instance.
/// </summary>
internal sealed class WateringModule
{
    private readonly List<(GrandpaWatererBehavior behavior, GrandpaSpiritOrbiter orbiter)> _entries = new();
    private readonly SharedTargetManager _targetMgr = new();

    public void AddGrandpa(GrandpaSpiritOrbiter orbiter)
    {
        var behavior = new GrandpaWatererBehavior();
        behavior.SetTargetManager(_targetMgr);
        _entries.Add((behavior, orbiter));
    }

    public void RemoveAllGrandpas()
    {
        foreach (var (behavior, orbiter) in _entries)
        {
            behavior.Reset();
            orbiter.WateringWorldPosition = null;
            orbiter.DrawShakeOffset      = Vector2.Zero;
        }
        _entries.Clear();
        _targetMgr.Clear();
    }

    // ── public API ─────────────────────────────────────────────

    public bool IsEnabled { get; private set; }

    public void Enable()  => IsEnabled = true;

    public void Disable()
    {
        IsEnabled = false;
        foreach (var (behavior, orbiter) in _entries)
        {
            behavior.Reset();
            orbiter.WateringWorldPosition = null;
            orbiter.DrawShakeOffset      = Vector2.Zero;
        }
        _targetMgr.Clear();
    }

    // ── game loop hooks ─────────────────────────────────────────

    public void Update()
    {
        if (!IsEnabled)
            return;

        foreach (var (behavior, orbiter) in _entries)
        {
            if (orbiter.IsFadingOut() || orbiter.IsDestroyed)
                continue;

            behavior.Update();

            if (behavior.IsWatering)
            {
                orbiter.WateringWorldPosition = behavior.WorldPosition;
                orbiter.DrawShakeOffset      = behavior.DrawShakeOffset;
            }
            else
            {
                orbiter.WateringWorldPosition = null;
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
            if (behavior.IsPreparing)
                HighlightDrawer.Draw(spriteBatch, behavior.PrepareCenterTile, behavior.CurrentStage);
        }
    }
}
