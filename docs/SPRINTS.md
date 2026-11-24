# MovieWorker – Sprint-Plan (16 Sprints)

Jeder Sprint ist ein in sich abgeschlossenes, testbares Paket. Am Ende steht jeweils eine lauffähige Version
mit Tests – und seit Sprint 2 ein **Selbsttest der echten Oberfläche unter Windows** (GitHub Actions).
Der Plan wurde nach Sprint 4 von 9 auf 16 Sprints erweitert (ohne 3D-Animation).

| Sprint | Thema | Status |
|---|---|---|
| 1 | Fundament & Kern | ✅ fertig |
| 2 | App-Grundgerüst & Import | ✅ fertig |
| 3 | Timeline-Oberfläche | ✅ fertig |
| 4 | Vorschau & Proxys | ✅ fertig |
| 5 | Ausgabe: Export-Dialog, Plattform-Vorlagen, Warteschlange (→ v0.5) | ✅ fertig |
| 6 | Medien-Arbeitsbereich: Ordner, Metadaten, Bild-Ton-Synchronisation | ✅ fertig |
| 7 | Schnellschnitt-Arbeitsbereich: Doppel-Timeline, Quellband, Einfügemodi | ✅ fertig |
| 8 | Profi-Schnitt: Trimm-Modi, Keyframes, Geschwindigkeitsrampen, verschachtelte Clips | ✅ fertig |
| 9 | Übergänge & Titel (eigenes Titelwerkzeug, animiert) | ✅ fertig |
| 10 | Mehrspur, Multicam & Mischmodi | ✅ fertig |
| 11 | Farbe I: LUTs, HDR, Farbräder, Scopes, Farbabgleich 70D ↔ R6 | ✅ fertig |
| 12 | Farbe II: Kurven, sekundäre Korrektur, Masken, Korrekturknoten, Tracking | ✅ fertig |
| 13 | Ton I: Mischpult, Effekte, Lautheit, Ducking, Rauschminderung | ✅ fertig |
| 14 | Ton II: ADR, Sprachaufnahme, Surround/räumlicher Ton | ✅ fertig |
| 15 | Effekte & Compositing: Keying, Masken, Tracking, Knoten-Editor | ✅ fertig |
| 16 | Feinschliff & Version 1.0 | ⏳ als Nächstes |

---

## Sprint 1 – Fundament & Kern ✅
- Repository, Projektstruktur (wie MusicWorker: `Core` + `App` + `Tests`), Build & Test per GitHub Actions
- Bildraten als exakte Brüche (23,976 / 29,97 / 59,94 …), SMPTE-Timecode inkl. Drop-Frame
- Medienanalyse über ffprobe (Codec, Bit-Tiefe, Farbunterabtastung, Kennlinie, Ton)
- **Canon-Kameraprofile** EOS R6 Mark II und EOS 70D, automatische Erkennung mit Hinweisen:
  Canon Log 3, HDR PQ, High-Frame-Rate-Zeitlupe, 4K-HEVC → Proxy-Empfehlung
- **4-GB-Dateiteilung** (EOS 70D) erkennen und als eine Aufnahme behandeln; .THM-Dateien überspringen
- Timeline-Kern: Einfügen, Teilen, Löschen (auch mit Lücke schließen), Verschieben, Trimmen,
  Geschwindigkeit/Zeitlupe, Blenden, Farbumrechnung pro Clip, Rückgängig/Wiederholen (100 Schritte)
- Projektdatei `.mvproj` (JSON)
- **Export-Engine**: Timeline → ein ffmpeg-Aufruf; Vorlagen H.264, H.264 Full HD, H.265 10 Bit,
  ProRes 422 HQ, DNxHR HQX; LUT (lut3d) und HDR→SDR-Tone-Mapping; Proxy- und Vorschaubild-Befehle
- 53 Tests, zusätzlich mit echtem ffmpeg an Testclips geprüft

