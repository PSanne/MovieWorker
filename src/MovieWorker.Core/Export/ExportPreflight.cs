using MovieWorker.Core.Media;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Export;

public enum IssueSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Ein Hinweis der Prüfung vor dem Export.</summary>
public sealed record PreflightIssue(IssueSeverity Severity, string Message)
{
    public string Icon => Severity switch
    {
        IssueSeverity.Error => "⛔",
        IssueSeverity.Warning => "⚠",
        _ => "ℹ",
    };
}

/// <summary>
/// Prüft vor dem Export, ob alles passt – Fehler verhindern den Export, Warnungen weisen auf
/// typische Fallen hin (fehlende LUT bei Canon Log 3, nicht konformierte Zeitlupe, Hochskalierung …).
/// </summary>
public static class ExportPreflight
{
    public static IReadOnlyList<PreflightIssue> Check(MovieProject project, ExportPreset preset, long? rangeStart = null, long? rangeEnd = null,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var issues = new List<PreflightIssue>();
        var rate = project.Settings.FrameRate;

        if (rangeStart is { } s && rangeEnd is { } e)
        {
            if (e <= s)
            {
                issues.Add(new(IssueSeverity.Error, "Der Bereich ist leer: Die Out-Marke muss hinter der In-Marke liegen."));
            }
            else
            {
                project = ExportRange.Extract(project, s, e);
            }
        }

        var clips = project.Tracks.Where(t => !t.Muted).SelectMany(t => t.Clips.Select(c => (Track: t, Clip: c))).ToList();
        if (clips.Count == 0)
        {
            issues.Add(new(IssueSeverity.Error, "Die Timeline (bzw. der Bereich) ist leer."));
            return issues;
        }

        var missing = clips.SelectMany(x => x.Clip.SourcePaths).Distinct(StringComparer.OrdinalIgnoreCase).Where(p => !fileExists(p)).ToList();
        foreach (var path in missing.Take(5))
        {
            issues.Add(new(IssueSeverity.Error, $"Datei fehlt: {path}"));
        }

        foreach (var (_, clip) in clips.Where(x => x.Clip.ColorTransform == ColorTransform.Lut))
        {
            if (string.IsNullOrWhiteSpace(clip.LutPath) || !fileExists(clip.LutPath))
            {
                issues.Add(new(IssueSeverity.Error, $"„{clip.Name}“: LUT-Datei fehlt ({clip.LutPath ?? "keine gewählt"})."));
            }
        }

        if (preset.AudioOnly && !clips.Any(x => x.Clip.HasAudio && !x.Clip.AudioMuted))
        {
            issues.Add(new(IssueSeverity.Error, "Es gibt keinen hörbaren Ton zum Exportieren."));
        }

        var (outWidth, outHeight) = ExportBuilder.OutputSize(project.Settings, preset);
        var seenMedia = new HashSet<Guid>();
        foreach (var (track, clip) in clips.Where(x => x.Track.Kind == TrackKind.Video))
        {
            var media = project.FindMedia(clip.MediaId);
            if (media is null || !seenMedia.Add(media.Id) || media.First.Video is not { } video)
            {
                continue;
            }

            var detection = media.Detection;
            if (detection.Transfer == ColorTransfer.CanonLog3 && clip.ColorTransform != ColorTransform.Lut)
            {
                issues.Add(new(IssueSeverity.Warning, $"„{clip.Name}“ ist Canon Log 3 ohne LUT – das Bild wirkt flau und blass."));
            }

            if (detection.Transfer is ColorTransfer.HdrPq or ColorTransfer.HdrHlg && clip.ColorTransform != ColorTransform.HdrToSdr)
            {
                issues.Add(new(IssueSeverity.Warning, $"„{clip.Name}“ ist HDR, wird aber ohne Tone-Mapping exportiert – Farben wirken falsch."));
            }

            if (detection.IsHighFrameRate && Math.Abs(clip.Speed - 1) < 1e-6)
            {
                issues.Add(new(IssueSeverity.Warning,
                    $"„{clip.Name}“ hat {video.FrameRate} fps, ist aber nicht als Zeitlupe konformiert – es wird in Echtzeit abgespielt (Bilder werden verworfen)."));
            }
            else if (!detection.IsHighFrameRate && Math.Abs(video.FrameRate.Value - rate.Value) > 0.01 && Math.Abs(clip.Speed - 1) < 1e-6)
            {
                issues.Add(new(IssueSeverity.Info, $"„{clip.Name}“ hat {video.FrameRate} fps und wird auf {rate} fps umgerechnet."));
            }

            var canvas = ExportBuilder.CanvasSize(project.Settings, preset);
            if (!preset.HasFixedSize && video.Height * 1.05 < canvas.Height && video.Width * 1.05 < canvas.Width)
            {
                issues.Add(new(IssueSeverity.Warning,
                    $"„{clip.Name}“ ({video.Width}×{video.Height}) wird hochskaliert – es wirkt weniger scharf als das übrige Material."));
            }
        }

        if (preset.HasFixedSize && Math.Abs((double)project.Settings.Width / project.Settings.Height - (double)outWidth / outHeight) > 0.01)
        {
            issues.Add(new(IssueSeverity.Info, preset.Reframe switch
            {
                ReframeMode.Fill => $"Das Bild wird für {outWidth}×{outHeight} mittig zugeschnitten.",
                ReframeMode.Fit => $"Das ganze Bild wird in {outWidth}×{outHeight} gezeigt, der Rand bleibt schwarz.",
                _ => $"Das ganze Bild wird in {outWidth}×{outHeight} gezeigt, der Rand mit unscharfem Bild gefüllt.",
            }));
        }

        if (preset.HdrOutput && !clips.Any(x => x.Clip.ColorTransform == ColorTransform.HdrToSdr))
        {
            issues.Add(new(IssueSeverity.Info,
                "Im Projekt ist kein HDR-Material – alles wird von SDR nach HDR umgerechnet. Für echtes HDR mit der R6 Mark II in „HDR PQ“ filmen."));
        }

        var outRate = ExportBuilder.OutputFrameRate(rate, preset);
        if (!outRate.Equals(rate))
        {
            issues.Add(new(IssueSeverity.Info, $"Die Bildrate wird für diese Plattform auf {outRate} fps gesenkt."));
        }

        return issues;
    }

    public static bool HasErrors(IEnumerable<PreflightIssue> issues) => issues.Any(i => i.Severity == IssueSeverity.Error);
}
