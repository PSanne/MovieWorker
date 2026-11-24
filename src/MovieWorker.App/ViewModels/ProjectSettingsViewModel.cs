using CommunityToolkit.Mvvm.ComponentModel;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.ViewModels;

public sealed record ResolutionChoice(int Width, int Height, string Label)
{
    public override string ToString() => Label;
}

public sealed record FrameRateChoice(FrameRate Rate, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Dialog „Projekteinstellungen“: Auflösung, Bildrate, Timecode.</summary>
public sealed partial class ProjectSettingsViewModel : ObservableObject
{
    public ProjectSettingsViewModel(ProjectSettings current)
    {
        SelectedResolution = Resolutions.FirstOrDefault(r => r.Width == current.Width && r.Height == current.Height)
            ?? new ResolutionChoice(current.Width, current.Height, $"{current.Width}×{current.Height}");
        if (!Resolutions.Contains(SelectedResolution))
        {
            Resolutions = [.. Resolutions, SelectedResolution];
        }

        SelectedFrameRate = FrameRates.FirstOrDefault(f => f.Rate == current.FrameRate)
            ?? new FrameRateChoice(current.FrameRate, $"{current.FrameRate} fps");
        if (!FrameRates.Contains(SelectedFrameRate))
        {
            FrameRates = [.. FrameRates, SelectedFrameRate];
        }

        DropFrame = current.DropFrameTimecode;
    }

    public IReadOnlyList<ResolutionChoice> Resolutions { get; } =
    [
        new(3840, 2160, "4K UHD – 3840×2160"),
        new(1920, 1080, "Full HD – 1920×1080"),
        new(1280, 720, "HD – 1280×720"),
        new(1080, 1920, "Hochformat (Reels/Shorts) – 1080×1920"),
        new(1080, 1080, "Quadratisch – 1080×1080"),
    ];

    public IReadOnlyList<FrameRateChoice> FrameRates { get; } =
    [
        new(FrameRate.Fps23_976, "23,976 fps (Kino-Look, NTSC)"),
        new(FrameRate.Fps24, "24 fps (Kino)"),
        new(FrameRate.Fps25, "25 fps (PAL, Europa)"),
        new(FrameRate.Fps29_97, "29,97 fps (NTSC)"),
        new(FrameRate.Fps30, "30 fps"),
        new(FrameRate.Fps50, "50 fps (PAL, flüssig)"),
        new(FrameRate.Fps59_94, "59,94 fps (NTSC, flüssig)"),
        new(FrameRate.Fps60, "60 fps"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseDropFrame))]
    private FrameRateChoice _selectedFrameRate;

    [ObservableProperty]
    private ResolutionChoice _selectedResolution;

    [ObservableProperty]
    private bool _dropFrame;

    public bool CanUseDropFrame => SelectedFrameRate.Rate.SupportsDropFrame;

    public ProjectSettings ToSettings(ProjectSettings basis)
    {
        var settings = basis.Clone();
        settings.Width = SelectedResolution.Width;
        settings.Height = SelectedResolution.Height;
        settings.FrameRate = SelectedFrameRate.Rate;
        settings.DropFrameTimecode = DropFrame && CanUseDropFrame;
        return settings;
    }
}
