using MovieWorker.Core.Camera;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;

namespace MovieWorker.Core.Timeline;

/// <summary>
/// Alle Schnitt-Operationen auf der Timeline. Jede Operation ist rückgängig machbar und
/// lässt das Projekt in einem gültigen Zustand (keine überlappenden Clips auf einer Spur).
/// </summary>
public sealed class TimelineEditor
{
    public TimelineEditor(MovieProject? project = null)
    {
        Project = project ?? MovieProject.CreateDefault();
    }

    public MovieProject Project { get; private set; }

    public TimelineHistory History { get; } = new();

    /// <summary>Wird nach jeder Änderung (auch Rückgängig/Wiederholen) ausgelöst.</summary>
    public event EventHandler? Changed;

    public long Duration => Project.DurationFrames;

    public void Load(MovieProject project)
    {
        Project = project;
        History.Clear();
        OnChanged();
    }

    /// <summary>Nimmt Aufnahmen in den Medienpool auf (doppelte Dateien werden übersprungen). Nicht Teil von Rückgängig.</summary>
    public IReadOnlyList<MediaItem> AddMedia(IEnumerable<MediaItem> items)
    {
        var known = Project.Media.SelectMany(m => m.Parts).Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = items.Where(i => i.Parts.Count > 0 && !i.Parts.Any(p => known.Contains(p.Path))).ToList();
        Project.Media.AddRange(added);
        if (added.Count > 0)
        {
            OnChanged();
        }

        return added;
    }

    /// <summary>Entfernt eine Aufnahme aus dem Medienpool – nur, wenn sie in der Timeline nicht verwendet wird.</summary>
    public bool RemoveMedia(Guid mediaId)
    {
        if (Project.AllClips.Any(c => c.MediaId == mediaId))
        {
            return false;
        }

        var removed = Project.Media.RemoveAll(m => m.Id == mediaId) > 0;
        if (removed)
        {
            OnChanged();
        }

        return removed;
    }

    /// <summary>Meldet eine Änderung am Medienpool (z. B. Proxy fertig, Kamera geändert).</summary>
    public void NotifyMediaChanged() => OnChanged();

    /// <summary>Erzeugt einen Clip aus einem Eintrag im Medienpool.</summary>
    public TimelineClip CreateClip(MediaItem media, double sourceIn = 0, double? sourceOut = null)
    {
        var clip = CreateClip(media.Recording, sourceIn, sourceOut, media.CameraOverride);
        clip.MediaId = media.Id;
        clip.Name = media.Name;
        return clip;
    }

    /// <summary>Erzeugt einen Clip aus einer (ggf. geteilten) Aufnahme.</summary>
    public TimelineClip CreateClip(RecordingGroup recording, double sourceIn = 0, double? sourceOut = null, Camera.CanonCamera? camera = null)
    {
        var first = recording.First;
        var total = recording.TotalDuration.TotalSeconds;
        var outPoint = Math.Clamp(sourceOut ?? total, 0, total);
        var inPoint = Math.Clamp(sourceIn, 0, outPoint);
        var detection = CanonCameraDetector.Detect(first, camera);
        var rate = Project.Settings.FrameRate;

        return new TimelineClip
        {
            SourcePaths = recording.Parts.Select(p => p.Path).ToList(),
            PartDurations = recording.Parts.Select(p => p.Duration.TotalSeconds).ToList(),
            Name = recording.DisplayName,
            SourceDuration = total,
            SourceIn = inPoint,
            LengthFrames = Math.Max(1, rate.SecondsToFrames(outPoint - inPoint)),
            HasVideo = first.HasVideo,
            HasAudio = first.HasAudio,
            ColorTransform = detection.Transfer is ColorTransfer.HdrPq or ColorTransfer.HdrHlg
                ? ColorTransform.HdrToSdr
                : ColorTransform.None,
        };
    }

    /// <summary>Hängt einen Clip an das Ende einer Spur an.</summary>
    public void Append(TimelineTrack track, TimelineClip clip)
    {
        Modify("Clip einfügen", p =>
        {
            var target = p.Tracks.First(t => t.Id == track.Id);
            clip.StartFrame = target.Clips.Select(c => c.EndFrame).DefaultIfEmpty(0).Max();
            target.Clips.Add(clip);
            return true;
        });
    }

    /// <summary>Fügt einen Clip an einer Stelle ein – geht nur, wenn dort Platz ist.</summary>
    public bool Insert(TimelineTrack track, TimelineClip clip, long startFrame)
    {
        return Modify("Clip einfügen", p =>
        {
            var target = p.Tracks.First(t => t.Id == track.Id);
            if (!target.IsFree(startFrame, clip.LengthFrames))
            {
                return false;
            }

            clip.StartFrame = startFrame;
            target.Clips.Add(clip);
            return true;
        });
    }

