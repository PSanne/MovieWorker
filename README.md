# 🎬 MovieWorker

**MovieWorker** ist ein Videoschnittprogramm für Windows, geschrieben in C# mit .NET 8 – das Schwesterprojekt
von [MusicWorker](https://github.com/PSanne/MusicWorker).

Es ist auf Material der **Canon EOS R6 Mark II** und der **Canon EOS 70D** zugeschnitten und soll daraus
professionelle Videos machen: vom Import der Speicherkarte über den Schnitt bis zum Export als
H.264/H.265 für YouTube oder als ProRes/DNxHR-Master.

> **Stand: Sprint 1 von 9 abgeschlossen.** Der Kern (Erkennung, Timeline, Export) ist fertig und getestet,
> die Bedienoberfläche folgt ab Sprint 2. Den kompletten Plan findest du in **[docs/SPRINTS.md](docs/SPRINTS.md)**.

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

## Funktionen des Kerns (Sprint 1)

| Bereich | Funktionen |
|---|---|
| **Zeit** | Bildraten als exakte Brüche (23,976 / 29,97 / 59,94 …) · SMPTE-Timecode inkl. Drop-Frame |
| **Analyse** | Codec, Bit-Tiefe, Farbunterabtastung, Kennlinie (Rec.709 / PQ / HLG / Canon Log 3), Ton, Aufnahmedatum |
| **Timeline** | Einfügen, Teilen, Löschen (auch mit Lücke schließen), Verschieben, Trimmen, Zeitlupe, Blenden, Rückgängig/Wiederholen (100 Schritte) |
| **Farbe** | Pro Clip: 3D-LUT (.cube) oder HDR→SDR-Tone-Mapping |
| **Export** | H.264 (Projektauflösung oder Full HD) · H.265 10 Bit · Apple ProRes 422 HQ · Avid DNxHR HQX – Ton AAC bzw. 24-Bit-PCM |
| **Projekte** | Speichern als `Name.mvproj` (JSON); die Originalvideos werden nie verändert |

## Selbst bauen

Voraussetzungen: das [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
(oder Visual Studio 2022) und [FFmpeg](https://ffmpeg.org/download.html) (ab Sprint 2 im Download enthalten).

```powershell
git clone https://github.com/PSanne/MovieWorker.git
cd MovieWorker
dotnet test
```

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
│     └─ Projects/               Projektdateien (.mvproj)
├─ tests/
│  └─ MovieWorker.Core.Tests/    xUnit-Tests für den Kern
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
- **Canon Log 3 wird über die Bit-Tiefe erkannt:** Die R6 Mark II nimmt 10 Bit nur mit Canon Log 3 oder HDR PQ auf;
  HDR PQ ist in den Metadaten markiert, also ist 10 Bit ohne PQ-Kennzeichen Canon Log 3.

## Lizenz

Apache License 2.0 – siehe [LICENSE](LICENSE). FFmpeg ist ein eigenständiges Programm unter eigener Lizenz (LGPL/GPL).
