using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Tools;
using StardewValley.Objects;
using StardewValley.Locations;

namespace OldFarmer;

/// <summary>The mod entry point.</summary>
internal sealed class ModEntry : Mod
{
    /// <summary>Maximum number of grandpas that can be summoned simultaneously.</summary>
    private const int MaxGrandpas = 10;

    // Orbit parameters — must match GrandpaSpiritOrbiter's constants
    private const float BaseOrbitRadius = 200f;
    private const float OrbitRadiusStep = 56f;

    private readonly List<GrandpaSpiritOrbiter> _grandpas = new();

    private readonly TillingModule tillingModule;
    private readonly WoodcuttingModule woodcuttingModule;
    private readonly WateringModule wateringModule;
    private readonly ScythingModule scythingModule;
    private readonly PlantingModule plantingModule;
    private readonly CombatModule combatModule;

    public ModEntry()
    {
        tillingModule    = new TillingModule();
        woodcuttingModule = new WoodcuttingModule();
        wateringModule   = new WateringModule();
        scythingModule   = new ScythingModule();
        plantingModule   = new PlantingModule(Monitor);
        combatModule     = new CombatModule();

        // Modules start disabled; ModEntry.OnUpdateTicked enables them
        // dynamically based on the player's currently equipped tool/item.
    }

    /// <summary>The mod entry point, called after the mod is first loaded.</summary>
    /// <param name="helper">Provides simplified APIs for writing mods.</param>
    public override void Entry(IModHelper helper)
    {
        tillingModule.SetMonitor(Monitor);
        wateringModule.SetMonitor(Monitor);
        woodcuttingModule.SetMonitor(Monitor);
        scythingModule.SetMonitor(Monitor);
        combatModule.SetMonitor(Monitor);

        helper.Events.GameLoop.UpdateTicked  += OnUpdateTicked;
        helper.Events.GameLoop.DayEnding     += OnDayEnding;
        helper.Events.Display.RenderedWorld  += OnRenderedWorld;
        helper.Events.Display.RenderedHud    += OnRenderedHud;

        PlantingExecutor.SetMonitor(Monitor);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        // ── Detect Mystic Syrup consumption ──────────────────────
        // When the player drinks Mystic Syrup, the game plays an eating
        // animation. We detect the start of eating and check if the item
        // is Mystic Syrup, then summon Grandpa when eating completes.
        // This is button-independent — works on PC, mobile, and controller.
        if (Game1.player != null)
        {
            bool isEating = Game1.player.isEating;
            if (!_wasEating && isEating)
            {
                // Eating just started — check if holding Mystic Syrup
                if (Game1.player.CurrentItem?.QualifiedItemId == "(O)MysticSyrup")
                    _pendingSummon = true;
            }
            if (_pendingSummon && _wasEating && !isEating)
            {
                _pendingSummon = false;
                SummonGrandpa();
            }
            _wasEating = isEating;
        }

        // Skip all grandpa logic when the game is paused (menu open, event, dialogue, etc.)
        if (!Context.IsPlayerFree)
            return;

        // Skip when the game window loses focus (alt-tab / switch to another window)
        if (!Game1.game1.IsActive)
            return;

        // Update all active grandpa orbiters
        foreach (var g in _grandpas)
            g.Update();

        // Remove fully destroyed orbiters (fade-out complete)
        _grandpas.RemoveAll(g => g.IsDestroyed);

        // Check if SAN dropped to 0 — trigger fade-out on ALL grandpas
        if (Game1.player != null && SanManager.GetSan(Game1.player) <= 0)
        {
            bool anyAlive = false;
            foreach (var g in _grandpas)
            {
                if (!g.IsDestroyed && !g.IsFadingOut())
                {
                    g.StartFadeOut();
                    anyAlive = true;
                }
            }
            if (anyAlive)
            {
                SanBarDrawer.ShouldDraw = false;
                Game1.addHUDMessage(new HUDMessage(
                    "爷爷们的灵力耗尽了...",
                    3)); // type 3 = red warning
            }
        }

        // If all grandpas are gone, stop everything
        if (_grandpas.Count == 0)
        {
            tillingModule.Disable();
            wateringModule.Disable();
            woodcuttingModule.Disable();
            scythingModule.Disable();
            plantingModule.Disable();
            combatModule.Disable();
            return;
        }

        // ── Combat takes priority over all other activities ──────
        // When monsters are within 10 tiles, all grandpas enter combat mode
        // and other modules are paused.
        bool monstersNearby = Game1.player != null
            && Game1.currentLocation != null
            && MonsterScanner.HasMonstersNearby(Game1.currentLocation, Game1.player);

        if (monstersNearby)
        {
            // Disable farming modules while fighting
            if (tillingModule.IsEnabled)    tillingModule.Disable();
            if (wateringModule.IsEnabled)   wateringModule.Disable();
            if (woodcuttingModule.IsEnabled) woodcuttingModule.Disable();
            if (scythingModule.IsEnabled)   scythingModule.Disable();
            if (plantingModule.IsEnabled)   plantingModule.Disable();

            if (!combatModule.IsEnabled)
                combatModule.Enable();
        }
        else
        {
            // No monsters — stop fighting, resume tool-based modules
            if (combatModule.IsEnabled)
                combatModule.Disable();

            // Enable tilling only when the player is holding a hoe
            bool holdingHoe = Game1.player?.CurrentTool is Hoe;
            if (holdingHoe && !tillingModule.IsEnabled)
                tillingModule.Enable();
            else if (!holdingHoe && tillingModule.IsEnabled)
                tillingModule.Disable();

            // Enable watering only when the player is holding a watering can
            bool holdingWateringCan = Game1.player?.CurrentTool is WateringCan;
            if (holdingWateringCan && !wateringModule.IsEnabled)
                wateringModule.Enable();
            else if (!holdingWateringCan && wateringModule.IsEnabled)
                wateringModule.Disable();

            // Enable scything only when the player is holding a scythe (MeleeWeapon with "Scythe" in the name)
            bool holdingScythe = Game1.player?.CurrentTool is MeleeWeapon mw && mw.Name.Contains("Scythe");
            if (holdingScythe && !scythingModule.IsEnabled)
                scythingModule.Enable();
            else if (!holdingScythe && scythingModule.IsEnabled)
                scythingModule.Disable();

            // Enable woodcutting only when the player is holding an axe
            bool holdingAxe = Game1.player?.CurrentTool is Axe;
            if (holdingAxe && !woodcuttingModule.IsEnabled)
                woodcuttingModule.Enable();
            else if (!holdingAxe && woodcuttingModule.IsEnabled)
                woodcuttingModule.Disable();

            // Enable planting only when the player is holding seeds
            bool holdingSeeds = IsHoldingSeeds();
            if (holdingSeeds && !plantingModule.IsEnabled)
                plantingModule.Enable();
            else if (!holdingSeeds && plantingModule.IsEnabled)
                plantingModule.Disable();
        }

        tillingModule.Update();
        wateringModule.Update();
        woodcuttingModule.Update();
        scythingModule.Update();
        plantingModule.Update();
        combatModule.Update();

        // ── out-of-season seed bubble ────────────────────────────
        UpdateOutOfSeasonBubble();

        // Update SAN bar smooth animation / shake timer
        if (Game1.player != null)
            SanBarDrawer.Update(SanManager.GetSan(Game1.player));
    }

    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        // Draw all grandpa orbiters
        foreach (var g in _grandpas)
            g.Draw(e.SpriteBatch);

