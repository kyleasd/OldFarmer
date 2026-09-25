using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;

namespace OldFarmer;

/// <summary>Draws the vanilla grandpa spirit sprite orbiting the player.</summary>
/// <remarks>
/// Multiple instances can coexist — each manages its own orbit angle and
/// fade state independently. Use <paramref name="angleOffset"/> to spread
/// multiple grandpas evenly around the orbit circle.
/// </remarks>
internal sealed class GrandpaSpiritOrbiter
{
    private const string TexturePath = "LooseSprites\\Cursors";
    private static readonly Rectangle SourceRect = new(555, 1956, 18, 35);
    private const float Scale = 8f;
    private const float OrbitYShift = -80f;     // shift orbit center upward on screen

    /// <summary>Angular speed of the shared orbit ring (radians per tick).</summary>
    public const float OrbitSpeed = 0.015f;

    /// <summary>How many grandpas share the single orbit ring.</summary>
    public const int GrandpasPerRing = 10;

    /// <summary>
    /// Radius of the single shared orbit ring. Every grandpa uses this same
    /// radius, and they are spread evenly by angle, so the spacing between
    /// neighbours is identical all the way around the ring.
    /// </summary>
    /// <remarks>
    /// The value makes the axis-aligned sprite bounding boxes of neighbouring
    /// grandpas just touch at the worst angle. The minimum centre-to-centre
    /// distance that keeps two axis-aligned rectangles of size (w, h) apart in
    /// every direction is sqrt(w² + h²); on a ring of N evenly spaced points
    /// that distance equals the chord 2·R·sin(π/N), hence:
    ///     R = sqrt(w² + h²) / (2·sin(π/N)).
    /// </remarks>
    public static float UniformOrbitRadius { get; } =
        (float)Math.Sqrt(
            (SourceRect.Width * Scale) * (SourceRect.Width * Scale) +
            (SourceRect.Height * Scale) * (SourceRect.Height * Scale))
        / (2f * (float)Math.Sin(Math.PI / GrandpasPerRing));

    // ── fade-out animation ──────────────────────────────────────
    private const float FadeOutDuration = 2.0f; // seconds
    private float _fadeTimer;
    private bool _isFadingOut;
    private bool _isDestroyed = true; // 默认不出现，需召唤

    private float _orbitPhase;                 // shared ring phase, advanced by ModEntry
    private readonly float _angleOffset;
    private readonly float _orbitRadius;  // shared ring radius (see UniformOrbitRadius)
    private Texture2D? texture;

    /// <summary>This grandpa's current angle on the shared ring.</summary>
    private float Angle => _orbitPhase + _angleOffset;

    /// <param name="angleOffset">
    ///   Starting angle (radians) on the orbit circle. Pass different values
    ///   for each grandpa to spread them evenly around the ring.
    /// </param>
    /// <param name="orbitRadius">
    ///   Radius of the shared orbit ring in pixels. Defaults to
    ///   <see cref="UniformOrbitRadius"/>; all grandpas should use the same
    ///   value so their spacing is uniform.
    /// </param>
    public GrandpaSpiritOrbiter(float angleOffset = 0f, float? orbitRadius = null)
    {
        _angleOffset  = angleOffset;
        _orbitRadius  = orbitRadius ?? UniformOrbitRadius;
    }

    /// <summary>
    /// Returns true if the grandpa spirit is fully destroyed (fade-out complete).
    /// </summary>
    public bool IsDestroyed => _isDestroyed;

    /// <summary>
    /// Returns true if the grandpa spirit is currently fading out (but not yet destroyed).
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
    /// Called when the day ends (player goes to sleep).
    /// </summary>
    public void Dismiss()
    {
        _isDestroyed  = true;
        _isFadingOut  = false;
        _fadeTimer    = 0f;
    }

    /// <summary>
    /// Set the shared orbit phase. Every grandpa on the ring receives the same
    /// phase, so their relative angles (the <c>angleOffset</c>s) stay fixed and
    /// the spacing between neighbours remains uniform no matter when each one
    /// was summoned.
    /// </summary>
    public void SetOrbitPhase(float phase) => _orbitPhase = phase;

