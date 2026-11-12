using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClaudeTaskMonitor.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal static class CollectionSync
{
    /// <summary>
    /// Gleicht eine ObservableCollection mit minimalen Änderungen an die gewünschte Liste an,
    /// damit Zustände wie aufgeklappte Gruppen und die Scrollposition erhalten bleiben.
    /// </summary>
    public static void Sync<TViewModel, TModel>(
        ObservableCollection<TViewModel> target,
        IReadOnlyList<TModel> models,
        Func<TModel, string> modelKey,
        Func<TViewModel, string> viewModelKey,
        Func<TModel, TViewModel> create,
        Action<TViewModel, TModel> update)
    {
        var wanted = new HashSet<string>(models.Select(modelKey));
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(viewModelKey(target[i])))
                target.RemoveAt(i);
        }

        var existing = target.ToDictionary(viewModelKey);
        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            if (existing.TryGetValue(modelKey(model), out var vm))
            {
                update(vm, model);
                var index = target.IndexOf(vm);
                if (index != i)
                    target.Move(index, i);
            }
            else
            {
                target.Insert(i, create(model));
            }
        }
    }
}
