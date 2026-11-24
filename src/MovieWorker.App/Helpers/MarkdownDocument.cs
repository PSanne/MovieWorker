using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MovieWorker.App.Helpers;

/// <summary>
/// Wandelt einfaches Markdown (Überschriften, Absätze, Listen, Tabellen, **fett**, *kursiv*, `Code`)
/// in ein WPF-FlowDocument – für die eingebaute Anleitung.
/// </summary>
public static partial class MarkdownDocument
{
    /// <summary>Die mitgelieferte Anleitung (docs/ANLEITUNG.md, als Ressource eingebettet).</summary>
    public static string LoadGuide()
    {
        using var stream = typeof(MarkdownDocument).Assembly.GetManifestResourceStream("MovieWorker.Anleitung.md");
        if (stream is null)
        {
            return "# Anleitung\n\nDie Anleitung ist in dieser Version nicht enthalten.";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static FlowDocument Build(string markdown, Brush text, Brush subText, Brush accent, Brush border)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = text,
            PagePadding = new Thickness(28, 18, 28, 28),
            ColumnWidth = 1200,
            LineHeight = 21,
        };

        List? list = null;
        Table? table = null;
        Paragraph? paragraph = null;
        foreach (var raw in markdown.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                list = null;
                table = null;
                paragraph = null;
                continue;
            }

            if (line.StartsWith('#'))
            {
                var level = line.TakeWhile(c => c == '#').Count();
                var heading = new Paragraph
                {
                    FontSize = level switch { 1 => 26, 2 => 20, _ => 16 },
                    FontWeight = FontWeights.SemiBold,
                    Foreground = level == 1 ? accent : text,
                    Margin = new Thickness(0, level == 1 ? 0 : 18, 0, 6),
                };
                AddInlines(heading.Inlines, line[level..].Trim(), subText);
                document.Blocks.Add(heading);
                list = null;
                table = null;
                paragraph = null;
                continue;
            }

            if (line.StartsWith('|'))
            {
                var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                if (cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':')))
                {
                    continue;   // Trennzeile unter der Kopfzeile
                }

                var header = table is null;
                if (table is null)
                {
                    table = new Table { CellSpacing = 0, BorderBrush = border, BorderThickness = new Thickness(1, 1, 0, 0), Margin = new Thickness(0, 6, 0, 10) };
                    foreach (var _ in cells)
                    {
                        table.Columns.Add(new TableColumn());
                    }

                    table.RowGroups.Add(new TableRowGroup());
                    document.Blocks.Add(table);
                }

                var row = new TableRow();
                foreach (var cell in cells.Take(table.Columns.Count))
                {
                    var content = new Paragraph { Margin = new Thickness(0) };
                    AddInlines(content.Inlines, cell, subText);
                    if (header)
                    {
                        content.FontWeight = FontWeights.SemiBold;
                    }

                    row.Cells.Add(new TableCell(content) { Padding = new Thickness(6, 3, 6, 3), BorderBrush = border, BorderThickness = new Thickness(0, 0, 1, 1) });
                }

                table.RowGroups[0].Rows.Add(row);
                list = null;
                paragraph = null;
                continue;
            }

            var bullet = BulletPattern().Match(line);
            if (bullet.Success)
            {
                var numbered = char.IsDigit(bullet.Groups[1].Value[0]);
                var style = numbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc;
                if (list is null || list.MarkerStyle != style)
                {
                    list = new List { MarkerStyle = style, Margin = new Thickness(0, 2, 0, 8), Padding = new Thickness(22, 0, 0, 0) };
                    document.Blocks.Add(list);
                }

                var item = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                AddInlines(item.Inlines, bullet.Groups[2].Value, subText);
                list.ListItems.Add(new ListItem(item));
                table = null;
                paragraph = null;
                continue;
            }

            if (paragraph is null)
            {
                paragraph = new Paragraph { Margin = new Thickness(0, 4, 0, 8) };
                document.Blocks.Add(paragraph);
            }
            else
            {
                paragraph.Inlines.Add(new Run(" "));
            }

            AddInlines(paragraph.Inlines, line, subText);
            list = null;
            table = null;
        }

        return document;
    }

    private static void AddInlines(InlineCollection inlines, string text, Brush subText)
    {
        var position = 0;
        foreach (Match match in InlinePattern().Matches(text))
        {
            if (match.Index > position)
            {
                inlines.Add(new Run(text[position..match.Index]));
            }

            var token = match.Value;
            if (token.StartsWith("**", StringComparison.Ordinal))
            {
                inlines.Add(new Bold(new Run(token[2..^2])));
            }
            else if (token.StartsWith('`'))
            {
                inlines.Add(new Run(token[1..^1]) { FontFamily = new FontFamily("Consolas"), Foreground = subText });
            }
            else
            {
                inlines.Add(new Italic(new Run(token[1..^1])));
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            inlines.Add(new Run(text[position..]));
        }
    }

    [GeneratedRegex(@"^\s*([-*]|\d+\.)\s+(.*)$")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"\*\*[^*]+\*\*|`[^`]+`|\*[^*\s][^*]*\*")]
    private static partial Regex InlinePattern();
}
