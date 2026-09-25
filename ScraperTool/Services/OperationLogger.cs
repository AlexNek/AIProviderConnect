using System.Collections.ObjectModel;

namespace ScraperTool.Services;

public sealed class OperationLogger : IOperationLogger
{
    private readonly ObservableCollection<string> _entries = [];

    public ReadOnlyObservableCollection<string> Entries { get; }

    public OperationLogger()
    {
        Entries = new ReadOnlyObservableCollection<string>(_entries);
    }

    public void Add(string message)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() => _entries.Add(message));
    }

    public void Clear()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() => _entries.Clear());
    }
}
