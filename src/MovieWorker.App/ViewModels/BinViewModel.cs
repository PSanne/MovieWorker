using CommunityToolkit.Mvvm.ComponentModel;
using MovieWorker.Core.Projects;

namespace MovieWorker.App.ViewModels;

public enum BinKind
{
    All,
    Unsorted,
    Folder,
}

/// <summary>Eintrag in der Ordnerauswahl des Medienpools.</summary>
public sealed partial class BinViewModel : ObservableObject
{
    public BinViewModel(BinKind kind, MediaBin? bin = null)
    {
        Kind = kind;
        Bin = bin;
    }

    public BinKind Kind { get; }

    public MediaBin? Bin { get; }

    public Guid? Id => Bin?.Id;

    public string Name => Kind switch
    {
        BinKind.All => "Alle Aufnahmen",
        BinKind.Unsorted => "Nicht einsortiert",
        _ => "📁 " + Bin!.Name,
    };

    public void Refresh() => OnPropertyChanged(nameof(Name));

    public override string ToString() => Name;
}
