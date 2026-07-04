using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace OldFarmer;

/// <summary>
/// Scans for plantable tiles — HoeDirt that has no crop planted yet.
/// </summary>
internal static class PlantingScanner
{
    private const int ScanRadius = 4; // 9x9 area

    /// <summary>
    /// Returns all tile positions within the 9x9 area centered on the player
    /// that have HoeDirt with no crop (plantable).
    /// </summary>
    public static List<Vector2> GetPlantableTiles(GameLocation location, Farmer player)
    {
        var results = new List<Vector2>();
        Vector2 playerTile = player.Tile;

        for (int dx = -ScanRadius; dx <= ScanRadius; dx++)
        {
            for (int dy = -ScanRadius; dy <= ScanRadius; dy++)
            {
                int tx = (int)playerTile.X + dx;
                int ty = (int)playerTile.Y + dy;
                var tilePos = new Vector2(tx, ty);

                if (!IsPlantable(location, tilePos))
                    continue;

                results.Add(tilePos);
            }
        }

        return results;
    }

    /// <summary>
    /// Returns true if the 9x9 block around <paramref name="center"/> has any plantable tile.
    /// </summary>
    public static bool HasPlantableInBlock(GameLocation location, Vector2 center)
    {
        const int halfStride = 4;

        for (int dx = -halfStride; dx <= halfStride; dx++)
        {
            for (int dy = -halfStride; dy <= halfStride; dy++)
            {
                var t = new Vector2(center.X + dx, center.Y + dy);
                if (!location.isTileOnMap(t))
                    continue;
                if (IsPlantable(location, t))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns true if the tile has HoeDirt with no crop (can be planted).
    /// </summary>
    private static bool IsPlantable(GameLocation location, Vector2 tile)
    {
        if (!location.isTileOnMap(tile))
            return false;

        if (!location.terrainFeatures.TryGetValue(tile, out var feature))
            return false;

        if (feature is not HoeDirt dirt)
            return false;

        // No crop planted yet
        if (dirt.crop != null)
            return false;

        return true;
    }
}
