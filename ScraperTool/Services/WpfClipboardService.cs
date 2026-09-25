namespace ScraperTool.Services;

/// <summary>
/// WPF implementation of IClipboardService.
/// </summary>
public sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        System.Windows.Clipboard.SetText(text);
    }
}
