using System;
using System.Collections.Generic;
using PathToTarkov.Models;
using SPTarkov.Server.Core.Services;

namespace PathToTarkov.Services;

/// <summary>
/// Injects PTT locale keys into SPT's global locale dictionaries.
///
/// Ported from Node.js path-to-tarkov-controller.js:
///   injectPromptTemplatesInLocales()    → PTT_EXTRACTS_PROMPT_TEMPLATE, PTT_TRANSITS_PROMPT_TEMPLATE
///   injectOffraidPositionDisplayNames() → PTT_OFFRAIDPOS_DISPLAY_NAME_{position}
///
/// Must be called from OnLoaded() (after DB is imported) since LocaleService
/// reads from the database which isn't available during PreSptLoadAsync.
/// </summary>
public static class LocaleInjectionService
{
    private const string DEFAULT_FALLBACK_LOCALE = "en";

    private const string EXTRACTS_PROMPT_KEY   = "PTT_EXTRACTS_PROMPT_TEMPLATE";
    private const string TRANSITS_PROMPT_KEY    = "PTT_TRANSITS_PROMPT_TEMPLATE";
    private const string EXTRACTS_PROMPT_DEFAULT = "Extract to {0}";
    private const string TRANSITS_PROMPT_DEFAULT  = "Transit to {0}";

    public static void InjectAll(PttConfig config, LocaleService localeService, Action<string> log)
    {
        try
        {
            var supportedLocales = localeService.GetServerSupportedLocales();
            if (supportedLocales == null || supportedLocales.Count == 0)
            {
                log("[PTT] LocaleInjection: no supported locales found, skipping");
                return;
            }

            var localeList = new System.Collections.Generic.List<string>(supportedLocales);
            int promptCount  = InjectPromptTemplates(config, localeService, localeList, log);
            int displayCount = InjectOffraidPositionDisplayNames(config, localeService, localeList, log);
            log($"[PTT] LocaleInjection: {promptCount} prompt keys + {displayCount} position display name keys injected across {localeList.Count} locales");
        }
        catch (Exception ex)
        {
            log($"[PTT] LocaleInjection ERROR: {ex.Message}");
        }
    }

    private static int InjectPromptTemplates(
        PttConfig config,
        LocaleService localeService,
        System.Collections.Generic.List<string> supportedLocales,
        Action<string> log)
    {
        var extractsTemplate = config.ExtractsPromptTemplate;
        var transitsTemplate = config.TransitsPromptTemplate;
        int injected = 0;

        foreach (var locale in supportedLocales)
        {
            var localeDb = localeService.GetLocaleDb(locale);
            if (localeDb == null) continue;

            localeDb[EXTRACTS_PROMPT_KEY] = ResolveLocaleValue(extractsTemplate, locale, EXTRACTS_PROMPT_DEFAULT);
            localeDb[TRANSITS_PROMPT_KEY]  = ResolveLocaleValue(transitsTemplate,  locale, TRANSITS_PROMPT_DEFAULT);
            injected += 2;
        }

        return injected;
    }

    private static int InjectOffraidPositionDisplayNames(
        PttConfig config,
        LocaleService localeService,
        System.Collections.Generic.List<string> supportedLocales,
        Action<string> log)
    {
        var offraidPositions = config.OffraidPositions;
        if (offraidPositions == null || offraidPositions.Count == 0)
            return 0;

        int injected = 0;

        foreach (var locale in supportedLocales)
        {
            var localeDb = localeService.GetLocaleDb(locale);
            if (localeDb == null) continue;

            foreach (var (positionId, positionConfig) in offraidPositions)
            {
                var key         = $"PTT_OFFRAIDPOS_DISPLAY_NAME_{positionId}";
                var displayName = ResolveLocaleValue(positionConfig.DisplayName, locale, positionId);
                localeDb[key]   = displayName;
                injected++;
            }
        }

        return injected;
    }

    /// <summary>
    /// Returns the value for the given locale from a per-locale dictionary,
    /// falling back to the English value, then the hardcoded default.
    /// </summary>
    private static string ResolveLocaleValue(
        Dictionary<string, string>? templateMap,
        string locale,
        string hardcodedDefault)
    {
        if (templateMap == null) return hardcodedDefault;
        if (templateMap.TryGetValue(locale, out var val) && !string.IsNullOrEmpty(val))
            return val;
        if (templateMap.TryGetValue(DEFAULT_FALLBACK_LOCALE, out var enVal) && !string.IsNullOrEmpty(enVal))
            return enVal;
        return hardcodedDefault;
    }
}
