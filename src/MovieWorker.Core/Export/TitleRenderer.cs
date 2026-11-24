using System.Globalization;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Export;

/// <summary>Findet die Schriftdatei zu einer Schriftfamilie (Windows-Schriften, unter Linux DejaVu als Ersatz).</summary>
public static class FontResolver
{
    public static IReadOnlyList<string> Families { get; } = ["Segoe UI", "Arial", "Calibri", "Georgia", "Times New Roman", "Impact", "Consolas"];

    private static readonly Dictionary<string, (string Regular, string Bold)> WindowsFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Segoe UI"] = ("segoeui.ttf", "segoeuib.ttf"),
        ["Arial"] = ("arial.ttf", "arialbd.ttf"),
        ["Calibri"] = ("calibri.ttf", "calibrib.ttf"),
        ["Georgia"] = ("georgia.ttf", "georgiab.ttf"),
        ["Times New Roman"] = ("times.ttf", "timesbd.ttf"),
        ["Impact"] = ("impact.ttf", "impact.ttf"),
        ["Consolas"] = ("consola.ttf", "consolab.ttf"),
    };

    public static string? Resolve(string family, bool bold, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var windows = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        if (WindowsFiles.TryGetValue(family, out var files))
        {
            var path = Path.Combine(windows, "Fonts", bold ? files.Bold : files.Regular);
            if (exists(path))
            {
                return path;
            }
        }

        var serif = family is "Georgia" or "Times New Roman";
        var mono = family == "Consolas";
        string[] linux =
        [
            $"/usr/share/fonts/truetype/dejavu/DejaVu{(serif ? "Serif" : mono ? "SansMono" : "Sans")}{(bold ? "-Bold" : string.Empty)}.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
        ];
        return linux.FirstOrDefault(exists);
    }
}

/// <summary>
/// Zeichnet Titel mit <c>drawtext</c> ins fertige Bild – je Zeile ein Filter (so sind Zeilen sauber ausgerichtet).
/// Animationen (Einblenden, Hereinschieben, Schreibmaschine, Abspann-Rollen) über zeitabhängige Ausdrücke.
/// </summary>
public static class TitleRenderer
{
    /// <summary>Filter für alle Titel des Projekts (leer, wenn es keine gibt).</summary>
    public static IReadOnlyList<string> Filters(MovieProject project, int width, int height, Func<string, bool>? fileExists = null, bool hdr = false)
    {
        var rate = project.Settings.FrameRate;
        var filters = new List<string>();
        foreach (var track in project.TitleTracks.Where(t => !t.Muted).Reverse())
        {
            foreach (var clip in track.OrderedClips.Where(c => c.Title is not null))
            {
                filters.AddRange(ForClip(clip, rate, width, height, null, fileExists, hdr));
            }
        }

        return filters;
    }

    /// <summary>Titel an einer festen Stelle (für Standbilder in der Vorschau).</summary>
    public static IReadOnlyList<string> StaticAt(MovieProject project, long frame, int width, int height)
    {
        var rate = project.Settings.FrameRate;
        return project.TitleTracks.Where(t => !t.Muted).Reverse()
            .SelectMany(t => t.Clips.Where(c => c.Title is not null && c.Contains(frame)))
            .SelectMany(c => ForClip(c, rate, width, height, rate.FramesToSeconds(frame), null))
            .ToList();
    }

