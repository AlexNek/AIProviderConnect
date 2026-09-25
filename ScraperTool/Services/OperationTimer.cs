using System.ComponentModel;
using System.Diagnostics;

namespace ScraperTool.Services;

public sealed class OperationTimer : IOperationTimer
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private Stopwatch? _stopwatch;

    private Timer? _timer;

    public TimeSpan Elapsed => _stopwatch?.Elapsed ?? TimeSpan.Zero;

    public string ElapsedTime { get; private set; } = "0m 0s";

    public bool IsRunning => _stopwatch?.IsRunning ?? false;

    public void Dispose()
    {
        Stop();
    }

    public void Pause()
    {
        _timer?.Dispose();
        _timer = null;
        _stopwatch?.Stop();
        OnPropertyChanged(nameof(IsRunning));
    }

    public void Reset()
    {
        Stop();
        ElapsedTime = "0m 0s";
        OnPropertyChanged(nameof(ElapsedTime));
    }

    public void Resume()
    {
        _timer = new Timer(OnTimerTick, null, 0, 1000);
        _stopwatch?.Start();
        OnPropertyChanged(nameof(IsRunning));
    }

    public void Start()
    {
        Reset();
        _stopwatch = Stopwatch.StartNew();
        _timer = new Timer(OnTimerTick, null, 0, 1000);
        OnPropertyChanged(nameof(IsRunning));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _stopwatch?.Stop();
        _stopwatch = null;
        OnPropertyChanged(nameof(IsRunning));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void OnTimerTick(object? state)
    {
        var sw = _stopwatch;
        if (sw is null)
        {
            return;
        }

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                ElapsedTime = $"{sw.Elapsed.Minutes}m {sw.Elapsed.Seconds}s";
                OnPropertyChanged(nameof(ElapsedTime));
            });
    }
}
