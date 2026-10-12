using Avizoro.Core.Models;

namespace Avizoro.Core.Diagnostics;

/// <summary>
/// A bounded, in-memory ring buffer of recent breadcrumbs. The oldest entries
/// are dropped once capacity is reached, so memory stays flat regardless of how
/// long the app runs.
/// </summary>
public interface IBreadcrumbBuffer
{
    void Add(Breadcrumb breadcrumb);

    /// <summary>A point-in-time copy of the current trail, oldest first.</summary>
    IReadOnlyList<Breadcrumb> Snapshot();

    void Clear();
}
