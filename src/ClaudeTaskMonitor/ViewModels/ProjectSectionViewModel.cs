using System.Collections.ObjectModel;
using ClaudeTaskMonitor.Models;

namespace ClaudeTaskMonitor.ViewModels;

/// <summary>Abschnitt mit allen Sitzungen eines Claude-Projekts bzw. „Ohne Projekt“.</summary>
public sealed class ProjectSectionViewModel : ObservableObject
{
    public const string NoProjectKey = "";

    private string _title = string.Empty;
    private string _cpuText = string.Empty;

    public ProjectSectionViewModel(string key) => Key = key;

    public string Key { get; }
    public ObservableCollection<SessionGroupViewModel> Sessions { get; } = new();

    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string CpuText { get => _cpuText; private set => SetProperty(ref _cpuText, value); }

    public void Update(string title, IReadOnlyList<SessionGroup> groups)
    {
        Title = title;
        CpuText = Formatting.Cpu(Math.Min(100, groups.Sum(g => g.TotalCpuPercent)));
        CollectionSync.Sync(
            Sessions,
            groups,
            g => g.Key,
            vm => vm.Key,
            g => new SessionGroupViewModel(g),
            (vm, g) => vm.Update(g));
    }
}
