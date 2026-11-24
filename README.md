# 🎬 MovieWorker

**MovieWorker** ist ein Videoschnittprogramm für Windows, geschrieben in C# mit .NET 8 – das Schwesterprojekt
von [MusicWorker](https://github.com/PSanne/MusicWorker).

Es ist auf Material der **Canon EOS R6 Mark II** und der **Canon EOS 70D** zugeschnitten und soll daraus
professionelle Videos machen: vom Import der Speicherkarte über den Schnitt bis zum Export als
H.264/H.265 für YouTube oder als ProRes/DNxHR-Master.

> **Version 1.0 – alle 16 Sprints abgeschlossen** 🎉 Import von der Speicherkarte, Medien-Arbeitsbereich, Schnellschnitt,
> Profi-Schnitt, Übergänge & Titel, Mehrspur & Multicam, Farbkorrektur mit Knoten, Masken und Tracking, Mischpult mit Lautheit,
> Ducking und 5.1-Raumklang, Sprachaufnahme & ADR, Effekte mit Greenscreen, Export auch über die Grafikkarte, automatisches
> Sichern, dunkles Design, eingebaute Anleitung und Windows-Installer. Den Plan und was in welchem Sprint entstand, findest du in
> **[docs/SPRINTS.md](docs/SPRINTS.md)**, die Bedienung Schritt für Schritt in der **[Anleitung](docs/ANLEITUNG.md)**
> (auch im Programm unter *Hilfe → Anleitung*, F1).

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
| **Ordnen** | **Ordner** (anlegen, umbenennen, löschen; Aufnahmen im Inspektor einsortieren) · **Bewertung** (0–5 Sterne), **Schlagwörter**, **Szene/Take**, **Notizen** · **Suche** über Name, Schlagwörter, Notizen, Szene und Take · Filter (Video/Ton, Mindestbewertung) · Sortierung nach Name, Aufnahmezeit, Bewertung oder Länge |
| **Bild-Ton-Sync** | Externen Ton (z. B. Zoom-/Tascam-Rekorder) per Wellenform an den Kameraton anlegen – auf 5 ms genau, auch wenn der Rekorder früher oder später gestartet wurde · *Bearbeiten → Externen Ton automatisch synchronisieren* probiert alle Kombinationen · beim Einfügen kommt der passende Ausschnitt des externen Tons automatisch auf A2, der Kameraton wird stummgeschaltet (abschaltbar) |
| **Timeline** | Clips per Doppelklick oder Ziehen aus dem Medienpool einfügen · verschieben (auch auf andere Spuren), an den Rändern trimmen · Einrasten an Schnittpunkten, Abspielkopf und Marken · Vorschaubilder und Ton-Wellenform in den Clips · Spuren hinzufügen und stummschalten |
| **Schnellschnitt** | Fünf Schnittarten aus dem Quellmonitor: **Einfügen** (F9, alles dahinter rückt auf allen Spuren nach), **Überschreiben** (F10), **Ersetzen** (F11, gleiche Position und Länge), **Oben platzieren** (F12, B-Roll auf die nächste freie Videospur), **Anhängen** (Umschalt+F12) · **Quellband**: alle (gefilterten) Aufnahmen als ein durchgehendes Band durchsehen, In/Out setzen und schneiden · **Doppel-Timeline**: oben die ganze Timeline, unten der Ausschnitt zum Arbeiten |
| **Profi-Schnitt** | **Trimm-Modi** (Auswahl „Ziehen:“ in der Timeline): *Ripple* (Rand trimmen, alles dahinter rückt nach), *Roll* (Schnittpunkt zwischen zwei Clips verschieben), *Slip* (anderer Ausschnitt bei gleicher Position), *Slide* (Clip zwischen Nachbarn verschieben) · **Keyframes** für Zoom, Position, Drehung, Deckkraft und Lautstärke – im Clip-Inspektor am Abspielkopf setzen (◆), springen (◀ ▶), zurücksetzen; weiche Übergänge; „Ken Burns“-Fahrten · **Geschwindigkeitsrampen** (Normal → Zeitlupe, Zeitlupe → Normal, Normal → Zeitlupe → Normal) · **Verschachteln** (In/Out-Bereich oder Clip zu einem Clip zusammenfassen) und wieder auflösen |
| **Übergänge** | 14 Übergänge: Kreuzblende, Abblende über Schwarz/Weiß, Auflösen, Wischen (4 Richtungen), Schieben, Kreis öffnen/schließen, Uhrzeiger, Verpixeln · Strg+D setzt eine Kreuzblende an den nächsten Schnitt; Art und Länge im Clip-Inspektor · der zweite Clip blendet aus seinem Vorlauf über, die Schnittzeiten bleiben gleich |
| **Titelwerkzeug** | Eigene Titelspur ganz oben · Vorlagen: Überschrift, Untertitel, **Bauchbinde** (Name + Funktion), Einblendung (Ort, Datum), **Abspann** (rollt) · Text mehrzeilig, Schrift, fett, Größe, Farbe, Kasten mit Deckkraft, Ausrichtung, Position · **Animationen**: Einblenden, von unten, von links, Schreibmaschine · beim Abspielen live im Monitor, im Stillstand und Export exakt über ffmpeg |
| **Mehrspur** | Beliebig viele Videospuren: obere Spuren liegen **über** dem Bild darunter – Bild-in-Bild (Größe, Lage, Ecken-Vorlagen), Deckkraft (auch per Keyframe), Drehung, **Zuschneiden** und **Spiegeln**; Ton der oberen Clips wird mitgemischt · **Mischmodi**: Multiplizieren, Negativ multiplizieren, Überlagern, Weiches Licht, Abdunkeln, Aufhellen, Differenz, Addieren |
| **Multicam** | Kameraaufnahmen im Medienpool markieren (Strg+Klick) → *Multicam*: MovieWorker synchronisiert R6 Mark II und 70D per Ton und legt einen Mehrkamera-Clip an · **Kamerawechsel mit 1, 2, 3 …** am Abspielkopf – auch live während der Wiedergabe · Bild- und Ton-Kamera getrennt wählbar (z. B. Bild von der 70D, Ton durchgehend von der R6) |
| **Farbe** | Reiter *Farbe*: **Eingang** – LUT aus der LUT-Bibliothek (z. B. die offizielle Canon-LUT *Canon Log 3 → BT.709*, wird beim Festlegen des LUT-Ordners automatisch als Standard für Log-Clips erkannt) oder HDR → SDR mit wählbarem Tone-Mapping · **Grundkorrektur**: Belichtung, Kontrast, Sättigung, Temperatur, Tönung · **Farbräder** Lift/Gamma/Gain (Punkt ziehen, Umschalt = fein, Doppelklick = zurück) · **Kopieren/Einfügen**, *Auf Kamera übertragen* · **Farbabgleich**: R6-Clip als Referenz, 70D-Clip daran angleichen · **Messinstrumente**: Waveform, RGB-Parade, Vektorskop (mit Hautton-Linie), Histogramm · Export **HDR10** (H.265, PQ, BT.2020) |
| **Farbe II** | **Kurven** (Helligkeit, Rot, Grün, Blau – Klick setzt Punkt, Doppelklick löscht) · **Sekundäre Korrektur**: Farbbereich nach Farbton, Sättigung und Helligkeit auswählen (*Auswahl anzeigen* zeigt ihn weiß) und nur ihn ändern – z. B. Himmel blauer, Haut wärmer · **Korrekturknoten** nacheinander, je mit eigener Korrektur und optionaler **Maske** (Ellipse/Rechteck, weiche Kante, umkehrbar – fertige *Vignette*) · **Tracking**: *Verfolgen ▶* lässt die Maske einem Objekt folgen · Masken-Umriss im Programmmonitor |
| **Ton / Mischpult** | Reiter *Ton* (Menü *Ton → Mischpult*): je Spur ein **Kanalzug** mit Fader, **Panorama**, **Stumm (M)** und **Solo (S)**, Pegelanzeige an der Abspielposition · **Effekte**: Hochpass (Wind, Trittschall), **Rauschminderung**, 3-Band-EQ (120 Hz / 1 kHz / 8 kHz), **Kompressor**, Hall · **Auto-Ducking**: Musik wird automatisch leiser, solange gesprochen wird (*Ton → Spur des Clips bei Sprache absenken*) · **Summe**: Lautstärke, Limiter (nie über −0,5 dB), **Lautheits-Ziel** −14 LUFS (YouTube/Spotify), −16 LUFS (Podcast) oder −23 LUFS (EBU R128) – *Lautheit messen* zeigt LUFS, Spitze und Umfang; beim Export wird die Mischung erst gemessen und dann genau auf das Ziel gebracht · Vorschau berücksichtigt Lautstärke, Panorama und Solo |
| **Sprachaufnahme & ADR** | Reiter *Ton → Sprachaufnahme & ADR* (Strg+Umschalt+R): Mikrofon wählen (⟳ sucht angeschlossene Mikrofone; *Testton* zum Ausprobieren ohne Mikrofon), Zielspur, Vorlauf · **Voice-over**: das Bild läuft ab dem Abspielkopf, gesprochen wird dazu, *Stopp* setzt die Aufnahme genau an die Stelle · **ADR** (Nachsynchronisation): In/Out um den Dialog → *Aus In/Out* legt eine ADR-Szene an (orange in der Timeline), Dialogtext zum Ablesen, mit **Schleife** entstehen Takes nacheinander, der letzte vollständige wird eingesetzt, ältere per *Verwenden*; der Originalton im Bereich wird stummgeschaltet · Aufnahmen landen im Ordner „‹Projekt› Aufnahmen“ und im Medienpool |
| **Raumklang 5.1** | Je Spur im Mischpult unter *Raum (5.1)*: vorne ↔ hinten, **Center** (Dialog), **Subwoofer** (nur unter 120 Hz) und Vorlagen *Dialog*, *Musik*, *Atmo*, *Effekt* · Export-Vorlagen **Master 5.1** (H.264 + AAC 5.1) und **WAV 5.1** · Stereo-Exporte werden nach ITU-Norm heruntergemischt – ohne Raumeinstellungen bleibt alles wie gewohnt |
| **Effekte & Compositing** | Reiter *Effekte* bzw. Menü *Effekte*: **Greenscreen/Bluescreen** freistellen (Chroma-Key) – Toleranz, weiche Kante, **Spill-Unterdrückung** gegen grüne Ränder, **Pipette** (Strg+Klick ins Bild), Hintergrund einfach auf die Spur darunter legen · **Luma-Key** (Helles/Dunkles freistellen) · **Weichzeichnen**, **Verpixeln**, Schärfen, Schwarzweiß, Sepia, Filmkorn, **Verwacklung reduzieren** · jeder Effekt optional nur in einer **Maske** (Ellipse/Rechteck, weich, umkehrbar; Umschalt+Klick setzt sie), die per **Tracking** einem Objekt folgt – z. B. Gesichter oder Kennzeichen verpixeln; bei Keys als „Garbage Matte“ · **Knoten-Editor**: die Effektkette als Knoten (Quelle → Farbe → Effekte → Ausgabe), Reihenfolge per Ziehen, Doppelklick schaltet an/aus · Effekte kopieren und einfügen |
| **Export über die Grafikkarte** | NVIDIA NVENC, Intel Quick Sync und AMD AMF werden beim Start erkannt (mit Probe-Kodierung, nicht nur nach Liste) · im Export-Dialog *Grafikkarte nutzen* – meist 3- bis 6-mal schneller, gleiche Vorlagen (H.264 bleibt H.264, H.265 10 Bit bleibt 10 Bit) · klappt es nicht, exportiert MovieWorker automatisch mit dem Prozessor |
| **Sicherheit** | **Automatisches Sichern** alle 3 Minuten · nach einem Absturz bietet der nächste Start die **Wiederherstellung** an · beim Speichern bleibt die vorige Fassung als `.mvproj.bak` erhalten |
| **Gestaltung & Hilfe** | *Extras → Dunkles Design* · eigenes Programmsymbol · **Anleitung** im Programm (F1, mit Suche) · Windows-**Installer** mit Startmenü-Eintrag und Dateizuordnung für `.mvproj` |
| **Exakte Vorschau** | Im Stillstand rechnet der Programmmonitor das Bild über **dieselbe Kette wie der Export** – alle Spuren, Bild-in-Bild, Mischmodi, Titel, Farbe, Keyframes |
| **Clip-Inspektor** | Position und Länge als Timecode · Geschwindigkeit (Zeitlupe/Zeitraffer) · „Als Zeitlupe abspielen“ für 150/180-fps-Clips · Lautstärke (auch per Keyframe), Ton stumm, Ein-/Ausblenden |
| **Monitore** | *Quelle*: Aufnahme ansehen, In/Out setzen, Ausschnitt ans Ende oder am Abspielkopf einfügen · *Programm*: Timeline abspielen; im Stillstand exaktes Standbild mit LUT/HDR-Umrechnung wie im Export · J/K/L-Shuttle |
| **Proxys** | Für Aufnahmen, die Windows nicht flüssig abspielt (4K-HEVC 10 Bit 4:2:2, HDR, MOV mit LPCM), entsteht automatisch eine Vorschaudatei: 70D-Clips werden nur neu verpackt (Sekunden), R6-HEVC wird als 540p-H.264 kodiert, HDR dabei nach SDR umgerechnet · geteilte Aufnahmen ergeben einen Proxy · Menü *Wiedergabe → Vorschau mit Proxys* schaltet aufs Original um · der Export nutzt immer die Originale |
| **Export** | *Datei → Exportieren* (Strg+E): Vorlagen für YouTube (4K/HD, H.264/H.265), Vimeo, Instagram Reels/TikTok/Shorts (9:16), Instagram-Quadrat und 4:5, Master (ProRes 422 HQ/LT, DNxHR HQX), Archiv (H.265 10 Bit), nur Ton (WAV/MP3) · anderes Seitenverhältnis: unscharfer Rand, zuschneiden oder schwarz · ganze Timeline oder In/Out-Bereich · **Prüfung vor dem Export** (fehlende Dateien/LUT, Canon Log 3 ohne LUT, nicht konformierte Zeitlupe, Hochskalierung) · **Warteschlange** mit Fortschritt, Restzeit und Abbrechen – weiterschneiden während des Exports · Einzelbild als PNG (Strg+Umschalt+E) |
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
| Exportieren / Einzelbild | Strg+E / Strg+Umschalt+E |
| Einfügen / Überschreiben / Ersetzen / Oben / Anhängen | F9 / F10 / F11 / F12 / Umschalt+F12 |
| Trimm-Modus Normal / Ripple / Roll / Slip / Slide | A / B / R / Y / U |
| Kreuzblende am Schnitt / neuer Titel | Strg+D / Strg+Umschalt+T |
| Multicam: Kamera wechseln | 1 … 9 (auch beim Abspielen) |
| Sprachaufnahme / ADR starten und stoppen | Strg+Umschalt+R |
| Pipette (Key-Farbe) / Maske setzen | Strg+Klick / Umschalt+Klick ins Bild |
| Anleitung | F1 |

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
Fertige Versionen gibt es unter **[Releases](https://github.com/PSanne/MovieWorker/releases/latest)**:

- **Installer** (empfohlen): `MovieWorker-Setup-v1.0.0.exe` herunterladen und starten. Legt MovieWorker im Startmenü an,
  auf Wunsch auch auf dem Desktop, und öffnet `.mvproj`-Projekte per Doppelklick. Administratorrechte sind nicht nötig.
- **Ohne Installation**: ZIP herunterladen, entpacken, `MovieWorker.exe` starten.

Es muss weder .NET noch FFmpeg installiert sein (FFmpeg liegt bei).

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
- **Farbkorrekturen werden als 3D-LUT berechnet.** MovieWorker rechnet jede Einstellung in eine 33er-LUT um und wendet
  sie per `lut3d` an. So sehen Vorschau und Export garantiert gleich aus, und 10-Bit-Material der R6 Mark II behält
  seine Abstufungen. Gleiche Einstellungen ergeben dieselbe Datei (Zwischenspeicher in `%LocalAppData%\MovieWorker`).
- **Farbabgleich über Mittelwert und Streuung je Kanal.** Einfach, robust und gut nachvollziehbar – Feinheiten danach
  mit den Farbrädern.
- **Keyframes hängen am Bildinhalt.** Wird ein Clip geteilt oder vorne gekürzt, wandern die Keyframes mit, sodass eine
  Kamerafahrt an derselben Stelle im Bild bleibt.
- **Verschachtelte Clips werden für Vorschau und Export aufgelöst.** So gilt für ihren Inhalt dieselbe Technik wie für
  normale Clips – es gibt keinen zweiten Render-Weg, der anders aussehen könnte.
- **Synchronisation über Lautstärke-Einsätze statt Rohsignal.** Kamera- und Rekordermikrofon klingen unterschiedlich,
  aber Klatschen, Silben und Geräusche setzen gleichzeitig ein. Verglichen werden deshalb die Anstiege der Lautstärke
  (5-ms-Raster) per Kreuzkorrelation über FFT – das ist robust und dauert auch bei langen Aufnahmen nur Sekunden.
- **Ordner, Bewertungen und Notizen gehören zum Projekt**, nicht zu den Dateien – die Originale bleiben unberührt.
- **Export-Aufträge arbeiten mit einer Kopie des Projekts.** So kann man während eines langen Exports weiterschneiden,
  ohne das Ergebnis zu verändern. Exportiert wird in eine `.part`-Datei, die erst am Ende umbenannt wird – ein
  abgebrochener Export hinterlässt keine halbe Datei.
- **Hochformat mit unscharfem Rand als Standard.** Bei 16:9-Material auf 9:16 bleibt so das ganze Bild sichtbar;
  wer lieber zuschneidet, stellt im Export-Dialog „Bildfüllend zuschneiden“ ein.
- **Proxys nur, wo nötig.** MP4 mit H.264 spielt Windows direkt; 70D-MOVs werden nur neu verpackt, nur HEVC/10 Bit/HDR
  wird neu kodiert. Proxys liegen in `%LocalAppData%\MovieWorker\Proxys` und werden nie exportiert.
- **Canon Log 3 wird über die Bit-Tiefe erkannt:** Die R6 Mark II nimmt 10 Bit nur mit Canon Log 3 oder HDR PQ auf;
  HDR PQ ist in den Metadaten markiert, also ist 10 Bit ohne PQ-Kennzeichen Canon Log 3.

## Lizenz

Apache License 2.0 – siehe [LICENSE](LICENSE). FFmpeg ist ein eigenständiges Programm unter eigener Lizenz (LGPL/GPL).
