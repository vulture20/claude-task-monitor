# Claude Task Monitor

Ein kleines Windows-Tool, das immer im Vordergrund bleibt (Stay-on-top). Es zeigt die Prozesse, die Claude gerade angestoßen hat, mit ihrer CPU-Last an. Erfasst werden die Claude-Desktop-App (auch Cowork und Remote Control) und Claude Code. Die Prozesse sind nach Claude-Projekt und Sitzung gruppiert. So sieht man auf einen Blick, welcher Claude-Thread gerade welche Last erzeugt, etwa einen Build, Tests oder ein hängendes Skript.

## Installation

Unter [Releases](../../releases) liegt `ClaudeTaskMonitor.exe` als einzelne Datei. Sie braucht die **.NET 10 Desktop Runtime (x64)**. Eine Installation ist nicht nötig: einfach die exe starten.

## Bedienung

- **Fenster:** schmal, immer im Vordergrund, frei verschieb- und skalierbar.
- **Projekte:** Die Sitzungen sind nach Claude-Projekt gruppiert. Sitzungen ohne Projekt stehen unter „Ohne Projekt“.
- **Karte je Sitzung:**
  - Zeile 1 zeigt die Kurz-Zusammenfassung des letzten Zugs, ersatzweise den Titel oder den Sitzungsnamen.
  - Zeile 2 zeigt Status (arbeitet / bereit / wartet / beendet), die Thread-Kurzform und die Prozesszahl.
  - Rechts stehen CPU-Last und RAM der ganzen Sitzung.
  - Beim Überfahren mit der Maus erscheinen Details wie Ordner, Sitzungs-ID und letzte Aktivität.
- **Klick** auf eine Karte klappt den Prozessbaum mit CPU-Last und RAM je Prozess auf. Prozesse, deren Elternprozess beendet ist, bleiben als „verwaist“ bei ihrer Sitzung.
- **Doppelklick** auf eine Karte öffnet den Thread im Browser (`https://claude.ai/code/project/<chan>?thread=<cmsg>`). Das geht nur, wenn Projekt und Thread bekannt sind.
- **Rechtsklick** öffnet das Kontextmenü mit diesen Einträgen:
  - Thread im Browser öffnen
  - Thread-Link kopieren
  - Immer im Vordergrund an/aus
  - Aktualisieren alle 1 / 2 / 5 Sekunden (Standard: 2 s)
  - Beenden
- **Minimiert** tastet das Tool nicht ab und braucht keine CPU.
- Die CPU-Last ist auf alle Kerne bezogen: 100 % heißt, alle Kerne sind voll ausgelastet.

Position, Größe, „Immer im Vordergrund“ und das Intervall werden in `%APPDATA%\ClaudeTaskMonitor\settings.json` gespeichert.

## Funktionsweise

Das Tool liest nur lokale Dateien und die Prozessliste von Windows. Es schreibt nichts außer seiner Einstellungsdatei und sendet nichts ins Netz.

> **Hinweis:** Die folgenden Dateien und Pfade von Claude sind **nicht offiziell dokumentiert**. Ihr Aufbau kann sich mit jedem Update von Claude Code oder der Desktop-App ändern. Dann fehlen im Tool Angaben wie Titel oder Projekt, die Prozessanzeige selbst funktioniert weiter.

### Claude-Prozesse erkennen

