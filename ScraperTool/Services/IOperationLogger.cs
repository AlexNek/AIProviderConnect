using System.Collections.ObjectModel;

namespace ScraperTool.Services;

public interface IOperationLogger
{
    ReadOnlyObservableCollection<string> Entries { get; }

    void Add(string message);

    void Clear();
}
