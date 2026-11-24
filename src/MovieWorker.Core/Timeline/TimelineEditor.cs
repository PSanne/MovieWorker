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
    /// Zeitlupe: Spielt einen High-Frame-Rate-Clip Bild für Bild in der Projektbildrate ab
    /// (z. B. 119,88 fps in einem 23,976-Projekt = fünffach langsamer). Der Ton wird stummgeschaltet.
    /// </summary>
    public bool ConformToProjectRate(Guid clipId, double sourceFrameRate)
    {
        var speed = Project.Settings.FrameRate.Value / sourceFrameRate;
        return SetSpeed(clipId, speed, "Als Zeitlupe konformieren");
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
            if (p.FindClip(clipId) is not ({ } track, { } clip))
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

    /// <summary>Spur stumm/aktiv schalten.</summary>
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
    public TimelineTrack AddTrack(TrackKind kind)
    {
        var count = Project.Tracks.Count(t => t.Kind == kind) + 1;
        var track = new TimelineTrack { Kind = kind, Name = kind == TrackKind.Video ? $"V{count}" : $"A{count}" };
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
