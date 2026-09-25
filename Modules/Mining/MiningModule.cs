using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace OldFarmer;

/// <summary>
/// Self-contained module that drives grandpa's mining behaviour.
///
/// While the player is inside a mine shaft holding a pickaxe, every active
/// grandpa plays a one-shot morph animation followed by a looping cyclone,
/// and ore/stone keeps raining down from the mine ceiling onto nearby tiles.
/// Each landed chunk becomes a real pickable item and costs SAN.
/// </summary>
internal sealed class MiningModule
{
    private const string OreTexturePath = "Maps/springobjects";
    private const int    OreSpriteSize  = 16;
    private const float  OreScale       = 4f;

    private const float SanCostPerOre      = 5f;
    private const int   SpawnIntervalTicks = 20;

    // Overall chance (percent) that a drop is a bonus resource instead of ore.
    private const int BonusChancePercent = 15;

    // Vanilla object ids used for the rain.
    private const int StoneId          = 390;
    private const int CopperOreId      = 378;
    private const int IronOreId        = 380;
    private const int CoalId           = 382;
    private const int GoldOreId        = 384;
    private const int IridiumOreId     = 386;
    private const int RadioactiveOreId = 909;
    private const int DragonToothId    = 852;

    // Gems / minerals
    private const int QuartzId           = 80;
    private const int EarthCrystalId     = 86;
    private const int AmethystId         = 66;
    private const int TopazId            = 68;
    private const int AquamarineId       = 62;
    private const int JadeId             = 70;
    private const int FrozenTearId       = 84;
    private const int EmeraldId          = 60;
    private const int RubyId             = 64;
    private const int FireQuartzId       = 82;
    private const int DiamondId          = 72;
    private const int PrismaticShardId   = 74;

    // Geodes
    private const int GeodeId            = 535;
    private const int FrozenGeodeId      = 536;
    private const int MagmaGeodeId       = 537;
    private const int OmniGeodeId        = 749;

    // Forage / mushrooms
    private const int CaveCarrotId       = 78;
    private const int CommonMushroomId   = 404;
    private const int RedMushroomId      = 420;
    private const int PurpleMushroomId   = 422;

    // Materials
    private const int ClayId             = 330;
    private const int RefinedQuartzId    = 338;
    private const int CopperBarId        = 334;
    private const int IronBarId          = 335;
    private const int GoldBarId          = 336;
    private const int IridiumBarId       = 337;

    // Monster drops
    private const int SlimeId            = 766;
    private const int BatWingId          = 767;
    private const int SolarEssenceId     = 768;
    private const int VoidEssenceId      = 769;
    private const int BugMeatId          = 684;
    private const int BoneFragmentId     = 579;

    // Volcano dungeon
    private const int CinderShardId      = 848;
    private const int MagmaCapId         = 851;
    private const int GoldenWalnutId     = 73;

    private sealed class Entry
    {
        public GrandpaAnimation Animation    = null!;
        public GrandpaSpiritOrbiter Orbiter  = null!;
        public int SpawnTimer;
    }

    private readonly IModHelper _helper;
    private readonly List<Entry> _entries = new();
    private readonly List<FallingOre> _falling = new();
    private Texture2D? _oreTexture;

    public MiningModule(IModHelper helper) => _helper = helper;

    public void AddGrandpa(GrandpaSpiritOrbiter orbiter)
    {
        var animation = new GrandpaAnimation(_helper);
        _entries.Add(new Entry
        {
            Animation  = animation,
            Orbiter    = orbiter,
            SpawnTimer = Game1.random.Next(SpawnIntervalTicks),
        });
    }

    public void RemoveAllGrandpas()
    {
        foreach (var entry in _entries)
        {
            entry.Orbiter.IsMiningMode    = false;
            entry.Orbiter.MiningAnimation = null;
        }
        _entries.Clear();
        _falling.Clear();
    }

    // ── public API ────────────────────────────────────────────────

    public bool IsEnabled { get; private set; }

    public void Enable() => IsEnabled = true;

    public void Disable()
    {
        IsEnabled = false;
        foreach (var entry in _entries)
        {
            entry.Orbiter.IsMiningMode    = false;
            entry.Orbiter.MiningAnimation = null;
        }
        _falling.Clear();
    }

