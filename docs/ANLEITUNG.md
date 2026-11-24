# MovieWorker – Anleitung

Willkommen bei MovieWorker! Diese Anleitung führt dich in wenigen Minuten vom Einstecken der Speicherkarte bis zum fertigen Video. Du kannst sie jederzeit im Programm unter **Hilfe → Anleitung** öffnen – oben rechts gibt es eine Suche.

## 1. Erste Schritte

1. **Speicherkarte importieren**: *Datei → Speicherkarte importieren*. MovieWorker erkennt Karten der **Canon EOS R6 Mark II** und der **EOS 70D**, kopiert die Videos nach `Zielordner\Datum\Kamera` und prüft jede Kopie per Prüfsumme.
2. Alternativ Dateien oder Ordner einfach ins Fenster ziehen oder *Dateien importieren* (Strg+I).
3. Doppelklick auf eine Aufnahme im Medienpool legt sie ans Ende der Timeline.
4. **Leertaste** spielt ab, **J/K/L** spult rückwärts/stoppt/vorwärts.
5. *Datei → Exportieren* (Strg+E), Vorlage wählen (z. B. *YouTube – Full HD*), fertig.

Tipp: Das erste Video bestimmt Auflösung und Bildrate eines leeren Projekts. Ändern lässt sich das unter *Datei → Projekteinstellungen*.

## 2. Deine Kameras

| Kamera | Was MovieWorker automatisch macht |
|---|---|
| EOS R6 Mark II | erkennt Canon Log 3 (schlägt die passende LUT vor), HDR PQ (rechnet für normale Bildschirme um), Zeitlupe mit 100/120/150/180 Bildern, erzeugt Proxys für flüssigen 4K-HEVC-Schnitt |
| EOS 70D | fügt in 4-GB-Stücke geteilte Aufnahmen zu einem Clip zusammen, überspringt .THM-Dateien, verpackt MOV-Dateien schnell als Proxy |

Ist die Kamera-Erkennung unsicher, lässt sie sich im Inspektor (Reiter *Aufnahme*) von Hand festlegen.

## 3. Medienpool ordnen

- **Ordner** anlegen und Aufnahmen einsortieren, **Bewertung** (0–5 Sterne), **Schlagwörter**, **Szene/Take** und **Notizen** vergeben.
- Die **Suche** findet Namen, Schlagwörter, Notizen, Szene und Take.
- **Externen Ton anlegen**: Hast du mit einem Rekorder (z. B. Zoom, Tascam) aufgenommen, findet *Bearbeiten → Externen Ton automatisch synchronisieren* die passende Stelle auf 5 ms genau.

## 4. Schneiden

### Schnellschnitt
Im **Quellmonitor** In (I) und Out (O) setzen, dann:

| Taste | Schnittart |
|---|---|
| F9 | Einfügen – alles dahinter rückt nach |
| F10 | Überschreiben |
| F11 | Ersetzen (gleiche Länge) |
| F12 | Oben platzieren (B-Roll) |
| Umschalt+F12 | Anhängen |

Das **Quellband** zeigt alle (gefilterten) Aufnahmen als ein durchgehendes Band – ideal zum schnellen Durchsehen.

### Profi-Schnitt
- **Trimm-Modi**: A = normal, B = Ripple, R = Roll, Y = Slip, U = Slide.
- **Teilen** am Abspielkopf: Strg+B. **Löschen**: Entf, **Lücke schließen**: Umschalt+Entf.
- **Keyframes** für Zoom, Position, Drehung, Deckkraft und Lautstärke im Clip-Inspektor (◆ setzt einen Punkt).
- **Geschwindigkeitsrampen** und **Verschachteln** im Menü *Schnitt*.

### Übergänge und Titel
- **Strg+D** setzt eine Kreuzblende an den nächsten Schnitt; Art und Länge im Clip-Inspektor.
- **Strg+Umschalt+T** legt einen Titel an. Vorlagen: Überschrift, Untertitel, Bauchbinde, Einblendung, Abspann – mit Animationen.

### Mehrspur und Multicam
- Obere Videospuren liegen über dem Bild darunter: Bild-in-Bild, Deckkraft, Mischmodi.
- **Multicam**: Aufnahmen der R6 und der 70D im Medienpool markieren → *Multicam*. MovieWorker synchronisiert per Ton. Beim Abspielen mit **1, 2, 3 …** die Kamera wechseln.

## 5. Farbe

Reiter **Farbe**:
- **Eingang**: LUT für Canon Log 3 (z. B. die offizielle Canon-LUT, Ordner unter *Farbe → LUT-Ordner festlegen*) oder HDR → SDR.
- **Grundkorrektur**: Belichtung, Kontrast, Sättigung, Temperatur, Tönung.
- **Farbräder** Lift/Gamma/Gain, **Kurven**, **sekundäre Korrektur** (nur eine Farbe ändern).
- **Korrekturknoten** mit **Masken** (Ellipse, Rechteck, Vignette); *Verfolgen ▶* lässt eine Maske einem Objekt folgen.
- **Farbabgleich 70D ↔ R6**: R6-Clip als Referenz merken, 70D-Clip anwählen, *An Referenz angleichen*.
- **Messinstrumente**: Waveform, RGB-Parade, Vektorskop, Histogramm.

## 6. Effekte und Greenscreen

Reiter **Effekte** (oder Menü *Effekte*):
1. Greenscreen-Clip auf **V2** legen, den neuen Hintergrund darunter auf **V1**.
2. *Effekte → Greenscreen freistellen*. Mit **Strg+Klick** ins Bild die Hintergrundfarbe aufnehmen (Pipette).
3. **Toleranz** erhöhen, bis der Hintergrund verschwindet; **Spill weg** entfernt grüne Ränder an Haaren.

