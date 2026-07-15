using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Self-contained module that drives grandpa's woodcutting behaviour.
/// Supports multiple grandpas — each gets its own behavior instance.
/// </summary>
internal sealed class WoodcuttingModule
{
    private const string AxeTexturePath = "TileSheets/tools";
    /// <summary>Source rectangle for the axe sprite in tools.png (row 2, frame 0).</summary>
    private static readonly Rectangle AxeSourceRect = new(0, 32, 16, 16);
    private const float AxeScale = 6f;

    private readonly List<(GrandpaWoodcutterBehavior behavior, GrandpaSpiritOrbiter orbiter)> _entries = new();
    private readonly SharedTargetManager _targetMgr = new();
    private IMonitor? _monitor;
    private Texture2D? _axeTexture;

    public void SetMonitor(IMonitor monitor)
    {
        _monitor = monitor;
        WoodcutterExecutor.SetMonitor(monitor);
    }

    public void AddGrandpa(GrandpaSpiritOrbiter orbiter)
    {
        var behavior = new GrandpaWoodcutterBehavior();
        behavior.SetTargetManager(_targetMgr);
        if (_monitor != null)
            behavior.SetMonitor(_monitor);
        _entries.Add((behavior, orbiter));
    }

    public void RemoveAllGrandpas()
    {
        foreach (var (behavior, orbiter) in _entries)
        {
            behavior.Reset();
            orbiter.WoodcuttingWorldPosition = null;
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
            orbiter.WoodcuttingWorldPosition = null;
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
            if (orbiter.IsFadingOut() || orbiter.IsDestroyed)
                continue;

            behavior.Update();

            if (behavior.IsChopping)
                orbiter.WoodcuttingWorldPosition = behavior.WorldPosition;
            else
                orbiter.WoodcuttingWorldPosition = null;
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!IsEnabled)
            return;

        _axeTexture ??= Game1.content.Load<Texture2D>(AxeTexturePath);

        foreach (var (behavior, _) in _entries)
        {
            if (!behavior.IsChopping)
                continue;

            var axeWorld = behavior.AxeWorldPosition;
            var axeScreen = Game1.GlobalToLocal(Game1.viewport, axeWorld);
            float layerDepth = Math.Max(0.0001f, (axeWorld.Y + 32f) / 10000f);

            spriteBatch.Draw(
                _axeTexture,
                axeScreen,
                AxeSourceRect,
                Color.White,
                behavior.AxeAngle,
                new Vector2(AxeSourceRect.Width / 2f, AxeSourceRect.Height / 2f),
                AxeScale,
                SpriteEffects.None,
                layerDepth
            );
        }
    }
}