    /// <summary>
    /// Revive the grandpa spirit after it was destroyed (SAN hit 0).
    /// Resets all internal state so the spirit can orbit and draw again.
    /// </summary>
    public void Revive()
    {
        _isDestroyed = false;
        _isFadingOut = false;
        _fadeTimer    = FadeOutDuration;
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

    // ── World position properties (set by modules) ──────────────

    public Vector2? TillingWorldPosition { get; set; }
    public Vector2? WoodcuttingWorldPosition { get; set; }
    public Vector2? ScythingWorldPosition { get; set; }
    public Vector2? WateringWorldPosition { get; set; }
    public Vector2? PlantingWorldPosition { get; set; }
    public Vector2? CombatWorldPosition { get; set; }
    public bool IsInCombatMode { get; set; }
    public Vector2 DrawShakeOffset { get; set; }

    /// <summary>True while grandpa is in the mining module (draws the morph/cyclone animation).</summary>
    public bool IsMiningMode { get; set; }

    /// <summary>Frame animation used while <see cref="IsMiningMode"/> is true.</summary>
    public GrandpaAnimation? MiningAnimation { get; set; }

    // ── orbit helpers (instance methods) ─────────────────────────

    /// <summary>
    /// Returns the current orbit offset from the player in world-space pixels.
    /// </summary>
    public Vector2 GetOrbitOffset()
    {
        return new Vector2(
            (float)Math.Cos(Angle) * _orbitRadius,
            (float)Math.Sin(Angle) * _orbitRadius + OrbitYShift);
    }

    /// <summary>Allow behaviors to read the current orbit angle.</summary>
    public float GetOrbitAngle() => Angle;

    // ── game loop ────────────────────────────────────────────────

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
                return;
            }
        }
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

        // Combat mode tint (red)
        if (IsInCombatMode)
            drawColor = new Color(255, 60, 60) * (fadeAlpha / 255f);

        // Priority chain: Combat > Scything > Woodcutting > Watering > Planting > Tilling > Orbit
        if (CombatWorldPosition.HasValue)
        {
            var worldPos = CombatWorldPosition.Value;
            screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos) + DrawShakeOffset;
            layerDepth = Math.Max(0.0001f, (worldPos.Y + 32f) / 10000f);
            flip = DrawShakeOffset.X < -0.5f;
        }
        else if (ScythingWorldPosition.HasValue)
        {
            var worldPos = ScythingWorldPosition.Value;
            screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos) + DrawShakeOffset;
            layerDepth = Math.Max(0.0001f, (worldPos.Y + 32f) / 10000f);
            flip = DrawShakeOffset.X < -0.5f;
        }
        else if (WoodcuttingWorldPosition.HasValue)
        {
            var worldPos = WoodcuttingWorldPosition.Value;
            screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos);
            layerDepth = Math.Max(0.0001f, (worldPos.Y + 32f) / 10000f);
            flip = false;
        }
        else if (WateringWorldPosition.HasValue)
        {
            var worldPos = WateringWorldPosition.Value;
            screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos) + DrawShakeOffset;
            layerDepth = Math.Max(0.0001f, (worldPos.Y + 32f) / 10000f);
            flip = DrawShakeOffset.X < -0.5f;
        }
        else if (PlantingWorldPosition.HasValue)
        {
            var worldPos = PlantingWorldPosition.Value;
            screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos) + DrawShakeOffset;
            layerDepth = Math.Max(0.0001f, (worldPos.Y + 32f) / 10000f);
            flip = DrawShakeOffset.X < -0.5f;
        }
        else if (TillingWorldPosition.HasValue)
        {
            var worldPos = TillingWorldPosition.Value;
            screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos) + DrawShakeOffset;
            layerDepth = Math.Max(0.0001f, (worldPos.Y + 32f) / 10000f);
            flip = DrawShakeOffset.X < -0.5f;
        }
        else
        {
            // Normal orbiting mode.
            Farmer player = Game1.player;
            Vector2 playerWorld = player.getStandingPosition();
            Vector2 playerScreen = Game1.GlobalToLocal(Game1.viewport, playerWorld);

            screenPos = playerScreen + new Vector2(
                (float)Math.Cos(Angle) * _orbitRadius,
                (float)Math.Sin(Angle) * _orbitRadius + OrbitYShift);

            // Approximate world Y for layer depth: convert screen offset back
            float worldY = playerWorld.Y + (float)Math.Sin(Angle) * _orbitRadius + OrbitYShift;
            layerDepth = Math.Max(0.0001f, (worldY + 32f) / 10000f);
            flip = Math.Cos(Angle) < 0;
        }

        // Mining mode replaces grandpa's body with the morph/cyclone animation.
        Texture2D drawTexture = texture;
        Rectangle drawSource  = SourceRect;
        Vector2   drawOrigin  = new(SourceRect.Width / 2f, SourceRect.Height / 2f);

        if (IsMiningMode && MiningAnimation is not null)
        {
            drawTexture = MiningAnimation.Texture;
            drawSource  = MiningAnimation.SourceRect;
            drawOrigin  = MiningAnimation.Origin;
        }

        spriteBatch.Draw(
            drawTexture,
            screenPos,
            drawSource,
            drawColor,
            0f,
            drawOrigin,
            Scale,
            flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
            layerDepth
        );
    }
}
