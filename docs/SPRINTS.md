# MovieWorker – Sprint-Plan

Jeder Sprint ist ein in sich abgeschlossenes, testbares Paket (etwa 1–2 Wochen Arbeit). Am Ende steht
jeweils eine lauffähige Version mit Tests. Die Reihenfolge folgt dem Grundsatz: erst ein stabiler Kern,
dann sichtbare Bedienung, dann Profi-Funktionen.

| Sprint | Thema | Ergebnis | Status |
|---|---|---|---|
| 1 | Fundament & Kern | Plattformunabhängiger Kern mit Canon-Erkennung, Timeline und Export | ✅ fertig |
| 2 | App-Grundgerüst & Import | Windows-App, Speicherkarte importieren, Medienpool | ⏳ als Nächstes |
| 3 | Timeline-Oberfläche | Schneiden mit der Maus | offen |
| 4 | Vorschau & Proxys | Abspielen, Quell- und Programmmonitor | offen |
| 5 | Export-Dialog | Fertige Filme ausgeben (→ erste nutzbare Version 0.5) | offen |
| 6 | Farbe & Canon-Look | Canon Log 3, HDR, Farbkorrektur, Messinstrumente | offen |
| 7 | Ton | Pegel, Lautheit, Ducking, externen Ton anlegen | offen |
| 8 | Übergänge, Titel & Mehrspur | Blenden, Texte, B-Roll, Stabilisierung | offen |
| 9 | Feinschliff & Version 1.0 | Tempo, GPU-Export, Autosave, Gestaltung | offen |

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

## Sprint 2 – App-Grundgerüst & Import
- WPF-Anwendung (.NET 8, MVVM) mit Hauptfenster, Menüs, Statuszeile
- ffmpeg finden (neben der .exe, im PATH oder in den Einstellungen); im Release-ZIP mitgeliefert
- **Speicherkarte/DCIM-Ordner importieren** (optional auf die Festplatte kopieren, mit Prüfsumme)
- Medienpool: Vorschaubilder, Kamera, Format, Hinweise (Log/HDR/Zeitlupe), Kamera von Hand korrigieren
- Projekt neu/öffnen/speichern, zuletzt geöffnete Projekte, Projekteinstellungen aus erstem Clip
- Release-Workflow: ZIP-Download wie bei MusicWorker

## Sprint 3 – Timeline-Oberfläche
- Spuransicht mit Zeitlineal (Timecode), Abspielkopf, Zoom und Scrollen
- Clips ziehen (auch zwischen Spuren), Ränder trimmen, Teilen (Strg+B), Löschen/Lücke schließen
- Einrasten an Clipkanten und Abspielkopf, Vorschaubilder im Clip, Ton-Wellenform
- Tastenkürzel nach Branchenstandard: J/K/L, I/O, Pfeiltasten bildweise

## Sprint 4 – Vorschau & Proxys
- Quellmonitor mit In-/Out-Punkten und „In Timeline einfügen“
- Programmmonitor: Wiedergabe der Timeline, bildgenaues Scrubbing
- Proxys im Hintergrund erzeugen (4K-HEVC → 540p H.264), automatisch beim Import möglich
- Umschalten Proxy ↔ Original, Vorschau mit LUT/Tone-Mapping

## Sprint 5 – Export-Dialog (→ Version 0.5, erste nutzbare Version)
- Export-Dialog mit Vorlagen, Zielordner, Fortschritt, Restzeit, Abbrechen
- Nur Bereich exportieren (In/Out), Export-Warteschlange
- Prüfung vor dem Export: fehlende Dateien, LUT fehlt, gemischte Bildraten

## Sprint 6 – Farbe & Canon-Look
- LUT-Bibliothek (z. B. offizielle Canon-LUTs Canon Log 3 → BT.709), pro Clip oder für alle Log-Clips
- HDR PQ → SDR mit einstellbarem Tone-Mapping; optional HDR-Export (HDR10)
- Grundkorrektur: Belichtung, Kontrast, Weißabgleich, Sättigung, Lift/Gamma/Gain
- Farbabgleich zwischen EOS 70D und R6 Mark II, Korrekturen kopieren/einfügen
- Messinstrumente: Waveform, Vektorskop, Histogramm

## Sprint 7 – Ton
- Pegelanzeigen, Clip- und Spurlautstärke, Lautstärke-Hüllkurven
- Lautheit messen und normalisieren (z. B. −14 LUFS für YouTube, −23 LUFS EBU R128)
- Auto-Ducking (Musik leiser bei Sprache), Rauschminderung, Hochpass
- Externen Ton (Rekorder) per Wellenform automatisch an den Kameraton anlegen

## Sprint 8 – Übergänge, Titel & Mehrspur
- Überblendungen (Kreuzblende, Abblende auf Schwarz/Weiß, Wischblenden)
- Titel und Bauchbinden mit Vorlagen
- Mehrere Videospuren: B-Roll, Bild-in-Bild, Deckkraft
- Zeitlupe/Zeitraffer-Werkzeuge, Bildstabilisierung (vid.stab), Zuschneiden/Drehen

## Sprint 9 – Feinschliff & Version 1.0
- GPU-Export (NVIDIA NVENC, Intel Quick Sync), schnellere Vorschau
- Automatisches Speichern und Wiederherstellung nach Absturz
- Gestaltung der Oberfläche, Anleitung, Installer
