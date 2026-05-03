using System;
using System.Collections.Generic;
using System.Linq;
using PathToTarkov.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
namespace PathToTarkov.Services;

/// <summary>
/// Creates and registers PTT intel items at server startup using SPT's CustomItemService.
/// For each entry in config.offraid_intel_items:
///   1. Clones Slim Diary via CustomItemService.CreateItemFromClone (registers in ModItemCacheService)
///   2. Injects locales via NewItemFromCloneDetails.Locales (visible to give-ui + profile editor)
///   3. Adds item to Scav pocket loot pool with configured weight
///   4. Optionally adds item to a trader assort
/// </summary>
public class IntelItemService
{
    private const string SLIM_DIARY_TPL        = "590c651286f7741e566b6461";
    private const string SLIM_DIARY_NAME       = "item_thin_diary";  // client bundle taxonomy key
    private const string ROUBLES_TPL           = "5449016a4bdc2d6f028b456f";
    private const string ASSAULT_BOT_KEY       = "assault";
    private const string DEFAULT_LOCALE        = "en";
    private const string HANDBOOK_INFO_PARENT  = "5b47574386f77428ca22b335";  // Handbooks -> Info category

    private readonly CustomItemService _customItemService;
    private readonly DatabaseService   _db;
    private readonly LocaleService     _localeService;
    private readonly ItemFilterService _itemFilterService;
    private readonly Action<string>    _log;
    private readonly Action<string>    _logWarn;

    public IntelItemService(
        CustomItemService customItemService,
        DatabaseService db,
        LocaleService localeService,
        ItemFilterService itemFilterService,
        Action<string>? log = null,
        Action<string>? logWarn = null)
    {
        _customItemService = customItemService;
        _db                = db;
        _localeService     = localeService;
        _itemFilterService = itemFilterService;
        _log               = log     ?? (_ => {});
        _logWarn           = logWarn ?? (_ => {});
    }

    public void InitIntelItems(PttConfig config)
    {
        var items = config.OffRaidIntelItems;
        if (items == null || items.Count == 0)
        {
            _log("[PTT] IntelItemService: no offraid_intel_items defined, skipping");
            return;
        }

        if (!config.EnableIntelItemLootInjection)
            _log("[PTT] IntelItemService: enable_intel_item_loot_injection=false — all loot injection disabled");

        int created = 0, skipped = 0, assorted = 0;

        foreach (var (itemId, itemCfg) in items)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                _logWarn("[PTT] IntelItemService: empty item ID, skipping");
                skipped++;
                continue;
            }

            if (!CreateIntelItem(itemId, itemCfg, config.EnableIntelItemLootInjection)) { skipped++; continue; }

            if (config.EnableIntelItemLootInjection)
            {
                AddToScavLoot(itemId, itemCfg.ScavDropWeight);
                ExcludeFromPmcLoot(itemId);
            }

            if (itemCfg.Trader != null && AddToTraderAssort(itemId, itemCfg.Trader))
                assorted++;

