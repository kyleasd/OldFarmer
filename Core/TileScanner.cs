using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace OldFarmer;

/// <summary>Scans an 8x8 area around the player and returns tillable tiles.</summary>
internal static class TileScanner
{
    private const int ScanRadius = 4; // 8x8 => half-size = 4

    /// <summary>
    /// Returns all tile positions within the 8x8 area centered on the player
    /// that are tillable and have not yet been tilled.
    /// </summary>
    public static List<Vector2> GetUntilledTiles(GameLocation location, Farmer player)
    {
        var results = new List<Vector2>();
        Vector2 playerTile = player.Tile;

        // Build the occupied-by-clump set ONCE for the whole scan.
        var occupiedByClumps = BuildOccupiedByClumps(location);

        for (int dx = -ScanRadius; dx < ScanRadius; dx++)
        {
            for (int dy = -ScanRadius; dy < ScanRadius; dy++)
            {
                int tx = (int)playerTile.X + dx;
                int ty = (int)playerTile.Y + dy;

                if (tx < 0 || ty < 0)
                    continue;

                var tilePos = new Vector2(tx, ty);

                if (!IsTillable(location, tilePos, occupiedByClumps))
                    continue;

                if (IsAlreadyTilled(location, tilePos))
                    continue;

                results.Add(tilePos);
            }
        }

        return results;
    }

    private static bool IsTillable(GameLocation location, Vector2 tile, System.Collections.Generic.HashSet<Vector2> occupiedByClumps)
    {
        // Must be on the map
        if (!location.isTileOnMap(tile))
            return false;

        // Use the game's own API to check the "Diggable" property on the Back layer.
        // doesTileHaveProperty reads TileIndexProperties (baked into the tilesheet) AND
        // Tile.Properties (per-instance overrides), so it covers both cases.
        string? diggable = location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Diggable", "Back");
        if (diggable == null)
            return false;

        // Must not have a blocking terrain feature (trees, grass, etc.)
        // HoeDirt is allowed here — IsAlreadyTilled will filter it out separately.
        if (location.terrainFeatures.TryGetValue(tile, out var feature))
        {
            if (feature is not HoeDirt)
                return false;
        }

        // Must not have an object placed on it
        if (location.objects.ContainsKey(tile))
            return false;

        // Must not be covered by a large terrain feature (stumps, logs, boulders)
        var tileRect = new Microsoft.Xna.Framework.Rectangle(
            (int)tile.X * 64, (int)tile.Y * 64, 64, 64);
        foreach (var lf in location.largeTerrainFeatures)
        {
            if (lf.getBoundingBox().Intersects(tileRect))
                return false;
        }

        // Must not be occupied by a multi-tile resource clump (giant crops etc.)
        // The set is built once by BuildOccupiedByClumps and passed in.
        if (occupiedByClumps.Contains(tile))
            return false;

        return true;
    }

    private static bool IsAlreadyTilled(GameLocation location, Vector2 tile)
    {
        if (!location.terrainFeatures.TryGetValue(tile, out var feature))
            return false;

        return feature is HoeDirt;
    }

    /// <summary>
    /// Builds a set of every tile occupied by a multi-tile <see cref="ResourceClump"/>
    /// (e.g. giant crops).  Call this ONCE per scan and reuse the set.
    /// </summary>
    private static System.Collections.Generic.HashSet<Vector2> BuildOccupiedByClumps(GameLocation location)
    {
        var set = new System.Collections.Generic.HashSet<Vector2>();
        foreach (var pair in location.terrainFeatures.Pairs)
        {
            if (pair.Value is not ResourceClump rc)
                continue;

            int ox = (int)pair.Key.X;
            int oy = (int)pair.Key.Y;
            for (int dx = 0; dx < rc.width.Value; dx++)
                for (int dy = 0; dy < rc.height.Value; dy++)
                    set.Add(new Vector2(ox + dx, oy + dy));
        }
        return set;
    }

    /// <summary>
    /// Returns all tile positions within the 8x8 area centered on the player
    /// that have HoeDirt which is not already watered.
    /// </summary>
    public static List<Vector2> GetUnwateredTiles(GameLocation location, Farmer player)
    {
        var results = new List<Vector2>();
        Vector2 playerTile = player.Tile;

        for (int dx = -ScanRadius; dx < ScanRadius; dx++)
        {
            for (int dy = -ScanRadius; dy < ScanRadius; dy++)
            {
                int tx = (int)playerTile.X + dx;
                int ty = (int)playerTile.Y + dy;
                var tilePos = new Vector2(tx, ty);

                if (!NeedsWatering(location, tilePos))
                    continue;

                results.Add(tilePos);
            }
        }

        return results;
    }

    /// <summary>Returns true if the 9x9 block around <paramref name="center"/> has any unwatered HoeDirt.</summary>
    public static bool HasUnwateredInBlock(GameLocation location, Vector2 center)
    {
        const int halfStride = 4;

        for (int dx = -halfStride; dx <= halfStride; dx++)
        {
            for (int dy = -halfStride; dy <= halfStride; dy++)
            {
                var t = new Vector2(center.X + dx, center.Y + dy);
                if (!location.isTileOnMap(t)) continue;
                if (NeedsWatering(location, t))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Returns true if the tile has HoeDirt that is not yet watered.</summary>
    private static bool NeedsWatering(GameLocation location, Vector2 tile)
    {
        if (!location.isTileOnMap(tile))
            return false;

        if (!location.terrainFeatures.TryGetValue(tile, out var feature))
            return false;

        if (feature is not HoeDirt dirt)
            return false;

        return dirt.state.Value != 1; // 1 = watered
    }
}
