using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;

namespace OldFarmer;

/// <summary>Draws the vanilla grandpa spirit sprite orbiting the player.</summary>
internal sealed class GrandpaSpiritOrbiter
{
    public static GrandpaSpiritOrbiter? Instance { get; private set; }

    private const string TexturePath = "LooseSprites\\Cursors";
    private static readonly Rectangle SourceRect = new(555, 1956, 18, 35);
    private const float Scale = 8f;
    private const float OrbitRadius = 200f;
    private const float OrbitSpeed = 0.015f;

    // ── fade-out animation ──────────────────────────────────────
    private const float FadeOutDuration = 2.0f; // seconds
    private float _fadeTimer;
    private bool _isFadingOut;
    private bool _isDestroyed = true; // 默认不出现，需召唤

    private float angle;
    private Texture2D? texture;

    public GrandpaSpiritOrbiter()
    {
        // 不直接设置 Instance，只有 Revive() 后才激活
    }

    /// <summary>
    /// Returns true if the grandpa spirit is fully destroyed (fade-out complete).
    /// ModEntry should stop updating/drawing and clean up references.
    /// </summary>
    public bool IsDestroyed => _isDestroyed;

    /// <summary>
    /// Returns true if the grandpa spirit is currently fading out (but not yet destroyed).
    /// Used to check if fade-out was already triggered.
    /// </summary>
    public bool IsFadingOut() => _isFadingOut;

    /// <summary>
    /// Start the fade-out animation. Called when SAN drops to 0.
    /// </summary>
    public void StartFadeOut()
    {
        if (_isFadingOut || _isDestroyed) return;
        _isFadingOut = true;
        _fadeTimer = FadeOutDuration;
    }

    /// <summary>
    /// Instantly dismiss the grandpa spirit without fade-out animation.
    /// Called when the day ends (player goes to sleep) — grandpa leaves overnight.
    /// </summary>
    public void Dismiss()
    {
        _isDestroyed  = true;
        _isFadingOut  = false;
        _fadeTimer    = 0f;
        Instance      = null;
    }

    /// <summary>
    /// Revive the grandpa spirit after it was destroyed (SAN hit 0).
    /// Resets all internal state so the spirit can orbit and draw again.
    /// </summary>
    public void Revive()
    {
        _isDestroyed = false;
        _isFadingOut = false;
        _fadeTimer    = FadeOutDuration;
        Instance      = this;
    }

    /// <summary>
    /// Returns the current fade alpha (255 = fully visible, 0 = fully transparent).
    /// </summary>
    private int GetFadeAlpha()
    {
        if (!_isFadingOut) return 255;
        float progress = 1f - (_fadeTimer / FadeOutDuration);
        return (int)MathHelper.Clamp(255f * (1f - progress), 0, 255);
    }

    /// <summary>
    /// When grandpa is tilling, the behavior sets this to his current world position
    /// so the sprite is drawn there instead of on the orbit path.
    /// Set to null to restore normal orbiting rendering.
    /// </summary>
    public Vector2? TillingWorldPosition { get; set; }

    /// <summary>
    /// When grandpa is chopping wood, the module sets this to his current world position.
    /// Takes priority over <see cref="TillingWorldPosition"/> and <see cref="WateringWorldPosition"/>.
    /// </summary>
    public Vector2? WoodcuttingWorldPosition { get; set; }

    /// <summary>
    /// When grandpa is scything, the module sets this to his current world position.
    /// Takes priority over <see cref="WoodcuttingWorldPosition"/>, <see cref="TillingWorldPosition"/>,
    /// and <see cref="WateringWorldPosition"/>.
    /// </summary>
    public Vector2? ScythingWorldPosition { get; set; }

    /// <summary>
    /// When grandpa is watering, the module sets this to his current world position.
    /// Takes priority over <see cref="TillingWorldPosition"/> but not over <see cref="WoodcuttingWorldPosition"/>.
    /// </summary>
    public Vector2? WateringWorldPosition { get; set; }

    /// <summary>
    /// When grandpa is planting seeds, the module sets this to his current world position.
    /// Takes priority over <see cref="TillingWorldPosition"/> and <see cref="WateringWorldPosition"/>,
    /// but not over <see cref="WoodcuttingWorldPosition"/> or <see cref="ScythingWorldPosition"/>.
    /// </summary>
    public Vector2? PlantingWorldPosition { get; set; }

    /// <summary>
    /// When grandpa is fighting monsters, the module sets this to his current world position.
    /// Takes the HIGHEST priority — combat overrides all other activities.
    /// </summary>
    public Vector2? CombatWorldPosition { get; set; }

    /// <summary>
    /// When true, the grandpa sprite is tinted red to indicate combat mode.
    /// Set by <see cref="CombatModule"/> when monsters are nearby.
    /// </summary>
    public bool IsInCombatMode { get; set; }

    /// <summary>
    /// Additional screen-space shake offset applied during hoe charge-up.
    /// Supplied by <see cref="GrandpaTillerBehavior"/>.
    /// </summary>
    public Vector2 DrawShakeOffset { get; set; }

