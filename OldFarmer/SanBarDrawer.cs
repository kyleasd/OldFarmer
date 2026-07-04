using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace OldFarmer;

/// <summary>
/// Draws the SAN (Sanity) bar on the HUD, using the vanilla stamina-bar
/// container sprite and fill formula (X=256 tiles, 0.625 height scale).
///
/// All math is a 1:1 clone of Game1.drawHUD's stamina section, with
/// maxSan replacing player.MaxStamina and _displayedSan replacing player.Stamina.
/// </summary>
internal static class SanBarDrawer
{
    // ── stamina-bar sprite tiles (X=256) ──────────────────────────
    private static readonly Rectangle SrcTop    = new(256, 408, 12, 16);
    private static readonly Rectangle SrcMiddle = new(256, 424, 12, 16);
    private static readonly Rectangle SrcBottom = new(256, 448, 12, 16);

    private const float BarScale = 4f;   // 12 × 4 = 48 px rendered width
    private const int   BarWidth = 48;

    // ── animation state ──────────────────────────────────────────
    private static int   _shakeTimer;
    private static float _displayedSan = 100f;

    /// <summary>
    /// Set to false when grandpa fades out (SAN = 0).
    /// The SAN bar will be hidden until the next grandpa spawn.
    /// </summary>
    public static bool ShouldDraw { get; set; } = false; // 默认不显示，召唤后才显示

    public static void Update(float realSan)
    {
        if (_shakeTimer > 0)
            _shakeTimer -= Game1.currentGameTime.ElapsedGameTime.Milliseconds;

        float delta = realSan - _displayedSan;
        _displayedSan += delta * 0.15f;
        if (Math.Abs(delta) < 0.1f)
            _displayedSan = realSan;

        if (realSan < 20f && _shakeTimer <= 0 && Game1.random.NextDouble() < 0.01)
            _shakeTimer = 500;
    }

