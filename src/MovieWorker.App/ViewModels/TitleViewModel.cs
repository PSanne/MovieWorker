using MovieWorker.Core.Export;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.ViewModels;

public sealed record LabeledValue<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Titelwerkzeug im Inspektor: Text, Vorlage, Schrift, Farbe, Kasten, Lage und Animation.</summary>
public sealed class TitleViewModel(TimelineEditor editor, Guid clipId) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private TitleSpec Spec => editor.Project.FindClip(clipId)?.Clip.Title ?? new TitleSpec();

    private void Change(string description, Action<TitleSpec> change, bool merge = true) =>
        editor.UpdateClip(clipId, description, c =>
        {
            if (c.Title is not null)
            {
                change(c.Title);
                c.Name = c.Title.Lines.FirstOrDefault() ?? "Titel";
            }
        }, merge);

    public static IReadOnlyList<LabeledValue<TitleStyle>> Styles { get; } =
    [
        new(TitleStyle.Headline, "Überschrift (groß, Mitte)"),
        new(TitleStyle.Subtitle, "Untertitel (unten, mit Kasten)"),
        new(TitleStyle.LowerThird, "Bauchbinde (Name + Funktion)"),
        new(TitleStyle.Credits, "Abspann (rollt nach oben)"),
        new(TitleStyle.Caption, "Einblendung (oben links)"),
    ];

    public static IReadOnlyList<LabeledValue<TitleAnimation>> Animations { get; } =
    [
        new(TitleAnimation.None, "Keine"),
        new(TitleAnimation.Fade, "Einblenden"),
        new(TitleAnimation.SlideUp, "Von unten"),
        new(TitleAnimation.SlideLeft, "Von links"),
        new(TitleAnimation.Typewriter, "Schreibmaschine"),
    ];

    public static IReadOnlyList<LabeledValue<TitleAlign>> Aligns { get; } =
    [
        new(TitleAlign.Left, "Links"),
        new(TitleAlign.Center, "Mitte"),
        new(TitleAlign.Right, "Rechts"),
    ];

    public static IReadOnlyList<string> Colors { get; } = ["white", "black", "yellow", "orange", "red", "deepskyblue", "lightgreen", "gray"];

    public static IReadOnlyList<string> Fonts => FontResolver.Families;

    public string Text
    {
        get => Spec.Text;
        set => Change("Titeltext", s => s.Text = value ?? string.Empty, merge: false);
    }

    /// <summary>Vorlage wechseln: übernimmt Gestaltung und Lage der Vorlage, der Text bleibt.</summary>
    public LabeledValue<TitleStyle> Style
    {
        get => Styles.First(s => s.Value == Spec.Style);
        set
        {
            if (value is null || value.Value == Spec.Style)
            {
                return;
            }

            var text = Spec.Text;
            editor.UpdateClip(clipId, "Titelvorlage", c =>
            {
                c.Title = TitleSpec.FromStyle(value.Value);
                c.Title.Text = text;
            });
        }
    }

    public string FontFamily
    {
        get => Spec.FontFamily;
        set => Change("Schrift", s => s.FontFamily = value);
    }

    public bool Bold
    {
        get => Spec.Bold;
        set => Change("Fett", s => s.Bold = value);
    }

    public double SizePercent
    {
        get => Spec.SizePercent;
        set => Change("Schriftgröße", s => s.SizePercent = Math.Clamp(value, 1, 40));
    }

    public string Color
    {
        get => Spec.Color;
        set => Change("Schriftfarbe", s => s.Color = value);
    }

    public bool Box
    {
        get => Spec.Box;
        set => Change("Kasten", s => s.Box = value);
    }

    public double BoxOpacity
    {
        get => Spec.BoxOpacity * 100;
        set => Change("Kasten-Deckkraft", s => s.BoxOpacity = Math.Clamp(value / 100, 0, 1));
    }

    public double PositionX
    {
        get => Spec.PositionX;
        set => Change("Titel-Position", s => s.PositionX = Math.Clamp(value, 0, 100));
    }

    public double PositionY
    {
        get => Spec.PositionY;
        set => Change("Titel-Position", s => s.PositionY = Math.Clamp(value, 0, 100));
    }

    public LabeledValue<TitleAlign> Align
    {
        get => Aligns.First(a => a.Value == Spec.Align);
        set => Change("Ausrichtung", s => s.Align = value.Value);
    }

    public LabeledValue<TitleAnimation> AnimationIn
    {
        get => Animations.First(a => a.Value == Spec.AnimationIn);
        set => Change("Animation", s => s.AnimationIn = value.Value);
    }

    public LabeledValue<TitleAnimation> AnimationOut
    {
        get => Animations.First(a => a.Value == Spec.AnimationOut);
        set => Change("Animation", s => s.AnimationOut = value.Value == TitleAnimation.Typewriter ? TitleAnimation.Fade : value.Value);
    }

    public double AnimationSeconds
    {
        get => Spec.AnimationSeconds;
        set => Change("Animationsdauer", s => s.AnimationSeconds = Math.Clamp(value, 0.05, 5));
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}