    /// <summary>
    /// Returns the current orbit offset from the player in world-space pixels.
    /// Behaviors use this to keep <see cref="WorldPosition"/> in sync
    /// while orbiting, so the transition to MovingToTarget has no teleport.
    /// </summary>
    public static Vector2 GetOrbitOffset()
    {
        float angle = Instance?.Angle ?? 0f;
        return new Vector2(
            (float)Math.Cos(angle) * OrbitRadius,
            (float)Math.Sin(angle) * OrbitRadius);
    }

    /// <summary>Allow behaviors to read the current orbit angle.</summary>
    public static float GetOrbitAngle() => Instance?.Angle ?? 0f;

    /// <summary>Public getter for the orbit angle (used by GetOrbitOffset).</summary>
    public float Angle => angle;

    public void Update()
    {
        if (_isDestroyed) return;

        // Handle fade-out animation
        if (_isFadingOut)
        {
            _fadeTimer -= (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
            if (_fadeTimer <= 0f)
            {
                _isDestroyed = true;
                Instance = null; // Clear static reference
                return;
            }
        }

        angle += OrbitSpeed;
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (Game1.player is null || Game1.currentLocation is null)
            return;
        if (_isDestroyed) return;

        texture ??= Game1.content.Load<Texture2D>(TexturePath);

        Vector2 screenPos;
        float layerDepth;
        bool flip;

        // Get fade alpha for drawing
        int fadeAlpha = GetFadeAlpha();
        Color drawColor = Color.White * (fadeAlpha / 255f);

        if (CombatWorldPosition.HasValue)
        {
            // Grandpa is actively fighting — draw at the combat position (highest priority)
            var worldPos = CombatWorldPosition.Value;
            screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos) + DrawShakeOffset;
            layerDepth = Math.Max(0.0001f, (worldPos.Y + 32f) / 10000f);
            flip = DrawShakeOffset.X < -0.5f;
        }
        else if (ScythingWorldPosition.HasValue)
        {
            // Grandpa is actively scything — draw at the scything position (highest priority)
            var worldPos = ScythingWorldPosition.Value;
            screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos) + DrawShakeOffset;
            layerDepth = Math.Max(0.0001f, (worldPos.Y + 32f) / 10000f);
            flip = DrawShakeOffset.X < -0.5f;
        }
        else if (WoodcuttingWorldPosition.HasValue)
        {
            // Grandpa is actively chopping — draw at the woodcutting position
            var worldPos = WoodcuttingWorldPosition.Value;
            screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos);
            layerDepth = Math.Max(0.0001f, (worldPos.Y + 32f) / 10000f);
            flip = false;
        }
        else if (WateringWorldPosition.HasValue)
        {
            // Grandpa is actively watering — draw at the watering position
            var worldPos = WateringWorldPosition.Value;
            screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos) + DrawShakeOffset;
            layerDepth = Math.Max(0.0001f, (worldPos.Y + 32f) / 10000f);
            flip = DrawShakeOffset.X < -0.5f;
        }
        else if (PlantingWorldPosition.HasValue)
        {
            // Grandpa is actively planting — draw at the planting position
            var worldPos = PlantingWorldPosition.Value;
            screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos) + DrawShakeOffset;
            layerDepth = Math.Max(0.0001f, (worldPos.Y + 32f) / 10000f);
            flip = DrawShakeOffset.X < -0.5f;
        }
        else if (TillingWorldPosition.HasValue)
        {
            // Grandpa is actively tilling — draw at the tilling position
            var worldPos = TillingWorldPosition.Value;
            screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos) + DrawShakeOffset;
            layerDepth = Math.Max(0.0001f, (worldPos.Y + 32f) / 10000f);
            // Face right while moving/tilling (DrawShakeOffset.X < 0 means recoiling left)
            flip = DrawShakeOffset.X < -0.5f;
        }
        else
        {
            // Normal orbiting mode.
            // Orbit is computed directly in screen space so it looks like a true circle
            // regardless of the game's isometric projection.
            Farmer player = Game1.player;
            Vector2 playerWorld = player.getStandingPosition();
            Vector2 playerScreen = Game1.GlobalToLocal(Game1.viewport, playerWorld);

            screenPos = playerScreen + new Vector2(
                (float)Math.Cos(angle) * OrbitRadius,
                (float)Math.Sin(angle) * OrbitRadius);

            // Approximate world Y for layer depth: convert screen offset back
            float worldY = playerWorld.Y + (float)Math.Sin(angle) * OrbitRadius;
            layerDepth = Math.Max(0.0001f, (worldY + 32f) / 10000f);
            flip = Math.Cos(angle) < 0;
        }

        spriteBatch.Draw(
            texture,
            screenPos,
            SourceRect,
            drawColor,
            0f,
            new Vector2(SourceRect.Width / 2f, SourceRect.Height / 2f),
            Scale,
            flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
            layerDepth
        );
    }
}
