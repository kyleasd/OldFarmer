using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Self-contained module that drives grandpa's scything behaviour.
/// Mirrors <see cref="TillingModule"/>: grandpa moves to a 9×9 block center,
/// charges with a shake, then instantly harvests the whole block.
///
/// When enabled:
///   1. Ticks <see cref="GrandpaScytherBehavior"/> every game update.
///   2. Feeds the position / shake back into <see cref="GrandpaSpiritOrbiter"/>.
///   3. Draws a range highlight during the charge wind-up.
///
/// When disabled, grandpa resumes normal orbiting.
/// </summary>
internal sealed class ScythingModule
{
    private readonly GrandpaScytherBehavior _scyther;
    private readonly GrandpaSpiritOrbiter   _orbiter;

    public ScythingModule(GrandpaSpiritOrbiter orbiter)
    {
        _orbiter = orbiter;
        _scyther = new GrandpaScytherBehavior();
    }

    public void SetMonitor(IMonitor monitor)
    {
        _scyther.SetMonitor(monitor);
        ScythingExecutor.SetMonitor(monitor);
    }

    // ── public API ────────────────────────────────────────────────

    public bool IsEnabled { get; private set; }

    public void Enable()  => IsEnabled = true;

    public void Disable()
    {
        IsEnabled = false;
        _scyther.Reset();
        _orbiter.ScythingWorldPosition = null;
        _orbiter.DrawShakeOffset       = Vector2.Zero;
    }

    // ── game loop hooks ───────────────────────────────────────────

    public void Update()
    {
        if (!IsEnabled) return;

        _scyther.Update();

        if (_scyther.IsScything)
        {
            _orbiter.ScythingWorldPosition = _scyther.WorldPosition;
            _orbiter.DrawShakeOffset       = _scyther.DrawShakeOffset;
        }
        else
        {
            _orbiter.ScythingWorldPosition = null;
            _orbiter.DrawShakeOffset       = Vector2.Zero;
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!IsEnabled) return;

        // Draw the 9×9 range highlight during the charge wind-up
        if (_scyther.IsCharging)
            HighlightDrawer.Draw(spriteBatch, _scyther.ChargeCenterTile, _scyther.CurrentStage);
    }
}
