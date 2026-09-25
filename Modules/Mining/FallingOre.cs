using Microsoft.Xna.Framework;

namespace OldFarmer;

/// <summary>
/// A single chunk of ore/stone falling straight down from above the mine
/// ceiling to a target tile on the ground. Once <see cref="HasLanded"/> is
/// true the owning module turns it into a real pickable item.
/// </summary>
internal sealed class FallingOre
{
    private const float InitialSpeed         = 6f;   // pixels/tick
    private const float Acceleration         = 0.75f; // pixels/tick²
    private const float SpawnHeightAboveTarget = 12f * 64f;

    private float _speed = InitialSpeed;

    public int ItemId { get; }
    public Vector2 TargetWorldPosition { get; }
    public Vector2 WorldPosition { get; private set; }
    public bool HasLanded { get; private set; }

    public FallingOre(int itemId, Vector2 targetWorldPosition)
    {
        ItemId              = itemId;
        TargetWorldPosition = targetWorldPosition;
        WorldPosition       = new Vector2(
            targetWorldPosition.X,
            targetWorldPosition.Y - SpawnHeightAboveTarget);
    }

    public void Update()
    {
        if (HasLanded)
            return;

        _speed += Acceleration;
        WorldPosition = new Vector2(WorldPosition.X, WorldPosition.Y + _speed);

        if (WorldPosition.Y >= TargetWorldPosition.Y)
        {
            WorldPosition = TargetWorldPosition;
            HasLanded     = true;
        }
    }
}
