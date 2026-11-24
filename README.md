# 🎬 MovieWorker

**MovieWorker** ist ein Videoschnittprogramm für Windows, geschrieben in C# mit .NET 8 – das Schwesterprojekt
von [MusicWorker](https://github.com/PSanne/MusicWorker).

Es ist auf Material der **Canon EOS R6 Mark II** und der **Canon EOS 70D** zugeschnitten und soll daraus
professionelle Videos machen: vom Import der Speicherkarte über den Schnitt bis zum Export als
H.264/H.265 für YouTube oder als ProRes/DNxHR-Master.

> **Stand: Sprint 4 von 9 abgeschlossen.** Import, Medienpool, Schnitt, Wiedergabe und Proxys funktionieren.
> Als Nächstes folgt der Export-Dialog (dann ist Version 0.5 die erste voll nutzbare Version). Den kompletten Plan findest du in
> **[docs/SPRINTS.md](docs/SPRINTS.md)**.

## Was MovieWorker über deine Kameras weiß

| | Canon EOS R6 Mark II | Canon EOS 70D |
|---|---|---|
| **Container** | MP4 | MOV |
| **Video** | 4K UHD bis 59,94p, Full HD bis 180p (High Frame Rate) · H.264 8 Bit oder H.265 10 Bit 4:2:2 | Full HD bis 30p, 720p bis 60p · H.264 8 Bit (ALL-I/IPB) |
| **Ton** | AAC oder LPCM | LPCM 48 kHz 16 Bit |
| **Besonderheiten** | Canon Log 3, HDR PQ, Zeitlupe | Aufteilung in 4-GB-Dateien, .THM-Vorschaudateien |
| **Was MovieWorker tut** | erkennt Log/HDR/HFR, schlägt LUT bzw. Tone-Mapping vor, Zeitlupe per „Konformieren“, Proxys für flüssigen 4K-HEVC-Schnitt | fügt geteilte Aufnahmen automatisch zusammen, überspringt .THM-Dateien |

Die Kamera wird aus den Dateimerkmalen erkannt (Canon speichert das Modell in eigenen Datenblöcken).
Ist die Erkennung unsicher, lässt sie sich von Hand festlegen.

## Bedienung

| Bereich | Funktionen |
|---|---|
| **Import** | *Speicherkarte importieren*: erkennt Karten mit DCIM-Ordner, kopiert die Videos nach `Zielordner\Datum\Kamera` und prüft jede Kopie per SHA-256 · *Dateien importieren* (Strg+I) oder Dateien/Ordner ins Fenster ziehen · geteilte 4-GB-Aufnahmen werden automatisch zu einem Clip |
| **Medienpool** | Vorschaubild, Länge, Kamera, Kennzeichen für Canon Log 3 / HDR / Zeitlupe · Inspektor mit technischen Daten, Hinweisen und Kamera-Tipps · Kamera von Hand festlegen |
| **Timeline** | Clips per Doppelklick oder Ziehen aus dem Medienpool einfügen · verschieben (auch auf andere Spuren), an den Rändern trimmen · Einrasten an Schnittpunkten, Abspielkopf und Marken · Vorschaubilder und Ton-Wellenform in den Clips · Spuren hinzufügen und stummschalten |
| **Clip-Inspektor** | Position und Länge als Timecode · Geschwindigkeit (Zeitlupe/Zeitraffer) · „Als Zeitlupe abspielen“ für 150/180-fps-Clips · Lautstärke, Ton stumm, Ein-/Ausblenden |
| **Monitore** | *Quelle*: Aufnahme ansehen, In/Out setzen, Ausschnitt ans Ende oder am Abspielkopf einfügen · *Programm*: Timeline abspielen; im Stillstand exaktes Standbild mit LUT/HDR-Umrechnung wie im Export · J/K/L-Shuttle |
| **Proxys** | Für Aufnahmen, die Windows nicht flüssig abspielt (4K-HEVC 10 Bit 4:2:2, HDR, MOV mit LPCM), entsteht automatisch eine Vorschaudatei: 70D-Clips werden nur neu verpackt (Sekunden), R6-HEVC wird als 540p-H.264 kodiert, HDR dabei nach SDR umgerechnet · geteilte Aufnahmen ergeben einen Proxy · Menü *Wiedergabe → Vorschau mit Proxys* schaltet aufs Original um · der Export nutzt immer die Originale |
| **Projekte** | Neu, Öffnen, Speichern (`.mvproj`), zuletzt geöffnete Projekte · Projekteinstellungen (Auflösung, Bildrate, Drop-Frame) · der erste Videoclip bestimmt die Einstellungen eines leeren Projekts |

### Tastenkürzel

| Aktion | Taste |
|---|---|
| Abspielen / Pause | Leertaste (im Quellmonitor: dessen Wiedergabe) |
| Rückwärts / Stopp / Vorwärts | J / K / L (mehrfach drücken = schneller) |
| Bild vor / zurück | → / ← (mit Umschalt: 1 Sekunde) |
| Zum nächsten / vorigen Schnitt | ↓ / ↑ |
| Anfang / Ende | Pos1 / Ende |
| Am Abspielkopf teilen | Strg+B (oder Strg+K) |
| Clip löschen / Lücke schließen | Entf / Umschalt+Entf |
| In- / Out-Marke, Marken entfernen | I / O, Strg+Umschalt+X (im Quellmonitor: In/Out der Aufnahme) |
| Einrasten an/aus | N |
| Zoomen | Strg+Mausrad, + / −, Umschalt+Z = ganze Timeline |
| Waagerecht scrollen | Mausrad über der Timeline |
| Rückgängig / Wiederholen | Strg+Z / Strg+Y |
| Neu / Öffnen / Speichern | Strg+N / Strg+O / Strg+S |

## Funktionen des Kerns (Sprint 1)

| Bereich | Funktionen |
|---|---|
| **Zeit** | Bildraten als exakte Brüche (23,976 / 29,97 / 59,94 …) · SMPTE-Timecode inkl. Drop-Frame |
| **Analyse** | Codec, Bit-Tiefe, Farbunterabtastung, Kennlinie (Rec.709 / PQ / HLG / Canon Log 3), Ton, Aufnahmedatum |
| **Timeline** | Einfügen, Teilen, Löschen (auch mit Lücke schließen), Verschieben, Trimmen, Zeitlupe, Blenden, Rückgängig/Wiederholen (100 Schritte) |
| **Farbe** | Pro Clip: 3D-LUT (.cube) oder HDR→SDR-Tone-Mapping |
| **Export** | H.264 (Projektauflösung oder Full HD) · H.265 10 Bit · Apple ProRes 422 HQ · Avid DNxHR HQX – Ton AAC bzw. 24-Bit-PCM |
| **Projekte** | Speichern als `Name.mvproj` (JSON); die Originalvideos werden nie verändert |

## Loslegen

### Fertige Version herunterladen
Fertige Versionen gibt es unter **[Releases](https://github.com/PSanne/MovieWorker/releases/latest)**: ZIP herunterladen,
entpacken, `MovieWorker.exe` starten. Es muss weder .NET noch FFmpeg installiert sein (FFmpeg liegt im Ordner `ffmpeg`).

Zwischenstände aus jedem Push gibt es unter **Actions → „Build & Test“ → Lauf auswählen → Artifacts**
(nur angemeldet sichtbar). Jeder Lauf startet die App unter Windows im **Selbsttest**: Testclips im Stil beider
Kameras werden importiert, geschnitten, gespeichert und wieder geladen.

### Selbst bauen
Voraussetzungen: Windows 10/11, das [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
(oder Visual Studio 2022) und [FFmpeg](https://ffmpeg.org/download.html) (`ffmpeg.exe` und `ffprobe.exe`
im PATH oder im Ordner `ffmpeg` neben der `MovieWorker.exe`).

```powershell
git clone https://github.com/PSanne/MovieWorker.git
cd MovieWorker
dotnet run --project src/MovieWorker.App
```

Tests ausführen: `dotnet test`

## Projektstruktur

```
MovieWorker.sln
├─ src/
│  └─ MovieWorker.Core/          Plattformunabhängiger Kern (ohne UI, voll testbar)
│     ├─ Time/                   Bildraten (Brüche), SMPTE-Timecode
│     ├─ Media/                  Medieninfos, ffprobe-Auswertung
│     ├─ Camera/                 Canon-Profile, Kamera-Erkennung, 4-GB-Aufteilung, Kartenscan
│     ├─ Timeline/               Projekt, Spuren, Clips, Schnitt-Operationen, Rückgängig
│     ├─ Export/                 Export-Vorlagen, ffmpeg-Befehle (Export, Proxy, Vorschaubild)
│     ├─ Preview/                Proxy-Planung, Wiedergabe-Planung (was läuft wann)
│     ├─ Projects/               Projektdateien (.mvproj), Medienpool, Import
│     └─ Settings/               Einstellungen (%AppData%\MovieWorker)
│  └─ MovieWorker.App/           WPF-Anwendung (MVVM, CommunityToolkit.Mvvm)
│     ├─ Controls/               Timeline-Ansicht, Quell- und Programmmonitor
│     ├─ ViewModels/             Hauptfenster, Medienpool, Clip-Inspektor, Dialoge
│     ├─ Views/                  Karten-Import, Projekteinstellungen
│     ├─ Services/               Dialoge, Einstellungen, FFmpeg, Vorschaubilder, Wellenformen
│     └─ SmokeTest.cs            Selbsttest der Oberfläche (läuft in GitHub Actions unter Windows)
├─ tests/
│  └─ MovieWorker.Core.Tests/    xUnit-Tests für den Kern
├─ build/                       Skripte für GitHub Actions (FFmpeg holen, Testclips, Selbsttest)
└─ docs/
   └─ SPRINTS.md                 Sprint-Plan
```

**Architektur in Kürze:** Wie bei MusicWorker steckt die gesamte Logik im Kern, die Oberfläche (ab Sprint 2:
`MovieWorker.App`, WPF/MVVM) nur in der App. Die Videoverarbeitung übernimmt **FFmpeg**: Die Timeline wird in einen
einzigen ffmpeg-Aufruf übersetzt, jeder Clip wird per schnellem Seek nur so weit gelesen, wie er gebraucht wird.

## Entscheidungen

- **FFmpeg statt eigener Decoder.** Es liest alle Canon-Formate (auch 10-Bit-HEVC 4:2:2), bringt Profi-Codecs
  (ProRes, DNxHR) mit und beherrscht LUTs und Tone-Mapping. Es wird als eigenständige Datei mitgeliefert.
- **Timeline in Projektbildern, Quelle in Sekunden.** So lassen sich Clips mit anderer Bildrate (Zeitlupe mit
  150/180 fps, 70D mit 30p im 25p-Projekt) frei mischen und trotzdem bildgenau schneiden.
- **Rückgängig speichert den Schnittzustand.** Es werden nur Schnittdaten kopiert, nie Videos – robust und sparsam.
- **Speicherkarten werden kopiert, nicht direkt verwendet.** So kann die Karte zurück in die Kamera; die
  SHA-256-Prüfung stellt sicher, dass jede Kopie bitgenau stimmt, bevor die Karte formatiert wird.
- **Der Medienpool ist nicht Teil von Rückgängig.** Strg+Z nimmt Schnitte zurück, aber keine Importe – sonst
  würden importierte Aufnahmen beim Zurücknehmen eines Schnitts verschwinden.
- **Schnelle Reglerbewegungen ergeben einen einzigen Rückgängig-Schritt** (z. B. Lautstärke ziehen).
- **Vorschau: Uhr statt Player als Taktgeber.** Beim Abspielen gibt eine Stoppuhr die Zeit vor, der Windows-Player
  wird nachgeführt. So stimmt die Position auch bei Lücken, Spurwechseln und Dateien, die Windows nicht abspielen
  kann (dann zeigt der Monitor Standbilder). Im Stillstand rechnet ffmpeg ein exaktes Bild – mit derselben
  Farbumrechnung wie der Export.
- **Proxys nur, wo nötig.** MP4 mit H.264 spielt Windows direkt; 70D-MOVs werden nur neu verpackt, nur HEVC/10 Bit/HDR
  wird neu kodiert. Proxys liegen in `%LocalAppData%\MovieWorker\Proxys` und werden nie exportiert.
- **Canon Log 3 wird über die Bit-Tiefe erkannt:** Die R6 Mark II nimmt 10 Bit nur mit Canon Log 3 oder HDR PQ auf;
  HDR PQ ist in den Metadaten markiert, also ist 10 Bit ohne PQ-Kennzeichen Canon Log 3.

## Lizenz

Apache License 2.0 – siehe [LICENSE](LICENSE). FFmpeg ist ein eigenständiges Programm unter eigener Lizenz (LGPL/GPL).