1. **Sitzungsdateien von Claude Code:** `%USERPROFILE%\.claude\sessions\<pid>.json` (bzw. `CLAUDE_CONFIG_DIR`). Eine Sitzung zählt nur, wenn der Prozess noch läuft und `procStart` genau seiner Startzeit entspricht. Liegengebliebene Dateien und wiederverwendete PIDs fallen so heraus.
2. **Rückfall:** `claude.exe`-Kindprozesse der Desktop-App (`WindowsApps\Claude_*`), deren Pfad unter `Claude-Data\…\claude-code\` liegt.
3. **Nachfahren:** Pro Durchlauf liest ein einziger Aufruf von `NtQuerySystemInformation` alle Prozesse mit Eltern-PID, Startzeit, CPU-Zeiten und Arbeitsspeicher. Die Nachfahren werden über die Eltern-PID verfolgt. Kinder, die älter sind als ihr Elternprozess, werden verworfen, sonst würden wiederverwendete PIDs falsch zugeordnet.
4. **Verwaiste Prozesse:** Ein Prozess, der im letzten Durchlauf zu einer Sitzung gehörte, bleibt dort, auch wenn sein Elternprozess endet. Die Merkliste prüft dafür PID und Startzeit. Prozesse mit `--sponsor <pid>` (z. B. `UnrealTraceServer.exe`) kommen zu der Sitzung, zu der der Sponsor gehört oder in den letzten 10 Minuten gehörte. Ist die Sitzung selbst beendet, erscheint sie als „beendet“, solange solche Prozesse laufen.

Programmpfade (nur bei `claude.exe`) und Kommandozeilen (nur bei verwaisten Prozessen) werden je Prozess einmal gelesen und zwischengespeichert.

### Projekt und Thread zuordnen

| Quelle | Inhalt | Wann gelesen |
|---|---|---|
| `.claude\sessions\<pid>.json` | `sessionId`, `hostSessionId`, Name, Status | bei Änderung der Datei |
| `%LOCALAPPDATA%\Claude-Data\claude_*\claude-code-sessions\*\*\<hostSessionId>.json` (bzw. `%APPDATA%\Claude\…`) | Titel, `postTurnSummary.status_detail`, Ordner, letzte Aktivität | Zeitstempel höchstens alle 5 s prüfen, nur bei Änderung neu lesen |
| `.claude\projects\*\<sessionId>.jsonl` (Transkript) | nur Projekt-ID, Projektname und Thread-ID | die ersten Datensätze (höchstens 200 Zeilen bzw. 2 MB), bis alle drei gefunden sind |

Aus dem Transkript werden per Regex nur wenige Muster ausgewertet: die Zeile `[project thread](https://claude.ai/code/project/chan_…?thread=cmsg_…)` und `Project: "…" (id: chan_…)` aus dem System-Prompt, ersatzweise `<project id="chan_…">` und `<thread ts="cmsg_…">` aus der ersten Nachricht. Andere Inhalte werden weder gespeichert noch angezeigt.

### Eigenlast

Gemessen auf einem Notebook mit 8 Kernen und rund 4.600 Threads im System:

- Ein Durchlauf kostet etwa 20–25 ms CPU.
- Nach dem Start ist die Last kurz höher, weil .NET häufig genutzte Methoden im Hintergrund nachoptimiert. Danach liegt sie im einstelligen ms-pro-Sekunde-Bereich.
- Tooltips werden absichtlich erst beim Überfahren gesetzt. Dauerhaft gebundene Tooltips haben bei aktiven UI-Automation-Clients (z. B. Bedienhilfen oder Automatisierungswerkzeugen) fast einen ganzen CPU-Kern gekostet.

## Bauen

Voraussetzung: .NET 10 SDK unter Windows.

```bash
dotnet build ClaudeTaskMonitor.sln -c Release
```

Einzelne exe, framework-abhängig (~0,3 MB, braucht die .NET 10 Desktop Runtime):

```bash
dotnet publish src/ClaudeTaskMonitor/ClaudeTaskMonitor.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Einzelne exe mit eingebauter Runtime (~133 MB, läuft ohne installiertes .NET):

```bash
dotnet publish src/ClaudeTaskMonitor/ClaudeTaskMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/self-contained
```

Ein Tag `v*` (z. B. `v0.4.0`) startet den Workflow `.github/workflows/release.yml`. Er baut die framework-abhängige exe und hängt sie an ein GitHub-Release.

## Aufbau

| Ordner | Inhalt |
|---|---|
| `src/ClaudeTaskMonitor/Native` | Win32-/NT-Aufrufe (Prozessliste, Pfad, Kommandozeile, dunkle Titelleiste) |
| `src/ClaudeTaskMonitor/Models` | Datentypen (Sitzungsdatei, Prozess, Sitzungsgruppe, Metadaten) |
| `src/ClaudeTaskMonitor/Services` | `ProcessSnapshot` (Prozessliste), `SessionRegistry` (Sitzungsdateien), `ProcessScanner` (Erkennung, Baum, CPU-Last, Waisen), `SessionMetadataResolver` (Titel, Projekt, Thread), `MonitorService` (ein Durchlauf), `AppSettings` (Einstellungen) |
| `src/ClaudeTaskMonitor/ViewModels` | MVVM ohne externe Pakete |

## Lizenz

GNU Affero General Public License v3.0, siehe [LICENSE](LICENSE).
