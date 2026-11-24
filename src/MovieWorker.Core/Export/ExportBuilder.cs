using System.Globalization;
using System.Text;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Export;

/// <summary>Ein fertiger ffmpeg-Auftrag.</summary>
public sealed record FfmpegJob(IReadOnlyList<string> Arguments, double DurationSeconds, string OutputPath);

/// <summary>
/// Übersetzt die Timeline in einen einzigen ffmpeg-Aufruf (filter_complex):
/// <list type="bullet">
///   <item>Videospur V1 von vorne bis hinten: Clips und schwarze Lücken werden aneinandergehängt,
///         jeder Clip auf Projektauflösung/-bildrate gebracht, Farben umgerechnet (LUT/Tone-Mapping), Blenden gesetzt.</item>
///   <item>Kameraton der V1-Clips läuft synchron mit; Zeitlupen-Clips und stumme Clips liefern Stille.</item>
///   <item>Alle Tonspuren werden an ihrer Position dazugemischt.</item>
/// </list>
/// Jeder Clip wird mit schnellem Eingangs-Seek (<c>-ss</c>/<c>-t</c>) nur so weit gelesen, wie er gebraucht wird.
/// </summary>
public static class ExportBuilder
{
    private const string AudioFormat = "aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo";

    public static FfmpegJob Build(MovieProject project, ExportPreset preset, string outputPath)
    {
        var settings = project.Settings;
        var rate = settings.FrameRate;
        var (width, height) = OutputSize(settings, preset);
        var totalFrames = project.DurationFrames;
        if (totalFrames <= 0)
        {
            throw new InvalidOperationException("Die Timeline ist leer – es gibt nichts zu exportieren.");
        }

        var inputs = new List<string>();
        var graph = new StringBuilder();
        var inputIndex = 0;
        var segmentLabels = new List<string>();

        // ---------- Videospur V1 mit Kameraton ----------
        var videoTrack = project.VideoTracks.FirstOrDefault() ?? new TimelineTrack { Kind = TrackKind.Video };
        long cursor = 0;
        var segment = 0;
        foreach (var clip in videoTrack.OrderedClips.Append(null))
        {
            var gapEnd = clip?.StartFrame ?? totalFrames;
            if (gapEnd > cursor)
            {
                var gap = FfmpegArgs.Seconds(rate.FramesToSeconds(gapEnd - cursor));
                graph.Append(CultureInfo.InvariantCulture,
                    $"color=c=black:s={width}x{height}:r={rate.ToFfmpeg()}:d={gap},format={preset.PixelFormat},setsar=1[v{segment}];");
                graph.Append(CultureInfo.InvariantCulture,
                    $"anullsrc=r=48000:cl=stereo,atrim=duration={gap}[a{segment}];");
                segmentLabels.Add($"[v{segment}][a{segment}]");
                segment++;
            }

            if (clip is null)
            {
                break;
            }

            var duration = rate.FramesToSeconds(clip.LengthFrames);
            var durationText = FfmpegArgs.Seconds(duration);
            var parts = AddClipInputs(clip, rate, inputs, ref inputIndex);

            // Bild
            var videoIn = ConcatParts(graph, parts, "v", $"cv{segment}");
            var vf = new List<string> { $"setpts=(PTS-STARTPTS)/{Number(clip.Speed)}" };
            if (FfmpegArgs.ColorFilter(clip) is { } color)
            {
                vf.Add(color);
            }

            vf.Add($"fps={rate.ToFfmpeg()}");
            vf.Add($"scale={width}:{height}:force_original_aspect_ratio=decrease:flags=lanczos");
            vf.Add($"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2");
            vf.Add("setsar=1");
            vf.Add($"trim=duration={durationText}");
            vf.Add($"tpad=stop_mode=clone:stop_duration={durationText}");
            vf.Add($"trim=duration={durationText}");
            if (clip.FadeInFrames > 0)
            {
                vf.Add($"fade=t=in:st=0:d={FfmpegArgs.Seconds(rate.FramesToSeconds(clip.FadeInFrames))}");
            }

            if (clip.FadeOutFrames > 0)
            {
                var d = rate.FramesToSeconds(clip.FadeOutFrames);
                vf.Add($"fade=t=out:st={FfmpegArgs.Seconds(duration - d)}:d={FfmpegArgs.Seconds(d)}");
            }

            vf.Add($"format={preset.PixelFormat}");
            graph.Append(CultureInfo.InvariantCulture, $"{videoIn}{string.Join(',', vf)}[v{segment}];");

            // Kameraton
            var useAudio = clip.HasAudio && !clip.AudioMuted && !videoTrack.Muted && Math.Abs(clip.Speed - 1) < 1e-9;
            if (useAudio)
            {
                var audioIn = ConcatParts(graph, parts, "a", $"ca{segment}");
                var af = new List<string> { "asetpts=PTS-STARTPTS", AudioFormat };
                af.AddRange(VolumeAndFades(clip, rate, clip.VolumeDb + videoTrack.VolumeDb));
                af.Add($"apad=whole_dur={durationText}");
                af.Add($"atrim=duration={durationText}");
                graph.Append(CultureInfo.InvariantCulture, $"{audioIn}{string.Join(',', af)}[a{segment}];");
            }
            else
            {
                graph.Append(CultureInfo.InvariantCulture, $"anullsrc=r=48000:cl=stereo,atrim=duration={durationText}[a{segment}];");
            }

            segmentLabels.Add($"[v{segment}][a{segment}]");
            segment++;
            cursor = clip.EndFrame;
        }

        graph.Append(CultureInfo.InvariantCulture,
            $"{string.Concat(segmentLabels)}concat=n={segmentLabels.Count}:v=1:a=1[vout][acam];");

        // ---------- Tonspuren ----------
        var mixLabels = new List<string> { "[acam]" };
        var audioClip = 0;
        foreach (var track in project.AudioTracks.Where(t => !t.Muted))
        {
            foreach (var clip in track.OrderedClips.Where(c => c.HasAudio && !c.AudioMuted))
            {
                var parts = AddClipInputs(clip, rate, inputs, ref inputIndex);
                var audioIn = ConcatParts(graph, parts, "a", $"ta{audioClip}");
                var duration = FfmpegArgs.Seconds(rate.FramesToSeconds(clip.LengthFrames));
                var delayMs = (long)Math.Round(rate.FramesToSeconds(clip.StartFrame) * 1000);
                var af = new List<string> { "asetpts=PTS-STARTPTS", AudioFormat };
                if (Math.Abs(clip.Speed - 1) > 1e-9 && clip.Speed is >= 0.5 and <= 2)
                {
                    af.Add($"atempo={Number(clip.Speed)}");
                }

                af.Add($"atrim=duration={duration}");
                af.AddRange(VolumeAndFades(clip, rate, clip.VolumeDb + track.VolumeDb));
                af.Add($"adelay=delays={delayMs}:all=1");
                graph.Append(CultureInfo.InvariantCulture, $"{audioIn}{string.Join(',', af)}[t{audioClip}];");
                mixLabels.Add($"[t{audioClip}]");
                audioClip++;
            }
        }

        if (mixLabels.Count == 1)
        {
            graph.Append("[acam]anull[aout]");
        }
        else
        {
            graph.Append(CultureInfo.InvariantCulture,
                $"{string.Concat(mixLabels)}amix=inputs={mixLabels.Count}:duration=first:normalize=0[aout]");
        }

        var args = new List<string> { "-y", "-v", "error", "-nostdin" };
        args.AddRange(inputs);
        args.AddRange(["-filter_complex", graph.ToString(), "-map", "[vout]", "-map", "[aout]"]);
        args.AddRange(preset.VideoArguments);
        args.AddRange(["-pix_fmt", preset.PixelFormat, "-r", rate.ToFfmpeg()]);
        args.AddRange(["-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709"]);
        args.AddRange(preset.AudioArguments);
        args.AddRange(["-ar", "48000", "-progress", "pipe:1", "-nostats", outputPath]);
        return new FfmpegJob(args, rate.FramesToSeconds(totalFrames), outputPath);
    }

