using System.Windows;
using ClaudeTaskMonitor.Models;

namespace ClaudeTaskMonitor.ViewModels;

public sealed class ProcessRowViewModel : ObservableObject
{
    private const double IndentPerLevel = 12;

    private string _cpuText = string.Empty;
    private string _memoryText = string.Empty;
    private double _cpuPercent;
    private Thickness _indent;

    public ProcessRowViewModel(ProcessSample sample)
    {
        Key = sample.Key;
        Pid = sample.Pid;
        Name = sample.Name;
        Update(sample);
    }

    public string Key { get; }
    public int Pid { get; }
    public string Name { get; }

    public Thickness Indent { get => _indent; private set => SetProperty(ref _indent, value); }
    public double CpuPercent { get => _cpuPercent; private set => SetProperty(ref _cpuPercent, value); }
    public string CpuText { get => _cpuText; private set => SetProperty(ref _cpuText, value); }
    public string MemoryText { get => _memoryText; private set => SetProperty(ref _memoryText, value); }

    public void Update(ProcessSample sample)
    {
        Indent = new Thickness(sample.Depth * IndentPerLevel, 0, 0, 0);
        CpuPercent = sample.CpuPercent;
        CpuText = Formatting.Cpu(sample.CpuPercent);
        MemoryText = Formatting.Memory(sample.WorkingSet);
    }
}
