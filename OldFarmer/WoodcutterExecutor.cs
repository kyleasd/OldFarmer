using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Damages trees by calling the game's own <c>Tree.performToolAction(Axe)</c>,
/// which triggers the native chopping animation, wood chips, sound effect,
/// falling animation, and loot drops — identical to the player's axe.
/// </summary>
internal static class WoodcutterExecutor
{
    /// <summary>How far (in pixels) from the axe center to check for trees.</summary>
    private const int HitRadius = 60; // covers the 96×96 axe sprite at 6× scale

    /// <summary>
    /// Minimum ticks between successive hits on the same tile.
    /// Prevents every-frame multi-hits while the axe orbit overlaps a tree.
    /// </summary>
    private const int HitCooldownTicks = 15;

    /// <summary>Copper axe — 2 damage per hit, ~5 hits to fell a full tree.</summary>
    private const int AxeUpgradeLevel = 1;

    /// <summary>Tracks the last Game1.ticks value when each tile was damaged.</summary>
    private static readonly System.Collections.Generic.Dictionary<Vector2, int> LastHitTick = new();

    private static Axe? _axe;
    private static IMonitor? _monitor;

    public static void SetMonitor(IMonitor monitor) => _monitor = monitor;

    /// <summary>
    /// Lazily creates and caches a single Axe tool instance to reuse for all hits.
    /// </summary>
    private static Axe GetAxe()
    {
        if (_axe is null)
        {
            _axe = new Axe();
            _axe.UpgradeLevel = AxeUpgradeLevel;
        }
        return _axe;
    }

    /// <summary>
    /// Damages any tree or stump overlapping <paramref name="axeWorldPos"/> using
    /// the vanilla <c>performToolAction</c> so all native effects play correctly.
    /// Returns the number of trees actually destroyed this tick.
    /// </summary>
    public static int ChopAtPosition(GameLocation location, Farmer player, Vector2 axeWorldPos)
    {
        int destroyed = 0;
        int now = Game1.ticks;
        var axe = GetAxe();

        // ── CRITICAL: the tool must know who's swinging it, otherwise
        //             performToolAction may deal 0 damage.
        axe.lastUser = player;

        var axeRect = new Microsoft.Xna.Framework.Rectangle(
            (int)(axeWorldPos.X - HitRadius),
            (int)(axeWorldPos.Y - HitRadius),
            HitRadius * 2,
            HitRadius * 2);

        int centerTx = (int)(axeWorldPos.X / 64);
        int centerTy = (int)(axeWorldPos.Y / 64);

        // ── Check the tile under the axe + 8‑neighbourhood ─────────
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                var tilePos = new Vector2(centerTx + dx, centerTy + dy);
                if (!location.isTileOnMap(tilePos))
                    continue;

                var tileRect = new Microsoft.Xna.Framework.Rectangle(
                    (int)tilePos.X * 64, (int)tilePos.Y * 64, 64, 64);

                if (!axeRect.Intersects(tileRect))
                    continue;

                if (!location.terrainFeatures.TryGetValue(tilePos, out var feature)
                    || feature is not Tree tree)
                    continue;

                // Only hit full-grown trees and stumps (ignore saplings)
                if (tree.growthStage.Value < 5 && !tree.stump.Value)
                    continue;

                // ── hit cooldown ────────────────────────────────────
                if (LastHitTick.TryGetValue(tilePos, out int lastHit)
                    && now - lastHit < HitCooldownTicks)
                    continue;

                LastHitTick[tilePos] = now;

                // ── vanilla chopping — triggers animation/sound/felling/drops ──
                float healthBefore = tree.health.Value;
                tree.performToolAction(axe, 0, tilePos);

                _monitor?.Log(
                    $"[Woodcutter] Hit tree at ({tilePos.X:F0},{tilePos.Y:F0})  "
                    + $"health: {healthBefore:F0}→{tree.health.Value:F0}  "
                    + $"stump={tree.stump.Value}  tick={now}",
                    LogLevel.Info);

                // If the tree was completely destroyed this tick, count it
                if (!location.terrainFeatures.ContainsKey(tilePos))
                {
                    destroyed++;
                    LastHitTick.Remove(tilePos);
                }
            }
        }

        return destroyed;
    }
}
