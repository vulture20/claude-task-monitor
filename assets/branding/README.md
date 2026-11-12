# Logo und Icon

Schlichtes Symbol: drei Prozesszeilen mit Status-Punkt und CPU-Balken, so wie sie im Fenster des Tools aussehen.
Alle Farben stammen aus `src/ClaudeTaskMonitor/App.xaml`:

| Farbe | Ressource | Verwendung |
|---|---|---|
| `#1B1D21` | WindowBackground | Hintergrund |
| `#30343A` | CardBorder / BarTrack | Rahmen, Balkenspur |
| `#D97757` | Accent | CPU-Balken |
| `#4CC38A` | StatusBusy | Punkt oben |
| `#E5A54B` | StatusWaiting | Punkt Mitte |
| `#8A8F98` | StatusIdle | Punkt unten |

## Dateien

- `logo.svg` – Symbol (Master, ab 40 px)
- `logo-horizontal.svg` – Symbol mit Schriftzug (für README, About-Dialog)
- `icon-32.svg` – vereinfachte, pixelgenaue Variante für 24–32 px
- `icon-16.svg` – Minimalvariante für 16–20 px (nur Balken)
- `app.ico` – Windows-Icon mit 16, 20, 24, 32, 40, 48, 64, 128, 256 px
- `png/` – PNG-Exporte (16–512 px) und `logo-horizontal.png`
- `build_icons.py` – erzeugt PNGs und `app.ico` neu aus den SVGs

## Einbindung

```xml
<!-- ClaudeTaskMonitor.csproj -->
<PropertyGroup>
  <ApplicationIcon>..\..\assets\branding\app.ico</ApplicationIcon>
</PropertyGroup>
<ItemGroup>
  <Resource Include="..\..\assets\branding\app.ico" Link="app.ico" />
</ItemGroup>
```

```xml
<!-- MainWindow.xaml -->
<Window ... Icon="app.ico">
```
