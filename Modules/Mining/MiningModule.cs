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

    // Chance (percent) that a drop is radioactive ore in a dangerous mine.
    private const int RadioactiveChancePercent = 5;

    // Chance (percent) that a deep/volcano drop is a prismatic shard. Kept as a
    // mutable property so Generic Mod Config Menu can adjust it at runtime.
    public static float PrismaticShardChancePercent { get; set; } = 0.9f;

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

    // Materials
    private const int ClayId             = 330;

    // Volcano dungeon
    private const int CinderShardId      = 848;
    private const int MagmaCapId         = 851;

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

    public void Enable()
    {
        IsEnabled = true;

        // Replay the morph transition every time mining mode is (re)entered,
        // so switching away to another item and back shows the animation again.
        foreach (var entry in _entries)
            entry.Animation.Reset();
    }

    public void Disable()
    {
        IsEnabled = false;
        _falling.Clear();

        // Play the morph backwards so grandpa visibly reverts to normal instead
        // of snapping back instantly.
        foreach (var entry in _entries)
        {
            if (entry.Orbiter.IsDestroyed || entry.Orbiter.IsFadingOut())
            {
                entry.Orbiter.IsMiningMode    = false;
                entry.Orbiter.MiningAnimation = null;
                continue;
            }

            entry.Animation.PlayReverse();
        }
    }

    // ── game loop hooks ───────────────────────────────────────────

    public void Update()
    {
        if (Game1.player is null || Game1.currentLocation is null)
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

        // Radioactive ore only spawns in a "dangerous" mine (vanilla: the
        // Danger in the Deep quest / Shrine of Challenge, or the Skull Cavern
        // Invasion/statue).
        bool dangerousMine = IsDangerousMine(loc);

        foreach (var entry in _entries)
        {
            if (entry.Orbiter.IsDestroyed || entry.Orbiter.IsFadingOut())
                continue;

            entry.Animation.Update();

            // Reverse morph: grandpa is turning back from the cyclone. Keep
            // drawing the animation until it fully reverts, then drop back to
            // the normal spirit sprite.
            if (entry.Animation.IsReversing)
            {
                bool stillReversing = !entry.Animation.IsReversingDone;
                entry.Orbiter.IsMiningMode    = stillReversing;
                entry.Orbiter.MiningAnimation = stillReversing ? entry.Animation : null;
                continue;
            }

            if (!IsEnabled)
                continue;

            entry.Orbiter.IsMiningMode    = true;
            entry.Orbiter.MiningAnimation = entry.Animation;

            if (!canWork)
                continue;

            entry.SpawnTimer++;
            if (entry.SpawnTimer >= SpawnIntervalTicks)
            {
                entry.SpawnTimer = 0;
                SpawnOre(loc, player, mineLevel, isVolcano, dangerousMine);
            }
        }

        if (!IsEnabled)
            return;

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

    private void SpawnOre(GameLocation loc, Farmer player, int mineLevel, bool isVolcano, bool dangerousMine)
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
            _falling.Add(new FallingOre(PickOreItem(mineLevel, isVolcano, dangerousMine), target));
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
    /// True when the current mine is in its "dangerous" variant, where vanilla
    /// radioactive nodes can spawn (Danger in the Deep / Shrine of Challenge,
    /// or the Skull Cavern Invasion / entrance statue).
    /// </summary>
    private static bool IsDangerousMine(GameLocation loc)
    {
        if (loc is not MineShaft shaft)
            return false;

        var world = Game1.netWorldState.Value;
        return shaft.mineLevel > 120
            ? world.SkullCavesDifficulty > 0
            : world.MinesDifficulty > 0;
    }

    /// <summary>
    /// Weighted ore pick based on how deep the mine shaft is, matching the
    /// vanilla ore distribution: copper (1-39), iron (40-79), gold (80-120),
    /// iridium (Skull Cavern), plus coal and stone. The volcano dungeon yields
    /// cinder shards, gold and iridium. Radioactive ore only appears in a
    /// dangerous mine / Skull Cavern.
    /// </summary>
    private static int PickOreItem(int mineLevel, bool isVolcano, bool dangerousMine)
    {
        int roll = Game1.random.Next(100);

        // ~15% of drops are bonus resources instead of ore.
        if (roll < BonusChancePercent)
            return PickBonusItem(mineLevel, isVolcano);

        // Radioactive nodes exist only in a dangerous mine, and can appear at
        // any depth (vanilla: Shrine of Challenge / Danger in the Deep, or the
        // Skull Cavern Invasion / entrance statue).
        if (dangerousMine && !isVolcano && Game1.random.Next(100) < RadioactiveChancePercent)
            return RadioactiveOreId;

        // Prismatic shards come from mystic stones / iridium nodes in the deep
        // mines, Skull Cavern and volcano dungeon. The chance is configurable
        // through Generic Mod Config Menu.
        if ((isVolcano || mineLevel >= 80)
            && Game1.random.NextDouble() * 100.0 < PrismaticShardChancePercent)
            return PrismaticShardId;

        if (isVolcano)
        {
            if (roll < 30) return StoneId;
            if (roll < 52) return CinderShardId;
            if (roll < 64) return IridiumOreId;
            if (roll < 78) return GoldOreId;
            if (roll < 88) return IronOreId;
            if (roll < 95) return CopperOreId;
            return CoalId;
        }

        if (mineLevel > 120)
        {
            // Skull Cavern: copper/iron/gold/iridium nodes all appear.
            if (roll < 34) return StoneId;
            if (roll < 52) return IridiumOreId;
            if (roll < 66) return GoldOreId;
            if (roll < 76) return IronOreId;
            if (roll < 85) return CopperOreId;
            if (roll < 92) return CoalId;
            return IridiumOreId;
        }
        if (mineLevel >= 80)
        {
            // Gold layer.
            if (roll < 44) return StoneId;
            if (roll < 66) return GoldOreId;
            if (roll < 78) return IronOreId;
            if (roll < 88) return CopperOreId;
            if (roll < 95) return CoalId;
            return GoldOreId;
        }
        if (mineLevel >= 40)
        {
            // Iron layer (no gold until floor 80).
            if (roll < 50) return StoneId;
            if (roll < 76) return IronOreId;
            if (roll < 88) return CopperOreId;
            return CoalId;
        }

        // Copper layer.
        if (roll < 55) return StoneId;
        if (roll < 82) return CopperOreId;
        return CoalId;
    }

    /// <summary>
    /// Picks one of the bonus (non-ore) resources for the current area: gems,
    /// geodes, forage and cinder shards. Only reached on the
    /// <see cref="BonusChancePercent"/> roll. The pools mirror what the vanilla
    /// stone/gem nodes of each area can actually drop.
    /// </summary>
    private static int PickBonusItem(int mineLevel, bool isVolcano)
    {
        int roll = Game1.random.Next(100);

        if (isVolcano)
        {
            // Volcano nodes: cinder shards, omni geodes, gems and magma caps,
            // plus the occasional dragon tooth from volcanic rocks.
            if (roll < 20) return CinderShardId;
            if (roll < 34) return OmniGeodeId;
            if (roll < 46) return DiamondId;
            if (roll < 58) return RubyId;
            if (roll < 68) return EmeraldId;
            if (roll < 76) return MagmaCapId;
            if (roll < 84) return AquamarineId;
            return DragonToothId;
        }

        if (mineLevel > 120)
        {
            // Skull Cavern: gem nodes, diamond nodes and omni geodes.
            if (roll < 20) return DiamondId;
            if (roll < 33) return OmniGeodeId;
            if (roll < 46) return RubyId;
            if (roll < 58) return EmeraldId;
            if (roll < 68) return AmethystId;
            if (roll < 77) return TopazId;
            if (roll < 85) return JadeId;
            if (roll < 92) return AquamarineId;
            return DiamondId;
        }

        if (mineLevel >= 80)
        {
            // Gold layer: fire quartz, ruby/emerald and magma geodes.
            if (roll < 16) return MagmaGeodeId;
            if (roll < 30) return DiamondId;
            if (roll < 44) return FireQuartzId;
            if (roll < 56) return RubyId;
            if (roll < 66) return EmeraldId;
            if (roll < 76) return AmethystId;
            if (roll < 85) return TopazId;
            if (roll < 92) return JadeId;
            return AquamarineId;
        }

        if (mineLevel >= 40)
        {
            // Iron layer: frozen geodes, aquamarine/jade, frozen tears.
            if (roll < 16) return FrozenGeodeId;
            if (roll < 28) return AquamarineId;
            if (roll < 38) return JadeId;
            if (roll < 48) return FrozenTearId;
            if (roll < 58) return QuartzId;
            if (roll < 66) return EarthCrystalId;
            if (roll < 74) return ClayId;
            if (roll < 82) return RedMushroomId;
            if (roll < 92) return CommonMushroomId;
            return DiamondId;
        }

        // Copper layer: geodes, low gems, clay and cave carrots.
        if (roll < 18) return GeodeId;
        if (roll < 32) return AmethystId;
        if (roll < 44) return TopazId;
        if (roll < 54) return EarthCrystalId;
        if (roll < 64) return QuartzId;
        if (roll < 72) return ClayId;
        if (roll < 82) return CaveCarrotId;
        if (roll < 92) return CommonMushroomId;
        return AmethystId;
    }
}
