using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;

namespace OldFarmer;

/// <summary>
/// Harmony patches that make the vanilla health/stamina hover readouts play
/// nicely with the custom SAN bar.
///
/// Vanilla stacks the "current/max" readouts beside the left-most visible bar
/// (stamina only → stamina's top-left; health + stamina → both at health's
/// top-left). While the SAN bar is visible it becomes the left-most bar, so
/// vanilla's readouts must move onto the SAN bar too.
///
/// <list type="bullet">
///   <item><see cref="Game1.drawHUD"/> is flagged so we know when vanilla is
///   drawing the HUD status bars.</item>
///   <item>The numeric readouts vanilla draws for health/stamina are captured
///   and suppressed by <see cref="Game1DrawWithBorderPatch"/>.</item>
/// </list>
/// <see cref="SanBarDrawer.Draw"/> then redraws every readout at the SAN bar's
/// top-left corner, on top of the SAN bar container so nothing is occluded.
/// </summary>
[HarmonyPatch(typeof(Game1), "drawHUD")]
internal static class Game1DrawHudPatch
{
    [HarmonyPrefix]
    private static void Prefix() => SanBarDrawer.BeginVanillaHud();

    [HarmonyPostfix]
    private static void Postfix() => SanBarDrawer.EndVanillaHud();
}

/// <summary>
/// Suppresses the numeric health/stamina readouts while the SAN bar is visible.
/// Only the bare "123/456" tooltips are intercepted, so unrelated uses of
/// <c>drawWithBorder</c> (chat boxes, menus, …) are left untouched.
///
/// All three overloads are patched: the two shorter ones are one-line
/// forwarders that the JIT may inline into <c>drawHUD</c>, so patching only the
/// top overload could be bypassed. Patching the deepest overload (the one with
/// the actual drawing loop) guarantees the readout is caught either way.
/// </summary>
[HarmonyPatch(typeof(Game1), "drawWithBorder",
    new[] { typeof(string), typeof(Color), typeof(Color), typeof(Vector2) })]
internal static class Game1DrawWithBorderPatch4
{
    [HarmonyPrefix]
    private static bool Prefix(string message, Color insideColor)
        => !SanBarDrawer.TryCaptureTooltip(message, insideColor);
}

[HarmonyPatch(typeof(Game1), "drawWithBorder", new[]
{
    typeof(string), typeof(Color), typeof(Color), typeof(Vector2),
    typeof(float), typeof(float), typeof(float)
})]
internal static class Game1DrawWithBorderPatch7
{
    [HarmonyPrefix]
    private static bool Prefix(string message, Color insideColor)
        => !SanBarDrawer.TryCaptureTooltip(message, insideColor);
}

[HarmonyPatch(typeof(Game1), "drawWithBorder", new[]
{
    typeof(string), typeof(Color), typeof(Color), typeof(Vector2),
    typeof(float), typeof(float), typeof(float), typeof(bool)
})]
internal static class Game1DrawWithBorderPatch8
{
    [HarmonyPrefix]
    private static bool Prefix(string message, Color insideColor)
        => !SanBarDrawer.TryCaptureTooltip(message, insideColor);
}
