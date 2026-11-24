using System.Windows;

namespace MovieWorker.App.Services;

/// <summary>Schaltet zwischen hellem und dunklem Design um (ohne Neustart).</summary>
public static class ThemeService
{
    public static bool IsDark { get; private set; }

    public static void Apply(bool dark)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var dictionaries = app.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Themes/", StringComparison.Ordinal) == true);
        var next = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        if (current is not null)
        {
            dictionaries[dictionaries.IndexOf(current)] = next;
        }
        else
        {
            dictionaries.Add(next);
        }

        IsDark = dark;
    }
}
