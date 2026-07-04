using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Self-contained module that drives grandpa's woodcutting behaviour.
///
/// When enabled:
///   1. Ticks <see cref="GrandpaWoodcutterBehavior"/> every game update.
///   2. Feeds the woodcutting position back into <see cref="GrandpaSpiritOrbiter"/>.
///   3. Draws the spinning axe sprite orbiting grandpa.
///
/// When disabled, grandpa resumes normal orbiting.
/// </summary>
internal sealed class WoodcuttingModule
{
    private const string AxeTexturePath = "TileSheets/tools";
    /// <summary>Source rectangle for the axe sprite in tools.png (row 2, frame 0).</summary>
    private static readonly Rectangle AxeSourceRect = new(0, 32, 16, 16);
    private const float AxeScale = 6f;

    private readonly GrandpaWoodcutterBehavior _chopper;
    private readonly GrandpaSpiritOrbiter        _orbiter;
    private IMonitor?                            _monitor;
    private Texture2D? _axeTexture;
    private int _logCooldown;
    private bool _didBootLog;

    public WoodcuttingModule(GrandpaSpiritOrbiter orbiter)
    {
        _orbiter = orbiter;
        _chopper = new GrandpaWoodcutterBehavior();
    }

    /// <summary>Inject the SMAPI monitor for diagnostic logging.</summary>
    public void SetMonitor(IMonitor monitor)
    {
        _monitor = monitor;
        _chopper.SetMonitor(monitor);
        WoodcutterExecutor.SetMonitor(monitor);
    }

    // ── public API ────────────────────────────────────────────────

    /// <summary>Whether the woodcutting module is currently active.</summary>
    public bool IsEnabled { get; private set; }

    public void Enable()  => IsEnabled = true;

    public void Disable()
    {
        IsEnabled = false;
        _chopper.Reset();
        _orbiter.WoodcuttingWorldPosition = null;
    }

    // ── game loop hooks ───────────────────────────────────────────

    public void Update()
    {
        if (!IsEnabled)
            return;

        // One-time boot confirmation
        if (!_didBootLog)
        {
            _didBootLog = true;
            _monitor?.Log("[Woodcutting] Module booted — enabled and running.", LogLevel.Info);
        }

        _chopper.Update();

        if (_chopper.IsChopping)
            _orbiter.WoodcuttingWorldPosition = _chopper.WorldPosition;
        else
            _orbiter.WoodcuttingWorldPosition = null;

        // Diagnostic: log state every 120 ticks (~2 seconds)
        _logCooldown = (_logCooldown + 1) % 120;
        if (_logCooldown == 0)
            _monitor?.Log($"[Woodcutting] IsChopping={_chopper.IsChopping}  WorldPos=({_chopper.WorldPosition.X:F0},{_chopper.WorldPosition.Y:F0})  Loc='{Game1.currentLocation?.Name}'", LogLevel.Info);
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!IsEnabled)
            return;

        // Only draw the axe when grandpa is actively woodcutting
        if (!_chopper.IsChopping)
            return;

        _axeTexture ??= Game1.content.Load<Texture2D>(AxeTexturePath);

        var axeWorld = _chopper.AxeWorldPosition;
        var axeScreen = Game1.GlobalToLocal(Game1.viewport, axeWorld);
        float layerDepth = Math.Max(0.0001f, (axeWorld.Y + 32f) / 10000f);

        // Rotate the axe so the blade always points outward from grandpa
        spriteBatch.Draw(
            _axeTexture,
            axeScreen,
            AxeSourceRect,
            Color.White,
            _chopper.AxeAngle,                               // rotation
            new Vector2(AxeSourceRect.Width / 2f, AxeSourceRect.Height / 2f), // origin
            AxeScale,
            SpriteEffects.None,
            layerDepth
        );
    }
}
