# Changelog

Alle nennenswerten Änderungen an Claude Task Monitor. Das Format folgt [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), die Versionsnummern folgen [Semantic Versioning](https://semver.org/lang/de/).

All notable changes to Claude Task Monitor. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [1.1.0] – 2026-11-13

### Neu

- Wählbare Transparenz des ganzen Fensters von 100 % (Standard, keine Transparenz) bis 30 % in 10er-Schritten ([#12](https://github.com/vulture20/claude-task-monitor/issues/12)):
  - Untermenü „Transparenz“ im Kontextmenü.
  - Strg + Mausrad über dem Fenster ändert die Deckkraft schrittweise.
  - Solange die Maus über dem Fenster ist, wird es voll deckend angezeigt.
  - Klicks gehen nicht durch das Fenster; Rahmen und Titelleiste bleiben erhalten.
  - Gespeichert als `Opacity` in `settings.json`; `--opacity 30-100` legt den Wert für einen Start fest.

### Added

- Selectable transparency of the whole window from 100% (default, no transparency) down to 30% in steps of 10 ([#12](https://github.com/vulture20/claude-task-monitor/issues/12)):
  - "Transparency" submenu in the context menu.
  - Ctrl + mouse wheel over the window changes the opacity step by step.
  - While the mouse is over the window, it is shown fully opaque.
  - Clicks do not pass through the window; frame and title bar are kept.
  - Saved as `Opacity` in `settings.json`; `--opacity 30-100` sets the value for one launch.

## [1.0.0] – 2026-11-12

Erstes Release. / First release.

### Neu

- Kleines Stay-on-top-Fenster mit allen Prozessen, die Claude (Desktop-App mit Cowork und Remote Control sowie Claude Code) gestartet hat, samt CPU-Last und RAM je Prozess und je Sitzung.
- Zuordnung jeder Sitzung zu Claude-Projekt und Thread. Die Karten sind nach Projekt gruppiert und zeigen die Kurz-Zusammenfassung der aktuellen Arbeit, den Status und die Thread-Kurzform.
- Doppelklick bzw. Kontextmenü öffnet den Thread im Browser oder kopiert den Link.
- Aufklappbarer Prozessbaum je Sitzung. Prozesse, die ihren Elternprozess überleben (z. B. `UnrealTraceServer.exe`), bleiben als „verwaist“ bei ihrer Sitzung.
- Geringe Eigenlast: Prozessliste über `NtQuerySystemInformation`, Aktualisierung alle 1, 2 oder 5 Sekunden (Standard 2 s), keine Abtastung bei minimiertem Fenster.
- Einstellungen (Position, Größe, Immer im Vordergrund, Intervall, Sprache) in `%APPDATA%\ClaudeTaskMonitor\settings.json`.
- Oberfläche auf Deutsch und Englisch mit automatischer Sprachwahl, Umschaltung im Kontextmenü und `--lang de|en|auto`.
- Demo-Modus `--demo` mit erfundenen Daten für Screenshots.
- Eigenes App-Icon, dunkles Design.
- Zwei Pakete je Release: framework-abhängig (braucht die .NET 10 Desktop Runtime) und self-contained (ohne Voraussetzungen), jeweils als ZIP mit README, CHANGELOG und LICENSE, dazu `SHA256SUMS.txt`.

### Added

- Small stay-on-top window listing every process started by Claude (desktop app including Cowork and Remote Control, and Claude Code), with CPU load and RAM per process and per session.
- Each session is matched to its Claude project and thread. Cards are grouped by project and show the short summary of the current work, the status and the short thread ID.
- Double-click or the context menu opens the thread in the browser or copies its link.
- Expandable process tree per session. Processes that outlive their parent (e.g. `UnrealTraceServer.exe`) stay with their session as "orphaned".
- Low overhead: process list via `NtQuerySystemInformation`, refresh every 1, 2 or 5 seconds (default 2 s), no sampling while minimized.
- Settings (position, size, always on top, interval, language) in `%APPDATA%\ClaudeTaskMonitor\settings.json`.
- German and English UI with automatic language selection, a switch in the context menu and `--lang de|en|auto`.
- Demo mode `--demo` with made-up data for screenshots.
- Custom app icon, dark design.
- Two packages per release: framework-dependent (requires the .NET 10 Desktop Runtime) and self-contained (no requirements), each as a ZIP with README, CHANGELOG and LICENSE, plus `SHA256SUMS.txt`.

[1.1.0]: https://github.com/vulture20/claude-task-monitor/releases/tag/v1.1.0
[1.0.0]: https://github.com/vulture20/claude-task-monitor/releases/tag/v1.0.0
