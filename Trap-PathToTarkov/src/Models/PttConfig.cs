using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using PathToTarkov.Helpers;

namespace PathToTarkov.Models;

// ---- PTT config.json (main per-config file) ----

public class RawStashConfig
{
    [JsonPropertyName("id")]         public string Id { get; set; } = "";
    [JsonPropertyName("size")]       public int Size { get; set; }
    [JsonPropertyName("access_via")] public object AccessVia { get; set; } = "";
}

/// <summary>Typed version of RawStashConfig used by StashController.</summary>
public class SecondaryStashConfig
{
    public string Id { get; init; } = "";
    public int Size { get; init; }
    public List<string> AccessVia { get; init; } = new();

    public static SecondaryStashConfig FromRaw(RawStashConfig raw) => new()
    {
        Id        = raw.Id,
        Size      = raw.Size,
        AccessVia = PttHelpers.NormalizeAccessVia(raw.AccessVia),
    };
}

public class OffraidRegenEntry
{
    [JsonPropertyName("access_via")] public object AccessVia { get; set; } = "";
}

public class OffraidRegenConfig
{
    [JsonPropertyName("hydration")] public OffraidRegenEntry Hydration { get; set; } = new();
    [JsonPropertyName("energy")]    public OffraidRegenEntry Energy    { get; set; } = new();
    [JsonPropertyName("health")]    public OffraidRegenEntry Health    { get; set; } = new();
}

public class TraderConfig
{
    [JsonPropertyName("access_via")]           public object? AccessVia          { get; set; }
    [JsonPropertyName("disable_warning")]      public bool?   DisableWarning     { get; set; }
    [JsonPropertyName("override_description")] public bool?   OverrideDescription { get; set; }
}

public class RestrictionValue
{
    [JsonPropertyName("Value")] public double Value { get; set; }
}

public class OverrideByProfile
{
    [JsonPropertyName("initial_offraid_position")]  public string? InitialOffraidPosition { get; set; }
    [JsonPropertyName("respawn_at")]                public List<string>? RespawnAt         { get; set; }
    [JsonPropertyName("hideout_main_stash_access_via")] public object? HideoutMainStashAccessVia { get; set; }
}

/// <summary>An offraid position definition from config.json5 offraid_positions section.</summary>
public class OffraidPositionConfig
{
    /// <summary>Per-locale display name. Key = locale code e.g. "en", "fr".</summary>
    [JsonPropertyName("displayName")]
    public Dictionary<string, string>? DisplayName { get; set; }
}

/// <summary>
/// Trader availability config for a PTT intel item.
/// Omit the entire trader section to make the item loot-only.
/// </summary>
public class IntelItemTraderConfig
{
    /// <summary>SPT trader MongoId. e.g. "54cb50c76803fa8b248b4571" = Prapor</summary>
    [JsonPropertyName("trader_id")]
    public string TraderId { get; set; } = "";

    /// <summary>Trader loyalty level required to purchase (1-4)</summary>
    [JsonPropertyName("loyalty_level")]
    public int LoyaltyLevel { get; set; } = 4;

    /// <summary>Purchase price in roubles</summary>
    [JsonPropertyName("price_roubles")]
    public int PriceRoubles { get; set; } = 100000;

    /// <summary>If true, only stock_count copies available per trader refresh cycle</summary>
    [JsonPropertyName("limited_stock")]
    public bool LimitedStock { get; set; } = false;

    /// <summary>Copies in stock per refresh if limited_stock is true</summary>
    [JsonPropertyName("stock_count")]
    public int StockCount { get; set; } = 1;
}

/// <summary>
/// Definition of a PTT intel item that gates an offraid position node.
/// Each item is cloned from Slim Diary (590c651286f7741e566b6461) at server startup.
/// The item ID is defined by its key in offraid_intel_items and must match
/// the value in offraid_position_conditions.
/// </summary>
public class IntelItemConfig
{
    /// <summary>Per-locale display name shown in inventory. "en" is the fallback.</summary>
    [JsonPropertyName("display_name")]
    public Dictionary<string, string>? DisplayName { get; set; }

    /// <summary>Per-locale short name (shown in compact inventory cells)</summary>
    [JsonPropertyName("short_name")]
    public Dictionary<string, string>? ShortName { get; set; }

    /// <summary>Per-locale item description shown in inspect window</summary>
    [JsonPropertyName("description")]
    public Dictionary<string, string>? Description { get; set; }

    /// <summary>
    /// Inventory slot background color.
    /// Valid: blue, green, yellow, violet, orange, red, black, grey, default
    /// Default: blue (inherited from Slim Diary base)
    /// </summary>
    [JsonPropertyName("background_color")]
    public string BackgroundColor { get; set; } = "blue";

    /// <summary>
    /// Whether this item can be sold on the flea market.
    /// Keep false to prevent players bypassing the loot requirement by purchasing.
    /// </summary>
    [JsonPropertyName("can_sell_on_ragfair")]
    public bool CanSellOnRagfair { get; set; } = false;

