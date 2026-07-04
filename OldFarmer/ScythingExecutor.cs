using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Executes a scything action centered on a given tile.
/// Mirrors <see cref="TillingExecutor.TillArea"/>: iterates a 9×9 area and
/// handles each harvestable tile (crops and grass) in one instant pass.
///
/// Grass handling:
///   - If the player has a silo with capacity, hay goes directly into it.
///   - Otherwise, hay is dropped as debris on the ground.
/// </summary>
internal static class ScythingExecutor
{
    /// <summary>Half-size of the scything square (9×9 → radius 4).</summary>
    private const int HarvestRadius = 4;

    private static IMonitor? _monitor;
    public static void SetMonitor(IMonitor monitor) => _monitor = monitor;

    /// <summary>
    /// Harvests all crops and grass in a 9×9 area centered on
    /// <paramref name="centerTile"/>, instant one-shot pass.
    /// </summary>
    public static void HarvestArea(GameLocation location, Farmer player, Vector2 centerTile)
    {
        for (int dx = -HarvestRadius; dx <= HarvestRadius; dx++)
        {
            for (int dy = -HarvestRadius; dy <= HarvestRadius; dy++)
            {
                var tilePos = new Vector2((int)centerTile.X + dx, (int)centerTile.Y + dy);

                if (!location.isTileOnMap(tilePos))
                    continue;

                if (!location.terrainFeatures.TryGetValue(tilePos, out var feature))
                    continue;

                // ── Crop harvest ─────────────────────────────────
                if (feature is HoeDirt dirt && dirt.crop is Crop crop)
                {
                    bool isReady = crop.currentPhase.Value >= crop.phaseDays.Count - 1
                                 && !crop.dead.Value;
                    if (!isReady)
                        continue;

                    try
                    {
                        crop.harvest((int)tilePos.X, (int)tilePos.Y, dirt, null, false);
                        _monitor?.Log(
                            $"[Scything] Harvested crop at ({tilePos.X:F0},{tilePos.Y:F0})",
                            LogLevel.Trace);
                    }
                    catch (System.Exception ex)
                    {
                        _monitor?.Log($"[Scything] Error harvesting crop: {ex.Message}", LogLevel.Warn);
                    }
                    continue;
                }

                // ── Grass cut ────────────────────────────────────
                if (feature is Grass)
                {
                    location.terrainFeatures.Remove(tilePos);

                    // Try to add hay directly to a silo (SDV 1.6 native API)
                    bool addedToSilo = TryAddHayToSilo(player);

                    if (!addedToSilo)
                    {
                        // Drop hay on the ground if no silo / silo full.
                        // SDV 1.6: ItemRegistry.Create returns a proper pickupable item
                        var hay = ItemRegistry.Create("(O)178");
                        if (hay != null)
                        {
                            Game1.createItemDebris(
                                hay,
                                tilePos * 64f + new Vector2(32f, 32f),
                                -1);
                        }
                    }

                    try { Game1.playSound("cut"); } catch { }

                    _monitor?.Log(
                        $"[Scything] Cut grass at ({tilePos.X:F0},{tilePos.Y:F0}){(addedToSilo ? " (→silo)" : " (→ground)")}",
                        LogLevel.Trace);
                }
            }
        }
    }

    // ── Silo hay helper ──────────────────────────────────────────

    /// <summary>
    /// Tries to add hay to the player's silos.
    /// In SDV 1.6, Farm.tryToAddHay(int) handles the entire silo pipeline
    /// (capacity check, hay increment, sound/notification).
    /// Returns true if hay was stored, false if silos are missing or full.
    /// </summary>
    private static bool TryAddHayToSilo(Farmer player)
    {
        try
        {
            var farm = Game1.getFarm();
            if (farm == null) return false;

            // SDV 1.6: Farm.tryToAddHay(int number) → leftover count (0 = stored)
            int leftover = farm.tryToAddHay(1);
            return leftover == 0;
        }
        catch
        {
            return false;
        }
    }
}
