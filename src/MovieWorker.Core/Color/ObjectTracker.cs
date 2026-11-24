namespace MovieWorker.Core.Color;

/// <summary>
/// Einfacher Objekt-Tracker (Vorlagenvergleich): sucht in jedem Bild die Stelle, die dem Ausschnitt im ersten
/// Bild am ähnlichsten ist (kleinste Summe der Helligkeitsunterschiede) – damit folgt eine Maske z. B. einem Gesicht.
/// </summary>
public static class ObjectTracker
{
    /// <param name="frames">Graustufenbilder (8 Bit) hintereinander.</param>
    /// <param name="width">Bildbreite.</param>
    /// <param name="height">Bildhöhe.</param>
    /// <param name="count">Anzahl Bilder.</param>
    /// <param name="centerX">Mitte des Objekts im ersten Bild (0 … 1).</param>
    /// <param name="centerY">Mitte des Objekts im ersten Bild (0 … 1).</param>
    /// <param name="boxWidth">Größe der Vorlage (0 … 1 der Bildbreite).</param>
    /// <param name="boxHeight">Größe der Vorlage (0 … 1 der Bildhöhe).</param>
    /// <param name="searchRadius">Wie weit sich das Objekt je Bild höchstens bewegt (Pixel).</param>
    public static IReadOnlyList<(double X, double Y)> Track(byte[] frames, int width, int height, int count,
        double centerX, double centerY, double boxWidth, double boxHeight, int searchRadius = 20)
    {
        var tw = Math.Clamp((int)(boxWidth * width), 8, width / 2);
        var th = Math.Clamp((int)(boxHeight * height), 8, height / 2);
        var x = Math.Clamp((int)(centerX * width) - tw / 2, 0, width - tw);
        var y = Math.Clamp((int)(centerY * height) - th / 2, 0, height - th);
        var template = new float[tw * th];
        Copy(frames, 0, width, x, y, tw, th, template);
        var result = new List<(double, double)>(count) { ((x + tw / 2.0) / width, (y + th / 2.0) / height) };
        var step = Math.Max(1, Math.Min(tw, th) / 16);

        for (var f = 1; f < count; f++)
        {
            var offset = f * width * height;
            var bestX = x;
            var bestY = y;
            var best = double.MaxValue;
            for (var dy = -searchRadius; dy <= searchRadius; dy += 2)
            {
                for (var dx = -searchRadius; dx <= searchRadius; dx += 2)
                {
                    var cx = x + dx;
                    var cy = y + dy;
                    if (cx < 0 || cy < 0 || cx + tw > width || cy + th > height)
                    {
                        continue;
                    }

                    var sad = Sad(frames, offset, width, cx, cy, tw, th, template, step, best);
                    if (sad < best)
                    {
                        best = sad;
                        bestX = cx;
                        bestY = cy;
                    }
                }
            }

            // Feinsuche um den besten Treffer
            var (fx, fy) = (bestX, bestY);
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var cx = fx + dx;
                    var cy = fy + dy;
                    if (cx < 0 || cy < 0 || cx + tw > width || cy + th > height)
                    {
                        continue;
                    }

                    var sad = Sad(frames, offset, width, cx, cy, tw, th, template, 1, best);
                    if (sad < best)
                    {
                        best = sad;
                        bestX = cx;
                        bestY = cy;
                    }
                }
            }

            x = bestX;
            y = bestY;

            // Vorlage langsam nachführen (Licht und Form ändern sich)
            var current = new float[tw * th];
            Copy(frames, offset, width, x, y, tw, th, current);
            for (var i = 0; i < template.Length; i++)
            {
                template[i] = template[i] * 0.85f + current[i] * 0.15f;
            }

            result.Add(((x + tw / 2.0) / width, (y + th / 2.0) / height));
        }

        return result;
    }

    private static void Copy(byte[] frames, int offset, int width, int x, int y, int tw, int th, float[] target)
    {
        for (var j = 0; j < th; j++)
        {
            for (var i = 0; i < tw; i++)
            {
                target[j * tw + i] = frames[offset + (y + j) * width + x + i];
            }
        }
    }

    private static double Sad(byte[] frames, int offset, int width, int x, int y, int tw, int th, float[] template, int step, double limit)
    {
        double sum = 0;
        for (var j = 0; j < th; j += step)
        {
            var row = offset + (y + j) * width + x;
            for (var i = 0; i < tw; i += step)
            {
                sum += Math.Abs(frames[row + i] - template[j * tw + i]);
            }

            if (sum * step * step > limit)
            {
                return double.MaxValue;
            }
        }

        return sum * step * step;
    }

    /// <summary>Macht aus den Positionen Keyframes (jedes n-te Bild, höchstens <paramref name="maxKeys"/>).</summary>
    public static (Timeline.KeyframeTrack X, Timeline.KeyframeTrack Y) ToKeyframes(IReadOnlyList<(double X, double Y)> centers,
        long firstFrameInClip, int maxKeys = 100)
    {
        var every = Math.Max(1, (int)Math.Ceiling(centers.Count / (double)maxKeys));
        var tx = new Timeline.KeyframeTrack();
        var ty = new Timeline.KeyframeTrack();
        for (var i = 0; i < centers.Count; i++)
        {
            if (i % every != 0 && i != centers.Count - 1)
            {
                continue;
            }

            tx.Keys.Add(new Timeline.Keyframe { Frame = firstFrameInClip + i, Value = Math.Round(centers[i].X, 4), Interpolation = Timeline.KeyframeInterpolation.Linear });
            ty.Keys.Add(new Timeline.Keyframe { Frame = firstFrameInClip + i, Value = Math.Round(centers[i].Y, 4), Interpolation = Timeline.KeyframeInterpolation.Linear });
        }

        return (tx, ty);
    }
}