Weitere Effekte: Weichzeichnen, Verpixeln, Schärfen, Schwarzweiß, Sepia, Filmkorn, Verwacklung reduzieren.

**Gesicht verpixeln**: *Effekte → Gesicht / Kennzeichen verpixeln*, die Maske mit **Umschalt+Klick** auf das Gesicht setzen, Größe anpassen und *Verfolgen ▶* drücken.

Der **Knoten-Editor** zeigt die Effektkette: Ziehen ändert die Reihenfolge, Doppelklick schaltet einen Effekt an oder aus.

## 7. Ton

Reiter **Ton**:
- **Mischpult**: Fader, Panorama, Stumm (M), Solo (S) und Pegelanzeige je Spur.
- **Effekte je Spur**: Hochpass gegen Wind, Rauschminderung, EQ, Kompressor, Hall.
- **Ducking**: Musik wird automatisch leiser, wenn gesprochen wird (*Ton → Spur des Clips bei Sprache absenken*).
- **Lautheit**: Ziel wählen (−14 LUFS für YouTube, −16 Podcast, −23 Fernsehen). Beim Export misst MovieWorker die Mischung und bringt sie genau auf das Ziel.
- **Sprachaufnahme**: Mikrofon wählen, Abspielkopf setzen, **Strg+Umschalt+R**. Ohne Mikrofon kannst du mit dem *Testton* ausprobieren.
- **ADR (Nachsynchronisation)**: In/Out um den Dialog setzen, *Aus In/Out*, Dialogtext eintragen, mit *Schleife* mehrere Takes nacheinander aufnehmen und den besten *verwenden*.
- **Raumklang 5.1**: je Spur vorne/hinten, Center und Subwoofer; Export mit *Master 5.1* oder *WAV 5.1*. Stereo-Exporte werden automatisch heruntergemischt.

## 8. Exportieren

*Datei → Exportieren* (Strg+E):
- Vorlagen für YouTube (4K/HD), Vimeo, Instagram/TikTok/Shorts (9:16), Master (ProRes, DNxHR), Archiv (H.265 10 Bit), HDR10, 5.1 und nur Ton.
- **Grafikkarte nutzen**: Mit einer NVIDIA-, Intel- oder AMD-Grafikkarte geht der Export meist 3- bis 6-mal schneller. Klappt es nicht, exportiert MovieWorker automatisch mit dem Prozessor.
- **Ganze Timeline** oder nur der **In/Out-Bereich**.
- Die **Prüfung vor dem Export** warnt vor typischen Fallen (fehlende LUT, nicht konformierte Zeitlupe, Hochskalierung …).
- Mehrere Exporte laufen nacheinander in der **Warteschlange** – du kannst währenddessen weiterschneiden.

## 9. Sicherheit für deine Arbeit

- **Automatisches Sichern**: Alle 3 Minuten werden ungespeicherte Änderungen gesichert. Stürzt der Rechner ab, bietet MovieWorker beim nächsten Start die **Wiederherstellung** an.
- **Sicherungskopie**: Beim Speichern bleibt die vorige Fassung als `Projekt.mvproj.bak` erhalten.
- Deine Videos werden nie verändert – das Projekt merkt sich nur, welche Stellen du verwendest.

## 10. Darstellung

*Extras → Dunkles Design* schaltet auf eine dunkle Oberfläche um – angenehm bei Farbkorrektur und abends.

## 11. Tastenkürzel

| Aktion | Taste |
|---|---|
| Abspielen / Pause | Leertaste |
| Rückwärts / Stopp / Vorwärts | J / K / L |
| Bild vor / zurück | → / ← (mit Umschalt: 1 Sekunde) |
| Nächster / voriger Schnitt | ↓ / ↑ |
| In / Out setzen | I / O |
| Teilen | Strg+B |
| Rückgängig / Wiederholen | Strg+Z / Strg+Y |
| Speichern | Strg+S |
| Exportieren / Einzelbild | Strg+E / Strg+Umschalt+E |
| Kreuzblende / Titel | Strg+D / Strg+Umschalt+T |
| Multicam-Kamera | 1 … 9 |
| Aufnahme starten/stoppen | Strg+Umschalt+R |
| Key-Farbe aufnehmen (Pipette) | Strg+Klick ins Bild |
| Maske setzen | Umschalt+Klick ins Bild |

## 12. Häufige Fragen

**Die Vorschau ruckelt bei 4K-Aufnahmen der R6.**
Unter *Wiedergabe → Vorschau mit Proxys* sollte ein Haken sein. MovieWorker erzeugt die Proxys im Hintergrund; der Export nutzt immer die Originale.

**Canon-Log-Aufnahmen sehen grau und flau aus.**
Das ist bei Log normal. Im Reiter *Farbe* die LUT *Canon Log 3 → BT.709* wählen – oder einmal den LUT-Ordner festlegen, dann geschieht das automatisch.

**Windows warnt beim ersten Start („SmartScreen“).**
Auf *Weitere Informationen → Trotzdem ausführen* klicken. Das Programm ist nicht digital signiert.

**Wo ist FFmpeg?**
FFmpeg liegt dem Programm bei (Ordner `ffmpeg`). Ein anderer Ordner lässt sich unter *Extras → FFmpeg-Ordner festlegen* wählen.

Viel Freude beim Schneiden! 🎬
