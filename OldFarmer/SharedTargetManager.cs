using Microsoft.Xna.Framework;

namespace OldFarmer;

/// <summary>
/// Lightweight claim manager that prevents multiple grandpas in the same
/// module from picking the same target tile simultaneously.
///
/// <para>Usage per module:</para>
/// <list type="bullet">
///   <item>Module creates <b>one</b> <c>SharedTargetManager</c> instance.</item>
///   <item>In <c>AddGrandpa</c>, pass it to each behavior
///         via <c>behavior.SetTargetManager(tm)</c>.</item>
///   <item>In <c>RemoveAllGrandpas</c> / <c>Disable</c>, call <c>tm.Clear()</c>.</item>
///   <item>In the behavior, call <c>TryClaim(tile)</c> before committing to
///         a target, and <c>Release(tile)</c> when returning to idle.</item>
/// </list>
/// </summary>
internal sealed class SharedTargetManager
{
    private readonly HashSet<Vector2> _claimed = new();

    /// <summary>Try to claim a target tile. Returns <c>true</c> on success.</summary>
    public bool TryClaim(Vector2 tile) => _claimed.Add(tile);

    /// <summary>Release a previously claimed tile.</summary>
    public void Release(Vector2 tile) => _claimed.Remove(tile);

    /// <summary>Check whether a tile is currently claimed by another grandpa.</summary>
    public bool IsClaimed(Vector2 tile) => _claimed.Contains(tile);

    /// <summary>Remove every outstanding claim (called on disable / reset).</summary>
    public void Clear() => _claimed.Clear();

    /// <summary>Number of currently claimed tiles.</summary>
    public int Count => _claimed.Count;
}
