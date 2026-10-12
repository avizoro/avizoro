using System.Collections.Concurrent;
using Avizoro.Core.Models;

namespace Avizoro.Core.Services;

/// <summary>
/// An ambient, async-flowing context (tags, user, breadcrumbs) layered over the
/// global one. Servers push one per request so concurrent requests don't mix
/// their breadcrumbs or tags:
/// <code>
/// using var scope = AvizoroScope.Push();
/// scope.SetTag("tenant", tenantId);
/// </code>
/// While a scope is active, <see cref="IAvizoro.AddBreadcrumb"/> writes
/// to it and captured events carry its tags, user and breadcrumbs (scope values
/// win over global ones). Disposing restores the previous scope.
/// </summary>
public sealed class AvizoroScope : IDisposable
{
    private static readonly AsyncLocal<AvizoroScope?> Ambient = new();

    private readonly AvizoroScope? _parent;
    private readonly int _capacity;
    private readonly LinkedList<Breadcrumb> _breadcrumbs = new();
    private bool _disposed;

    private AvizoroScope(AvizoroScope? parent, int capacity)
    {
        _parent = parent;
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>The innermost active scope on this async flow, or null.</summary>
    public static AvizoroScope? Current => Ambient.Value;

    /// <summary>Starts a nested scope (inherits the parent's tags and user).</summary>
    public static AvizoroScope Push(int breadcrumbCapacity = 30)
    {
        var parent = Ambient.Value;
        var scope = new AvizoroScope(parent, breadcrumbCapacity);
        if (parent is not null)
        {
            foreach (var tag in parent.Tags)
                scope.Tags[tag.Key] = tag.Value;
            scope.UserId = parent.UserId;
        }
        Ambient.Value = scope;
        return scope;
    }

    public ConcurrentDictionary<string, string> Tags { get; } = new();

    public string? UserId { get; set; }

    public void SetTag(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;
        if (value is null)
            Tags.TryRemove(key, out _);
        else
            Tags[key] = value;
    }

    public void AddBreadcrumb(Breadcrumb breadcrumb)
    {
        lock (_breadcrumbs)
        {
            _breadcrumbs.AddLast(breadcrumb);
            while (_breadcrumbs.Count > _capacity)
                _breadcrumbs.RemoveFirst();
        }
    }

    public IReadOnlyList<Breadcrumb> Breadcrumbs()
    {
        lock (_breadcrumbs)
            return _breadcrumbs.ToList();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (Ambient.Value == this)
            Ambient.Value = _parent;
    }
}

/// <summary>
/// Marks exceptions that were already reported, so the same exception observed by
/// two integrations (e.g. request middleware and the logger provider) becomes one event.
/// </summary>
public static class ExceptionCapture
{
    private const string Key = "Avizoro.Captured";

    public static void MarkCaptured(Exception exception)
    {
        try
        {
            exception.Data[Key] = true;
        }
        catch
        {
            // Some exception types have read-only Data.
        }
    }

    public static bool IsCaptured(Exception exception)
    {
        try
        {
            return exception.Data.Contains(Key);
        }
        catch
        {
            return false;
        }
    }
}