        tillingModule.Draw(e.SpriteBatch);
        wateringModule.Draw(e.SpriteBatch);
        woodcuttingModule.Draw(e.SpriteBatch);
        scythingModule.Draw(e.SpriteBatch);
        plantingModule.Draw(e.SpriteBatch);
        combatModule.Draw(e.SpriteBatch);
    }

    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (Game1.player != null)
            SanBarDrawer.Draw(e.SpriteBatch, Game1.player);
    }

    /// <summary>
    /// When the player goes to sleep, all grandpas leave for the night.
    /// The player must summon grandpa(s) again the next day with Mystic Syrup.
    /// </summary>
    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        // Dismiss all grandpas
        foreach (var g in _grandpas)
            g.Dismiss();
        _grandpas.Clear();

        // Clear all module grandpa entries
        tillingModule.RemoveAllGrandpas();
        wateringModule.RemoveAllGrandpas();
        woodcuttingModule.RemoveAllGrandpas();
        scythingModule.RemoveAllGrandpas();
        plantingModule.RemoveAllGrandpas();
        combatModule.RemoveAllGrandpas();

        // Disable all modules
        tillingModule.Disable();
        wateringModule.Disable();
        woodcuttingModule.Disable();
        scythingModule.Disable();
        plantingModule.Disable();
        combatModule.Disable();

        SanBarDrawer.ShouldDraw = false;
    }

    /// <summary>
    /// Summons a new grandpa spirit (or restores SAN if already at max capacity).
    /// Each call adds one additional grandpa, up to <see cref="MaxGrandpas"/>.
    /// All grandpas share the same SAN bar.
    /// </summary>
    private void SummonGrandpa()
    {
        if (Game1.player == null)
            return;

        // Restore SAN to maximum (dynamically calculated from player skills)
        SanManager.SetSan(Game1.player, SanManager.GetMaxSan(Game1.player));

        // Add a new grandpa if under the cap
        if (_grandpas.Count < MaxGrandpas)
        {
            // Spread grandpas evenly around the orbit circle,
            // each on a different concentric ring so they don't overlap
            float angleOffset = _grandpas.Count * (MathF.PI * 2f / MaxGrandpas);
            float orbitRadius = BaseOrbitRadius + _grandpas.Count * OrbitRadiusStep;
            var orbiter = new GrandpaSpiritOrbiter(angleOffset, orbitRadius);
            orbiter.Revive();
            _grandpas.Add(orbiter);

            // Register with all modules
            tillingModule.AddGrandpa(orbiter);
            wateringModule.AddGrandpa(orbiter);
            woodcuttingModule.AddGrandpa(orbiter);
            scythingModule.AddGrandpa(orbiter);
            plantingModule.AddGrandpa(orbiter);
            combatModule.AddGrandpa(orbiter);

            int count = _grandpas.Count;
            string msg = count switch
            {
                1 => "爷爷听到了你的呼唤，回来了！",
                _ => $"又一位爷爷回来了！现在共有 {count} 位爷爷守护着你。",
            };
            Game1.addHUDMessage(new HUDMessage(msg, 2));
        }
        else
        {
            Game1.addHUDMessage(new HUDMessage(
                $"爷爷们已经到齐了！({MaxGrandpas} 位爷爷正守护着你)",
                2));
        }

        // Make sure SAN bar is visible
        SanBarDrawer.ShouldDraw = true;

        // Summoning visual & sound effects
        Game1.currentLocation?.playSound("yoba");
        Game1.flashAlpha = 0.5f;
    }

    private static bool IsHoldingSeeds()
    {
        if (Game1.player?.CurrentItem is not StardewValley.Object obj)
            return false;
        // Seeds category = -74 in SDV
        return obj.Category == -74;
    }

    /// <summary>
    /// Checks if the player is holding seeds that cannot grow in the current season.
    /// Used to show a speech bubble above grandpa's head.
    /// </summary>
    private static bool IsHoldingOutOfSeasonSeeds()
    {
        if (Game1.player?.CurrentItem is not StardewValley.Object obj)
            return false;
        if (obj.Category != -74)
            return false;

        GameLocation? location = Game1.currentLocation;
        if (location == null)
            return false;

        // Resolve the seed ID (handles mixed seeds etc.)
        string rawItemId = obj.ItemId;
        string resolvedId = Crop.ResolveSeedId(rawItemId, location);

        if (!Crop.TryGetData(resolvedId, out var cropData) || cropData.Seasons.Count == 0)
            return false;

        // Locations like Greenhouse ignore seasons — seeds are never "out of season" there
        if (location.SeedsIgnoreSeasonsHere())
            return false;

        Season currentSeason = location.GetSeason();
        return !cropData.Seasons.Contains(currentSeason);
    }

    /// <summary>
    /// Sets the speech bubble on grandpa's head when the player is holding out-of-season seeds.
    /// Called every tick from OnUpdateTicked.
    /// </summary>
    private void UpdateOutOfSeasonBubble()
    {
        if (_grandpas.Count == 0)
            return;

        bool holdingBadSeeds = IsHoldingOutOfSeasonSeeds();
        double currentTime = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;

        if (holdingBadSeeds && currentTime - _lastOutOfSeasonWarningTime > 5.0)
        {
            // Show a HUD message (like "Grandpa has returned!" type)
            // Type 2 = green notification, Type 3 = red warning
            Game1.addHUDMessage(new HUDMessage(
                "乖孙，这种子过季了哟",
                3)); // type 3 = warning/red style

            _lastOutOfSeasonWarningTime = currentTime;
        }
    }

    private double _lastOutOfSeasonWarningTime = 0;

    // ── Mystic Syrup drinking detection ─────────────────────────
    // When the player presses the action button while holding Mystic Syrup,
    // we set _pendingSummon and let the game's natural eating animation play.
    // OnUpdateTicked detects when eating completes and summons Grandpa.
    private bool _pendingSummon = false;
    private bool _wasEating = false;
}