## Sprint 2 – App-Grundgerüst & Import ✅
- WPF-Anwendung (.NET 8, MVVM) mit Hauptfenster, Menüs, Statuszeile
- ffmpeg finden (neben der .exe, im PATH oder in den Einstellungen); im Release-ZIP mitgeliefert
- **Speicherkarte/DCIM-Ordner importieren** (optional auf die Festplatte kopieren, mit Prüfsumme)
- Medienpool: Vorschaubilder, Kamera, Format, Hinweise (Log/HDR/Zeitlupe), Kamera von Hand korrigieren
- Projekt neu/öffnen/speichern, zuletzt geöffnete Projekte, Projekteinstellungen aus erstem Clip
- Release-Workflow: ZIP-Download wie bei MusicWorker

## Sprint 3 – Timeline-Oberfläche ✅
- Spuransicht mit Zeitlineal (Timecode), Abspielkopf, Zoom und Scrollen
- Clips ziehen (auch zwischen Spuren), Ränder trimmen, Teilen (Strg+B), Löschen/Lücke schließen
- Einrasten an Clipkanten und Abspielkopf, Vorschaubilder im Clip, Ton-Wellenform
- Tastenkürzel nach Branchenstandard: I/O, Pfeiltasten bildweise, Strg+B (J/K/L folgt mit der Wiedergabe in Sprint 4)
- Clip-Inspektor: Geschwindigkeit, Zeitlupe konformieren, Lautstärke, Blenden; Spuren hinzufügen und stummschalten

## Sprint 4 – Vorschau & Proxys ✅
- Quellmonitor mit In-/Out-Punkten und „In Timeline einfügen“
- Programmmonitor: Wiedergabe der Timeline, bildgenaues Scrubbing
- Proxys im Hintergrund erzeugen (4K-HEVC → 540p H.264), automatisch beim Import möglich
- Umschalten Proxy ↔ Original, Vorschau mit LUT/Tone-Mapping
- J/K/L-Shuttle (rückwärts, Stopp, vorwärts bis 8×), Leertaste spielt ab

## Sprint 5 – Ausgabe (→ Version 0.5) ✅
- Export-Dialog: Vorlage, Zielordner, Dateiname, Fortschritt, Restzeit, Abbrechen
- Plattform-Vorlagen: YouTube (4K/HD), Vimeo, Instagram/TikTok/Shorts (Hochformat 9:16), Master (ProRes, DNxHR), Archiv (H.265 10 Bit)
- Ganze Timeline oder nur In/Out-Bereich, Einzelbild als PNG
- Export-Warteschlange (mehrere Aufträge nacheinander)
- Prüfung vor dem Export: fehlende Dateien, fehlende LUT, gemischte Bildraten, nicht konformierte Zeitlupe, Hochskalierung

## Sprint 6 – Medien-Arbeitsbereich ✅
- Ordner (Bins) im Medienpool, Aufnahmen per Ziehen einsortieren
- Metadaten: Bewertung (Sterne), Schlagwörter, Notizen, Szene/Take; Suchen und Filtern
- Bild-Ton-Synchronisation: externen Ton (Rekorder) per Wellenform automatisch an den Kameraton anlegen

## Sprint 7 – Schnellschnitt-Arbeitsbereich ✅
- Doppel-Timeline: oben die ganze Timeline, unten der Ausschnitt um den Abspielkopf
- Quellband: alle Aufnahmen hintereinander wie ein langes Band durchsehen
- Einfügemodi: Einfügen (Insert), Überschreiben, Ersetzen, Anhängen, Oben platzieren

## Sprint 8 – Profi-Schnitt ✅
- Trimm-Modi: Ripple, Roll, Slip, Slide
- Keyframes für Position, Zoom, Drehung, Deckkraft, Lautstärke (weich, linear oder halten); Zoom unter 100 % fest (animiertes Verkleinern folgt mit Bild-in-Bild in Sprint 10)
- Geschwindigkeitsrampen (fließende Zeitlupe)
- Verschachtelte Clips (mehrere Clips zu einem zusammenfassen)

