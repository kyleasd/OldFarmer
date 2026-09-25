using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace OldFarmer;

/// <summary>
/// Executes watering on a 9x9 area.
/// Directly sets HoeDirt to watered state and spawns splash particles
/// at the target tile — NEVER assigns the watering can to player.CurrentTool,
/// because that causes the player sprite to show the watering animation.
/// </summary>
internal static class WateringExecutor
{
    /// <summary>
    /// Waters a 9x9 area centered on <paramref name="centerTile"/>.
    /// Only affects tiles with HoeDirt that are not already watered.
    /// </summary>
    public static void WaterArea(GameLocation location, Vector2 centerTile)
    {
        const int halfStride = 4;

        for (int dx = -halfStride; dx <= halfStride; dx++)
        {
            for (int dy = -halfStride; dy <= halfStride; dy++)
            {
                var tile = new Vector2(centerTile.X + dx, centerTile.Y + dy);
                if (!location.isTileOnMap(tile))
                    continue;

                if (location.terrainFeatures.TryGetValue(tile, out var feature)
                    && feature is HoeDirt dirt
                    && dirt.state.Value != 1)  // 1 = watered
                {
                    dirt.state.Value = 1; // water it

                    // Spawn splash particles at the tile — NOT at the player
                    SpawnSplashAt(location, tile);
                }
            }
        }
    }

    /// <summary>
    /// Creates water-splash TemporaryAnimatedSprites at the given tile.
    /// Replicates what WateringCan.DoFunction does, but without touching
    /// the player's held tool (so no player watering animation fires).
    ///
    /// The game's DoFunction uses texture index 13 from Game1.animations
    /// for the splash.  We add two sprites at slightly different positions
    /// and intervals to match the vanilla visual.
    /// </summary>
    private static void SpawnSplashAt(GameLocation location, Vector2 tile)
    {
        if (location == null)
            return;

        // Pixel position of the tile center
        float px = tile.X * 64f;
        float py = tile.Y * 64f;

        // Sprite index 13 in Game1.animations = water splash
        // Constructor: (textureIndex, position, color, totalFrames, flipped, interval)
        location.temporarySprites.Add(
            new TemporaryAnimatedSprite(
                13,
                new Vector2(px + 16f, py + 16f),
                Color.White,
                10,   // totalFrames
                false, // flipped
                70f    // interval (ms per frame)
            )
        );

        location.temporarySprites.Add(
            new TemporaryAnimatedSprite(
                13,
                new Vector2(px + 32f, py + 32f),
                Color.White,
                10,
                false,
                50f
            )
        );
    }
}
