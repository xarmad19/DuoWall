namespace DuoWall.Core.Services;

public readonly record struct TaskbarButtonCandidate(
    double Left,
    double Right,
    double Top,
    double Bottom,
    bool IsButton,
    bool IsOffscreen,
    string Name);

public readonly record struct TaskbarClusterBounds(int LeftPx, int RightPx, int ButtonCount = 0)
{
    public int WidthPx => Math.Max(0, RightPx - LeftPx);
    public bool IsValid => WidthPx > 10 && ButtonCount > 0;
}

/// <summary>
/// Filters UIA descendants of MSTaskListWClass to compute the exact contiguous pixel bounds of visible
/// taskbar icon buttons, excluding ToolBar wrappers, offscreen overflow pagers, container spacers, and trailing orphan slots.
/// </summary>
public static class TaskbarClusterLayoutHelper
{
    private static readonly HashSet<string> PagerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Scroll Up",
        "Scroll Down",
        "Scroll Left",
        "Scroll Right",
        "Previous Page",
        "Next Page",
        "Running applications",
        "Taskbar"
    };

    public static TaskbarClusterBounds ComputeContiguousButtonClusterBounds(
        IEnumerable<TaskbarButtonCandidate> candidates,
        double containerLeft,
        double containerRight,
        double minButtonHeight)
    {
        double minH = Math.Max(16.0, minButtonHeight);
        var valid = new List<TaskbarButtonCandidate>();

        foreach (var c in candidates)
        {
            if (!c.IsButton || c.IsOffscreen) continue;
            if (string.IsNullOrWhiteSpace(c.Name) || PagerNames.Contains(c.Name.Trim())) continue;

            double w = c.Right - c.Left;
            double h = c.Bottom - c.Top;
            if (double.IsNaN(w) || double.IsNaN(h) || double.IsInfinity(w) || double.IsInfinity(h)) continue;
            if (w < 16.0 || w > 260.0 || h < minH) continue;
            if (c.Right <= containerLeft + 1.0 || c.Left >= containerRight + 4.0) continue;

            valid.Add(c);
        }

        if (valid.Count == 0)
            return default;

        valid.Sort((a, b) => a.Left.CompareTo(b.Left));

        double clusterLeft = valid[0].Left;
        double clusterRight = valid[0].Right;
        int count = 1;

        for (int i = 1; i < valid.Count; i++)
        {
            var next = valid[i];
            double btnHeight = Math.Max(20.0, next.Bottom - next.Top);
            double maxContiguousGap = Math.Max(28.0, btnHeight * 0.75);
            if (next.Left - clusterRight > maxContiguousGap)
                break;

            if (next.Right > clusterRight)
                clusterRight = next.Right;
            count++;
        }

        return new TaskbarClusterBounds((int)Math.Floor(clusterLeft), (int)Math.Ceiling(clusterRight), count);
    }
}

