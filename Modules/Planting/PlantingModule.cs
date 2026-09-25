using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OldFarmer;

/// <summary>
/// Module wrapper for the auto-planting feature.
/// Supports multiple grandpas — each gets its own behavior instance.
/// </summary>
internal sealed class PlantingModule
{
    private readonly List<(GrandpaPlanterBehavior behavior, GrandpaSpiritOrbiter orbiter)> _entries = new();
    private readonly SharedTargetManager _targetMgr = new();
    private bool _enabled;

    public bool IsEnabled => _enabled;

    public void AddGrandpa(GrandpaSpiritOrbiter orbiter)
    {
        var behavior = new GrandpaPlanterBehavior();
        behavior.SetTargetManager(_targetMgr);
        _entries.Add((behavior, orbiter));
    }

    public void RemoveAllGrandpas()
    {
        foreach (var (behavior, orbiter) in _entries)
        {
            behavior.Reset();
            orbiter.PlantingWorldPosition = null;
        }
        _entries.Clear();
        _targetMgr.Clear();
    }

    public void Enable()
    {
        if (_enabled) return;
        _enabled = true;
        foreach (var (behavior, _) in _entries)
            behavior.Reset();
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;
        foreach (var (behavior, orbiter) in _entries)
        {
            behavior.Reset();
            orbiter.PlantingWorldPosition = null;
        }
        _targetMgr.Clear();
    }

    public void Update()
    {
        if (!_enabled) return;

        foreach (var (behavior, orbiter) in _entries)
        {
            if (orbiter.IsFadingOut() || orbiter.IsDestroyed)
                continue;

            behavior.Update();

            if (behavior.IsPlanting)
            {
                orbiter.PlantingWorldPosition = behavior.WorldPosition;
                orbiter.DrawShakeOffset = behavior.DrawShakeOffset;
            }
            else
            {
                orbiter.PlantingWorldPosition = null;
                orbiter.DrawShakeOffset = Vector2.Zero;
            }
        }
    }

    public void Draw(SpriteBatch sb)
    {
        // No drawing — user asked not to add extra sprites
    }
}