    /// <summary>
    /// Fügt einen Videoclip ein (am Ende oder an <paramref name="start"/>) und – falls vorhanden – den dazu
    /// synchronisierten externen Ton auf einer freien Tonspur (bevorzugt A2). Ein einziger Rückgängig-Schritt.
    /// </summary>
    public bool InsertWithAudio(TimelineTrack track, TimelineClip clip, long? start, TimelineClip? audioClip, long audioOffsetFrames = 0)
    {
        return Modify("Clip einfügen", p =>
        {
            var target = p.Tracks.First(t => t.Id == track.Id);
            var position = start ?? target.Clips.Select(c => c.EndFrame).DefaultIfEmpty(0).Max();
            if (!target.IsFree(position, clip.LengthFrames))
            {
                return false;
            }

            clip.StartFrame = position;
            target.Clips.Add(clip);
            if (audioClip is not null)
            {
                audioClip.StartFrame = position + audioOffsetFrames;
                var audioTrack = p.AudioTracks.Skip(1).Concat(p.AudioTracks.Take(1))
                    .FirstOrDefault(t => t.IsFree(audioClip.StartFrame, audioClip.LengthFrames));
                if (audioTrack is null)
                {
                    audioTrack = new TimelineTrack { Kind = TrackKind.Audio, Name = $"A{p.AudioTracks.Count() + 1}" };
                    p.Tracks.Add(audioTrack);
                }

                audioTrack.Clips.Add(audioClip);
            }

            return true;
        });
    }