    public static void Draw(SpriteBatch sb, Farmer player)
    {
        if (!ShouldDraw) return;
        if (Game1.eventUp || Game1.farmEvent != null)
            return;
        if (!Context.IsWorldReady)
            return;

        var safeArea = Game1.graphics.GraphicsDevice.Viewport.TitleSafeArea;
        int   maxSan = SanManager.GetMaxSan(player);
        float num    = 2.5f;   // 0.625 × 4 = taller bar

        // ── bar height is fixed (based on 100 SAN) regardless of dynamic max ──
        const float FixedBarSan = 100f;
        float barInnerH = FixedBarSan * num;

        // ── replicate stamina bar X (Game1.drawHUD) ──
        float staminaX = safeArea.Right - 48 - 8;
        if (Game1.isOutdoorMapSmallerThanViewport())
            staminaX = Math.Min(staminaX,
                -Game1.viewport.X + Game1.currentLocation.map.Layers[0].LayerWidth * 64 - 48);

        // ── SAN bar sits 56 px left of stamina (or 112 if health bar visible) ──
        bool healthShowing = Game1.showingHealth;
        float sanX = healthShowing
            ? staminaX - 56 - 56
            : staminaX - 56;

        // ── SAN bar Y: based on fixed bar height ──
        float sanY = safeArea.Bottom - 80 - barInnerH;

        var sanVec = new Vector2(sanX, sanY);
        sanVec.Y = Math.Max(0, sanVec.Y);

        if (_shakeTimer > 0)
        {
            sanVec.X += Game1.random.Next(-3, 4);
            sanVec.Y += Game1.random.Next(-3, 4);
        }

        Color containerTint = new Color(200, 170, 255);

        // ── container: top cap (scale 4×) ──
        sb.Draw(Game1.mouseCursors, sanVec,
            SrcTop, containerTint, 0f, Vector2.Zero, BarScale, SpriteEffects.None, 1f);

        // ── container: middle stretch (top cap end → just past bottom cap start) ──
        sb.Draw(Game1.mouseCursors,
            new Rectangle(
                (int)sanVec.X,
                (int)(sanVec.Y + 64f),
                BarWidth,
                (int)(barInnerH - 56f)),
            SrcMiddle, containerTint);

        // ── container: bottom cap (overlaps with fill end, like vanilla) ──
        sb.Draw(Game1.mouseCursors,
            new Vector2(sanVec.X, sanVec.Y + barInnerH),
            SrcBottom, containerTint, 0f, Vector2.Zero, BarScale, SpriteEffects.None, 1f);

        // ── fill — scaled so it reaches the bottom cap at full SAN ──
        //   total distance from fill start (Y+48) to bottom edge = barInnerH + 16
        int   fillMaxH  = (int)(barInnerH + 8f);
        int   fillH     = (int)(Math.Clamp(_displayedSan / maxSan, 0f, 1f) * fillMaxH);
        if (fillH < 0) fillH = 0;

        var fillRect = new Rectangle(
            (int)sanVec.X + 12,
            (int)sanVec.Y + 48 + fillMaxH - fillH,
            24,
            fillH);

        if (fillRect.Height > 0)
        {
            float ratio = Math.Clamp(_displayedSan / maxSan, 0f, 1f);
            Color fillColor = GetSanColor(ratio);

            sb.Draw(Game1.staminaRect, fillRect, fillColor);

            // Dark sheen at top of fill (vanilla trick: height=4, darker colour)
            var sheenRect = fillRect;
            sheenRect.Height = 4;
            Color sheenColor = new Color(
                Math.Max(0, fillColor.R - 50),
                Math.Max(0, fillColor.G - 50),
                Math.Max(0, fillColor.B - 40));
            sb.Draw(Game1.staminaRect, sheenRect, sheenColor);
        }

        // ── critically low pulsing icon ──
        if (_displayedSan < 20f)
        {
            float pulse = 0.75f + 0.25f * (float)Math.Sin(
                Game1.currentGameTime.TotalGameTime.TotalMilliseconds / (_displayedSan * 50f + 1f));
            sb.Draw(Game1.mouseCursors,
                sanVec - new Vector2(0f, 11f) * 4f,
                new Rectangle(191, 406, 12, 11),
                Color.MediumPurple * pulse,
                0f, Vector2.Zero, BarScale, SpriteEffects.None, 1f);
        }

        // ── hover tooltip ──
        int mouseX = Game1.getOldMouseX();
        int mouseY = Game1.getOldMouseY();
        if (mouseX >= sanVec.X && mouseY >= sanVec.Y && mouseX < sanVec.X + BarWidth)
        {
            Game1.drawWithBorder(
                $"{(int)Math.Max(0f, _displayedSan)}/{maxSan}",
                Color.Black * 0f,
                new Color(180, 140, 255),
                sanVec + new Vector2(-Game1.dialogueFont.MeasureString("999/999").X - 16f, 64f));
        }
    }

    // ── colour ramp ──────────────────────────────────────────────

    /// <summary>
    /// Maps SAN ratio [0,1] to a colour:
    ///   1.0 → bright cyan-blue  (perfectly sane)
    ///   0.5 → medium purple
    ///   0.0 → dark red-purple   (on the edge of madness)
    /// </summary>
    private static Color GetSanColor(float ratio)
    {
        // Two-segment lerp:
        //   [0.5, 1.0] → purple (#9B59B6) → cyan-blue (#5DADE2)
        //   [0.0, 0.5] → dark red-purple (#6C0F0F) → purple (#9B59B6)
        if (ratio >= 0.5f)
        {
            float t = (ratio - 0.5f) * 2f;   // 0→1 within upper half
            return Color.Lerp(
                new Color(155,  89, 182),  // mid purple
                new Color( 93, 173, 226),  // sane blue
                t);
        }
        else
        {
            float t = ratio * 2f;              // 0→1 within lower half
            return Color.Lerp(
                new Color(108,  15,  15),  // madness red-purple
                new Color(155,  89, 182),  // mid purple
                t);
        }
    }
}
