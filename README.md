# Claude Task Monitor

Ein kleines Windows-Tool, das immer im Vordergrund bleibt (Stay-on-top). Es zeigt die Prozesse, die Claude gerade angestoßen hat, mit ihrer CPU-Last an. Erfasst werden die Claude-Desktop-App (auch Cowork und Remote Control) und Claude Code. Die Prozesse sind nach Claude-Sitzung gruppiert. Ziel ist, auf einen Blick zu sehen, welcher Claude-Thread gerade welche Last erzeugt, etwa einen Build, Tests oder ein hängendes Skript.

## Stand

- WPF-Fenster (420 × 500, immer im Vordergrund, dunkles Design).
- Die Sitzungen sind nach Claude-Projekt gruppiert. Sitzungen ohne Projekt stehen unter „Ohne Projekt“.
- Jede Sitzung hat eine Karte. Zeile 1 zeigt die Kurz-Zusammenfassung des letzten Zugs aus der Desktop-App, ersatzweise den Titel oder den Sitzungsnamen. Zeile 2 zeigt Status, Thread-Kurzform und Prozesszahl. Rechts stehen CPU-Last und RAM.
- Ein Doppelklick auf eine Karte öffnet den Thread im Browser (`https://claude.ai/code/project/<chan>?thread=<cmsg>`).
- Aufgeklappt zeigt die Karte den Prozessbaum mit CPU-Last und RAM je Prozess. Prozesse, deren Elternprozess beendet ist, bleiben als „verwaist“ bei ihrer Sitzung.
- Aktualisierung jede Sekunde. Die CPU-Last ist auf alle Kerne bezogen (100 % = alle Kerne voll ausgelastet).

## So werden Claude-Prozesse erkannt

1. **Sitzungsdateien von Claude Code:** `%USERPROFILE%\.claude\sessions\<pid>.json`. Eine Sitzung zählt nur, wenn der Prozess noch läuft und `procStart` genau seiner Startzeit entspricht. Liegengebliebene Dateien und wiederverwendete PIDs fallen so heraus.
2. **Rückfall:** `claude.exe`-Kindprozesse der Desktop-App (`WindowsApps\Claude_*`), deren Pfad unter `Claude-Data\…\claude-code\` liegt.
3. **Nachfahren:** Ein Snapshot aller Prozesse pro Durchlauf (`CreateToolhelp32Snapshot`). Die Nachfahren werden über die Eltern-PID verfolgt. Kinder, die älter sind als ihr Elternprozess, werden verworfen, weil sonst wiederverwendete PIDs falsch zugeordnet würden.

4. **Verwaiste Prozesse:** Ein Prozess, der im letzten Durchlauf zu einer Sitzung gehörte, bleibt dort, auch wenn sein Elternprozess endet. Die Merkliste prüft dafür PID und Startzeit. Prozesse mit `--sponsor <pid>` (z. B. `UnrealTraceServer.exe`) kommen zu der Sitzung, zu der der Sponsor gehört oder in den letzten 10 Minuten gehörte. Ist die Sitzung selbst beendet, erscheint sie als „beendet“, solange solche Prozesse laufen.

## Zuordnung zu Projekt und Thread

| Quelle | Inhalt | Wann gelesen |
|---|---|---|
| `.claude\sessions\<pid>.json` | `sessionId`, `hostSessionId`, Name, Status | bei Änderung der Datei |
| `%LOCALAPPDATA%\Claude-Data\claude_*\claude-code-sessions\*\*\<hostSessionId>.json` (bzw. `%APPDATA%\Claude\…`) | Titel, `postTurnSummary.status_detail`, Ordner, letzte Aktivität | Zeitstempel höchstens alle 5 s prüfen, nur bei Änderung neu lesen |
| `.claude\projects\*\<sessionId>.jsonl` (Transkript) | nur Projekt-ID, Projektname und Thread-ID | die ersten Datensätze (höchstens 200 Zeilen bzw. 2 MB), bis alle drei gefunden sind |

Aus dem Transkript werden per Regex nur drei Muster ausgewertet: die Zeile `[project thread](https://claude.ai/code/project/chan_…?thread=cmsg_…)` und `Project: "…" (id: chan_…)` aus dem System-Prompt sowie ersatzweise `<project id="chan_…">` und `<thread ts="cmsg_…">` aus der ersten Nachricht. Andere Inhalte werden weder gespeichert noch angezeigt.

Diese Dateien und Pfade sind nicht offiziell dokumentiert und können sich mit Updates von Claude ändern.

## Bauen und starten

Voraussetzung: .NET 10 SDK unter Windows.

```bash
dotnet build ClaudeTaskMonitor.sln -c Release
```

```bash
src/ClaudeTaskMonitor/bin/Release/net10.0-windows/ClaudeTaskMonitor.exe
```

## Aufbau

| Ordner | Inhalt |
|---|---|
| `src/ClaudeTaskMonitor/Native` | Win32-Aufrufe (Toolhelp, Prozesszeiten, Speicher, dunkle Titelleiste) |
| `src/ClaudeTaskMonitor/Models` | Datentypen (Sitzungsdatei, Prozess, Sitzungsgruppe) |
| `src/ClaudeTaskMonitor/Services` | `SessionRegistry` (Sitzungsdateien), `ProcessScanner` (Erkennung, Baum, CPU-Last, Waisen), `SessionMetadataResolver` (Titel, Projekt, Thread), `MonitorService` (ein Durchlauf) |
| `src/ClaudeTaskMonitor/ViewModels` | MVVM ohne externe Pakete |

## Lizenz

GNU Affero General Public License v3.0, siehe [LICENSE](LICENSE).
