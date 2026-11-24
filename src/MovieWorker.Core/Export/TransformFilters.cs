using System.Globalization;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Export;

/// <summary>
/// Übersetzt Bildlage und Keyframes eines Clips in ffmpeg-Filter (auf die bereits auf Arbeitsgröße
/// gebrachte Spur angewendet, Ausgabe gleich groß):
/// <list type="bullet">
///   <item>Zoom ≥ 100 % mit Schwenk (auch animiert, „Ken Burns“) über <c>zoompan</c>.</item>
///   <item>Verkleinern (fest) über <c>scale</c> + <c>pad</c> – z. B. für Bild-im-Bild.</item>
///   <item>Drehung (auch animiert) über <c>rotate</c>, Deckkraft (auch animiert) über <c>eq</c> zu Schwarz.</item>
/// </list>
/// </summary>
public static class TransformFilters
{
    public static IReadOnlyList<string> For(TimelineClip clip, int width, int height, FrameRate rate)
    {
        var filters = new List<string>();
        if (!clip.HasTransform)
        {
            return filters;
        }

        string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        string Expr(AnimatedProperty property, string variable, Func<long, double> toUnit) =>
            clip.KeyframesFor(property) is { } track ? track.ToExpression(variable, toUnit) : N(clip.Transform.Get(property));

        var scaleAnimated = clip.IsAnimated(AnimatedProperty.Scale);
        var positionAnimated = clip.IsAnimated(AnimatedProperty.PositionX) || clip.IsAnimated(AnimatedProperty.PositionY);
        var staticScale = clip.Transform.Scale;

        if (!scaleAnimated && staticScale < 99.999)
        {
            // Fest verkleinern und verschieben (Rand schwarz).
            var w = Even(width * staticScale / 100);
            var h = Even(height * staticScale / 100);
            var x = $"(ow-iw)/2+{N(clip.Transform.PositionX / 100 * width)}";
            var y = $"(oh-ih)/2+{N(clip.Transform.PositionY / 100 * height)}";
            filters.Add($"scale={w}:{h}");
            filters.Add($"pad={width}:{height}:'max(0,min(ow-iw,{x}))':'max(0,min(oh-ih,{y}))':black");
        }
        else if (scaleAnimated || positionAnimated || staticScale > 100.001
                 || Math.Abs(clip.Transform.PositionX) > 1e-6 || Math.Abs(clip.Transform.PositionY) > 1e-6)
        {
            // zoompan rechnet in Ausgabebildern (on); Zoom unter 100 % wird hier auf 100 % begrenzt.
            Func<long, double> frames = f => f;
            var z = $"max(1,({Expr(AnimatedProperty.Scale, "on", frames)})/100)";
            var px = Expr(AnimatedProperty.PositionX, "on", frames);
            var py = Expr(AnimatedProperty.PositionY, "on", frames);
            var x = $"max(0,min(iw-iw/zoom,(iw-iw/zoom)/2-({px})/100*iw/zoom))";
            var y = $"max(0,min(ih-ih/zoom,(ih-ih/zoom)/2-({py})/100*ih/zoom))";
            filters.Add($"zoompan=z='{z}':x='{x}':y='{y}':d=1:s={width}x{height}:fps={rate.ToFfmpeg()}");
        }

        if (clip.IsAnimated(AnimatedProperty.Rotation) || Math.Abs(clip.Transform.Rotation) > 1e-6)
        {
            Func<long, double> seconds = f => rate.FramesToSeconds(f);
            filters.Add($"rotate=a='({Expr(AnimatedProperty.Rotation, "t", seconds)})*PI/180':c=black");
        }

        if (clip.IsAnimated(AnimatedProperty.Opacity) || clip.Transform.Opacity < 99.999)
        {
            Func<long, double> seconds = f => rate.FramesToSeconds(f);
            var o = $"(max(0,min(100,{Expr(AnimatedProperty.Opacity, "t", seconds)}))/100)";
            filters.Add($"eq=contrast='{o}':brightness='0.5*{o}-0.5':saturation='{o}':eval=frame");
        }

        return filters;
    }

    /// <summary>
    /// Dieselbe Bildlage als feste Werte an einem Bild – für exakte Standbilder in der Vorschau.
    /// </summary>
    public static IReadOnlyList<string> StaticAt(TimelineClip clip, long frameInClip, int width, int height)
    {
        var filters = new List<string>();
        if (!clip.HasTransform)
        {
            return filters;
        }

        string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        var scale = clip.ValueAt(AnimatedProperty.Scale, frameInClip) / 100;
        var px = clip.ValueAt(AnimatedProperty.PositionX, frameInClip) / 100;
        var py = clip.ValueAt(AnimatedProperty.PositionY, frameInClip) / 100;
        var rotation = clip.ValueAt(AnimatedProperty.Rotation, frameInClip);
        var opacity = Math.Clamp(clip.ValueAt(AnimatedProperty.Opacity, frameInClip), 0, 100) / 100;

        if (scale < 0.99999 && !clip.IsAnimated(AnimatedProperty.Scale))
        {
            filters.Add($"scale={Even(width * scale)}:{Even(height * scale)}");
            filters.Add($"pad={width}:{height}:'max(0,min(ow-iw,(ow-iw)/2+{N(px * width)}))':'max(0,min(oh-ih,(oh-ih)/2+{N(py * height)}))':black");
        }
        else if (Math.Abs(scale - 1) > 1e-6 || Math.Abs(px) > 1e-6 || Math.Abs(py) > 1e-6)
        {
            var z = Math.Max(1, scale);
            var cw = width / z;
            var ch = height / z;
            var x = Math.Clamp((width - cw) / 2 - px * cw, 0, width - cw);
            var y = Math.Clamp((height - ch) / 2 - py * ch, 0, height - ch);
            filters.Add($"crop={N(cw)}:{N(ch)}:{N(x)}:{N(y)}");
            filters.Add($"scale={width}:{height}");
        }

        if (Math.Abs(rotation) > 1e-6)
        {
            filters.Add($"rotate=a={N(rotation)}*PI/180:c=black");
        }

        if (opacity < 0.99999)
        {
            filters.Add($"eq=contrast={N(opacity)}:brightness={N(0.5 * opacity - 0.5)}:saturation={N(opacity)}");
        }

        return filters;
    }

    /// <summary>Lautstärke-Filter: fester Wert oder Keyframe-Verlauf (dB) plus Spurlautstärke.</summary>
    public static string? Volume(TimelineClip clip, double trackDb, FrameRate rate)
    {
        string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        if (clip.KeyframesFor(AnimatedProperty.Volume) is { } track)
        {
            var db = track.ToExpression("t", f => rate.FramesToSeconds(f));
            return $"volume='pow(10,(({db})+{N(trackDb)})/20)':eval=frame";
        }

        var total = clip.VolumeDb + trackDb;
        return Math.Abs(total) > 1e-6 ? $"volume={N(total)}dB" : null;
    }

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);
}
