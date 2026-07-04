using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace OldFarmer;

/// <summary>
/// Draws the hoe charge highlight overlay using the exact same vanilla
/// sprite from Game1.mouseCursors (208,388,16,16) that the game uses
/// for the hoe and watering-can charge indicators.
/// </summary>
internal static class HighlightDrawer
{
    // Vanilla tool-charge sprite: LooseSprites/Cursors → (208, 384, 16, 16)
    private static readonly Rectangle SourceRect = new(194, 388, 16, 16);

    // Scale matches vanilla: 16×4 = 64 px (one full tile)
    private const float TileScale = 4f;

    /// <summary>
    /// Draws the charge highlight tiles around <paramref name="centerTile"/>,
    /// expanding outward based on <paramref name="stage"/>.
    /// Stage 1 → 3×3, 2 → 5×5, 3 → 7×7, 4 → 9×9 (full iridium charge).
    /// </summary>
    public static void Draw(SpriteBatch sb, Vector2 centerTile, int stage)
    {
        if (stage <= 0)
            return;

        // radius = stage maps directly: 1→3×3, 2→5×5, 3→7×7, 4→9×9
        int radius = stage;

        // Smooth pulsation driven by game time (cycles about every 1.5 seconds)
        double totalMs = Game1.currentGameTime.TotalGameTime.TotalMilliseconds;
        float globalPulse = 0.82f + 0.18f * (float)Math.Sin(totalMs * 0.0042);

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int chebDist = Math.Max(Math.Abs(dx), Math.Abs(dy));

                // Per-tile phase offset creates a rhythmic outward flow
                float phase = chebDist * 0.55f;
                float tilePulse = 0.78f + 0.22f * (float)Math.Sin(totalMs * 0.0042 - phase);

                float finalAlpha = globalPulse * tilePulse;

                var tile = new Vector2(centerTile.X + dx, centerTile.Y + dy);
                Vector2 screen = Game1.GlobalToLocal(
                    Game1.viewport,
                    new Vector2(tile.X * 64f, tile.Y * 64f));

                // Use the vanilla charge-indicator sprite scaled to fill one tile
                sb.Draw(Game1.mouseCursors, screen, SourceRect,
                    Color.White * finalAlpha,
                    0f, Vector2.Zero, TileScale, SpriteEffects.None, 0.01f);
            }
        }
    }
}