    /// <summary>Ausgabegröße – bei Vorlagen mit Höchstgröße proportional verkleinert (immer gerade Zahlen).</summary>
    public static (int Width, int Height) OutputSize(ProjectSettings settings, ExportPreset preset)
    {
        if (preset.MaxHeight is not { } max || settings.Height <= max)
        {
            return (settings.Width, settings.Height);
        }

        var width = (int)Math.Round(settings.Width * (double)max / settings.Height / 2) * 2;
        return (width, max);
    }

    /// <summary>
    /// Legt die ffmpeg-Eingänge für einen Clip an. Bei geteilten Aufnahmen (4-GB-Grenze) werden nur die
    /// Dateien verwendet, die der Clip tatsächlich berührt – jeweils mit passendem Start und Länge.
    /// Die Teil-Längen werden aus der Gesamtlänge gleichmäßig geschätzt, wenn sie nicht bekannt sind.
    /// </summary>
    private static List<int> AddClipInputs(TimelineClip clip, Time.FrameRate rate, List<string> inputs, ref int inputIndex)
    {
        var result = new List<int>();
        var start = clip.SourceIn;
        var end = clip.SourceOut(rate);
        var partDurations = clip.PartDurations.Count == clip.SourcePaths.Count
            ? clip.PartDurations
            : clip.SourcePaths.Select(_ => clip.SourceDuration / Math.Max(1, clip.SourcePaths.Count)).ToList();

        double partStart = 0;
        for (var i = 0; i < clip.SourcePaths.Count; i++)
        {
            var partEnd = partStart + partDurations[i];
            var isLast = i == clip.SourcePaths.Count - 1;
            if (end > partStart && (start < partEnd || isLast))
            {
                var localIn = Math.Max(0, start - partStart);
                var localOut = isLast ? end - partStart : Math.Min(partDurations[i], end - partStart);
                inputs.AddRange(["-ss", FfmpegArgs.Seconds(localIn), "-t", FfmpegArgs.Seconds(localOut - localIn), "-i", clip.SourcePaths[i]]);
                result.Add(inputIndex++);
            }

            partStart = partEnd;
        }

        return result;
    }