    /// <summary>
    /// Weight added to the Scav pocket loot pool globally (all Scavs, all maps).
    /// Total pool weight is ~161,000.
    /// Reference: Case Key=4 (0.002%), Golden 1GPhone=8 (0.005%), LEDX=14 (0.009%).
    /// Default: 7 — extremely rare, between Case Key and Golden Phone rarity.
    /// </summary>
    [JsonPropertyName("scav_drop_weight")]
    public int ScavDropWeight { get; set; } = 7;

    /// <summary>
    /// Optional trader listing. Omit this section entirely to make the item loot-only.
    /// When present, the item is added to the specified trader's assort.
    /// </summary>
    [JsonPropertyName("trader")]
    public IntelItemTraderConfig? Trader { get; set; }
}

public class PttConfig
{
    [JsonPropertyName("initial_offraid_position")]
    public string InitialOffraidPosition { get; set; } = "FactoryZB-1011";

    [JsonPropertyName("respawn_at")]
    public List<string> RespawnAt { get; set; } = new();

    [JsonPropertyName("enable_automatic_transits_creation")]
    public bool EnableAutomaticTransitsCreation { get; set; } = true;

    [JsonPropertyName("enable_all_vanilla_transits")]
    public bool EnableAllVanillaTransits { get; set; } = false;

    [JsonPropertyName("bypass_exfils_override")]
    public bool BypassExfilsOverride { get; set; } = false;

    [JsonPropertyName("hideout_main_stash_access_via")]
    public object HideoutMainStashAccessVia { get; set; } = new List<string>();

    [JsonPropertyName("hideout_secondary_stashes")]
    public List<RawStashConfig> HideoutSecondaryStashes { get; set; } = new();

    [JsonPropertyName("traders_config")]
    public Dictionary<string, TraderConfig> TradersConfig { get; set; } = new();

    // exfiltrations: mapName -> exitName -> single string or array of strings
    [JsonPropertyName("exfiltrations")]
    public Dictionary<string, Dictionary<string, object>> Exfiltrations { get; set; } = new();

    // infiltrations: offraidPosition -> mapName -> list of spawnIds
    [JsonPropertyName("infiltrations")]
    public Dictionary<string, Dictionary<string, List<string>>> Infiltrations { get; set; } = new();

    [JsonPropertyName("offraid_regen_config")]
    public OffraidRegenConfig OffraidRegenConfig { get; set; } = new();

    [JsonPropertyName("restrictions_in_raid")]
    public Dictionary<string, RestrictionValue> RestrictionsInRaid { get; set; } = new();

    [JsonPropertyName("override_by_profiles")]
    public Dictionary<string, OverrideByProfile>? OverrideByProfiles { get; set; }

    // ---- Locale / tooltip fields ----

    /// <summary>Per-locale template for extract prompts. e.g. { "en": "Extract to {0}" }</summary>
    [JsonPropertyName("extracts_prompt_template")]
    public Dictionary<string, string>? ExtractsPromptTemplate { get; set; }

    /// <summary>Per-locale template for transit prompts. e.g. { "en": "Transit to {0}" }</summary>
    [JsonPropertyName("transits_prompt_template")]
    public Dictionary<string, string>? TransitsPromptTemplate { get; set; }

    /// <summary>
    /// Global tooltip template for exfil labels.
    /// Variables: $exfilDisplayName, $offraidPositionDisplayName
    /// </summary>
    [JsonPropertyName("exfiltrations_tooltips_template")]
    public string? ExfiltrationsTooltipsTemplate { get; set; }

    /// <summary>offraid_positions: positionId -> { displayName: { en: "...", fr: "..." }, ... }</summary>
    [JsonPropertyName("offraid_positions")]
    public Dictionary<string, OffraidPositionConfig>? OffraidPositions { get; set; }

    /// <summary>
    /// Maps offraid position IDs to the intel item ID required to unlock them.
    /// Positions omitted here are always unlocked.
    /// e.g. { "Crossroads": "ptt_intel_crossroads" }
    /// </summary>
    [JsonPropertyName("offraid_position_conditions")]
    public Dictionary<string, string>? OffRaidPositionConditions { get; set; }

    /// <summary>
    /// Global kill switch for all intel item loot injection code.
    /// When false, completely disables:
    ///   - Scav pocket loot pool addition
    ///   - PMC loot pool removal
    ///   - ItemFilterService lootable blacklist registration
    /// The item still exists and works as a stash key — only loot system
    /// interactions are disabled. Use if intel items conflict with loot mods.
    /// Default: true
    /// </summary>
    [JsonPropertyName("enable_intel_item_loot_injection")]
    public bool EnableIntelItemLootInjection { get; set; } = true;

    /// <summary>
    /// Defines the PTT intel items that gate offraid positions.
    /// Each key must match a value in offraid_position_conditions.
    /// Items are cloned from Slim Diary at server startup.
    /// </summary>
    [JsonPropertyName("offraid_intel_items")]
    public Dictionary<string, IntelItemConfig>? OffRaidIntelItems { get; set; }

    // ---- Typed accessors (not serialized) ----

    [JsonIgnore]
    public List<string> HideoutMainStashAccessViaList =>
        PttHelpers.NormalizeAccessVia(HideoutMainStashAccessVia);

    [JsonIgnore]
    public List<SecondaryStashConfig> HideoutSecondaryStashesTyped =>
        HideoutSecondaryStashes.Select(SecondaryStashConfig.FromRaw).ToList();
}