    public static IReadOnlyList<string> ForClip(TimelineClip clip, FrameRate rate, int width, int height, double? fixedTime,
        Func<string, bool>? fileExists = null, bool hdr = false)
    {
        var spec = clip.Title!;
        var lines = spec.Lines;
        if (lines.Count == 0)
        {
            return [];
        }

        string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        var start = rate.FramesToSeconds(clip.StartFrame);
        var end = rate.FramesToSeconds(clip.EndFrame);
        var anim = Math.Clamp(spec.AnimationSeconds, 0.05, Math.Max(0.05, (end - start) / 2));
        var t = fixedTime is { } ft ? N(ft) : "t";
        var font = FontResolver.Resolve(spec.FontFamily, spec.Bold, fileExists);
        var fontOption = font is null ? $"font='{spec.FontFamily}'" : $"fontfile={FfmpegArgs.EscapeFilterPath(font)}";

        // Zeilenhöhen und Blockhöhe
        var sizes = lines.Select((_, i) => (int)Math.Max(8, Math.Round(height * spec.SizePercent / 100 * (i == 0 ? 1 : spec.SecondaryScale)))).ToList();
        var lineHeights = sizes.Select(s => s * 1.3).ToList();
        var blockHeight = lineHeights.Sum();
        var centerY = spec.PositionY / 100 * height;
        var anchorX = spec.PositionX / 100 * width;

        // Fortschritt der Ein-/Ausblendung (0…1), weich
        var pIn = $"clip(({t}-{N(start)})/{N(anim)},0,1)";
        var pOut = $"clip(({N(end)}-{t})/{N(anim)},0,1)";
        string Ease(string p) => $"({p})*({p})*(3-2*({p}))";
        var alphaIn = spec.AnimationIn == TitleAnimation.None ? "1" : pIn;
        var alphaOut = spec.AnimationOut == TitleAnimation.None ? "1" : pOut;
        var alpha = $"({alphaIn})*({alphaOut})";
        var dy = spec.AnimationIn == TitleAnimation.SlideUp ? $"+(1-{Ease(pIn)})*{N(height * 0.08)}" : string.Empty;
        dy += spec.AnimationOut == TitleAnimation.SlideUp ? $"-(1-{Ease(pOut)})*{N(height * 0.08)}" : string.Empty;
        var dx = spec.AnimationIn == TitleAnimation.SlideLeft ? $"-(1-{Ease(pIn)})*{N(width * 0.12)}" : string.Empty;
        dx += spec.AnimationOut == TitleAnimation.SlideLeft ? $"-(1-{Ease(pOut)})*{N(width * 0.12)}" : string.Empty;

        var filters = new List<string>();
        double offset = 0;
        var totalChars = Math.Max(1, lines.Sum(l => l.Length));
        var charsBefore = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var text = Sanitize(lines[i]);
            var lineTop = spec.Style == TitleStyle.Credits
                ? $"{N(height)}-({t}-{N(start)})/{N(Math.Max(0.1, end - start))}*{N(height + blockHeight)}+{N(offset)}"
                : $"{N(centerY - blockHeight / 2 + offset + (lineHeights[i] - sizes[i]) / 2)}{dy}";
            var x = spec.Align switch
            {
                TitleAlign.Left => $"{N(anchorX)}{dx}",
                TitleAlign.Right => $"{N(anchorX)}-text_w{dx}",
                _ => $"{N(anchorX)}-text_w/2{dx}",
            };
            var style = $"{fontOption}:fontsize={sizes[i]}:fontcolor={TextColor(spec.Color, hdr)}:x='{x}':y='{lineTop}':expansion=none";
            style += spec.Box
                ? $":box=1:boxcolor={spec.BoxColor}@{N(Math.Clamp(spec.BoxOpacity, 0, 1))}:boxborderw={Math.Max(4, sizes[i] / 4)}"
                : ":shadowcolor=black@0.6:shadowx=2:shadowy=2";

            if (spec.AnimationIn == TitleAnimation.Typewriter && text.Length <= 80)
            {
                // Schreibmaschine: Buchstabe für Buchstabe (je Zeile anteilig an der Einblendzeit).
                var typing = Math.Min(Math.Max(0.4, totalChars * 0.06), (end - start) * 0.6);
                for (var k = 1; k <= text.Length; k++)
                {
                    var from = start + typing * (charsBefore + k - 1) / totalChars;
                    var to = k == text.Length ? end : start + typing * (charsBefore + k) / totalChars;
                    if (fixedTime is { } now && (now < from || now >= to))
                    {
                        continue;
                    }

                    filters.Add($"drawtext={style}:text='{text[..k]}':alpha='{alphaOut}':enable='between({t},{N(from)},{N(to)})'");
                }

                charsBefore += text.Length;
            }
            else
            {
                filters.Add($"drawtext={style}:text='{text}':alpha='{alpha}':enable='between({t},{N(start)},{N(end)})'");
            }

            offset += lineHeights[i];
        }

        return filters;
    }

    private static readonly Dictionary<string, (int R, int G, int B)> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["white"] = (255, 255, 255), ["black"] = (0, 0, 0), ["yellow"] = (255, 255, 0), ["orange"] = (255, 165, 0),
        ["red"] = (255, 0, 0), ["deepskyblue"] = (0, 191, 255), ["lightgreen"] = (144, 238, 144), ["gray"] = (128, 128, 128),
    };

    /// <summary>
    /// Textfarbe; bei HDR-Ausgabe auf SDR-Helligkeit (≈ 203 Nits, PQ-Wert ≈ 0,58) gedämpft,
    /// sonst wäre weißer Text in PQ gleißend hell.
    /// </summary>
    public static string TextColor(string color, bool hdr)
    {
        if (!hdr)
        {
            return color;
        }

        var (r, g, b) = Named.TryGetValue(color, out var rgb) ? rgb : (255, 255, 255);
        int Scale(int v) => (int)Math.Round(v * 0.58);
        return $"0x{Scale(r):X2}{Scale(g):X2}{Scale(b):X2}";
    }

    /// <summary>Zeichen, die in ffmpeg-Filtern stören, durch unkritische ersetzen.</summary>
    public static string Sanitize(string text) =>
        text.Replace('\\', '/').Replace('\'', '’').Replace("%", "％", StringComparison.Ordinal);
}
