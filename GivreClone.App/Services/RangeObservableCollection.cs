using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GivreClone.App.Services;

// ObservableCollection qui sait remplacer tout son contenu d'un coup (un seul Reset).
// Insérer 5000 messages un par un = O(n²) + 5000 notifications -> l'UI gèle. Un Reset unique
// laisse la ListBox/DataGrid virtualisée ne rendre que le visible.
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var it in items) Items.Add(it);
        OnReset();
    }

    private void OnReset()
    {
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }
}
