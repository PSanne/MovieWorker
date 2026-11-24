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
    /// <param name="loudness">Messwerte einer vorherigen Lautheitsmessung (für genaue Normalisierung in zwei Durchgängen).</param>
    /// <param name="measureLoudness">Nur die Lautheit messen (Ausgabe ins Nichts, Messwerte auf stderr).</param>
    public static FfmpegJob Build(MovieProject project, ExportPreset preset, string outputPath, long? rangeStart = null, long? rangeEnd = null,
        Audio.LoudnessMeasurement? loudness = null, bool measureLoudness = false)
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

        project = Timeline.MulticamResolver.Resolve(Timeline.NestFlattener.Flatten(project));
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
            if (FfmpegArgs.ColorFilter(clip, preset.HdrOutput) is { } color)
            {
                vf.Add(color);
            }

            vf.Add($"fps={rate.ToFfmpeg()}");
            vf.Add($"scale={width}:{height}:force_original_aspect_ratio=decrease:flags=lanczos");
            vf.Add($"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2");
            vf.Add("setsar=1");

            // Korrekturknoten (mit Masken) brauchen eigene Zweige im Filtergraph.
            if (clip.Nodes.Any(n => n.Enabled && !n.Grade.IsNeutral))
            {
                var preLabel = $"[pre{segment}]";
                graph.Append(CultureInfo.InvariantCulture, $"{videoIn}{string.Join(',', vf)}{preLabel};");
                videoIn = ApplyNodes(graph, preLabel, clip, width, height, rate, pixelFormat, $"n{segment}");
                vf.Clear();
            }

            // Effekte (Keying, Weichzeichnen, Verpixeln …), mit Masken über eigene Zweige.
            if (clip.HasActiveEffects)
            {
                if (vf.Count > 0)
                {
                    graph.Append(CultureInfo.InvariantCulture, $"{videoIn}{string.Join(',', vf)}[fx{segment}];");
                    videoIn = $"[fx{segment}]";
                    vf.Clear();
                }

                videoIn = ApplyEffects(graph, videoIn, clip, width, height, rate, $"e{segment}", keepAlpha: false);
                if (clip.Effects.Any(e => e.Enabled && e.IsKey))
                {
                    // Freigestellter Clip auf der Hauptspur: über Schwarz legen.
                    graph.Append(CultureInfo.InvariantCulture,
                        $"color=c=black:s={width}x{height}:r={rate.ToFfmpeg()},format={pixelFormat}[kb{segment}];[kb{segment}]{videoIn}overlay=shortest=1:format=auto,format={pixelFormat}[kv{segment}];");
                    videoIn = $"[kv{segment}]";
                }
            }

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
            var useAudio = clip.HasAudio && !clip.AudioMuted && project.IsAudible(videoTrack) && Math.Abs(clip.Speed - 1) < 1e-9;
            if (useAudio)
            {
                var audioIn = ConcatParts(graph, parts, "a", $"ca{segment}");
                var af = new List<string> { "asetpts=PTS-STARTPTS", AudioFormat };
                af.AddRange(VolumeAndFades(clip, rate, 0));
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

        // ---------- Obere Videospuren (Bild-in-Bild, B-Roll, Mischmodi) ----------
        var composite = "[vjoin]";
        var overlayAudio = new List<(TimelineClip Clip, TimelineTrack Track, List<int> Parts)>();
        var layer = 0;
        foreach (var track in project.VideoTracks.Skip(1).Where(t => !t.Muted))
        {
            foreach (var clip in track.OrderedClips.Where(c => c.SourcePaths.Count > 0 && c.HasVideo))
            {
                var parts = AddClipInputs(clip, rate, inputs, ref inputIndex);
                composite = AddOverlay(graph, composite, clip, parts, layer, width, height, rate, pixelFormat, preset.HdrOutput);
                if (clip.HasAudio && !clip.AudioMuted && Math.Abs(clip.Speed - 1) < 1e-9)
                {
                    overlayAudio.Add((clip, track, parts));
                }

                layer++;
            }
        }

        // ---------- Titel ----------
        var titles = TitleRenderer.Filters(project, width, height, hdr: preset.HdrOutput);
        if (titles.Count > 0)
        {
            graph.Append(CultureInfo.InvariantCulture, $"{composite}{string.Join(',', titles)}[vcat];");
        }
        else
        {
            graph.Append(CultureInfo.InvariantCulture, $"{composite}null[vcat];");
        }

        // ---------- Ausgabeformat (Hochformat, Quadrat …) und Bildrate ----------
        var outputRate = OutputFrameRate(rate, preset);
        graph.Append(CultureInfo.InvariantCulture, $"[vcat]{OutputStage(width, height, preset, rate, outputRate)}[vout];");

        // ---------- Mischpult: je Spur ein Kanalzug, dann Summe ----------
        var total = FfmpegArgs.Seconds(rate.FramesToSeconds(totalFrames));
        var buses = new List<(TimelineTrack Track, string Label)>();
        var busIndex = 0;

        string ChannelStrip(TimelineTrack track, string input)
        {
            var chain = new List<string>(track.Audio.Filters(preset.SurroundAudio));
            if (Math.Abs(track.VolumeDb) > 1e-6)
            {
                chain.Add($"volume={Number(track.VolumeDb)}dB");
            }

            var label = $"[bus{busIndex++}]";
            graph.Append(CultureInfo.InvariantCulture, $"{input}{(chain.Count == 0 ? "anull" : string.Join(',', chain))}{label};");
            return label;
        }

        buses.Add((videoTrack, ChannelStrip(videoTrack, "[acam]")));

        var audioClip = 0;
        var trackClips = project.AudioTracks.Where(project.IsAudible)
            .Select(t => (Track: t, Clips: t.OrderedClips.Where(c => c.HasAudio && !c.AudioMuted && c.SourcePaths.Count > 0)
                .Select(c => (Clip: c, Parts: AddClipInputs(c, rate, inputs, ref inputIndex))).ToList()))
            .Concat(overlayAudio.Where(o => project.IsAudible(o.Track)).GroupBy(o => o.Track)
                .Select(g => (Track: g.Key, Clips: g.Select(o => (o.Clip, o.Parts)).ToList())))
            .ToList();
        foreach (var (track, clips) in trackClips.Where(t => t.Clips.Count > 0))
        {
            var labels = new List<string>();
            foreach (var (clip, parts) in clips)
            {
                var audioIn = ConcatParts(graph, parts, "a", $"ta{audioClip}");
                var duration = FfmpegArgs.Seconds(rate.FramesToSeconds(clip.LengthFrames));
                var delayMs = (long)Math.Round(rate.FramesToSeconds(clip.StartFrame) * 1000);
                var af = new List<string> { "asetpts=PTS-STARTPTS", AudioFormat };
                if (Math.Abs(clip.Speed - 1) > 1e-9 && clip.Speed is >= 0.5 and <= 2)
                {
                    af.Add($"atempo={Number(clip.Speed)}");
                }

                af.Add($"atrim=duration={duration}");
                af.AddRange(VolumeAndFades(clip, rate, 0));
                af.Add($"adelay=delays={delayMs}:all=1");
                graph.Append(CultureInfo.InvariantCulture, $"{audioIn}{string.Join(',', af)}[t{audioClip}];");
                labels.Add($"[t{audioClip}]");
                audioClip++;
            }

            var raw = $"[raw{busIndex}]";
            graph.Append(labels.Count == 1
                ? $"{labels[0]}apad=whole_dur={total},atrim=duration={total}{raw};"
                : string.Create(CultureInfo.InvariantCulture, $"{string.Concat(labels)}amix=inputs={labels.Count}:duration=longest:normalize=0,apad=whole_dur={total},atrim=duration={total}{raw};"));
            buses.Add((track, ChannelStrip(track, raw)));
        }

        // Auto-Ducking: Spur wird leiser, solange auf der Quellspur Ton ist (Sidechain-Kompressor).
        var ducked = buses.Where(b => b.Track.Audio.DuckBy is { } source && buses.Any(o => o.Track.Id == source && o.Track.Id != b.Track.Id)).ToList();
        foreach (var source in ducked.Select(d => d.Track.Audio.DuckBy!.Value).Distinct())
        {
            var index = buses.FindIndex(b => b.Track.Id == source);
            var targets = ducked.Where(d => d.Track.Audio.DuckBy == source).ToList();
            var main = $"[dm{index}]";
            var sidechains = targets.Select((_, i) => $"[sc{index}_{i}]").ToList();
            graph.Append(CultureInfo.InvariantCulture, $"{buses[index].Label}asplit={targets.Count + 1}{main}{string.Concat(sidechains)};");
            buses[index] = (buses[index].Track, main);
            for (var i = 0; i < targets.Count; i++)
            {
                var targetIndex = buses.FindIndex(b => b.Track.Id == targets[i].Track.Id);
                var ratio = Math.Clamp(targets[i].Track.Audio.DuckAmountDb / 2, 2, 20);
                var output = $"[dk{targetIndex}]";
                graph.Append(CultureInfo.InvariantCulture,
                    $"{buses[targetIndex].Label}{sidechains[i]}sidechaincompress=threshold=0.03:ratio={Number(ratio)}:attack=20:release=500:makeup=1{output};");
                buses[targetIndex] = (buses[targetIndex].Track, output);
            }
        }

        // Summe (Master): Lautstärke, Lautheit, Limiter
        var master = new List<string>();
        var stillOnly = preset.IsStillImage;
        if (Math.Abs(project.Master.VolumeDb) > 1e-6 && !stillOnly)
        {
            master.Add($"volume={Number(project.Master.VolumeDb)}dB");
        }

        var target = project.Master.LoudnessTarget ?? -14;
        if (measureLoudness)
        {
            master.Add($"loudnorm=I={Number(target)}:TP=-1:LRA=11:print_format=json");
        }
        else if (project.Master.LoudnessTarget is { } lufs && !stillOnly)
        {
            master.Add(loudness is { } m
                ? $"loudnorm=I={Number(lufs)}:TP=-1:LRA=11:measured_I={Number(m.IntegratedLufs)}:measured_TP={Number(m.TruePeakDb)}:measured_LRA={Number(m.Range)}:measured_thresh={Number(m.Threshold)}:offset={Number(m.Offset)}:linear=true"
                : $"loudnorm=I={Number(lufs)}:TP=-1:LRA=11");
            master.Add("aresample=48000");
        }

        if (project.Master.Limiter && !measureLoudness && !stillOnly)
        {
            master.Add("alimiter=limit=0.944:level=disabled");
        }

        var mix = buses.Count == 1
            ? $"{buses[0].Label}anull"
            : $"{string.Concat(buses.Select(b => b.Label))}amix=inputs={buses.Count}:duration=first:normalize=0";
        if (preset.SurroundAudio && !measureLoudness)
        {
            // Subwoofer-Kanal nur tiefe Frequenzen (wie im Kino üblich).
            graph.Append(CultureInfo.InvariantCulture, $"{mix}{(master.Count == 0 ? string.Empty : "," + string.Join(',', master))}[amaster];");
            graph.Append("[amaster]channelsplit=channel_layout=5.1[sFL][sFR][sFC][sLFE][sBL][sBR];[sLFE]lowpass=f=120[sLFEf];");
            graph.Append("[sFL][sFR][sFC][sLFEf][sBL][sBR]join=inputs=6:channel_layout=5.1[aout]");
        }
        else
        {
            graph.Append(CultureInfo.InvariantCulture, $"{mix}{(master.Count == 0 ? string.Empty : "," + string.Join(',', master))}[aout]");
        }

        if (measureLoudness)
        {
            graph.Append(";[vout]nullsink");
            var measureArgs = new List<string> { "-v", "info", "-nostdin", "-hide_banner" };
            measureArgs.AddRange(inputs);
            measureArgs.AddRange(["-filter_complex", graph.ToString(), "-map", "[aout]", "-f", "null", "-"]);
            return new FfmpegJob(measureArgs, rate.FramesToSeconds(totalFrames), "-");
        }

        if (preset.AudioOnly)
        {
            graph.Append(";[vout]nullsink");
        }

        if (preset.IsStillImage)
        {
            // Ein einzelnes Bild auf die Standardausgabe (kein Fortschritt dorthin, sonst wäre das PNG kaputt).
            graph.Append(";[aout]anullsink");
            var stillArgs = new List<string> { "-v", "error", "-nostdin" };
            stillArgs.AddRange(inputs);
            stillArgs.AddRange(["-filter_complex", graph.ToString(), "-map", "[vout]", "-frames:v", "1"]);
            stillArgs.AddRange(preset.Id == "stillraw"
                ? ["-f", "rawvideo", "-pix_fmt", "rgb24", "-"]
                : ["-f", "image2pipe", "-vcodec", "png", "-"]);
            return new FfmpegJob(stillArgs, rate.FramesToSeconds(totalFrames), "-");
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
            args.AddRange(preset.HdrOutput
                ? ["-color_primaries", "bt2020", "-color_trc", "smpte2084", "-colorspace", "bt2020nc"]
                : ["-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709"]);
        }

        args.AddRange(preset.AudioArguments);
        args.AddRange(["-ar", "48000", "-progress", "pipe:1", "-nostats", outputPath]);
        return new FfmpegJob(args, rate.FramesToSeconds(totalFrames), outputPath);
    }

    /// <summary>Misst die Lautheit der fertigen Mischung (ffmpeg gibt die Werte auf stderr aus).</summary>
    public static FfmpegJob BuildLoudnessMeasurement(MovieProject project, long? rangeStart = null, long? rangeEnd = null, bool surround = false) =>
        Build(project, ExportPreset.Find(surround ? "audio-wav-51" : "audio-wav")!, "-", rangeStart, rangeEnd, measureLoudness: true);

    /// <summary>Exaktes Vorschaubild eines Timeline-Bildes – über dieselbe Kette wie der Export.</summary>
    public static FfmpegJob BuildStill(MovieProject project, long frame, int height) =>
        Build(project, ExportPreset.StillImage(height), "-", frame, frame + 1);

    /// <summary>Rohbild (RGB24) eines Timeline-Bildes in <paramref name="height"/> Zeilen – für Messinstrumente.</summary>
    public static FfmpegJob BuildRawFrame(MovieProject project, long frame, int height) =>
        Build(project, ExportPreset.RawImage(height), "-", frame, frame + 1);

    /// <summary>
    /// Legt einen Clip einer oberen Videospur über das bisherige Bild: als Bild-in-Bild (Größe, Lage, Zuschnitt,
    /// Drehung, Deckkraft; Lage auch per Keyframes) oder bildfüllend mit Mischmodus. Liefert das neue Label.
    /// </summary>
    private static string AddOverlay(StringBuilder graph, string below, TimelineClip clip, List<int> parts, int layer,
        int width, int height, Time.FrameRate rate, string pixelFormat, bool hdrOutput)
    {
        string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        var start = rate.FramesToSeconds(clip.StartFrame);
        var duration = rate.FramesToSeconds(clip.LengthFrames);
        var end = start + duration;
        var videoIn = ConcatParts(graph, parts, "v", $"ocv{layer}");
        var vf = new List<string> { $"setpts=(PTS-STARTPTS)/{N(clip.Speed)}" };
        if (FfmpegArgs.ColorFilter(clip, hdrOutput) is { } color)
        {
            vf.Add(color);
        }

        // Korrekturknoten bei oberen Spuren: ohne Maske (Masken gelten auf der Hauptspur)
        foreach (var node in clip.Nodes.Where(n => n.Enabled && !n.Grade.IsNeutral))
        {
            vf.Add($"lut3d=file={FfmpegArgs.EscapeFilterPath(Color.GradeLutCache.Ensure(node.Grade))}:interp=tetrahedral");
        }

        vf.Add($"fps={rate.ToFfmpeg()}");
        if (clip.HasActiveEffects)
        {
            // Effekte auf Bildgröße rechnen (Rand durchsichtig), damit Masken im Bild an der richtigen Stelle sitzen.
            vf.Add($"scale={width}:{height}:force_original_aspect_ratio=decrease:flags=lanczos");
            vf.Add("format=yuva420p");
            vf.Add($"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black@0");
            vf.Add("setsar=1");
            graph.Append(CultureInfo.InvariantCulture, $"{videoIn}{string.Join(',', vf)}[oe{layer}];");
            videoIn = ApplyEffects(graph, $"[oe{layer}]", clip, width, height, rate, $"oe{layer}x", keepAlpha: true);
            vf.Clear();
            vf.Add("setsar=1");
        }

        var t = clip.Transform;
        var output = $"[comp{layer}]";

        if (t.Blend != BlendMode.Normal)
        {
            // Mischmodus: bildfüllend verrechnen (gbrp für saubere Farbmathematik).
            vf.Add($"scale={width}:{height}:force_original_aspect_ratio=decrease:flags=lanczos");
            vf.Add($"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2");
            vf.Add("setsar=1");
            vf.AddRange(TransformFilters.CropAndFlip(t, width, height, padBack: true));
            vf.Add($"trim=duration={N(duration)}");
            vf.Add($"tpad=start_duration={N(start)}:color=black");
            vf.Add("format=gbrp");
            graph.Append(CultureInfo.InvariantCulture, $"{videoIn}{string.Join(',', vf)}[ov{layer}];");
            graph.Append(CultureInfo.InvariantCulture, $"{below}format=gbrp[ob{layer}];");
            graph.Append(CultureInfo.InvariantCulture,
                $"[ob{layer}][ov{layer}]blend=all_mode={BlendModeInfo.FfmpegName(t.Blend)}:all_opacity={N(Math.Clamp(t.Opacity, 0, 100) / 100)}:enable='between(t,{N(start)},{N(end)})',format={pixelFormat}{output};");
            return output;
        }

        // Bild-in-Bild / B-Roll
        var scale = Math.Clamp(clip.IsAnimated(AnimatedProperty.Scale) ? clip.ValueAt(AnimatedProperty.Scale, 0) : t.Scale, 1, 400) / 100;
        vf.Add($"scale={Even(width * scale)}:{Even(height * scale)}:force_original_aspect_ratio=decrease:flags=lanczos");
        vf.Add("setsar=1");
        vf.AddRange(TransformFilters.CropAndFlip(t, Even(width * scale), Even(height * scale), padBack: false));
        vf.Add("format=yuva420p");
        var rotation = clip.IsAnimated(AnimatedProperty.Rotation) ? clip.ValueAt(AnimatedProperty.Rotation, 0) : t.Rotation;
        if (Math.Abs(rotation) > 1e-6)
        {
            vf.Add($"rotate=a={N(rotation)}*PI/180:c=none:ow='hypot(iw,ih)':oh=ow");
        }

        if (clip.KeyframesFor(AnimatedProperty.Opacity) is { } opacityKeys)
        {
            var o = opacityKeys.ToExpression("T", f => rate.FramesToSeconds(f));
            vf.Add($"geq=lum='lum(X,Y)':cb='cb(X,Y)':cr='cr(X,Y)':a='alpha(X,Y)*max(0,min(1,({o})/100))'");
        }
        else if (t.Opacity < 99.999)
        {
            vf.Add($"colorchannelmixer=aa={N(Math.Clamp(t.Opacity, 0, 100) / 100)}");
        }

        vf.Add($"trim=duration={N(duration)}");
        if (clip.FadeInFrames > 0)
        {
            vf.Add($"fade=t=in:st=0:d={N(rate.FramesToSeconds(clip.FadeInFrames))}:alpha=1");
        }

        if (clip.FadeOutFrames > 0)
        {
            var d = rate.FramesToSeconds(clip.FadeOutFrames);
            vf.Add($"fade=t=out:st={N(duration - d)}:d={N(d)}:alpha=1");
        }

        vf.Add($"setpts=PTS-STARTPTS+{N(start)}/TB");
        graph.Append(CultureInfo.InvariantCulture, $"{videoIn}{string.Join(',', vf)}[ov{layer}];");

        string Pos(AnimatedProperty property, double size) =>
            clip.KeyframesFor(property) is { } keys
                ? $"({keys.ToExpression($"(t-{N(start)})", f => rate.FramesToSeconds(f))})/100*{N(size)}"
                : N(t.Get(property) / 100 * size);
        var x = $"(W-w)/2+{Pos(AnimatedProperty.PositionX, width)}";
        var y = $"(H-h)/2+{Pos(AnimatedProperty.PositionY, height)}";
        graph.Append(CultureInfo.InvariantCulture,
            $"{below}[ov{layer}]overlay=x='{x}':y='{y}':eval=frame:enable='between(t,{N(start)},{N(end)})':eof_action=pass,format={pixelFormat}{output};");
        return output;
    }

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);

    /// <summary>
    /// Korrekturknoten nacheinander: ohne Maske als LUT in der Kette, mit Maske über einen Zweig
    /// (korrigiertes Bild + weiche Maske als Alphakanal über das Original gelegt). Liefert das Ausgangslabel.
    /// </summary>
    private static string ApplyNodes(StringBuilder graph, string input, TimelineClip clip, int width, int height, Time.FrameRate rate,
        string pixelFormat, string id)
    {
        var current = input;
        var pending = new List<string>();
        var k = 0;
        foreach (var node in clip.Nodes.Where(n => n.Enabled && !n.Grade.IsNeutral))
        {
            var lut = $"lut3d=file={FfmpegArgs.EscapeFilterPath(Color.GradeLutCache.Ensure(node.Grade))}:interp=tetrahedral";
            if (node.Mask is not { } mask)
            {
                pending.Add(lut);
                continue;
            }

            var flushed = $"[{id}f{k}]";
            graph.Append(CultureInfo.InvariantCulture, $"{current}{(pending.Count == 0 ? "null" : string.Join(',', pending))}{flushed};");
            pending.Clear();
            var (mw, mh) = (Math.Max(2, width / 4 / 2 * 2), Math.Max(2, height / 4 / 2 * 2));
            graph.Append(CultureInfo.InvariantCulture, $"{flushed}split=3[{id}b{k}][{id}g{k}][{id}m{k}];");
            graph.Append(CultureInfo.InvariantCulture, $"[{id}g{k}]{lut},format=yuva444p[{id}gl{k}];");
            graph.Append(CultureInfo.InvariantCulture,
                $"[{id}m{k}]scale={mw}:{mh},format=gray,geq=lum='255*({mask.ToExpression(rate)})',scale={width}:{height}:flags=bilinear[{id}mk{k}];");
            graph.Append(CultureInfo.InvariantCulture, $"[{id}gl{k}][{id}mk{k}]alphamerge[{id}ga{k}];");
            var merged = $"[{id}o{k}]";
            graph.Append(CultureInfo.InvariantCulture, $"[{id}b{k}][{id}ga{k}]overlay=format=auto,format={pixelFormat}{merged};");
            current = merged;
            k++;
        }

        if (pending.Count > 0 || current == input)
        {
            var output = $"[{id}end]";
            graph.Append(CultureInfo.InvariantCulture, $"{current}{(pending.Count == 0 ? "null" : string.Join(',', pending))}{output};");
            current = output;
        }

        return current;
    }

    /// <summary>
    /// Effektkette: Effekte ohne Maske direkt hintereinander, mit Maske über einen Zweig (Effekt nur innerhalb der Maske).
    /// Bei Keys mit Maske („Garbage Matte“) wird der Alphakanal des Keys mit der Maske multipliziert.
    /// </summary>
    private static string ApplyEffects(StringBuilder graph, string input, TimelineClip clip, int width, int height, Time.FrameRate rate,
        string id, bool keepAlpha)
    {
        var current = input;
        var pending = new List<string>();
        var k = 0;
        var (mw, mh) = (Math.Max(2, width / 4 / 2 * 2), Math.Max(2, height / 4 / 2 * 2));
        string Flush()
        {
            var label = $"[{id}f{k}]";
            graph.Append(CultureInfo.InvariantCulture, $"{current}{(pending.Count == 0 ? "null" : string.Join(',', pending))}{label};");
            pending.Clear();
            return label;
        }

        foreach (var effect in clip.Effects.Where(e => e.Enabled))
        {
            var filter = effect.Filter(width, height);
            if (effect.Mask is not { } mask)
            {
                pending.Add(filter);
                continue;
            }

            var flushed = Flush();
            var maskChain = $"scale={mw}:{mh},format=gray,geq=lum='255*({mask.ToExpression(rate)})',scale={width}:{height}:flags=bilinear";
            var output = $"[{id}o{k}]";
            if (effect.IsKey)
            {
                graph.Append(CultureInfo.InvariantCulture, $"{flushed}split=2[{id}kk{k}][{id}km{k}];");
                graph.Append(CultureInfo.InvariantCulture, $"[{id}kk{k}]{filter},split=2[{id}kc{k}][{id}kx{k}];");
                graph.Append(CultureInfo.InvariantCulture, $"[{id}km{k}]{maskChain}[{id}kmk{k}];");
                graph.Append(CultureInfo.InvariantCulture, $"[{id}kx{k}]alphaextract[{id}kal{k}];");
                graph.Append(CultureInfo.InvariantCulture, $"[{id}kal{k}][{id}kmk{k}]blend=all_mode=multiply[{id}kam{k}];");
                graph.Append(CultureInfo.InvariantCulture, $"[{id}kc{k}][{id}kam{k}]alphamerge,format=yuva420p{output};");
            }
            else
            {
                graph.Append(CultureInfo.InvariantCulture, $"{flushed}split=3[{id}b{k}][{id}g{k}][{id}m{k}];");
                graph.Append(CultureInfo.InvariantCulture, $"[{id}g{k}]{filter},format=yuva444p[{id}gl{k}];");
                graph.Append(CultureInfo.InvariantCulture, $"[{id}m{k}]{maskChain}[{id}mk{k}];");
                graph.Append(CultureInfo.InvariantCulture, $"[{id}gl{k}][{id}mk{k}]alphamerge[{id}ga{k}];");
                graph.Append(CultureInfo.InvariantCulture,
                    $"[{id}b{k}][{id}ga{k}]overlay=format=auto{(keepAlpha ? ",format=yuva420p" : string.Empty)}{output};");
            }

            current = output;
            k++;
        }

        if (pending.Count > 0)
        {
            current = Flush();
        }

        return current;
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
