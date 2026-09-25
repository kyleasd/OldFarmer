using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Monsters;

namespace OldFarmer;

/// <summary>
/// Scans the area around the player for hostile monsters.
/// Unlike tile-based scanners, this iterates over <c>location.characters</c>
/// and filters for <see cref="Monster"/> instances within the scan radius.
/// </summary>
internal static class MonsterScanner
{
    /// <summary>Scan radius in tiles (matches the user's "10 tiles" requirement).</summary>
    private const int ScanRadiusTiles = 10;

    /// <summary>Scan radius in world pixels (10 tiles × 64 px/tile).</summary>
    private const float ScanRadiusPixels = ScanRadiusTiles * 64f;

    /// <summary>
    /// Returns all monsters within <see cref="ScanRadiusTiles"/> of the player,
    /// sorted nearest-first. Dead/dying monsters are excluded.
    /// </summary>
    public static List<Monster> GetNearbyMonsters(GameLocation location, Farmer player)
    {
        var results = new List<Monster>();
        var playerPos = player.getStandingPosition();

        // Death/damage is applied after this scan completes (the caller acts on
        // the returned list), so the live collection can be iterated directly.
        foreach (var character in location.characters)
        {
            if (character is not Monster monster)
                continue;

            // Skip dead monsters (health <= 0)
            if (monster.Health <= 0)
                continue;

            float dist = Vector2.Distance(monster.getStandingPosition(), playerPos);
            if (dist <= ScanRadiusPixels)
                results.Add(monster);
        }

        // Sort nearest-first
        results.Sort((a, b) =>
            Vector2.Distance(a.getStandingPosition(), playerPos)
                .CompareTo(Vector2.Distance(b.getStandingPosition(), playerPos)));

        return results;
    }

    /// <summary>
    /// Quick check: are there any monsters within scan range?
    /// More efficient than <see cref="GetNearbyMonsters"/> when we only need a bool.
    /// </summary>
    public static bool HasMonstersNearby(GameLocation location, Farmer player)
    {
        var playerPos = player.getStandingPosition();

        foreach (var character in location.characters)
        {
            if (character is not Monster monster)
                continue;
            if (monster.Health <= 0)
                continue;

            if (Vector2.Distance(monster.getStandingPosition(), playerPos) <= ScanRadiusPixels)
                return true;
        }

        return false;
    }
}
