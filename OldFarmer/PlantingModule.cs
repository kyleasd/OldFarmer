using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Module wrapper for the auto-planting feature.
/// Enable when the player is holding seeds; disable when not.
/// </summary>
internal sealed class PlantingModule
{
    private readonly GrandpaSpiritOrbiter _orbiter;
    private readonly GrandpaPlanterBehavior _behavior;
    private readonly IMonitor _monitor;
    private bool _enabled;

    public bool IsEnabled => _enabled;

    public PlantingModule(GrandpaSpiritOrbiter orbiter, IMonitor monitor)
    {
        _orbiter  = orbiter;
        _monitor  = monitor;
        _behavior = new GrandpaPlanterBehavior(monitor);
    }

    public void Enable()
    {
        if (_enabled) return;
        _enabled = true;
        _behavior.Reset();
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;
        _behavior.Reset();
        _orbiter.PlantingWorldPosition = null;
    }

    /// <summary>
    /// Call every tick from ModEntry.OnUpdateTicked.
    /// </summary>
    public void Update()
    {
        if (!_enabled) return;
        _behavior.Update();

        // Sync grandpa's world position and shake offset from the behavior
        _orbiter.PlantingWorldPosition = _behavior.WorldPosition;
        _orbiter.DrawShakeOffset = _behavior.DrawShakeOffset;
    }

    /// <summary>
    /// Draw is intentionally empty — the planting module does not render
    /// any overlay.  The highlight can be added later if desired.
    /// </summary>
    public void Draw(SpriteBatch sb)
    {
        // No drawing — user asked not to add extra sprites
    }
}
