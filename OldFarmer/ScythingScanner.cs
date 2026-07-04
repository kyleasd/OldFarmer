using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using System.Linq;

namespace OldFarmer;

/// <summary>
/// Scans the area around the player for harvestable targets:
///   - Mature crops              (any crop at final growth phase, not dead)
///   - Grass tiles              (produces hay → silo or ground drop)
/// </summary>
internal static class ScythingScanner
{
    /// <summary>Scan radius in tiles (half-size of the square scan area).</summary>
    private const int ScanRadius = 8; // 17×17 area, matches tillage scan

    // ── Scythe-harvestable crop detection ───────────────────────

    /// <summary>
    /// Returns all tiles within scan range that contain a crop ready to be
    /// harvested with a scythe (harvestMethod == 1).
    /// </summary>
    public static List<Vector2> GetHarvestableCropTiles(GameLocation location, Farmer player)
    {
        var results = new List<Vector2>();
        var playerTile = player.Tile;

        if (location.terrainFeatures is null)
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

                // Check HoeDirt for harvestable crops
                if (location.terrainFeatures.TryGetValue(tilePos, out var feature)
                    && feature is HoeDirt dirt
                    && dirt.crop is Crop crop)
                {
                    // Ready when the crop is in its final phase (harvestable)
                    if (crop.currentPhase.Value >= crop.phaseDays.Count - 1
                        && !crop.dead.Value)
                    {
                        results.Add(tilePos);
                    }
                }
            }
        }

        return results;
    }

    // ── Grass detection ─────────────────────────────────────────

    /// <summary>
    /// Returns all tiles within scan range that contain <see cref="Grass"/>.
    /// </summary>
    public static List<Vector2> GetGrassTiles(GameLocation location, Farmer player)
    {
        var results = new List<Vector2>();
        var playerTile = player.Tile;

        if (location.terrainFeatures is null)
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

                if (location.terrainFeatures.TryGetValue(tilePos, out var feature)
                    && feature is Grass)
                {
                    results.Add(tilePos);
                }
            }
        }

        return results;
    }

    // ── Combined target list ────────────────────────────────────

    /// <summary>
    /// Returns all harvestable targets (crops + grass) as a combined list.
    /// Crops come first, then grass — so food is prioritised over hay.
    /// </summary>
    public static List<Vector2> GetAllScytheTargets(GameLocation location, Farmer player)
    {
        var results = new List<Vector2>();

        // Deduplicate: a tile can only be in one list
        var seen = new System.Collections.Generic.HashSet<Vector2>();

        var cropTiles = GetHarvestableCropTiles(location, player);
        foreach (var t in cropTiles)
        {
            if (seen.Add(t))
                results.Add(t);
        }

        var grassTiles = GetGrassTiles(location, player);
        foreach (var t in grassTiles)
        {
            if (seen.Add(t))
                results.Add(t);
        }

        return results;
    }

    // ── Diagnostics ─────────────────────────────────────────────

    /// <summary>
    /// Counts harvestable crops and grass tiles separately.
    /// </summary>
    public static (int harvestableCrops, int grassTiles) CountTargets(GameLocation location, Farmer player)
    {
        int crops = 0, grass = 0;

        if (location.terrainFeatures is null || location.terrainFeatures.Count() == 0)
            return (crops, grass);

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

                if (location.terrainFeatures.TryGetValue(tilePos, out var feature))
                {
                if (feature is HoeDirt dirt
                    && dirt.crop is Crop crop
                    && crop.currentPhase.Value >= crop.phaseDays.Count - 1
                    && !crop.dead.Value)
                    {
                        crops++;
                    }
                    else if (feature is Grass)
                    {
                        grass++;
                    }
                }
            }
        }

        return (crops, grass);
    }
}