    /// <summary>
    /// Schnitt nach <paramref name="mode"/>: Anhängen, Einfügen (Ripple), Überschreiben, Ersetzen oder Oben platzieren.
    /// Optional kommt synchronisierter externer Ton mit (auf eine Tonspur, bevorzugt A2). Ein Rückgängig-Schritt.
    /// </summary>
    public bool Place(EditMode mode, Guid trackId, TimelineClip clip, long at, TimelineClip? linkedAudio = null,
        long audioOffsetFrames = 0, Guid? replaceClipId = null)
    {
        return Modify(EditModeInfo.Label(mode), p =>
        {
            var track = p.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track is null)
            {
                return false;
            }

            switch (mode)
            {
                case EditMode.Append:
                    at = track.Clips.Select(c => c.EndFrame).DefaultIfEmpty(0).Max();
                    break;
                case EditMode.Insert:
                    RippleInsertSpace(p, at, clip.LengthFrames);
                    break;
                case EditMode.Overwrite:
                    ClearRange(track, at, at + clip.LengthFrames, p.Settings.FrameRate);
                    break;
                case EditMode.Replace:
                {
                    var target = track.Clips.FirstOrDefault(c => c.Id == replaceClipId);
                    if (target is null)
                    {
                        return false;
                    }

                    at = target.StartFrame;
                    clip.LengthFrames = Math.Min(clip.LengthFrames, target.LengthFrames);
                    track.Clips.Remove(target);
                    break;
                }

                case EditMode.PlaceOnTop:
                {
                    var videoTracks = p.Tracks.Where(t => t.Kind == track.Kind).ToList();
                    var above = videoTracks.Skip(videoTracks.IndexOf(track) + 1).FirstOrDefault(t => t.IsFree(at, clip.LengthFrames));
                    if (above is null)
                    {
                        above = new TimelineTrack { Kind = track.Kind, Name = $"{(track.Kind == TrackKind.Video ? "V" : "A")}{videoTracks.Count + 1}" };
                        var lastIndex = p.Tracks.FindLastIndex(t => t.Kind == track.Kind);
                        p.Tracks.Insert(lastIndex + 1, above);
                    }

                    track = above;
                    break;
                }
            }

            if (at < 0 || !track.IsFree(at, clip.LengthFrames))
            {
                return false;
            }

            clip.StartFrame = at;
            track.Clips.Add(clip);

            if (linkedAudio is not null)
            {
                linkedAudio.StartFrame = at + audioOffsetFrames;
                var audioTracks = p.AudioTracks.Skip(1).Concat(p.AudioTracks.Take(1)).ToList();
                if (mode == EditMode.Overwrite && audioTracks.Count > 0)
                {
                    ClearRange(audioTracks[0], linkedAudio.StartFrame, linkedAudio.EndFrame, p.Settings.FrameRate);
                }

                var audioTrack = audioTracks.FirstOrDefault(t => t.IsFree(linkedAudio.StartFrame, linkedAudio.LengthFrames));
                if (audioTrack is null)
                {
                    audioTrack = new TimelineTrack { Kind = TrackKind.Audio, Name = $"A{p.AudioTracks.Count() + 1}" };
                    p.Tracks.Add(audioTrack);
                }

                audioTrack.Clips.Add(linkedAudio);
            }

            return true;
        });
    }

    /// <summary>Schafft auf allen Spuren Platz: Clips am Punkt werden geteilt, alles dahinter rückt nach.</summary>
    internal static void RippleInsertSpace(MovieProject project, long at, long length)
    {
        var rate = project.Settings.FrameRate;
        foreach (var track in project.Tracks)
        {
            foreach (var clip in track.Clips.Where(c => c.StartFrame < at && c.EndFrame > at).ToList())
            {
                var right = SplitClip(clip, at, rate);
                track.Clips.Add(right);
            }

            foreach (var clip in track.Clips.Where(c => c.StartFrame >= at))
            {
                clip.StartFrame += length;
            }
        }
    }

    /// <summary>Macht den Bereich [start, end) einer Spur frei: Clips werden gekürzt, geteilt oder entfernt.</summary>
    internal static void ClearRange(TimelineTrack track, long start, long end, Time.FrameRate rate)
    {
        foreach (var clip in track.Clips.Where(c => c.StartFrame < end && c.EndFrame > start).ToList())
        {
            if (clip.StartFrame >= start && clip.EndFrame <= end)
            {
                track.Clips.Remove(clip);
            }
            else if (clip.StartFrame < start && clip.EndFrame > end)
            {
                var right = SplitClip(clip, end, rate);
                track.Clips.Add(right);
                clip.LengthFrames = start - clip.StartFrame;
                clip.FadeOutFrames = 0;
            }
            else if (clip.StartFrame < start)
            {
                clip.LengthFrames = start - clip.StartFrame;
                clip.FadeOutFrames = Math.Min(clip.FadeOutFrames, clip.LengthFrames);
            }
            else
            {
                var cut = end - clip.StartFrame;
                clip.SourceIn += rate.FramesToSeconds(cut) * clip.Speed;
                clip.ShiftKeyframes(-cut);
                clip.StartFrame = end;
                clip.LengthFrames -= cut;
                clip.FadeInFrames = Math.Min(clip.FadeInFrames, clip.LengthFrames);
            }
        }
    }

    /// <summary>Teilt einen Clip; der linke Teil bleibt im Clip, der rechte wird zurückgegeben.</summary>
    internal static TimelineClip SplitClip(TimelineClip clip, long frame, Time.FrameRate rate)
    {
        var right = clip.Clone();
        right.Id = Guid.NewGuid();
        right.StartFrame = frame;
        right.LengthFrames = clip.EndFrame - frame;
        right.SourceIn = clip.SourceTimeAt(frame, rate);
        right.FadeInFrames = 0;
        right.ShiftKeyframes(-(frame - clip.StartFrame));
        clip.LengthFrames = frame - clip.StartFrame;
        clip.FadeOutFrames = 0;
        return right;
    }

    /// <summary>Teilt alle Clips an der Abspielposition (oder nur einen bestimmten Clip).</summary>
    public bool SplitAt(long frame, Guid? onlyClip = null)
    {
        return Modify("Teilen", p =>
        {
            var rate = p.Settings.FrameRate;
            var changed = false;
            foreach (var track in p.Tracks)
            {
                var clip = track.Clips.FirstOrDefault(c => c.StartFrame < frame && c.EndFrame > frame
                    && (onlyClip is null || c.Id == onlyClip));
                if (clip is null)
                {
                    continue;
                }

                var right = clip.Clone();
                right.Id = Guid.NewGuid();
                var leftLength = frame - clip.StartFrame;
                right.StartFrame = frame;
                right.LengthFrames = clip.LengthFrames - leftLength;
                right.SourceIn = clip.SourceTimeAt(frame, rate);
                right.FadeInFrames = 0;
                right.ShiftKeyframes(-leftLength);
                clip.LengthFrames = leftLength;
                clip.FadeOutFrames = 0;
                track.Clips.Add(right);
                changed = true;
            }

            return changed;
        });
    }

    /// <summary>Entfernt einen Clip. Mit <paramref name="ripple"/> rücken die folgenden Clips der Spur nach.</summary>
    public bool Delete(Guid clipId, bool ripple = false)
    {
        return Modify(ripple ? "Löschen und Lücke schließen" : "Löschen", p =>
        {
            if (p.FindClip(clipId) is not ({ } track, { } clip))
            {
                return false;
            }

            track.Clips.Remove(clip);
            if (ripple)
            {
                foreach (var later in track.Clips.Where(c => c.StartFrame >= clip.EndFrame))
                {
                    later.StartFrame -= clip.LengthFrames;
                }
            }

            return true;
        });
    }

    /// <summary>Verschiebt einen Clip (auch auf eine andere Spur gleicher Art). Überlappungen werden abgelehnt.</summary>
    public bool Move(Guid clipId, long newStart, Guid? targetTrackId = null)
    {
        return Modify("Verschieben", p =>
        {
            if (p.FindClip(clipId) is not ({ } track, { } clip))
            {
                return false;
            }

            var target = targetTrackId is { } id ? p.Tracks.FirstOrDefault(t => t.Id == id) : track;
            if (target is null || target.Kind != track.Kind || !target.IsFree(newStart, clip.LengthFrames, clip.Id))
            {
                return false;
            }

            if (clip.StartFrame == newStart && target == track)
            {
                return false;
            }

            track.Clips.Remove(clip);
            clip.StartFrame = newStart;
            target.Clips.Add(clip);
            return true;
        });
    }

    /// <summary>
    /// Verschiebt den Anfang eines Clips um <paramref name="deltaFrames"/> (positiv = kürzer).
    /// Der Rest des Clips bleibt an seiner Stelle; begrenzt durch Quelle und Nachbarclips.
    /// </summary>
    public bool TrimStart(Guid clipId, long deltaFrames)
    {
        return Modify("Anfang trimmen", p =>
        {
            if (p.FindClip(clipId) is not ({ } track, { } clip))
            {
                return false;
            }

            var rate = p.Settings.FrameRate;
            var maxExtend = (long)Math.Floor(clip.SourceIn / clip.Speed / rate.FrameDuration + 1e-6);
            var previousEnd = track.Clips.Where(c => c.Id != clip.Id && c.EndFrame <= clip.StartFrame)
                .Select(c => c.EndFrame).DefaultIfEmpty(0).Max();
            maxExtend = Math.Min(maxExtend, clip.StartFrame - previousEnd);
            var delta = Math.Clamp(deltaFrames, -maxExtend, clip.LengthFrames - 1);
            if (delta == 0)
            {
                return false;
            }

            clip.SourceIn = Math.Max(0, clip.SourceIn + rate.FramesToSeconds(delta) * clip.Speed);
            clip.StartFrame += delta;
            clip.LengthFrames -= delta;
            clip.ShiftKeyframes(-delta);
            return true;
        });
    }

    /// <summary>Verschiebt das Ende eines Clips um <paramref name="deltaFrames"/> (positiv = länger).</summary>
    public bool TrimEnd(Guid clipId, long deltaFrames)
    {
        return Modify("Ende trimmen", p =>
        {
            if (p.FindClip(clipId) is not ({ } track, { } clip))
            {
                return false;
            }

            var rate = p.Settings.FrameRate;
            var remainingSource = clip.SourceDuration - clip.SourceOut(rate);
            var maxExtend = (long)Math.Floor(remainingSource / clip.Speed / rate.FrameDuration + 1e-6);
            var nextStart = track.Clips.Where(c => c.Id != clip.Id && c.StartFrame >= clip.EndFrame)
                .Select(c => c.StartFrame).DefaultIfEmpty(long.MaxValue).Min();
            if (nextStart != long.MaxValue)
            {
                maxExtend = Math.Min(maxExtend, nextStart - clip.EndFrame);
            }

            var delta = Math.Clamp(deltaFrames, -(clip.LengthFrames - 1), Math.Max(0, maxExtend));
            if (delta == 0)
            {
                return false;
            }

            clip.LengthFrames += delta;
            return true;
        });
    }

    /// <summary>
    /// Ripple-Trimmen: Rand um <paramref name="delta"/> verschieben; alles dahinter rückt auf allen Spuren mit.
    /// Am Anfang heißt positives delta „kürzer“ (der Clip bleibt stehen, der Rest rückt nach vorne).
    /// </summary>
    public bool RippleTrim(Guid clipId, bool atStart, long delta)
    {
        return Modify("Ripple-Trimmen", p =>
        {
            if (p.FindClip(clipId) is not ({ } track, { } clip))
            {
                return false;
            }

            var rate = p.Settings.FrameRate;
            var oldEnd = clip.EndFrame;
            long shift;
            if (atStart)
            {
                var d = Math.Clamp(delta, -ClipHandles.Head(clip, rate), clip.LengthFrames - 1);
                if (d == 0)
                {
                    return false;
                }

                clip.SourceIn = Math.Max(0, clip.SourceIn + rate.FramesToSeconds(d) * clip.Speed);
                clip.LengthFrames -= d;
                clip.ShiftKeyframes(-d);
                shift = -d;
            }
            else
            {
                var d = Math.Clamp(delta, 1 - clip.LengthFrames, ClipHandles.Tail(clip, rate));
                if (d == 0)
                {
                    return false;
                }

                clip.LengthFrames += d;
                shift = d;
            }

            foreach (var other in p.AllClips.Where(c => c.Id != clip.Id && c.StartFrame >= oldEnd))
            {
                other.StartFrame += shift;
            }

            // Würde etwas auf einer anderen Spur überlappen, wird der Schritt verworfen.
            return !p.Tracks.Any(HasOverlap);
        });
    }

    /// <summary>
    /// Roll: verschiebt den Schnitt zwischen <paramref name="leftClipId"/> und dem direkt anschließenden Clip um
    /// <paramref name="delta"/> Bilder. Die Gesamtlänge bleibt gleich.
    /// </summary>
    public bool Roll(Guid leftClipId, long delta)
    {
        return Modify("Roll-Schnitt", p =>
        {
            if (p.FindClip(leftClipId) is not ({ } track, { } left))
            {
                return false;
            }

            var right = track.Clips.FirstOrDefault(c => c.StartFrame == left.EndFrame);
            if (right is null)
            {
                return false;
            }

            var rate = p.Settings.FrameRate;
            var d = Math.Clamp(delta,
                Math.Max(1 - left.LengthFrames, -ClipHandles.Head(right, rate)),
                Math.Min(right.LengthFrames - 1, ClipHandles.Tail(left, rate)));
            if (d == 0)
            {
                return false;
            }

            left.LengthFrames += d;
            right.StartFrame += d;
            right.LengthFrames -= d;
            right.SourceIn = Math.Max(0, right.SourceIn + rate.FramesToSeconds(d) * right.Speed);
            right.ShiftKeyframes(-d);
            return true;
        });
    }

    /// <summary>Slip: zeigt einen früheren/späteren Ausschnitt der Quelle; Position und Länge bleiben.</summary>
    public bool Slip(Guid clipId, long deltaFrames)
    {
        return Modify("Slip", p =>
        {
            if (p.FindClip(clipId) is not (_, { } clip))
            {
                return false;
            }

            var rate = p.Settings.FrameRate;
            var d = Math.Clamp(deltaFrames, -ClipHandles.Head(clip, rate), ClipHandles.Tail(clip, rate));
            if (d == 0)
            {
                return false;
            }

            clip.SourceIn = Math.Max(0, clip.SourceIn + rate.FramesToSeconds(d) * clip.Speed);
            return true;
        });
    }

    /// <summary>
    /// Slide: verschiebt einen Clip zwischen zwei direkt anliegenden Nachbarn. Der linke Nachbar wird länger
    /// bzw. kürzer, der rechte entsprechend kürzer bzw. länger – der Inhalt des Clips bleibt gleich.
    /// </summary>
    public bool Slide(Guid clipId, long delta)
    {
        return Modify("Slide", p =>
        {
            if (p.FindClip(clipId) is not ({ } track, { } clip))
            {
                return false;
            }

            var left = track.Clips.FirstOrDefault(c => c.EndFrame == clip.StartFrame);
            var right = track.Clips.FirstOrDefault(c => c.StartFrame == clip.EndFrame);
            if (left is null || right is null)
            {
                return false;
            }

            var rate = p.Settings.FrameRate;
            var d = Math.Clamp(delta,
                Math.Max(1 - left.LengthFrames, -ClipHandles.Head(right, rate)),
                Math.Min(right.LengthFrames - 1, ClipHandles.Tail(left, rate)));
            if (d == 0)
            {
                return false;
            }

            left.LengthFrames += d;
            clip.StartFrame += d;
            right.StartFrame += d;
            right.LengthFrames -= d;
            right.SourceIn = Math.Max(0, right.SourceIn + rate.FramesToSeconds(d) * right.Speed);
            right.ShiftKeyframes(-d);
            return true;
        });
    }

    private static bool HasOverlap(TimelineTrack track)
    {
        long end = long.MinValue;
        foreach (var clip in track.OrderedClips)
        {
            if (clip.StartFrame < end)
            {
                return true;
            }

            end = clip.EndFrame;
        }

        return false;
    }

    /// <summary>
    /// Zeitlupe: Spielt einen High-Frame-Rate-Clip Bild für Bild in der Projektbildrate ab
    /// (z. B. 119,88 fps in einem 23,976-Projekt = fünffach langsamer). Der Ton wird stummgeschaltet.
    /// </summary>
    public bool ConformToProjectRate(Guid clipId, double sourceFrameRate)
    {
        var speed = Project.Settings.FrameRate.Value / sourceFrameRate;
        return SetSpeed(clipId, speed, "Als Zeitlupe konformieren");
    }

    /// <summary>
    /// Fasst alles im Bereich [<paramref name="start"/>, <paramref name="end"/>) auf allen Spuren zu einem
    /// verschachtelten Clip zusammen (Clips an den Grenzen werden geteilt). Liefert den neuen Clip.
    /// </summary>
    public TimelineClip? Nest(long start, long end, string name)
    {
        TimelineClip? result = null;
        if (end <= start)
        {
            return null;
        }

        Modify("Verschachteln", p =>
        {
            var rate = p.Settings.FrameRate;
            var inner = new List<TimelineTrack>();
            foreach (var track in p.Tracks)
            {
                foreach (var boundary in new[] { start, end })
                {
                    foreach (var clip in track.Clips.Where(c => c.StartFrame < boundary && c.EndFrame > boundary).ToList())
                    {
                        track.Clips.Add(SplitClip(clip, boundary, rate));
                    }
                }

                var inside = track.Clips.Where(c => c.StartFrame >= start && c.EndFrame <= end).ToList();
                if (inside.Count == 0)
                {
                    continue;
                }

                track.Clips.RemoveAll(inside.Contains);
                foreach (var clip in inside)
                {
                    clip.StartFrame -= start;
                }

                inner.Add(new TimelineTrack { Kind = track.Kind, Name = track.Name, Clips = inside });
            }

            if (inner.Count == 0)
            {
                return false;
            }

            var hasVideo = inner.Any(t => t.Kind == TrackKind.Video);
            var nested = new TimelineClip
            {
                Name = name,
                NestedTracks = inner,
                SourceIn = 0,
                SourceDuration = rate.FramesToSeconds(end - start),
                StartFrame = start,
                LengthFrames = end - start,
                HasVideo = hasVideo,
                HasAudio = inner.Any(t => t.Clips.Any(c => c.HasAudio)),
            };
            (hasVideo ? p.VideoTracks.First() : p.AudioTracks.First()).Clips.Add(nested);
            result = nested;
            return true;
        });
        return result;
    }

    /// <summary>
    /// Multicam: Kamerawechsel am Bild <paramref name="frame"/> – der Multicam-Clip wird dort geteilt
    /// und ab dort zeigt er Kamera <paramref name="angle"/> (am Clipanfang wird nur umgeschaltet).
    /// </summary>
    public bool SwitchAngle(long frame, int angle, Guid? trackId = null)
    {
        return Modify("Kamerawechsel", p =>
        {
            var tracks = trackId is { } id ? p.Tracks.Where(t => t.Id == id) : p.VideoTracks;
            foreach (var track in tracks)
            {
                var clip = track.Clips.FirstOrDefault(c => c.IsMulticam && c.Contains(frame));
                if (clip is null || angle < 0 || angle >= clip.MulticamAngles!.Count)
                {
                    continue;
                }

                if (clip.ActiveAngle == angle)
                {
                    return false;
                }

                if (frame > clip.StartFrame)
                {
                    var right = SplitClip(clip, frame, p.Settings.FrameRate);
                    right.ActiveAngle = angle;
                    right.TransitionIn = null;
                    track.Clips.Add(right);
                }
                else
                {
                    clip.ActiveAngle = angle;
                }

                return true;
            }

            return false;
        });
    }

    /// <summary>Löst einen verschachtelten Clip wieder in seine Einzelclips auf.</summary>
    public bool Unnest(Guid clipId) =>
        Modify("Verschachtelung auflösen", p =>
        {
            if (p.FindClip(clipId) is not ({ } track, { IsNested: true } clip))
            {
                return false;
            }

            NestFlattener.Expand(p, track, clip);
            return true;
        });

    /// <summary>
    /// Geschwindigkeitsrampe: teilt den Clip in Abschnitte mit fließend wechselnder Geschwindigkeit
    /// (z. B. 100 % → 25 %). Der Clip wird dadurch länger oder kürzer; Clips dahinter auf derselben Spur rücken mit.
    /// </summary>
    public bool ApplySpeedRamp(Guid clipId, SpeedRampShape shape, double slowSpeed, int steps = 6)
    {
        if (slowSpeed is <= 0.05 or >= 1)
        {
            return false;
        }

        return Modify("Geschwindigkeitsrampe", p =>
        {
            if (p.FindClip(clipId) is not ({ } track, { } clip) || clip.IsNested || Math.Abs(clip.Speed - 1) > 1e-6)
            {
                return false;
            }

            var rate = p.Settings.FrameRate;
            var plan = new List<(double Fraction, double Speed)>();
            void Ramp(double fraction, double from, double to)
            {
                for (var i = 0; i < steps; i++)
                {
                    var u = (i + 0.5) / steps;
                    var eased = u * u * (3 - 2 * u);
                    plan.Add((fraction / steps, from + (to - from) * eased));
                }
            }

            switch (shape)
            {
                case SpeedRampShape.SlowDown:
                    plan.Add((0.5, 1));
                    Ramp(0.2, 1, slowSpeed);
                    plan.Add((0.3, slowSpeed));
                    break;
                case SpeedRampShape.SpeedUp:
                    plan.Add((0.3, slowSpeed));
                    Ramp(0.2, slowSpeed, 1);
                    plan.Add((0.5, 1));
                    break;
                default:
                    plan.Add((0.25, 1));
                    Ramp(0.1, 1, slowSpeed);
                    plan.Add((0.3, slowSpeed));
                    Ramp(0.1, slowSpeed, 1);
                    plan.Add((0.25, 1));
                    break;
            }

            var sourceLength = clip.SourceLength(rate);
            var pieces = new List<TimelineClip>();
            double used = 0;
            var cursor = clip.StartFrame;
            for (var i = 0; i < plan.Count; i++)
            {
                var (fraction, speed) = plan[i];
                var piece = clip.Clone();
                piece.Id = i == 0 ? clip.Id : Guid.NewGuid();
                piece.SourceIn = clip.SourceIn + used * sourceLength;
                piece.Speed = speed;
                piece.LengthFrames = Math.Max(1, rate.SecondsToFrames(fraction * sourceLength / speed));
                piece.StartFrame = cursor;
                piece.Keyframes = [];
                piece.FadeInFrames = i == 0 ? clip.FadeInFrames : 0;
                piece.FadeOutFrames = i == plan.Count - 1 ? clip.FadeOutFrames : 0;
                piece.AudioMuted = clip.AudioMuted || Math.Abs(speed - 1) > 1e-6;
                cursor += piece.LengthFrames;
                used += fraction;
                pieces.Add(piece);
            }

            var delta = cursor - clip.EndFrame;
            foreach (var other in track.Clips.Where(c => c.StartFrame >= clip.EndFrame))
            {
                other.StartFrame += delta;
            }

            track.Clips.Remove(clip);
            track.Clips.AddRange(pieces);
            return !HasOverlap(track);
        });
    }

    /// <summary>Ändert die Geschwindigkeit; die Länge auf der Timeline passt sich an (Platz dahinter wird geprüft).</summary>
    public bool SetSpeed(Guid clipId, double speed, string description = "Geschwindigkeit")
    {
        if (speed is <= 0.01 or > 16)
        {
            return false;
        }

        return Modify(description, p =>
        {
            if (p.FindClip(clipId) is not ({ } track, { } clip) || clip.IsNested)
            {
                return false;
            }

            var rate = p.Settings.FrameRate;
            var sourceLength = clip.SourceLength(rate);
            var newLength = Math.Max(1, rate.SecondsToFrames(sourceLength / speed));
            if (!track.IsFree(clip.StartFrame, newLength, clip.Id))
            {
                return false;
            }

            clip.Speed = speed;
            clip.LengthFrames = newLength;
            if (Math.Abs(speed - 1) > 1e-9)
            {
                clip.AudioMuted = true;
            }

            return true;
        });
    }

    /// <summary>Ändert Eigenschaften eines Clips (Lautstärke, Farbe, Blenden …) rückgängig machbar.</summary>
    public bool UpdateClip(Guid clipId, string description, Action<TimelineClip> change, bool mergeSimilar = false)
    {
        return Modify(description, mergeSimilar ? $"{description}|{clipId}" : null, p =>
        {
            if (p.FindClip(clipId) is not (_, { } clip))
            {
                return false;
            }

            change(clip);
            clip.FadeInFrames = Math.Clamp(clip.FadeInFrames, 0, clip.LengthFrames);
            clip.FadeOutFrames = Math.Clamp(clip.FadeOutFrames, 0, clip.LengthFrames);
            return true;
        });
    }

    public void ChangeSettings(ProjectSettings settings)
    {
        Modify("Projekteinstellungen", p =>
        {
            var oldRate = p.Settings.FrameRate;
            p.Settings = settings.Clone();
            var newRate = p.Settings.FrameRate;
            if (oldRate != newRate)
            {
                // Clips behalten ihre Zeit in Sekunden – Bildpositionen werden umgerechnet.
                foreach (var clip in p.AllClips)
                {
                    clip.StartFrame = newRate.SecondsToFrames(oldRate.FramesToSeconds(clip.StartFrame));
                    clip.LengthFrames = Math.Max(1, newRate.SecondsToFrames(oldRate.FramesToSeconds(clip.LengthFrames)));
                    clip.FadeInFrames = newRate.SecondsToFrames(oldRate.FramesToSeconds(clip.FadeInFrames));
                    clip.FadeOutFrames = newRate.SecondsToFrames(oldRate.FramesToSeconds(clip.FadeOutFrames));
                }

                foreach (var track in p.Tracks)
                {
                    ResolveOverlaps(track);
                }
            }

            return true;
        });
    }

    /// <summary>Setzt In-/Out-Marken (null = entfernen). Out liegt immer hinter In.</summary>
    public void SetMarks(long? markIn, long? markOut)
    {
        if (markIn is { } i && markOut is { } o && o <= i)
        {
            markOut = null;
        }

        Modify("Marken setzen", p =>
        {
            if (p.MarkIn == markIn && p.MarkOut == markOut)
            {
                return false;
            }

            p.MarkIn = markIn;
            p.MarkOut = markOut;
            return true;
        });
    }

    /// <summary>Legt einen Titel an (auf der ersten Titelspur, bei Bedarf auf einer neuen).</summary>
    public TimelineClip AddTitle(TitleSpec spec, long start, long lengthFrames)
    {
        var clip = new TimelineClip
        {
            Name = spec.Lines.FirstOrDefault() ?? "Titel",
            Title = spec,
            HasVideo = true,
            HasAudio = false,
            LengthFrames = Math.Max(1, lengthFrames),
            SourceDuration = double.MaxValue / 4,
        };
        Modify("Titel einfügen", p =>
        {
            var track = p.TitleTracks.FirstOrDefault(t => t.IsFree(start, clip.LengthFrames));
            if (track is null)
            {
                track = new TimelineTrack { Kind = TrackKind.Title, Name = $"T{p.TitleTracks.Count() + 1} Titel" };
                p.Tracks.Insert(0, track);
            }

            clip.StartFrame = start;
            track.Clips.Add(clip);
            return true;
        });
        return clip;
    }

    /// <summary>Setzt den Übergang am Anfang eines Clips (null = harter Schnitt).</summary>
    public bool SetTransition(Guid clipId, Transition? transition) =>
        UpdateClip(clipId, transition is null ? "Übergang entfernen" : "Übergang", c => c.TransitionIn = transition?.Clone());

    /// <summary>Spur stumm/aktiv schalten.</summary>
    /// <summary>ADR-Szene für einen Bereich anlegen (gibt eine vorhandene mit gleichem Bereich zurück).</summary>
    public Audio.AdrCue AddAdrCue(long start, long end, string? name = null)
    {
        if (Project.AdrCues.FirstOrDefault(c => c.StartFrame == start && c.EndFrame == end) is { } existing)
        {
            return existing;
        }

        var cue = new Audio.AdrCue { StartFrame = start, EndFrame = Math.Max(start + 1, end), Name = name ?? $"ADR {Project.AdrCues.Count + 1}" };
        Modify("ADR-Szene anlegen", p =>
        {
            p.AdrCues.Add(cue.Clone());
            return true;
        });
        return Project.AdrCues.First(c => c.Id == cue.Id);
    }

    /// <summary>ADR-Szene umbenennen bzw. den Dialogtext ändern.</summary>
    public bool UpdateAdrCue(Guid cueId, string description, Action<Audio.AdrCue> change) =>
        Modify(description, $"adr:{cueId}:{description}", p =>
        {
            if (p.AdrCues.FirstOrDefault(c => c.Id == cueId) is not { } cue)
            {
                return false;
            }

            change(cue);
            return true;
        });

    public bool RemoveAdrCue(Guid cueId) =>
        Modify("ADR-Szene entfernen", p => p.AdrCues.RemoveAll(c => c.Id == cueId) > 0);

    /// <summary>Neuen Take zu einer ADR-Szene hinzufügen.</summary>
    public bool AddAdrTake(Guid cueId, Audio.AdrTake take) =>
        Modify("ADR-Take aufgenommen", p =>
        {
            if (p.AdrCues.FirstOrDefault(c => c.Id == cueId) is not { } cue)
            {
                return false;
            }

            cue.Takes.Add(take.Clone());
            return true;
        });

    /// <summary>Einen Take in die Timeline legen: überschreibt den Bereich auf der Spur und merkt den Take als verwendet.</summary>
    public bool UseAdrTake(Guid cueId, Guid takeId, Guid trackId, TimelineClip clip) =>
        Modify("ADR-Take verwenden", p =>
        {
            if (p.AdrCues.FirstOrDefault(c => c.Id == cueId) is not { } cue || cue.Takes.All(t => t.Id != takeId)
                || p.Tracks.FirstOrDefault(t => t.Id == trackId) is not { } track)
            {
                return false;
            }

            ClearRange(track, cue.StartFrame, cue.EndFrame, p.Settings.FrameRate);
            clip.StartFrame = cue.StartFrame;
            clip.LengthFrames = Math.Min(clip.LengthFrames, cue.LengthFrames);
            track.Clips.Add(clip);
            cue.UsedTakeId = takeId;
            return true;
        });

    /// <summary>
    /// Originalton (Kameraton auf V1) in einem Bereich stummschalten – die Clips werden an den Bereichsgrenzen geteilt.
    /// Typisch für ADR: Der neu gesprochene Dialog ersetzt den Originalton.
    /// </summary>
    public bool MuteOriginalAudio(long start, long end) =>
        Modify("Originalton stumm", p =>
        {
            var rate = p.Settings.FrameRate;
            var track = p.VideoTracks.FirstOrDefault();
            if (track is null || end <= start)
            {
                return false;
            }

            var changed = false;
            foreach (var clip in track.Clips.Where(c => c.StartFrame < end && c.EndFrame > start && c.HasAudio && !c.AudioMuted).ToList())
            {
                var target = clip;
                if (target.StartFrame < start)
                {
                    target = SplitClip(target, start, rate);
                    target.TransitionIn = null;
                    track.Clips.Add(target);
                }

                if (target.EndFrame > end)
                {
                    var right = SplitClip(target, end, rate);
                    right.TransitionIn = null;
                    track.Clips.Add(right);
                }

                target.AudioMuted = true;
                changed = true;
            }

            return changed;
        });

    /// <summary>Ändert den Kanalzug einer Spur (Fader, Panorama, Effekte, Ducking) – aufeinanderfolgende Änderungen werden zusammengefasst.</summary>
    public bool SetTrackAudio(Guid trackId, string description, Action<TimelineTrack> change) =>
        Modify(description, $"mix:{trackId}:{description}", p =>
        {
            var track = p.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track is null)
            {
                return false;
            }

            var before = System.Text.Json.JsonSerializer.Serialize(track.Audio) + track.VolumeDb.ToString(System.Globalization.CultureInfo.InvariantCulture);
            change(track);
            if (track.Audio.DuckBy == track.Id)
            {
                track.Audio.DuckBy = null;
            }

            return before != System.Text.Json.JsonSerializer.Serialize(track.Audio) + track.VolumeDb.ToString(System.Globalization.CultureInfo.InvariantCulture);
        });

    /// <summary>Ändert die Summe (Master): Lautstärke, Limiter, Lautheits-Ziel.</summary>
    public bool SetMaster(string description, Action<Audio.MasterAudio> change) =>
        Modify(description, $"master:{description}", p =>
        {
            var before = System.Text.Json.JsonSerializer.Serialize(p.Master);
            change(p.Master);
            return before != System.Text.Json.JsonSerializer.Serialize(p.Master);
        });

    public bool SetTrackMuted(Guid trackId, bool muted) =>
        Modify(muted ? "Spur stumm" : "Spur aktiv", p =>
        {
            var track = p.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track is null || track.Muted == muted)
            {
                return false;
            }

            track.Muted = muted;
            return true;
        });

    /// <summary>Fügt eine neue Spur hinzu (Video oben, Audio unten).</summary>
    public TimelineTrack AddTrack(TrackKind kind, string? name = null)
    {
        var count = Project.Tracks.Count(t => t.Kind == kind) + 1;
        var track = new TimelineTrack { Kind = kind, Name = name ?? (kind == TrackKind.Video ? $"V{count}" : $"A{count}") };
        Modify("Spur hinzufügen", p =>
        {
            if (kind == TrackKind.Video)
            {
                var lastVideo = p.Tracks.FindLastIndex(t => t.Kind == TrackKind.Video);
                p.Tracks.Insert(lastVideo + 1, track);
            }
            else
            {
                p.Tracks.Add(track);
            }

            return true;
        });
        return track;
    }

    public bool Undo() => Restore(History.Undo(Project));

    public bool Redo() => Restore(History.Redo(Project));

    /// <summary>Führt eine Änderung an einer Kopie aus; nur bei Erfolg wird sie übernommen und im Verlauf gespeichert.</summary>
    private bool Modify(string description, Func<MovieProject, bool> change) => Modify(description, null, change);

    private bool Modify(string description, string? mergeKey, Func<MovieProject, bool> change)
    {
        var working = Project.Clone();
        if (!change(working))
        {
            return false;
        }

        History.Record(Project, description, mergeKey);
        Project = working;
        OnChanged();
        return true;
    }

    private bool Restore(MovieProject? state)
    {
        if (state is null)
        {
            return false;
        }

        state.Media = Project.Media; // Der Medienpool ist nicht Teil von Rückgängig.
        state.Bins = Project.Bins;
        Project = state;
        OnChanged();
        return true;
    }

    private static void ResolveOverlaps(TimelineTrack track)
    {
        long end = 0;
        foreach (var clip in track.OrderedClips)
        {
            if (clip.StartFrame < end)
            {
                clip.StartFrame = end;
            }

            end = clip.EndFrame;
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
