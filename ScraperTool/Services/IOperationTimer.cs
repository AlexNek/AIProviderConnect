using System.ComponentModel;

namespace ScraperTool.Services;

public interface IOperationTimer : IDisposable, INotifyPropertyChanged
{
    TimeSpan Elapsed { get; }

    string ElapsedTime { get; }

    bool IsRunning { get; }

    void Pause();

    void Reset();

    void Resume();

    void Start();

    void Stop();
}
