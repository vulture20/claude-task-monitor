# Claude Task Monitor

Ein kleines Windows-Tool, das immer im Vordergrund bleibt (Stay-on-top). Es zeigt die Prozesse, die Claude gerade angestoßen hat, mit ihrer CPU-Last an. Erfasst werden die Claude-Desktop-App (auch Cowork und Remote Control) und Claude Code. Die Prozesse sind nach Claude-Sitzung gruppiert. Ziel ist, auf einen Blick zu sehen, welcher Claude-Thread gerade welche Last erzeugt, etwa einen Build, Tests oder ein hängendes Skript.

## Stand

- WPF-Fenster (420 × 500, immer im Vordergrund, dunkles Design).
- Je Claude-Sitzung eine Gruppe mit Name und Status (arbeitet / bereit / wartet) sowie der summierten CPU-Last und dem Arbeitsspeicher. Aufgeklappt zeigt die Gruppe den Prozessbaum mit CPU-Last und RAM je Prozess.
- Aktualisierung jede Sekunde. Die CPU-Last ist auf alle Kerne bezogen (100 % = alle Kerne voll ausgelastet).
- Noch nicht umgesetzt: die Zuordnung zu Projekt und Thread aus den Transkripten und den Daten der Desktop-App.

## So werden Claude-Prozesse erkannt

1. **Sitzungsdateien von Claude Code:** `%USERPROFILE%\.claude\sessions\<pid>.json`. Eine Sitzung zählt nur, wenn der Prozess noch läuft und `procStart` genau seiner Startzeit entspricht. Liegengebliebene Dateien und wiederverwendete PIDs fallen so heraus.
2. **Rückfall:** `claude.exe`-Kindprozesse der Desktop-App (`WindowsApps\Claude_*`), deren Pfad unter `Claude-Data\…\claude-code\` liegt.
3. **Nachfahren:** Ein Snapshot aller Prozesse pro Durchlauf (`CreateToolhelp32Snapshot`). Die Nachfahren werden über die Eltern-PID verfolgt. Kinder, die älter sind als ihr Elternprozess, werden verworfen, weil sonst wiederverwendete PIDs falsch zugeordnet würden.

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
| `src/ClaudeTaskMonitor/Services` | `SessionRegistry` (Sitzungsdateien), `ProcessScanner` (Erkennung, Baum, CPU-Last) |
| `src/ClaudeTaskMonitor/ViewModels` | MVVM ohne externe Pakete |

## Lizenz

GNU Affero General Public License v3.0, siehe [LICENSE](LICENSE).
