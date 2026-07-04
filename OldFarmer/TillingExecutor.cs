using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace OldFarmer;

/// <summary>
/// Executes a tilling action centered on a given tile.
/// Instead of relying on the hoe's built-in AoE (which requires the player
/// to have the tool equipped and charged), we explicitly loop over a 9x9
/// area and call DoFunction on each tillable cell individually.
/// </summary>
internal static class TillingExecutor
{
    /// <summary>Half-size of the tilling square (9x9 → radius 4).</summary>
    private const int TillRadius = 4;

    /// <summary>
    /// Tills a 9×9 area centered on <paramref name="centerTile"/>.
    /// Already-tilled tiles, objects, and out-of-bounds cells are silently skipped.
    /// </summary>
    public static void TillArea(GameLocation location, Farmer player, Vector2 centerTile)
    {
        var hoe = new StardewValley.Tools.Hoe { UpgradeLevel = 4 };

        // Preserve stamina — grandpa does the work, not the player
        float savedStamina = player.Stamina;

        try
        {
            // Set stamina to a very large value so hoe swings never drain it
            player.Stamina = 999999f;

            // Build a set of all tiles occupied by multi-tile resource clumps
            // (giant crops etc.) so we don't try to till under them.
            var occupiedByClumps = new HashSet<Vector2>();
            foreach (var kvp in location.terrainFeatures.Pairs)
            {
                if (kvp.Value is ResourceClump rc)
                {
                    int ox = (int)kvp.Key.X;
                    int oy = (int)kvp.Key.Y;
                    for (int cx = 0; cx < rc.width.Value; cx++)
                        for (int cy = 0; cy < rc.height.Value; cy++)
                            occupiedByClumps.Add(new Vector2(ox + cx, oy + cy));
                }
            }

            for (int dx = -TillRadius; dx <= TillRadius; dx++)
            {
                for (int dy = -TillRadius; dy <= TillRadius; dy++)
                {
                    int tx = (int)centerTile.X + dx;
                    int ty = (int)centerTile.Y + dy;

                    if (tx < 0 || ty < 0)
                        continue;

                    var tilePos = new Vector2(tx, ty);

                    if (!location.isTileOnMap(tilePos))
                        continue;

                    // Skip tiles that are already HoeDirt
                    if (location.terrainFeatures.TryGetValue(tilePos, out var feature)
                        && feature is HoeDirt)
                        continue;

                    // Skip tiles with objects on them
                    if (location.objects.ContainsKey(tilePos))
                        continue;

                    // Skip tiles occupied by a multi-tile resource clump (giant crops etc.)
                    if (occupiedByClumps.Contains(tilePos))
                        continue;

                    // Only till if the tile is actually diggable
                    if (location.doesTileHaveProperty(tx, ty, "Diggable", "Back") == null)
                        continue;

                    hoe.DoFunction(location, tx * 64, ty * 64, 1, player);

                    // makeHoeDirt() auto-waters the dirt when IsRainingHere() is true
                    // (vanilla behaviour: tilling during rain = pre-watered). Grandpa's
                    // tiles should always be dry so the player still has to water them.
                    if (location.terrainFeatures.TryGetValue(tilePos, out var tf)
                        && tf is HoeDirt dirt)
                    {
                        dirt.state.Value = HoeDirt.dry;
                    }
                }
            }
        }
        finally
        {
            // Always restore the player's original stamina, even if something throws
            player.Stamina = savedStamina;
        }
    }
}