            created++;
        }

        _log($"[PTT] IntelItemService: {created} created, {assorted} added to traders, {skipped} skipped");
    }

    // ---- Step 1: Create via CustomItemService ----

    private bool CreateIntelItem(string itemId, IntelItemConfig cfg, bool enableLootInjection = true)
    {
        var hexId = DeterministicId(itemId);

        // Already registered — safe on server restart
        var existing = _db.GetTables()?.Templates?.Items;
        if (existing != null && existing.ContainsKey(new MongoId(hexId)))
        {
            _log($"[PTT] IntelItemService: '{itemId}' already in DB, skipping");
            return true;
        }

        // Build locale dict for all supported locales
        var supported = _localeService.GetServerSupportedLocales();
        var locales   = new Dictionary<string, LocaleDetails>();
        foreach (var locale in supported ?? new HashSet<string>())
        {
            locales[locale] = new LocaleDetails
            {
                Name        = Resolve(cfg.DisplayName,  locale, itemId),
                ShortName   = Resolve(cfg.ShortName,    locale, itemId),
                Description = Resolve(cfg.Description,  locale, ""),
            };
        }

        // Ensure English is always present
        if (!locales.ContainsKey(DEFAULT_LOCALE))
        {
            locales[DEFAULT_LOCALE] = new LocaleDetails
            {
                Name        = Resolve(cfg.DisplayName,  DEFAULT_LOCALE, itemId),
                ShortName   = Resolve(cfg.ShortName,    DEFAULT_LOCALE, itemId),
                Description = Resolve(cfg.Description,  DEFAULT_LOCALE, ""),
            };
        }

        var overrideProps = new TemplateItemProperties
        {
            Name             = Resolve(cfg.DisplayName,  DEFAULT_LOCALE, itemId),
            ShortName        = Resolve(cfg.ShortName,    DEFAULT_LOCALE, itemId),
            Description      = Resolve(cfg.Description,  DEFAULT_LOCALE, ""),
            BackgroundColor  = string.IsNullOrWhiteSpace(cfg.BackgroundColor) ? "blue" : cfg.BackgroundColor,
            CanSellOnRagfair = cfg.CanSellOnRagfair,
            // Prevent SPT's runtime BotLootGenerator from adding this item to
            // random bot loot. Intel items must only enter the world through
            // deliberate placement (trader, specific container, quest reward).
            Unlootable       = true,
        };

        var details = new NewItemFromCloneDetails
        {
            ItemTplToClone       = new MongoId(SLIM_DIARY_TPL),
            NewId                = hexId,
            // Use Map parent category (567849dd4bdc2d150f8b456e) — NOT in pmc.json
            // backpackLoot or pocketLoot whitelists, so PMCLootGenerator never
            // includes this item when building PMC loot pools.
            // Previous mistake: used BarterItem (5448eb774bdc2d0a728b4567) which IS
            // in both whitelists — caused item to spawn on every PMC.
            ParentId             = "567849dd4bdc2d150f8b456e",  // Map category
            OverrideProperties   = overrideProps,
            Locales              = locales,
            // Set high handbook price (1.5M roubles) as a safety net:
            // even if item somehow enters a loot pool, the PMC ruble budget
            // (max 3M) means at most 1-2 copies ever spawn on any PMC.
            // Previous mistake: price=1 rouble caused hundreds of copies per PMC.
            HandbookPriceRoubles = 1500000,
            HandbookParentId     = HANDBOOK_INFO_PARENT,
        };

        var result = _customItemService.CreateItemFromClone(details);
        if (result.Success == true)
        {
            // Fix: reset _name to base item so client finds the correct bundle via taxonomy
            var dbItems = _db.GetTables()?.Templates?.Items;
            if (dbItems != null && dbItems.TryGetValue(new MongoId(hexId), out var newItem) && newItem != null)
                newItem.Name = SLIM_DIARY_NAME;

            // Block ALL runtime category-based bot loot generation for this item.
            // PMCLootGenerator.GeneratePMCBackpackLootPool/PocketLootPool/VestLootPool all call
            // GetContainerLootBlacklist() which reads this cache via GetBlacklistedLootableItems().
            // Gated by enableLootInjection — disabled via enable_intel_item_loot_injection=false.
            if (enableLootInjection)
                _itemFilterService.AddItemToLootableBlacklistCache(new[] { new MongoId(hexId) });

            _log($"[PTT] IntelItemService: cloned '{itemId}' via CustomItemService (id={hexId}), added to lootable blacklist");
            return true;
        }

        _logWarn($"[PTT] IntelItemService: failed to clone '{itemId}': {string.Join(", ", result.Errors ?? new List<string>())}");
        return false;
    }

    private static readonly string[] PMC_BOT_TYPES = { "usec", "bear", "pmcbot", "exusec" };
    private static readonly string[] PMC_LOOT_CONTAINERS = { "Backpack", "Pockets", "TacticalVest", "SpecialLoot" };

    // ---- Step 2: Add to Scav pocket loot pool ----

    private void AddToScavLoot(string itemId, int weight)
    {
        if (weight <= 0) { _log($"[PTT] IntelItemService: '{itemId}' weight=0, skipping loot pool"); return; }

        var bots = _db.GetTables()?.Bots?.Types;
        if (bots == null) { _logWarn("[PTT] IntelItemService: Bots.Types not found"); return; }

        if (!bots.TryGetValue(ASSAULT_BOT_KEY, out var bot) || bot == null)
        {
            _logWarn("[PTT] IntelItemService: assault bot not found");
            return;
        }

        var pockets = bot.BotInventory?.Items?.Pockets;
        if (pockets == null) { _logWarn("[PTT] IntelItemService: assault Pockets not found"); return; }

        pockets[new MongoId(DeterministicId(itemId))] = weight;
        _log($"[PTT] IntelItemService: '{itemId}' -> assault Pockets weight={weight}");

        // Explicitly remove from assault Backpack — the runtime category-based
        // loot generator ignores Unlootable=true and picks items from the Info
        // parent category for backpacks. Removing it here prevents backpack drops
        // while keeping the controlled pockets drop rate.
        var backpack = bot.BotInventory?.Items?.Backpack;
        if (backpack != null)
        {
            backpack.Remove(new MongoId(DeterministicId(itemId)));
            _log($"[PTT] IntelItemService: '{itemId}' removed from assault Backpack");
        }
    }

    // ---- Step 3: Explicitly exclude from all PMC loot pools ----

    private void ExcludeFromPmcLoot(string itemId)
    {
        var bots = _db.GetTables()?.Bots?.Types;
        if (bots == null) return;

        var tplKey = new MongoId(DeterministicId(itemId));
        int excluded = 0;

        foreach (var botType in PMC_BOT_TYPES)
        {
            if (!bots.TryGetValue(botType, out var bot) || bot == null) continue;
            var containers = bot.BotInventory?.Items;
            if (containers == null) continue;

            foreach (var containerName in PMC_LOOT_CONTAINERS)
            {
                var pool = containerName switch
                {
                    "Backpack"     => containers.Backpack,
                    "Pockets"      => containers.Pockets,
                    "TacticalVest" => containers.TacticalVest,
                    "SpecialLoot"  => containers.SpecialLoot,
                    _              => null
                };
                if (pool == null) continue;

                // Remove entirely — do NOT set to 0.
                // Weight-0 entries cause bot generation to hang during loot validation.
                if (pool.Remove(tplKey))
                    excluded++;
            }
        }

        _log($"[PTT] IntelItemService: '{itemId}' removed from {excluded} PMC loot pools");
    }

    // ---- Step 4: Add to trader assort (optional) ----

    private bool AddToTraderAssort(string itemId, IntelItemTraderConfig tc)
    {
        if (string.IsNullOrWhiteSpace(tc.TraderId))
        {
            _logWarn($"[PTT] IntelItemService: '{itemId}' trader config missing trader_id");
            return false;
        }

        var traders = _db.GetTables()?.Traders;
        if (traders == null) return false;

        var traderKey = new MongoId(tc.TraderId);
        if (!traders.TryGetValue(traderKey, out var trader) || trader?.Assort == null)
        {
            _logWarn($"[PTT] IntelItemService: trader '{tc.TraderId}' not found");
            return false;
        }

        var assort    = trader.Assort;
        var assortKey = new MongoId(DeterministicId("assort_" + itemId));

        if (assort.Items?.Any(i => i.Id == assortKey) == true)
        {
            _log($"[PTT] IntelItemService: '{itemId}' already in trader assort");
            return true;
        }

        assort.Items ??= new List<Item>();
        assort.Items.Add(new Item
        {
            Id       = assortKey,
            Template = new MongoId(DeterministicId(itemId)),
            SlotId   = "hideout",
        });

        assort.BarterScheme ??= new Dictionary<MongoId, List<List<BarterScheme>>>();
        assort.BarterScheme[assortKey] = new List<List<BarterScheme>>
        {
            new List<BarterScheme> { new BarterScheme { Count = (double)tc.PriceRoubles, Template = new MongoId(ROUBLES_TPL) } }
        };

        assort.LoyalLevelItems ??= new Dictionary<MongoId, int>();
        assort.LoyalLevelItems[assortKey] = Math.Clamp(tc.LoyaltyLevel, 1, 4);

        _log($"[PTT] IntelItemService: '{itemId}' -> trader '{tc.TraderId}' LL{tc.LoyaltyLevel} {tc.PriceRoubles}r");
        return true;
    }

    // ---- Helpers ----

    private static string Resolve(Dictionary<string, string>? map, string locale, string fallback)
    {
        if (map == null) return fallback;
        if (map.TryGetValue(locale, out var v) && !string.IsNullOrEmpty(v)) return v;
        if (map.TryGetValue(DEFAULT_LOCALE, out var en) && !string.IsNullOrEmpty(en)) return en;
        return fallback;
    }

    /// <summary>Returns the deterministic 24-char hex MongoId for a config item ID string.</summary>
    public static string GetMongoId(string configItemId) => DeterministicId(configItemId);

    private static string DeterministicId(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash  = System.Security.Cryptography.MD5.HashData(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLower().PadRight(24, '0')[..24];
    }
}
