using System.Globalization;

namespace MovieWorker.Core.Timeline;

/// <summary>Was sich per Keyframe animieren lässt.</summary>
public enum AnimatedProperty
{
    /// <summary>Waagerechte Verschiebung in % der Bildbreite (positiv = nach rechts).</summary>
    PositionX,

    /// <summary>Senkrechte Verschiebung in % der Bildhöhe (positiv = nach unten).</summary>
    PositionY,

    /// <summary>Zoom in % (100 = Originalgröße).</summary>
    Scale,

    /// <summary>Drehung in Grad (im Uhrzeigersinn).</summary>
    Rotation,

    /// <summary>Deckkraft in % (0 = unsichtbar).</summary>
    Opacity,

    /// <summary>Lautstärke in dB.</summary>
    Volume,
}

public enum KeyframeInterpolation
{
    /// <summary>Gleichmäßig.</summary>
    Linear,

    /// <summary>Weich ein- und ausschwingen.</summary>
    Smooth,

    /// <summary>Wert halten bis zum nächsten Keyframe (Sprung).</summary>
    Hold,
}

/// <summary>Ein Keyframe: Wert zu einem Zeitpunkt (Bilder ab Clipanfang).</summary>
public sealed class Keyframe
{
    public long Frame { get; set; }

    public double Value { get; set; }

    public KeyframeInterpolation Interpolation { get; set; } = KeyframeInterpolation.Smooth;

    public Keyframe Clone() => (Keyframe)MemberwiseClone();
}

/// <summary>Alle Keyframes einer Eigenschaft eines Clips.</summary>
public sealed class KeyframeTrack
{
    public AnimatedProperty Property { get; set; }

    public List<Keyframe> Keys { get; set; } = [];

    public IEnumerable<Keyframe> Ordered => Keys.OrderBy(k => k.Frame);

    public KeyframeTrack Clone() => new() { Property = Property, Keys = Keys.Select(k => k.Clone()).ToList() };

    /// <summary>Wert an einem Bild (vor dem ersten bzw. nach dem letzten Keyframe gilt dessen Wert).</summary>
    public double ValueAt(double frame)
    {
        var keys = Ordered.ToList();
        if (keys.Count == 0)
        {
            return 0;
        }

        if (frame <= keys[0].Frame)
        {
            return keys[0].Value;
        }

        for (var i = 0; i < keys.Count - 1; i++)
        {
            var a = keys[i];
            var b = keys[i + 1];
            if (frame < b.Frame)
            {
                var u = (frame - a.Frame) / (double)(b.Frame - a.Frame);
                return a.Interpolation switch
                {
                    KeyframeInterpolation.Hold => a.Value,
                    KeyframeInterpolation.Smooth => a.Value + (b.Value - a.Value) * (u * u * (3 - 2 * u)),
                    _ => a.Value + (b.Value - a.Value) * u,
                };
            }
        }

        return keys[^1].Value;
    }

    /// <summary>
    /// ffmpeg-Ausdruck für den Wert in Abhängigkeit von <paramref name="timeVariable"/> (z. B. „t“ in Sekunden
    /// oder „on“ in Bildern). <paramref name="framesToUnit"/> rechnet Bilder in diese Einheit um.
    /// </summary>
    public string ToExpression(string timeVariable, Func<long, double> framesToUnit)
    {
        var keys = Ordered.ToList();
        if (keys.Count == 0)
        {
            return "0";
        }

        string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        var expression = N(keys[^1].Value);
        for (var i = keys.Count - 2; i >= 0; i--)
        {
            var a = keys[i];
            var b = keys[i + 1];
            var ta = framesToUnit(a.Frame);
            var tb = framesToUnit(b.Frame);
            var u = $"(({timeVariable}-{N(ta)})/{N(Math.Max(1e-9, tb - ta))})";
            var segment = a.Interpolation switch
            {
                KeyframeInterpolation.Hold => N(a.Value),
                KeyframeInterpolation.Smooth => $"({N(a.Value)}+({N(b.Value - a.Value)})*{u}*{u}*(3-2*{u}))",
                _ => $"({N(a.Value)}+({N(b.Value - a.Value)})*{u})",
            };
            expression = $"if(lt({timeVariable},{N(tb)}),{segment},{expression})";
        }

        return $"if(lt({timeVariable},{N(framesToUnit(keys[0].Frame))}),{N(keys[0].Value)},{expression})";
    }
}

/// <summary>Feste Bildlage eines Clips (gilt, solange es für die Eigenschaft keine Keyframes gibt).</summary>
public sealed class ClipTransform
{
    public double PositionX { get; set; }

    public double PositionY { get; set; }

    public double Scale { get; set; } = 100;

    public double Rotation { get; set; }

    public double Opacity { get; set; } = 100;

    public ClipTransform Clone() => (ClipTransform)MemberwiseClone();

    public double Get(AnimatedProperty property) => property switch
    {
        AnimatedProperty.PositionX => PositionX,
        AnimatedProperty.PositionY => PositionY,
        AnimatedProperty.Scale => Scale,
        AnimatedProperty.Rotation => Rotation,
        AnimatedProperty.Opacity => Opacity,
        _ => 0,
    };

    public void Set(AnimatedProperty property, double value)
    {
        switch (property)
        {
            case AnimatedProperty.PositionX: PositionX = value; break;
            case AnimatedProperty.PositionY: PositionY = value; break;
            case AnimatedProperty.Scale: Scale = value; break;
            case AnimatedProperty.Rotation: Rotation = value; break;
            case AnimatedProperty.Opacity: Opacity = value; break;
        }
    }
}