    // ── game loop hooks ───────────────────────────────────────────

    public void Update()
    {
        if (!IsEnabled || Game1.player is null || Game1.currentLocation is null)
            return;

        var loc    = Game1.currentLocation;
        var player = Game1.player;
        bool canWork = SanManager.GetSan(player) > 0;

        // Volcano Dungeon counts as a high-tier area (iridium/gold).
        bool isVolcano = loc is VolcanoDungeon;
        int mineLevel = loc switch
        {
            MineShaft shaft => shaft.mineLevel,
            VolcanoDungeon   => 120,
            _                => 1,
        };

        foreach (var entry in _entries)
        {
            if (entry.Orbiter.IsDestroyed || entry.Orbiter.IsFadingOut())
                continue;

            entry.Animation.Update();
            entry.Orbiter.IsMiningMode    = true;
            entry.Orbiter.MiningAnimation = entry.Animation;

            if (!canWork)
                continue;

            entry.SpawnTimer++;
            if (entry.SpawnTimer >= SpawnIntervalTicks)
            {
                entry.SpawnTimer = 0;
                SpawnOre(loc, player, mineLevel, isVolcano);
            }
        }

        for (int i = _falling.Count - 1; i >= 0; i--)
        {
            _falling[i].Update();
            if (_falling[i].HasLanded)
            {
                LandOre(loc, _falling[i]);
                _falling.RemoveAt(i);
            }
        }
    }

