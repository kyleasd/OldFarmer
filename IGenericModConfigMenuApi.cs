using System;
using StardewModdingAPI;

namespace OldFarmer;

/// <summary>
/// Minimal subset of the Generic Mod Config Menu API
/// (<c>spacechase0.GenericModConfigMenu</c>) used by this mod. Declaring only
/// the members we call keeps GMCM an optional, soft dependency.
/// </summary>
public interface IGenericModConfigMenuApi
{
    /// <summary>Register a mod whose config can be edited through the UI.</summary>
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

    /// <summary>Add a float option to the mod's config UI.</summary>
    void AddNumberOption(
        IManifest mod,
        Func<float> getValue,
        Action<float> setValue,
        Func<string> name,
        Func<string>? tooltip = null,
        float? min = null,
        float? max = null,
        float? interval = null,
        Func<float, string>? formatValue = null,
        string? fieldId = null);
}
