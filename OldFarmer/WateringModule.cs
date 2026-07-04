using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Self-contained module that drives grandpa's watering behaviour.
/// Call <see cref="Enable"/> / <see cref="Disable"/> to turn it on or off at runtime.
///
/// When enabled the module:
///   1. Ticks <see cref="GrandpaWatererBehavior"/> every game update.
///   2. Feeds the resulting position/shake back into <see cref="GrandpaSpiritOrbiter"/>.
///
/// When disabled, none of the above happens and the orbiter resumes its normal
/// idle orbit.
/// </summary>
internal sealed class WateringModule
{
    private readonly GrandpaWatererBehavior _waterer = new();
    private readonly GrandpaSpiritOrbiter   _orbiter;

    /// <param name="orbiter">
    ///   The shared orbiter that this module will write <c>WateringWorldPosition</c>
    ///   and <c>DrawShakeOffset</c> into while active.
    /// </param>
    public WateringModule(GrandpaSpiritOrbiter orbiter)
    {
        _orbiter = orbiter;
    }

    public void SetMonitor(IMonitor monitor) => _waterer.SetMonitor(monitor);

    // ── public API ─────────────────────────────────────────────

    /// <summary>Whether the watering module is currently active.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Start watering. Safe to call multiple times.</summary>
    public void Enable()  => IsEnabled = true;

    /// <summary>
    /// Stop watering and reset orbiter state.
    /// Safe to call even when already disabled.
    /// </summary>
    public void Disable()
    {
        IsEnabled = false;
        _waterer.Reset();
        _orbiter.WateringWorldPosition = null;
        _orbiter.DrawShakeOffset      = Vector2.Zero;
    }

    // ── game loop hooks ─────────────────────────────────────────

    /// <summary>
    /// Called every game tick (from <c>UpdateTicked</c>).
    /// Does nothing when <see cref="IsEnabled"/> is false.
    /// </summary>
    public void Update()
    {
        if (!IsEnabled)
            return;

        _waterer.Update();

        // Sync orbiter draw state
        if (_waterer.IsWatering)
        {
            _orbiter.WateringWorldPosition = _waterer.WorldPosition;
            _orbiter.DrawShakeOffset      = _waterer.DrawShakeOffset;
        }
        else
        {
            _orbiter.WateringWorldPosition = null;
            _orbiter.DrawShakeOffset      = Vector2.Zero;
        }
    }

    /// <summary>
    /// Called every render frame (from <c>RenderedWorld</c>).
    /// Does nothing when <see cref="IsEnabled"/> is false or grandpa is not preparing.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch)
    {
        if (!IsEnabled)
            return;

        // Draw a highlight while grandpa is preparing to water
        if (_waterer.IsPreparing)
            HighlightDrawer.Draw(spriteBatch, _waterer.PrepareCenterTile, _waterer.CurrentStage);
    }
}