    private void SpawnOre(GameLocation loc, Farmer player, int mineLevel, bool isVolcano)
    {
        Vector2 playerTile = player.Tile;

        // Pick a random open tile near the player to land on.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int tx = (int)playerTile.X + Game1.random.Next(-7, 8);
            int ty = (int)playerTile.Y + Game1.random.Next(-5, 6);
            var tile = new Vector2(tx, ty);

            if (!loc.isTileOnMap(tile))
                continue;

            var target = new Vector2(tx * 64f + 32f, ty * 64f + 32f);
            _falling.Add(new FallingOre(PickOreItem(mineLevel, isVolcano), target));
            return;
        }
    }

    private static void LandOre(GameLocation loc, FallingOre ore)
    {
        loc.playSound("hammer");

        if (Game1.player is not null)
            SanManager.AddSan(Game1.player, -SanCostPerOre);

        // Exactly the vanilla dropped-item behaviour: create the debris at the
        // landing tile. The game handles the bounce, resting height and the
        // magnetic pickup.
        var item   = ItemRegistry.Create("(O)" + ore.ItemId, 1);
        var debris = new Debris(item, ore.TargetWorldPosition, ore.TargetWorldPosition);
        loc.debris.Add(debris);
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (_falling.Count == 0)
            return;

        _oreTexture ??= Game1.content.Load<Texture2D>(OreTexturePath);

        foreach (var ore in _falling)
        {
            // Use the game's own helper so the frame matches the landed item's
            // sprite exactly (hardcoding columns picks the wrong frame).
            var source = Game1.getSourceRectForStandardTileSheet(
                _oreTexture, ore.ItemId, OreSpriteSize, OreSpriteSize);

            var screen = Game1.GlobalToLocal(Game1.viewport, ore.WorldPosition);
            float layerDepth = Math.Max(0.0001f, (ore.WorldPosition.Y + 32f) / 10000f);

            spriteBatch.Draw(
                _oreTexture,
                screen,
                source,
                Color.White,
                0f,
                new Vector2(OreSpriteSize / 2f, OreSpriteSize / 2f),
                OreScale,
                SpriteEffects.None,
                layerDepth);
        }
    }

    /// <summary>
    /// Weighted ore pick based on how deep the mine shaft is.
    /// Deeper levels bias towards more valuable ores; the volcano dungeon
    /// additionally yields radioactive ore and dragon teeth.
    /// </summary>
    private static int PickOreItem(int mineLevel, bool isVolcano)
    {
        int roll = Game1.random.Next(100);

        // ~15% of drops are bonus resources instead of ore.
        if (roll < BonusChancePercent)
            return PickBonusItem(mineLevel, isVolcano);

        if (isVolcano)
        {
            if (roll < 35) return StoneId;
            if (roll < 55) return RadioactiveOreId;
            if (roll < 67) return DragonToothId;
            if (roll < 82) return IridiumOreId;
            if (roll < 92) return GoldOreId;
            return CoalId;
        }

        if (mineLevel >= 120)
        {
            if (roll < 40) return StoneId;
            if (roll < 55) return IridiumOreId;
            if (roll < 70) return GoldOreId;
            if (roll < 82) return RadioactiveOreId;
            if (roll < 90) return CoalId;
            if (roll < 95) return IronOreId;
            return DragonToothId;
        }
        if (mineLevel >= 80)
        {
            if (roll < 50) return StoneId;
            if (roll < 70) return GoldOreId;
            if (roll < 88) return IronOreId;
            if (roll < 96) return CoalId;
            return CopperOreId;
        }
        if (mineLevel >= 40)
        {
            if (roll < 55) return StoneId;
            if (roll < 75) return IronOreId;
            if (roll < 90) return CopperOreId;
            if (roll < 97) return CoalId;
            return GoldOreId;
        }

        if (roll < 60) return StoneId;
        if (roll < 80) return CopperOreId;
        if (roll < 92) return CoalId;
        if (roll < 98) return IronOreId;
        return GoldOreId;
    }

    /// <summary>
    /// Picks one of the bonus (non-ore) resources for the current area:
    /// gems, geodes, forage/mushrooms, materials, monster drops and
    /// volcano-exclusive loot. Only reached on the <see cref="BonusChancePercent"/>
    /// roll.
    /// </summary>
    private static int PickBonusItem(int mineLevel, bool isVolcano)
    {
        int roll = Game1.random.Next(100);

        if (isVolcano)
        {
            if (roll < 25) return CinderShardId;
            if (roll < 38) return MagmaCapId;
            if (roll < 46) return DragonToothId;
            if (roll < 50) return GoldenWalnutId;
            if (roll < 60) return DiamondId;
            if (roll < 68) return OmniGeodeId;
            if (roll < 74) return PrismaticShardId;
            if (roll < 82) return RubyId;
            if (roll < 88) return EmeraldId;
            if (roll < 92) return SolarEssenceId;
            if (roll < 96) return VoidEssenceId;
            return RefinedQuartzId;
        }

        if (mineLevel >= 80)
        {
            if (roll < 14) return MagmaGeodeId;
            if (roll < 22) return GoldBarId;
            if (roll < 30) return RubyId;
            if (roll < 38) return EmeraldId;
            if (roll < 44) return DiamondId;
            if (roll < 50) return OmniGeodeId;
            if (roll < 56) return FireQuartzId;
            if (roll < 62) return SolarEssenceId;
            if (roll < 68) return VoidEssenceId;
            if (roll < 74) return BoneFragmentId;
            if (roll < 80) return RefinedQuartzId;
            if (roll < 86) return PurpleMushroomId;
            if (roll < 94) return IridiumBarId;
            return PrismaticShardId;
        }

        if (mineLevel >= 40)
        {
            if (roll < 16) return FrozenGeodeId;
            if (roll < 26) return AquamarineId;
            if (roll < 36) return JadeId;
            if (roll < 44) return FrozenTearId;
            if (roll < 52) return IronBarId;
            if (roll < 58) return QuartzId;
            if (roll < 64) return RefinedQuartzId;
            if (roll < 70) return SlimeId;
            if (roll < 76) return BatWingId;
            if (roll < 82) return RedMushroomId;
            if (roll < 88) return ClayId;
            if (roll < 94) return BoneFragmentId;
            return DiamondId;
        }

        if (roll < 18) return GeodeId;
        if (roll < 28) return AmethystId;
        if (roll < 36) return TopazId;
        if (roll < 44) return EarthCrystalId;
        if (roll < 50) return QuartzId;
        if (roll < 56) return ClayId;
        if (roll < 62) return CaveCarrotId;
        if (roll < 68) return CommonMushroomId;
        if (roll < 76) return CopperBarId;
        if (roll < 82) return SlimeId;
        if (roll < 88) return BugMeatId;
        if (roll < 94) return RefinedQuartzId;
        return DiamondId;
    }
}