## Sprint 9 – Übergänge & Titel ✅
- Übergänge: Kreuzblende, Abblende Schwarz/Weiß, Wischblenden, Schieben, Kreis …
- Eigenes Titelwerkzeug: Texte, Bauchbinden, Abspann; animiert (Einblenden, Schieben, Tippen)

## Sprint 10 – Mehrspur, Multicam & Mischmodi ✅
- Mehrere Videospuren mit Bild-in-Bild, Deckkraft, Zuschneiden, Drehen
- Multicam: R6 Mark II und 70D synchron (per Ton) als Mehrkamera-Clip, Kamerawechsel live
- Mischmodi (Multiplizieren, Negativ multiplizieren, Überlagern …)

## Sprint 11 – Farbe I ✅
- LUT-Bibliothek (z. B. offizielle Canon-LUTs Canon Log 3 → BT.709), HDR PQ → SDR, HDR10-Export
- Farbräder (Lift/Gamma/Gain), Belichtung, Kontrast, Weißabgleich, Sättigung
- Messinstrumente: Waveform, Vektorskop, Histogramm
- Farbabgleich EOS 70D ↔ R6 Mark II, Korrekturen kopieren/einfügen

## Sprint 12 – Farbe II ✅
- Kurven (RGB und Luma)
- Sekundäre Korrektur: Farbbereich auswählen (Farbton/Sättigung/Helligkeit) und gezielt ändern
- Masken (Rechteck, Ellipse) mit weicher Kante, Korrekturknoten (seriell)
- Tracking: Maske folgt einem Objekt

## Sprint 13 – Ton I ✅
- Mischpult mit Spurfadern, Pegelanzeigen, Panorama, Stumm/Solo und Summe (Master) mit Limiter
- Effekte je Spur: Hochpass, Rauschminderung, 3-Band-EQ, Kompressor, Hall
- Lautheit messen und beim Export genau normalisieren (zwei Durchgänge; −14 LUFS YouTube, −16 Podcast, −23 EBU R128)
- Auto-Ducking (Musik leiser bei Sprache) per Sidechain-Kompressor

## Sprint 14 – Ton II ✅
- Sprachaufnahme (Voice-over) direkt in die Timeline – Mikrofon über ffmpeg/DirectShow, Vorlauf, genaue Ausrichtung
- ADR: Dialog nachsynchronisieren – Szene aus In/Out, Dialogtext zum Ablesen, Schleife mit Takes, Take wählen, Originalton stummschalten
- Surround (5.1) und räumlicher Ton: je Spur vorne/hinten, Center, Subwoofer (mit Tiefpass), Vorlagen Dialog/Musik/Atmo/Effekt
- Stereo-Downmix nach ITU-Norm, 5.1-Vorlagen (MP4 mit AAC 5.1, WAV 6 Kanäle)

## Sprint 15 – Effekte & Compositing ✅
- Keying: Greenscreen/Bluescreen (Chroma-Key) mit Spill-Unterdrückung und Pipette, Luma-Key, Garbage Matte
- Effekte: Weichzeichnen, Verpixeln, Schärfen, Schwarzweiß, Sepia, Filmkorn, Verwacklung reduzieren
- Masken und Tracking für Effekte (z. B. Gesichter oder Kennzeichen verpixeln)
- Knoten-Editor für Effektketten (Reihenfolge ziehen, an/aus), Effekte kopieren/einfügen

## Sprint 16 – Feinschliff & Version 1.0
- GPU-Export (NVIDIA NVENC, Intel Quick Sync), schnellere Vorschau
- Automatisches Speichern und Wiederherstellung nach Absturz
- Gestaltung der Oberfläche, Anleitung, Installer
