using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Self-contained module that drives grandpa's tilling behaviour.
/// Call <see cref="Enable"/> / <see cref="Disable"/> to turn it on or off at runtime.
///
/// When enabled the module:
///   1. Ticks <see cref="GrandpaTillerBehavior"/> every game update.
///   2. Feeds the resulting position/shake back into <see cref="GrandpaSpiritOrbiter"/>.
///   3. Draws the charge-highlight overlay via <see cref="HighlightDrawer"/>.
///
/// When disabled, none of the above happens and the orbiter resumes its normal
/// idle orbit.
/// </summary>
internal sealed class TillingModule
{
    private readonly GrandpaTillerBehavior _tiller = new();
    private readonly GrandpaSpiritOrbiter  _orbiter;

    /// <param name="orbiter">
    ///   The shared orbiter that this module will write <c>TillingWorldPosition</c>
    ///   and <c>DrawShakeOffset</c> into while active.
    /// </param>
    public TillingModule(GrandpaSpiritOrbiter orbiter)
    {
        _orbiter = orbiter;
    }

    public void SetMonitor(IMonitor monitor) => _tiller.SetMonitor(monitor);

    // ── public API ────────────────────────────────────────────────

    /// <summary>Whether the tilling module is currently active.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Start tilling. Safe to call multiple times.</summary>
    public void Enable()  => IsEnabled = true;

    /// <summary>
    /// Stop tilling and reset orbiter state.
    /// Safe to call even when already disabled.
    /// </summary>
    public void Disable()
    {
        IsEnabled = false;
        _tiller.Reset();
        _orbiter.TillingWorldPosition = null;
        _orbiter.DrawShakeOffset      = Vector2.Zero;
    }

    // ── game loop hooks ───────────────────────────────────────────

    /// <summary>
    /// Called every game tick (from <c>UpdateTicked</c>).
    /// Does nothing when <see cref="IsEnabled"/> is false.
    /// </summary>
    public void Update()
    {
        if (!IsEnabled)
            return;

        _tiller.Update();

        // Sync orbiter draw state
        if (_tiller.IsTilling)
        {
            _orbiter.TillingWorldPosition = _tiller.WorldPosition;
            _orbiter.DrawShakeOffset      = _tiller.DrawShakeOffset;
        }
        else
        {
            _orbiter.TillingWorldPosition = null;
            _orbiter.DrawShakeOffset      = Vector2.Zero;
        }
    }

    /// <summary>
    /// Called every render frame (from <c>RenderedWorld</c>).
    /// Does nothing when <see cref="IsEnabled"/> is false or grandpa is not charging.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch)
    {
        if (!IsEnabled)
            return;

        if (_tiller.IsCharging)
            HighlightDrawer.Draw(spriteBatch, _tiller.ChargeCenterTile, _tiller.CurrentStage);
    }
}
