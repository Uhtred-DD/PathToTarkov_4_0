using System;
using System.IO;
using System.Threading.Tasks;
using PathToTarkov.Controllers;
using PathToTarkov.Models;
using PathToTarkov.Patches;
using PathToTarkov.Routers;
using PathToTarkov.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.External;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;

namespace PathToTarkov;

[Injectable(InjectionType.Singleton)]
public class PttMod : IPreSptLoadModAsync, IOnLoad
{
    private readonly SaveServer           _saveServer;
    private readonly DatabaseService      _db;
    private readonly LocaleService        _localeService;
    private readonly CustomItemService    _customItemService;
    private readonly ItemFilterService    _itemFilterService;
    private readonly ISptLogger<PttMod>   _logger;

    private static readonly string ModDir =
        Path.GetDirectoryName(typeof(PttMod).Assembly.Location)
        ?? AppContext.BaseDirectory;

    // Shared controller instance — set in PreSptLoadAsync, used in OnLoad
    private PttController? _controller;
    private PttConfig?     _loadedConfig;

    public PttMod(
        SaveServer saveServer,
        DatabaseService db,
        LocaleService localeService,
        CustomItemService customItemService,
        ItemFilterService itemFilterService,
        ISptLogger<PttMod> logger)
    {
        _saveServer        = saveServer;
        _db                = db;
        _localeService     = localeService;
        _customItemService = customItemService;
        _itemFilterService = itemFilterService;
        _logger            = logger;
    }

    // ---- IPreSptLoadModAsync — runs BEFORE database is imported ----

    public async Task PreSptLoadAsync()
    {
        _logger.Info("[PTT] PreSptLoadAsync — Path To Tarkov 7.0.0 loading...");

        try
        {
            var (config, spawnConfig, userConfig, exfilsConfig, spawnsConfig) = LoadConfigs();
            _loadedConfig = config;

            _controller = new PttController(
                config, spawnConfig, userConfig, exfilsConfig, spawnsConfig,
                _saveServer, _db, _localeService, _customItemService, _itemFilterService,
                msg => _logger.Info(msg),
                msg => _logger.Warning(msg),
                msg => _logger.Debug(msg));

            var raidCache = new RaidCacheService();
            _controller.SetRaidCache(raidCache);

            PttLocationLifecycleService.Controller = _controller;
            PttLocationLifecycleService.RaidCache  = raidCache;
            PttLocationController.Controller       = _controller;
            PttDataCallbacks.Controller            = _controller;
            PttStaticRouter.Controller             = _controller;

            _logger.Info("[PTT] PreSptLoadAsync complete — awaiting DB for full init");
        }
        catch (Exception ex)
        {
            _logger.Error($"[PTT] Failed to load: {ex.Message}\n{ex.StackTrace}");
        }

        await Task.CompletedTask;
    }

    // ---- IOnLoad — runs AFTER database is imported ----

    public async Task OnLoad()
    {
        if (_controller == null)
        {
            _logger.Error("[PTT] OnLoad: controller not initialised — PreSptLoadAsync may have failed");
            return;
        }

        try
        {
            _controller.OnLoaded();

            // Inject PTT locale keys into SPT's global locale dictionaries.
            // Must run after OnLoaded (DB is ready) but before any client request uses them.
            if (_loadedConfig != null)
                LocaleInjectionService.InjectAll(_loadedConfig, _localeService, msg => _logger.Info(msg));

            _logger.Info("[PTT] Path To Tarkov 7.0.0 loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.Error($"[PTT] OnLoad failed: {ex.Message}\n{ex.StackTrace}");
        }

        await Task.CompletedTask;
    }

    // ---- Config loading ----

    private (PttConfig config, SpawnConfig spawnConfig, UserConfig userConfig, ExfilsConfig exfilsConfig, SpawnsConfig spawnsConfig) LoadConfigs()
    {
        var userConfigPath = Path.Combine(ModDir, "UserConfig.json");
        var userConfig     = ConfigLoader.LoadOrDefault<UserConfig>(userConfigPath);

        if (!File.Exists(userConfigPath))
        {
            ConfigLoader.Save(userConfigPath, userConfig);
            _logger.Info($"[PTT] Created default UserConfig at {userConfigPath}");
        }

        var configDir  = Path.Combine(ModDir, "configs", userConfig.SelectedConfig);
        var configPath = Path.Combine(configDir, "config.json5");

        if (!File.Exists(configPath))
            throw new FileNotFoundException(
                $"Config not found: {configPath}. " +
                $"Check selectedConfig in UserConfig.json (current: '{userConfig.SelectedConfig}')");

        var config = ConfigLoader.Load<PttConfig>(configPath);
        _logger.Info($"[PTT] Loaded config '{userConfig.SelectedConfig}'");

        var spawnPath   = Path.Combine(ModDir, "configs", "shared_player_spawnpoints.json5");
        var spawnConfig = ConfigLoader.LoadOrDefault<SpawnConfig>(spawnPath);
        _logger.Info($"[PTT] Loaded spawn config ({spawnConfig.Count} maps)");

        var additionalPath = Path.Combine(configDir, "additional_player_spawnpoints.json5");
        if (File.Exists(additionalPath))
        {
            var additional = ConfigLoader.LoadOrDefault<SpawnConfig>(additionalPath);
            foreach (var (mapName, spawns) in additional)
            {
                if (!spawnConfig.ContainsKey(mapName)) spawnConfig[mapName] = spawns;
                else foreach (var (id, data) in spawns) spawnConfig[mapName][id] = data;
            }
            _logger.Info($"[PTT] Merged additional spawnpoints from '{userConfig.SelectedConfig}'");
        }

        // Generate/update exfils_config.json5 and spawns_config.json5 per profile
        // Use absolute path to SPT_Data — server DLL lives in user/mods/Trap-PathToTarkov/
        var modAbsDir  = Path.GetFullPath(ModDir);
        var sptDataDir = Path.GetFullPath(Path.Combine(modAbsDir, "..", "..", "..", "SPT_Data"));
        _logger.Info($"[PTT] SPT_Data path: {sptDataDir}");
        var (exfilsConfig, spawnsConfig) = ExfilsSpawnsConfigGenerator.EnsureConfigs(
            configDir, sptDataDir, spawnConfig, msg => _logger.Info(msg));

        return (config, spawnConfig, userConfig, exfilsConfig, spawnsConfig);
    }
}
