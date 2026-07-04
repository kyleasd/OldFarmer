using StardewValley;

namespace OldFarmer;

/// <summary>
/// Manages the player's SAN (Sanity) value.
/// SAN is stored persistently in <c>Farmer.modData</c> so it survives save/load.
///
/// Range: 0 – <see cref="GetMaxSan"/> (dynamically calculated from player skills).
/// A value at or above the max means the player is perfectly sane.
/// A value at or near 0 means the player is on the edge of madness.
/// </summary>
internal static class SanManager
{
    // ── constants ────────────────────────────────────────────────

    private const string ModDataKey = "Kyle.OldFarmer/SAN";

    /// <summary>Base maximum SAN value (before skill bonuses).</summary>
    private const int BaseMaxSan = 100;

    // ── public API ───────────────────────────────────────────────

    /// <summary>
    /// Calculates the maximum SAN based on the player's skill levels.
    /// Formula: 100 + 10 * (Farming + Mining + Foraging + Fishing + Combat)
    /// Minimum: 100
    /// </summary>
    public static int GetMaxSan(Farmer farmer)
    {
        return Math.Max(BaseMaxSan,
            BaseMaxSan + 10 * (farmer.FarmingLevel + farmer.MiningLevel
                             + farmer.ForagingLevel + farmer.FishingLevel
                             + farmer.CombatLevel));
    }

    /// <summary>
    /// Returns the current SAN of <paramref name="farmer"/>.
    /// Initialises to <see cref="GetMaxSan"/> on first access.
    /// </summary>
    public static float GetSan(Farmer farmer)
    {
        if (!farmer.modData.TryGetValue(ModDataKey, out string? raw) ||
            !float.TryParse(raw, out float value))
        {
            // First ever access for this save: start at full SAN
            value = GetMaxSan(farmer);
            SetSan(farmer, value);
        }
        return value;
    }

    /// <summary>
    /// Sets the SAN of <paramref name="farmer"/>, clamped to [0, <see cref="GetMaxSan"/>].
    /// </summary>
    public static void SetSan(Farmer farmer, float value)
    {
        float clamped = Math.Clamp(value, 0f, GetMaxSan(farmer));
        farmer.modData[ModDataKey] = clamped.ToString("F2");
    }

    /// <summary>
    /// Adds <paramref name="delta"/> to the player's SAN (can be negative).
    /// Returns the new value after clamping.
    /// </summary>
    public static float AddSan(Farmer farmer, float delta)
    {
        float newVal = GetSan(farmer) + delta;
        SetSan(farmer, newVal);
        return Math.Clamp(newVal, 0f, GetMaxSan(farmer));
    }

    /// <summary>Convenience: Returns SAN ratio in [0, 1].</summary>
    public static float GetRatio(Farmer farmer) =>
        GetSan(farmer) / GetMaxSan(farmer);
}