    private static string ConcatParts(StringBuilder graph, List<int> parts, string kind, string label)
    {
        if (parts.Count == 1)
        {
            return $"[{parts[0]}:{kind}:0]";
        }

        var v = kind == "v" ? 1 : 0;
        var a = kind == "a" ? 1 : 0;
        graph.Append(CultureInfo.InvariantCulture,
            $"{string.Concat(parts.Select(p => $"[{p}:{kind}:0]"))}concat=n={parts.Count}:v={v}:a={a}[{label}];");
        return $"[{label}]";
    }

    private static IEnumerable<string> VolumeAndFades(TimelineClip clip, Time.FrameRate rate, double volumeDb)
    {
        if (Math.Abs(volumeDb) > 1e-6)
        {
            yield return $"volume={Number(volumeDb)}dB";
        }

        if (clip.FadeInFrames > 0)
        {
            yield return $"afade=t=in:st=0:d={FfmpegArgs.Seconds(rate.FramesToSeconds(clip.FadeInFrames))}";
        }

        if (clip.FadeOutFrames > 0)
        {
            var duration = rate.FramesToSeconds(clip.LengthFrames);
            var d = rate.FramesToSeconds(clip.FadeOutFrames);
            yield return $"afade=t=out:st={FfmpegArgs.Seconds(duration - d)}:d={FfmpegArgs.Seconds(d)}";
        }
    }

    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
