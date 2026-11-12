using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace ClaudeTaskMonitor.Localization;

/// <summary>Gewählte Oberflächensprache. <see cref="Auto"/> folgt der Windows-Anzeigesprache.</summary>
public enum UiLanguage
{
    Auto,
    German,
    English,
}

/// <summary>
/// Oberflächentexte auf Deutsch und Englisch. In XAML per Indexer gebunden
/// (<c>{Binding [Key], Source={x:Static loc:Loc.Instance}}</c>), damit ein Sprachwechsel sofort greift.
/// Bewusst ohne .resx-Satellitenassemblies, damit die Auslieferung eine einzelne exe bleibt.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    // Windows-Anzeigesprache beim Start, bevor die Thread-Kulturen umgestellt werden.
    // Muss vor Instance stehen: statische Felder werden in Reihenfolge der Datei initialisiert.
    private static readonly CultureInfo SystemUiCulture = CultureInfo.CurrentUICulture;
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentCulture;

    public static Loc Instance { get; } = new();

    private UiLanguage _setting = UiLanguage.Auto;

    private Loc()
    {
        IsGerman = ResolveGerman(_setting);
        Culture = CultureFor(IsGerman);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Nach jedem tatsächlichen Wechsel der angezeigten Sprache.</summary>
    public event EventHandler? LanguageChanged;

    public UiLanguage Setting => _setting;

    /// <summary>true, wenn deutsche Texte angezeigt werden.</summary>
    public bool IsGerman { get; private set; }

    /// <summary>Zahlen- und Datumsformat passend zur angezeigten Sprache.</summary>
    public CultureInfo Culture { get; private set; }

    public string this[string key] => Get(key);

    public static string T(string key) => Instance.Get(key);

    public static string F(string key, params object?[] args) => string.Format(Instance.Culture, Instance.Get(key), args);

    /// <summary>Stellt Texte und Zahlenformat um; gebundene Texte und <see cref="LanguageChanged"/> folgen sofort.</summary>
    public void SetLanguage(UiLanguage setting)
    {
        _setting = setting;
        var changed = ResolveGerman(setting) != IsGerman;
        IsGerman = ResolveGerman(setting);
        Culture = CultureFor(IsGerman);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = Culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Setting)));
        if (!changed)
            return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGerman)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Wandelt "auto", "de" oder "en" (auch "deutsch", "english", "de-AT" …) um; sonst null.</summary>
    public static UiLanguage? Parse(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(v))
            return null;
        if (v is "auto" or "automatic" or "automatisch" or "system")
            return UiLanguage.Auto;
        if (v is "de" or "deu" or "ger" or "german" or "deutsch" || v.StartsWith("de-", StringComparison.Ordinal))
            return UiLanguage.German;
        if (v is "en" or "eng" or "english" or "englisch" || v.StartsWith("en-", StringComparison.Ordinal))
            return UiLanguage.English;
        return null;
    }

    public static string ToSettingValue(UiLanguage language) => language switch
    {
        UiLanguage.German => "de",
        UiLanguage.English => "en",
        _ => "auto",
    };

    private static bool ResolveGerman(UiLanguage setting) => setting switch
    {
        UiLanguage.German => true,
        UiLanguage.English => false,
        _ => SystemUiCulture.TwoLetterISOLanguageName == "de",
    };

    /// <summary>Behält das Windows-Format, wenn es zur Sprache passt (z. B. de-CH), sonst de-DE bzw. en-US.</summary>
    private static CultureInfo CultureFor(bool german)
    {
        var lang = german ? "de" : "en";
        if (SystemCulture.TwoLetterISOLanguageName == lang)
            return SystemCulture;
        if (SystemUiCulture.TwoLetterISOLanguageName == lang && !SystemUiCulture.IsNeutralCulture)
            return SystemUiCulture;
        return CultureInfo.GetCultureInfo(german ? "de-DE" : "en-US");
    }

    private string Get(string key)
    {
        if (!Texts.TryGetValue(key, out var pair))
            return key;
        return IsGerman ? pair.De : pair.En;
    }

    private static readonly Dictionary<string, (string De, string En)> Texts = new()
    {
        // Kontextmenü
        ["MenuOpenThread"] = ("Thread im Browser öffnen", "Open thread in browser"),
        ["MenuCopyThread"] = ("Thread-Link kopieren", "Copy thread link"),
        ["MenuTopmost"] = ("Immer im Vordergrund", "Always on top"),
        ["MenuInterval"] = ("Aktualisieren alle", "Refresh every"),
        ["MenuInterval1"] = ("1 Sekunde", "1 second"),
        ["MenuInterval2"] = ("2 Sekunden", "2 seconds"),
        ["MenuInterval5"] = ("5 Sekunden", "5 seconds"),
        ["MenuLanguage"] = ("Sprache", "Language"),
        ["MenuLanguageAuto"] = ("Automatisch (Windows)", "Automatic (Windows)"),
        ["MenuExit"] = ("Beenden", "Exit"),

        // Fenster
        ["ColumnProcess"] = ("Prozess", "Process"),
        ["ColumnPid"] = ("PID", "PID"),
        ["ColumnCpu"] = ("CPU", "CPU"),
        ["ColumnRam"] = ("RAM", "RAM"),
        ["Orphan"] = ("verwaist", "orphaned"),
        ["OrphanToolTip"] = ("Elternprozess beendet; über die Merkliste oder --sponsor dieser Sitzung zugeordnet",
                             "Parent process has exited; assigned to this session via the tracking list or --sponsor"),
        ["Empty"] = ("Keine laufenden Claude-Sitzungen gefunden.", "No running Claude sessions found."),
        ["Total"] = ("Gesamt ", "Total "),
        ["Searching"] = ("Suche Claude-Prozesse …", "Looking for Claude processes …"),

        // Zusammenfassung und Abschnitte
        ["SummaryOne"] = ("1 Sitzung · {0} Prozesse", "1 session · {0} processes"),
        ["SummaryMany"] = ("{0} Sitzungen · {1} Prozesse", "{0} sessions · {1} processes"),
        ["NoProject"] = ("Ohne Projekt", "No project"),
        ["ProjectFallback"] = ("Projekt {0}", "Project {0}"),
        ["ProcessOne"] = ("1 Prozess", "1 process"),
        ["ProcessMany"] = ("{0} Prozesse", "{0} processes"),

        // Status
        ["StatusBusy"] = ("arbeitet", "working"),
        ["StatusIdle"] = ("bereit", "ready"),
        ["StatusWaiting"] = ("wartet", "waiting"),
        ["StatusWaitingFor"] = ("wartet: {0}", "waiting: {0}"),
        ["StatusUnknown"] = ("ohne Sitzungsdatei", "no session file"),
        ["StatusEnded"] = ("beendet", "ended"),

        // Tooltip einer Sitzung
        ["TipTitle"] = ("Titel: {0}", "Title: {0}"),
        ["TipSession"] = ("Sitzung: {0} (PID {1})", "Session: {0} (PID {1})"),
        ["TipProject"] = ("Projekt: {0}", "Project: {0}"),
        ["TipThread"] = ("Thread: {0}", "Thread: {0}"),
        ["TipSessionId"] = ("Sitzungs-ID: {0}", "Session ID: {0}"),
        ["TipFolder"] = ("Ordner: {0}", "Folder: {0}"),
        ["TipLastActivity"] = ("Letzte Aktivität: {0:g}", "Last activity: {0:g}"),
        ["TipEntrypoint"] = ("Gestartet über: {0}", "Started via: {0}"),
        ["TipNoSessionFile"] = ("Keine Sitzungsdatei gefunden (über die Desktop-App erkannt)",
                                "No session file found (detected via the desktop app)"),
        ["TipEnded"] = ("Sitzung beendet, es laufen nur noch verwaiste Prozesse",
                        "Session has ended, only orphaned processes are still running"),
        ["TipDoubleClick"] = ("Doppelklick: Thread im Browser öffnen", "Double-click: open thread in browser"),

        // Demo-Daten (--demo)
        ["Demo1Summary"] = ("Baut die Firmware und führt die Unit-Tests aus", "Building the firmware and running the unit tests"),
        ["Demo2Summary"] = ("Möchte die Changelog-Datei aktualisieren", "Wants to update the changelog file"),
        ["Demo2WaitingFor"] = ("Freigabe", "approval"),
        ["Demo3Summary"] = ("Startet den Dev-Server und prüft die Galerie-Ansicht", "Starting the dev server and checking the gallery view"),
        ["Demo4Summary"] = ("Pull Request für die Upload-Grenze erstellt", "Opened a pull request for the upload limit"),
        ["Demo5Summary"] = ("Datenimport läuft im Hintergrund weiter", "Data import keeps running in the background"),
        ["DemoProjectWeather"] = ("Wetterstation", "Weather Station"),
        ["DemoProjectPhotos"] = ("Fotoalbum-Web", "Photo Album Web"),
    };
}
