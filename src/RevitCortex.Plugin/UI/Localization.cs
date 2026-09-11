using Autodesk.Revit.ApplicationServices;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Lightweight runtime localization. Detects Revit UI language on first use
/// and returns translated strings by key. Unknown keys or missing languages
/// fall back to English.
/// </summary>
internal static class Localization
{
    private static string? _cachedLocale;

    public static string Locale
    {
        get
        {
            if (_cachedLocale != null) return _cachedLocale;
            _cachedLocale = DetectLocale();
            return _cachedLocale;
        }
    }

    public static string T(string key)
    {
        if (Table.TryGetValue(key, out var variants))
        {
            if (variants.TryGetValue(Locale, out var s)) return s;
            if (variants.TryGetValue("en", out var en)) return en;
        }
        return key;
    }

    public static string T(string key, params object?[] args)
    {
        var fmt = T(key);
        try { return string.Format(fmt, args); }
        catch (FormatException) { return fmt; }
    }

    private static string DetectLocale()
    {
        try
        {
            var fromRevit = TryDetectFromRevit();
            if (fromRevit != null) return fromRevit;
        }
        catch { }

        try
        {
            var iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName?.ToLowerInvariant();
            if (iso == "it" || iso == "fr" || iso == "de" || iso == "es") return iso;
        }
        catch { }

        return "en";
    }

    private static string? TryDetectFromRevit()
    {
        var app = RevitCortexApp.Instance?.UiApplication?.Application;
        if (app == null) return null;

        return app.Language switch
        {
            LanguageType.Italian     => "it",
            LanguageType.French      => "fr",
            LanguageType.German      => "de",
            LanguageType.Spanish     => "es",
            LanguageType.English_USA => "en",
            LanguageType.English_GB  => "en",
            _                        => "en"
        };
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Table = new()
    {
        ["support.title"] = new()
        {
            ["en"] = "RevitCortex 2026",
            ["it"] = "RevitCortex 2026",
        },
        ["support.already_running"] = new()
        {
            ["en"] = "A diagnostic report is already being generated. Please wait before retrying.",
            ["it"] = "È già in corso la generazione di un report diagnostico. Attendi prima di riprovare.",
        },
        ["support.package_failed"] = new()
        {
            ["en"] = "Unable to create the diagnostic package: {0}",
            ["it"] = "Impossibile creare il pacchetto diagnostico: {0}",
        },
        ["support.settings.title"] = new()
        {
            ["en"] = "Diagnostic Reports",
            ["it"] = "Report diagnostici",
        },
        ["support.settings.subtitle"] = new()
        {
            ["en"] = "Number of local reports to keep on disk",
            ["it"] = "Numero di report locali da conservare su disco",
        },
        ["support.settings.delete_now"] = new()
        {
            ["en"] = "Delete all now",
            ["it"] = "Elimina tutti adesso",
        },
        ["support.settings.open_folder"] = new()
        {
            ["en"] = "Open folder",
            ["it"] = "Apri cartella",
        },
        ["support.settings.open_folder_failed"] = new()
        {
            ["en"] = "Unable to open the folder: {0}",
            ["it"] = "Impossibile aprire la cartella: {0}",
        },
        ["support.cleanup.confirm_title"] = new()
        {
            ["en"] = "Delete all diagnostic reports?",
            ["it"] = "Eliminare tutti i report diagnostici?",
        },
        ["support.cleanup.confirm_body"] = new()
        {
            ["en"] = "Found {0} report(s) ({1}). All report files will be permanently deleted. Continue?",
            ["it"] = "Trovati {0} report ({1}). Tutti i file dei report verranno eliminati definitivamente. Continuare?",
        },
        ["support.cleanup.none"] = new()
        {
            ["en"] = "No diagnostic reports to delete.",
            ["it"] = "Nessun report diagnostico da eliminare.",
        },
        ["support.cleanup.done"] = new()
        {
            ["en"] = "{0} report(s) deleted.",
            ["it"] = "{0} report eliminati.",
        },
        ["support.cleanup.partial"] = new()
        {
            ["en"] = "{0} report(s) deleted. {1} could not be removed (files in use).",
            ["it"] = "{0} report eliminati. {1} non rimossi (file in uso).",
        },
        ["update.available_title"] = new()
        {
            ["en"] = "RevitCortex 2026 {0} is available",
            ["it"] = "È disponibile RevitCortex 2026 {0}",
        },
        ["update.available_detail"] = new()
        {
            ["en"] = "You are on {0}. Click Download to get the new release.",
            ["it"] = "Versione installata: {0}. Clicca Download per scaricare la nuova release.",
        },
        ["update.download_button"] = new()
        {
            ["en"] = "Download",
            ["it"] = "Scarica",
        },
        ["update.open_browser_failed"] = new()
        {
            ["en"] = "Unable to open the download link: {0}",
            ["it"] = "Impossibile aprire il link di download: {0}",
        },
        ["telemetry.consent_instruction"] = new()
        {
            ["en"] = "Do you consent to sending anonymous error reports to help us improve the product?",
            ["it"] = "Acconsenti a inviare segnalazioni di errore anonime per aiutarci a migliorare il prodotto?",
        },
        ["telemetry.consent_body"] = new()
        {
            ["en"] = "When a command fails, RevitCortex 2026 can send an anonymous error report: tool name, error type, versions, timing. Never sent: model names, file paths, parameter values, user or machine names.\n\nConsent is optional and you can change it anytime in Settings > General.",
            ["it"] = "Quando un comando fallisce, RevitCortex 2026 può inviare una segnalazione di errore anonima: nome del tool, tipo di errore, versioni, tempi. Mai inviati: nomi dei modelli, percorsi file, valori dei parametri, nomi utente o macchina.\n\nIl consenso è facoltativo e puoi modificarlo in qualsiasi momento da Impostazioni > Generale.",
        },
        ["telemetry.consent_enable"] = new()
        {
            ["en"] = "Enable error telemetry",
            ["it"] = "Attiva la telemetria errori",
        },
        ["telemetry.consent_decline"] = new()
        {
            ["en"] = "Keep it disabled",
            ["it"] = "Lascia disattivata",
        },
        ["telemetry.settings_toggle"] = new()
        {
            ["en"] = "Send anonymous error telemetry (no model data)",
            ["it"] = "Invia telemetria errori anonima (nessun dato del modello)",
        },
    };
}
