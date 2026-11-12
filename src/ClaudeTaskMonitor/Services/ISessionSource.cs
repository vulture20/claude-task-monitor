using ClaudeTaskMonitor.Models;

namespace ClaudeTaskMonitor.Services;

/// <summary>Liefert pro Durchlauf die Sitzungen samt Prozessbäumen (echte Abtastung oder Demo-Daten).</summary>
public interface ISessionSource : IDisposable
{
    IReadOnlyList<SessionGroup> Scan();

    /// <summary>Nach einer Pause: CPU-Last nicht über die ganze Pause mitteln, sondern neu beginnen.</summary>
    void ResetBaseline();
}
