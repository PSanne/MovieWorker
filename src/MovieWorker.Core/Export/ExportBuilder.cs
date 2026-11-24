using System.Globalization;
using System.Text;
using MovieWorker.Core.Timeline;
using ClipHandles = MovieWorker.Core.Timeline.ClipHandlesAccess;

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

    /// <param name="project">Das Projekt.</param>
    /// <param name="preset">Die Export-Vorlage.</param>
    /// <param name="outputPath">Zieldatei.</param>
    /// <param name="rangeStart">Optional: erstes Bild des Bereichs (In-Marke).</param>
    /// <param name="rangeEnd">Optional: Ende des Bereichs (Out-Marke, exklusiv).</param>
    public static FfmpegJob Build(MovieProject project, ExportPreset preset, string outputPath, long? rangeStart = null, long? rangeEnd = null)
    {
        long totalFrames;
        if (rangeStart is { } rs && rangeEnd is { } re)
        {
            project = ExportRange.Extract(project, rs, re);
            totalFrames = re - rs;
        }
        else
        {
            totalFrames = project.DurationFrames;
        }

        project = Timeline.NestFlattener.Flatten(project);
        var settings = project.Settings;
        var rate = settings.FrameRate;
        var (width, height) = CanvasSize(settings, preset);
        var pixelFormat = preset.AudioOnly ? "yuv420p" : preset.PixelFormat;
        if (totalFrames <= 0)
        {
            throw new InvalidOperationException("Die Timeline ist leer – es gibt nichts zu exportieren.");
        }

        var inputs = new List<string>();
        var graph = new StringBuilder();
        var inputIndex = 0;
        var segments = new List<(int Index, long Length, long Transition, string Type)>();

        // ---------- Videospur V1 mit Kameraton ----------
        var videoTrack = project.VideoTracks.FirstOrDefault() ?? new TimelineTrack { Kind = TrackKind.Video };
        long cursor = 0;
        var segment = 0;
        foreach (var originalClip in videoTrack.OrderedClips.Where(c => c.SourcePaths.Count > 0 && c.HasVideo).Append(null))
        {
            var clip = originalClip;
            var gapEnd = clip?.StartFrame ?? totalFrames;
            if (gapEnd > cursor)
            {
                var gap = FfmpegArgs.Seconds(rate.FramesToSeconds(gapEnd - cursor));
                graph.Append(CultureInfo.InvariantCulture,
                    $"color=c=black:s={width}x{height}:r={rate.ToFfmpeg()}:d={gap},format={pixelFormat},setsar=1,settb=AVTB[v{segment}];");
                graph.Append(CultureInfo.InvariantCulture,
                    $"anullsrc=r=48000:cl=stereo,atrim=duration={gap}[a{segment}];");
                segments.Add((segment, gapEnd - cursor, 0, string.Empty));
                segment++;
            }

            if (clip is null)
            {
                break;
            }

            // Übergang: der Clip beginnt um die Übergangslänge früher (Vorlauf aus seinem Material) und
            // wird über das Ende des vorherigen Abschnitts geblendet – die Schnittzeiten bleiben gleich.
            var transition = 0L;
            var timelineClip = clip;
            if (clip.TransitionIn is { DurationFrames: > 0 } t && segments.Count > 0)
            {
                transition = Math.Min(Math.Min(t.DurationFrames, segments[^1].Length - 1), ClipHandles.Head(clip, rate));
                if (transition > 0)
                {
                    clip = clip.Clone();
                    clip.SourceIn -= rate.FramesToSeconds(transition) * clip.Speed;
                    clip.StartFrame -= transition;
                    clip.LengthFrames += transition;
                    clip.ShiftKeyframes(transition);
                    clip.FadeInFrames = 0;
                }
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
            vf.AddRange(TransformFilters.For(clip, width, height, rate));
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

            vf.Add($"format={pixelFormat}");
            vf.Add("settb=AVTB");
            graph.Append(CultureInfo.InvariantCulture, $"{videoIn}{string.Join(',', vf)}[v{segment}];");

            // Kameraton
            var useAudio = clip.HasAudio && !clip.AudioMuted && !videoTrack.Muted && Math.Abs(clip.Speed - 1) < 1e-9;
            if (useAudio)
            {
                var audioIn = ConcatParts(graph, parts, "a", $"ca{segment}");
                var af = new List<string> { "asetpts=PTS-STARTPTS", AudioFormat };
                af.AddRange(VolumeAndFades(clip, rate, videoTrack.VolumeDb));
                af.Add($"apad=whole_dur={durationText}");
                af.Add($"atrim=duration={durationText}");
                graph.Append(CultureInfo.InvariantCulture, $"{audioIn}{string.Join(',', af)}[a{segment}];");
            }
            else
            {
                graph.Append(CultureInfo.InvariantCulture, $"anullsrc=r=48000:cl=stereo,atrim=duration={durationText}[a{segment}];");
            }

            segments.Add((segment, timelineClip.LengthFrames, transition,
                transition > 0 ? Transition.FfmpegName(timelineClip.TransitionIn!.Type) : string.Empty));
            segment++;
            cursor = timelineClip.EndFrame;
        }

        JoinSegments(graph, segments, rate);

        // ---------- Titel ----------
        var titles = TitleRenderer.Filters(project, width, height);
        if (titles.Count > 0)
        {
            graph.Append(CultureInfo.InvariantCulture, $"[vjoin]{string.Join(',', titles)}[vcat];");
        }
        else
        {
            graph.Append("[vjoin]null[vcat];");
        }

        // ---------- Ausgabeformat (Hochformat, Quadrat …) und Bildrate ----------
        var outputRate = OutputFrameRate(rate, preset);
        graph.Append(CultureInfo.InvariantCulture, $"[vcat]{OutputStage(width, height, preset, rate, outputRate)}[vout];");

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
                af.AddRange(VolumeAndFades(clip, rate, track.VolumeDb));
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

        if (preset.AudioOnly)
        {
            graph.Append(";[vout]nullsink");
        }

        var args = new List<string> { "-y", "-v", "error", "-nostdin" };
        args.AddRange(inputs);
        args.AddRange(["-filter_complex", graph.ToString()]);
        if (!preset.AudioOnly)
        {
            args.AddRange(["-map", "[vout]"]);
        }

        args.AddRange(["-map", "[aout]"]);
        if (!preset.AudioOnly)
        {
            args.AddRange(preset.VideoArguments);
            args.AddRange(["-pix_fmt", preset.PixelFormat, "-r", outputRate.ToFfmpeg()]);
            args.AddRange(["-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709"]);
        }

        args.AddRange(preset.AudioArguments);
        args.AddRange(["-ar", "48000", "-progress", "pipe:1", "-nostats", outputPath]);
        return new FfmpegJob(args, rate.FramesToSeconds(totalFrames), outputPath);
    }

    /// <summary>
    /// Fügt die Abschnitte der Hauptspur zusammen: ohne Übergang per <c>concat</c>, mit Übergang per
    /// <c>xfade</c> (Bild) und <c>acrossfade</c> (Ton). Ergebnis: [vjoin] und [acam].
    /// </summary>
    private static void JoinSegments(StringBuilder graph, List<(int Index, long Length, long Transition, string Type)> segments, Time.FrameRate rate)
    {
        var pending = new List<(string V, string A)> { ($"[v{segments[0].Index}]", $"[a{segments[0].Index}]") };
        var total = segments[0].Length;
        var step = 0;

        (string V, string A) Flush(string? targetV = null, string? targetA = null)
        {
            if (pending.Count == 1 && targetV is null)
            {
                return pending[0];
            }

            var v = targetV ?? $"[jv{step}]";
            var a = targetA ?? $"[ja{step}]";
            step++;
            if (pending.Count == 1)
            {
                graph.Append(CultureInfo.InvariantCulture, $"{pending[0].V}null{v};{pending[0].A}anull{a};");
            }
            else
            {
                graph.Append(CultureInfo.InvariantCulture,
                    $"{string.Concat(pending.Select(p => p.V + p.A))}concat=n={pending.Count}:v=1:a=1{v}{a};");
            }

            return (v, a);
        }

        foreach (var seg in segments.Skip(1))
        {
            var next = ($"[v{seg.Index}]", $"[a{seg.Index}]");
            if (seg.Transition <= 0)
            {
                pending.Add(next);
                total += seg.Length;
                continue;
            }

            var (curV, curA) = Flush();
            var d = FfmpegArgs.Seconds(rate.FramesToSeconds(seg.Transition));
            var offset = FfmpegArgs.Seconds(rate.FramesToSeconds(total - seg.Transition));
            var xv = $"[xv{step}]";
            var xa = $"[xa{step}]";
            step++;
            graph.Append(CultureInfo.InvariantCulture,
                $"{curV}{next.Item1}xfade=transition={seg.Type}:duration={d}:offset={offset}{xv};");
            graph.Append(CultureInfo.InvariantCulture, $"{curA}{next.Item2}acrossfade=d={d}:c1=tri:c2=tri{xa};");
            pending = [(xv, xa)];
            total += seg.Length;
        }

        if (pending.Count == 1 && segments.Count == 1)
        {
            graph.Append(CultureInfo.InvariantCulture, $"{pending[0].V}null[vjoin];{pending[0].A}anull[acam];");
        }
        else if (pending.Count == 1)
        {
            graph.Append(CultureInfo.InvariantCulture, $"{pending[0].V}null[vjoin];{pending[0].A}anull[acam];");
        }
        else
        {
            Flush("[vjoin]", "[acam]");
        }
    }

    /// <summary>Endgültige Größe der exportierten Datei.</summary>
    public static (int Width, int Height) OutputSize(ProjectSettings settings, ExportPreset preset) =>
        preset.HasFixedSize ? (preset.OutputWidth!.Value, preset.OutputHeight!.Value) : CanvasSize(settings, preset);

    /// <summary>
    /// Größe, auf die alle Clips gebracht werden: die Projektauflösung, bei Vorlagen mit Höchstgröße proportional
    /// verkleinert; bei festen Ausgabegrößen (z. B. 1080×1920) nur so groß wie nötig (spart Rechenzeit). Immer gerade Zahlen.
    /// </summary>
    public static (int Width, int Height) CanvasSize(ProjectSettings settings, ExportPreset preset)
    {
        var limit = preset.HasFixedSize
            ? Math.Max(preset.OutputWidth!.Value, preset.OutputHeight!.Value)
            : preset.MaxHeight;
        if (limit is not { } max || settings.Height <= max)
        {
            return (settings.Width, settings.Height);
        }

        var width = (int)Math.Round(settings.Width * (double)max / settings.Height / 2) * 2;
        return (width, max);
    }

    /// <summary>Bildrate der Ausgabe – Vorlagen mit Obergrenze halbieren z. B. 119,88 auf 59,94.</summary>
    public static Time.FrameRate OutputFrameRate(Time.FrameRate rate, ExportPreset preset)
    {
        var result = rate;
        while (preset.MaxFrameRate is { } max && result.Value > max + 0.01)
        {
            result = new Time.FrameRate(result.Numerator, result.Denominator * 2);
        }

        return result;
    }

    /// <summary>Letzte Bildstufe: an ein festes Format anpassen (Hochformat, Quadrat) und ggf. Bildrate senken.</summary>
    public static string OutputStage(int canvasWidth, int canvasHeight, ExportPreset preset, Time.FrameRate rate, Time.FrameRate outputRate)
    {
        var steps = new List<string>();
        if (!outputRate.Equals(rate))
        {
            steps.Add($"fps={outputRate.ToFfmpeg()}");
        }

        if (preset.HasFixedSize)
        {
            var w = preset.OutputWidth!.Value;
            var h = preset.OutputHeight!.Value;
            var sameAspect = Math.Abs((double)canvasWidth / canvasHeight - (double)w / h) < 0.01;
            var fit = $"scale={w}:{h}:force_original_aspect_ratio=decrease:flags=lanczos,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2";
            var fill = $"scale={w}:{h}:force_original_aspect_ratio=increase:flags=lanczos,crop={w}:{h}";
            if (sameAspect)
            {
                steps.Add($"scale={w}:{h}:flags=lanczos");
            }
            else if (preset.Reframe == ReframeMode.Fill)
            {
                steps.Add(fill);
            }
            else if (preset.Reframe == ReframeMode.Fit)
            {
                steps.Add(fit);
            }
            else
            {
                // Unscharfer Hintergrund: Kopie bildfüllend + stark weichgezeichnet, darüber das ganze Bild.
                var pre = steps.Count > 0 ? string.Join(',', steps) + "," : string.Empty;
                return $"{pre}split[bgsrc][fgsrc];[bgsrc]{fill},boxblur=luma_radius=40:luma_power=2,eq=brightness=-0.08[bg];"
                    + $"[fgsrc]scale={w}:{h}:force_original_aspect_ratio=decrease:flags=lanczos[fg];[bg][fg]overlay=(W-w)/2:(H-h)/2,setsar=1,format={preset.PixelFormat}";
            }

            steps.Add("setsar=1");
        }

        steps.Add(preset.AudioOnly ? "null" : $"format={preset.PixelFormat}");
        return string.Join(',', steps);
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

    private static IEnumerable<string> VolumeAndFades(TimelineClip clip, Time.FrameRate rate, double trackDb)
    {
        if (TransformFilters.Volume(clip, trackDb, rate) is { } volume)
        {
            yield return volume;
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
