using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using System.Linq;

namespace OldFarmer;

/// <summary>
/// Scans the area around a center tile for trees and stumps that can be chopped.
/// </summary>
internal static class WoodcutterScanner
{
    /// <summary>Scan radius in tiles (half-size of the square scan area).</summary>
    private const int ScanRadius = 8; // 17×17 area, matches tillage scan

    /// <summary>
    /// Returns all tile positions within scan range of <paramref name="player"/>
    /// that contain a choppable tree or stump.
    /// </summary>
    public static List<Vector2> GetChoppableTiles(GameLocation location, Farmer player)
    {
        var results = new List<Vector2>();
        var playerTile = player.Tile;

        // Guard: terrainFeatures might not be populated yet on map load
        if (location.terrainFeatures is null || location.terrainFeatures.Count() == 0)
            return results;

        int minX = (int)playerTile.X - ScanRadius;
        int minY = (int)playerTile.Y - ScanRadius;
        int maxX = (int)playerTile.X + ScanRadius;
        int maxY = (int)playerTile.Y + ScanRadius;

        for (int tx = minX; tx <= maxX; tx++)
        {
            for (int ty = minY; ty <= maxY; ty++)
            {
                if (tx < 0 || ty < 0)
                    continue;

                var tilePos = new Vector2(tx, ty);

                if (!location.isTileOnMap(tilePos))
                    continue;

                // Check terrain features for Tree objects
                if (location.terrainFeatures.TryGetValue(tilePos, out var feature)
                    && feature is Tree tree)
                {
                    // Include fully-grown trees AND stumps
                    if (tree.growthStage.Value >= 5 || tree.stump.Value)
                        results.Add(tilePos);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Diagnostic: counts total Tree terrain features (any growth stage) and
    /// choppable ones (stage ≥ 5 or stump), both within scan range.
    /// Used for logging in <see cref="GrandpaWoodcutterBehavior"/>.
    /// </summary>
    public static (int totalTrees, int choppable) CountTrees(GameLocation location, Farmer player)
    {
        int total = 0, choppable = 0;

        if (location.terrainFeatures is null || location.terrainFeatures.Count() == 0)
            return (total, choppable);

        var playerTile = player.Tile;

        int minX = (int)playerTile.X - ScanRadius;
        int minY = (int)playerTile.Y - ScanRadius;
        int maxX = (int)playerTile.X + ScanRadius;
        int maxY = (int)playerTile.Y + ScanRadius;

        for (int tx = minX; tx <= maxX; tx++)
        {
            for (int ty = minY; ty <= maxY; ty++)
            {
                if (tx < 0 || ty < 0) continue;
                var tilePos = new Vector2(tx, ty);
                if (!location.isTileOnMap(tilePos)) continue;

                if (location.terrainFeatures.TryGetValue(tilePos, out var feature)
                    && feature is Tree tree)
                {
                    total++;
                    if (tree.growthStage.Value >= 5 || tree.stump.Value)
                        choppable++;
                }
            }
        }

        return (total, choppable);
    }
}
