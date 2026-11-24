using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.ViewModels;

/// <summary>Eine animierbare Eigenschaft im Clip-Inspektor (Wert am Abspielkopf, Keyframe setzen/löschen, springen).</summary>
public sealed partial class AnimatedValueViewModel : ObservableObject
{
    private readonly TimelineEditor _editor;
    private readonly Guid _clipId;
    private readonly Func<long> _playhead;
    private readonly Action<long> _setPlayhead;

    public AnimatedValueViewModel(TimelineEditor editor, Guid clipId, AnimatedProperty property, string label, string unit,
        double minimum, double maximum, double defaultValue, Func<long> playhead, Action<long> setPlayhead)
    {
        _editor = editor;
        _clipId = clipId;
        Property = property;
        Label = label;
        Unit = unit;
        Minimum = minimum;
        Maximum = maximum;
        DefaultValue = defaultValue;
        _playhead = playhead;
        _setPlayhead = setPlayhead;
    }

    public AnimatedProperty Property { get; }

    public string Label { get; }

    public string Unit { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    public double DefaultValue { get; }

    private TimelineClip? Clip => _editor.Project.FindClip(_clipId)?.Clip;

    /// <summary>Bild relativ zum Clipanfang (auf den Clip begrenzt).</summary>
    private long FrameInClip => Clip is { } c ? Math.Clamp(_playhead() - c.StartFrame, 0, Math.Max(0, c.LengthFrames - 1)) : 0;

    public double Value
    {
        get => Clip?.ValueAt(Property, FrameInClip) ?? DefaultValue;
        set
        {
            var frame = FrameInClip;
            var clamped = Math.Clamp(Math.Round(value, 2), Minimum, Maximum);
            _editor.UpdateClip(_clipId, Label, c => c.SetValue(Property, clamped, frame, addKeyframe: false), mergeSimilar: true);
        }
    }

    public bool IsAnimated => Clip?.IsAnimated(Property) == true;

    public bool HasKeyHere => Clip?.KeyframesFor(Property)?.Keys.Any(k => k.Frame == FrameInClip) == true;

    public string KeySymbol => HasKeyHere ? "◆" : IsAnimated ? "◇" : "◇";

    public string KeyToolTip => HasKeyHere ? "Keyframe an dieser Stelle löschen" : "Keyframe an der Abspielposition setzen";

    [RelayCommand]
    private void ToggleKey()
    {
        var frame = FrameInClip;
        var value = Value;
        _editor.UpdateClip(_clipId, HasKeyHere ? "Keyframe löschen" : "Keyframe setzen", c =>
        {
            var track = c.Keyframes.FirstOrDefault(k => k.Property == Property);
            var existing = track?.Keys.FirstOrDefault(k => k.Frame == frame);
            if (existing is not null)
            {
                track!.Keys.Remove(existing);
                if (track.Keys.Count == 0)
                {
                    // Letzter Keyframe weg: Wert bleibt als fester Wert erhalten.
                    c.Keyframes.Remove(track);
                    c.SetValue(Property, existing.Value, frame, addKeyframe: false);
                }
            }
            else
            {
                c.SetValue(Property, value, frame, addKeyframe: true);
            }
        });
    }

    [RelayCommand]
    private void PreviousKey() => Jump(previous: true);

    [RelayCommand]
    private void NextKey() => Jump(previous: false);

    [RelayCommand]
    private void Reset() =>
        _editor.UpdateClip(_clipId, Label + " zurücksetzen", c =>
        {
            c.Keyframes.RemoveAll(k => k.Property == Property);
            c.SetValue(Property, DefaultValue, 0, addKeyframe: false);
        });

    private void Jump(bool previous)
    {
        if (Clip is not { } clip || clip.KeyframesFor(Property) is not { } track)
        {
            return;
        }

        var frame = FrameInClip;
        var target = previous
            ? track.Ordered.LastOrDefault(k => k.Frame < frame)
            : track.Ordered.FirstOrDefault(k => k.Frame > frame);
        if (target is not null)
        {
            _setPlayhead(clip.StartFrame + Math.Clamp(target.Frame, 0, clip.LengthFrames - 1));
        }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}
