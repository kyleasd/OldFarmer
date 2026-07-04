using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Executes planting on a 9x9 area.
/// Consumes seeds from the player's held item.
/// Bypasses HoeDirt.plant() to avoid false negatives from player-state checks.
/// </summary>
internal static class PlantingExecutor
{
    private static IMonitor? _monitor;

    public static void SetMonitor(IMonitor monitor)
    {
        _monitor = monitor;
    }

    /// <summary>
    /// Plants seeds on a 9x9 area centered on <paramref name="centerTile"/>.
    /// Only affects HoeDirt tiles with no crop.
    /// Returns the number of seeds actually planted.
    /// </summary>
    public static int PlantArea(GameLocation location, Farmer player, Vector2 centerTile, StardewValley.Object seedItem)
    {
        if (seedItem == null || seedItem.Stack <= 0)
        {
            _monitor?.Log("[Planting] seedItem is null or empty", LogLevel.Debug);
            return 0;
        }

        // Resolve the seed ID (handles mixed seeds etc.)
        string rawItemId = seedItem.ItemId;  // e.g. "495" for parsnip seeds
        string resolvedId = Crop.ResolveSeedId(rawItemId, location);
        _monitor?.Log($"[Planting] raw ItemId = {rawItemId}, resolved = {resolvedId}, stack = {seedItem.Stack}", LogLevel.Debug);

        // Check if the resolved seed has valid data
        if (!Crop.TryGetData(resolvedId, out var cropData) || cropData.Seasons.Count == 0)
        {
            _monitor?.Log($"[Planting] No crop data for {resolvedId}, aborting", LogLevel.Warn);
            return 0;
        }

        // Check season compatibility (unless location ignores seasons)
        Season currentSeason = location.GetSeason();
        bool ignoreSeasons = location.SeedsIgnoreSeasonsHere();
        if (!ignoreSeasons && !cropData.Seasons.Contains(currentSeason))
        {
            _monitor?.Log($"[Planting] {resolvedId} cannot grow in {currentSeason}, aborting", LogLevel.Warn);
            return 0;
        }

        const int halfStride = 4;
        int planted = 0;

        for (int dx = -halfStride; dx <= halfStride; dx++)
        {
            for (int dy = -halfStride; dy <= halfStride; dy++)
            {
                // Stop if we run out of seeds
                if (seedItem.Stack <= 0)
                    break;

                var tile = new Vector2(centerTile.X + dx, centerTile.Y + dy);
                if (!location.isTileOnMap(tile))
                    continue;

                if (location.terrainFeatures.TryGetValue(tile, out var feature)
                    && feature is HoeDirt dirt
                    && dirt.crop == null)  // no crop yet
                {
                    _monitor?.Log($"[Planting] Planting at ({tile.X},{tile.Y})", LogLevel.Trace);

                    // Directly create the Crop object — bypasses HoeDirt.plant()
                    // Signature: Crop(string itemId, int tileX, int tileY, GameLocation location)
                    dirt.crop = new Crop(resolvedId, (int)tile.X, (int)tile.Y, location);

                    // Apply speed increases from fertilizer under the player's professions
                    dirt.applySpeedIncreases(player);

                    // Play sounds
                    if (dirt.crop.raisedSeeds.Value)
                        location.playSound("stoneStep");
                    location.playSound("dirtyHit");

                    // Update stats
                    Game1.stats.SeedsSown++;

                    // Handle paddy water check
                    dirt.nearWaterForPaddy.Value = -1;
                    if (dirt.hasPaddyCrop() && dirt.paddyWaterCheck())
                    {
                        dirt.state.Value = 1;  // watered
                        dirt.updateNeighbors();
                    }

                    planted++;
                    seedItem.Stack--;
                    _monitor?.Log($"[Planting] Success at ({tile.X},{tile.Y}), remaining stack = {seedItem.Stack}", LogLevel.Debug);
                }
            }
            if (seedItem.Stack <= 0)
                break;
        }

        _monitor?.Log($"[Planting] Total planted = {planted}", LogLevel.Info);

        // If stack is now zero, clear the player's held item
        if (seedItem.Stack <= 0 && player.CurrentItem == seedItem)
        {
            player.removeItemFromInventory(seedItem);
            _monitor?.Log("[Planting] Seed stack exhausted, removed from inventory", LogLevel.Debug);
        }

        return planted;
    }
}
