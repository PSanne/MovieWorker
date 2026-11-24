using System.Windows;
using System.Windows.Media;
using MovieWorker.App.Helpers;

namespace MovieWorker.App.Views;

/// <summary>Die eingebaute Anleitung (mit Suche über das Lupensymbol oder Strg+F).</summary>
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        Brush Get(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
        Document = MarkdownDocument.Build(MarkdownDocument.LoadGuide(),
            Get("Brush.Text", Brushes.Black), Get("Brush.SubText", Brushes.DimGray), Get("Brush.Accent", Brushes.MediumPurple), Get("Brush.Border", Brushes.LightGray));
        Reader.Document = Document;
    }

    public System.Windows.Documents.FlowDocument Document { get; }
}
