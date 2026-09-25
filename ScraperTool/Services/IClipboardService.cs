namespace ScraperTool.Services;

/// <summary>
/// Abstraction for clipboard operations to enable testing and eliminate duplication.
/// </summary>
public interface IClipboardService
{
    void SetText(string text);
}
