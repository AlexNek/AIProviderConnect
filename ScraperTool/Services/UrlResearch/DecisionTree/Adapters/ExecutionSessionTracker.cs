namespace ScraperTool.Services.UrlResearch.DecisionTree.Adapters;

/// <summary>
/// Tracks the current decision-tree execution session via <see cref="AsyncLocal{T}"/>
/// so that DI-resolved event handlers (e.g. <see cref="DecisionTreeProgressAdapter"/>)
/// can access the tree ID and progress reporter without requiring per-execution DI scopes.
/// </summary>
public sealed class ExecutionSessionTracker
{
    private static readonly AsyncLocal<SessionState?> _current = new();

    /// <summary>Begins a new execution session.</summary>
    public void Begin(string treeId, IProgress<string>? progress)
    {
        _current.Value = new SessionState(treeId, progress);
    }

    /// <summary>Ends the current execution session.</summary>
    public void End()
    {
        _current.Value = null;
    }

    /// <summary>Gets the current session, or <c>null</c> if no execution is in progress.</summary>
    public SessionState? Current => _current.Value;
}
