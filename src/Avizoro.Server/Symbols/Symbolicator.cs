using System.Collections.Concurrent;
using System.Text;
using Avizoro.Core.Models;
using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Symbols;

/// <summary>
/// Process-wide cache of parsed symbol files (PDB readers are costly to open).
/// Bounded crudely — cleared when it grows past <see cref="MaxEntries"/> — and
/// invalidated whenever symbols are uploaded or deleted.
/// </summary>
public sealed class SymbolCache
{
    private const int MaxEntries = 64;

    private readonly ConcurrentDictionary<string, object?> _entries = new();

    /// <summary>True when <paramref name="key"/> was looked up before (value may be a cached null = "no symbols").</summary>
    public bool TryGet<T>(string key, out T? value) where T : class
    {
        var found = _entries.TryGetValue(key, out var hit);
        value = hit as T;
        return found;
    }

    public void Set(string key, object? value)
    {
        if (_entries.Count >= MaxEntries)
            Clear();
        _entries[key] = value;
    }

    public void Clear()
    {
        foreach (var disposable in _entries.Values.OfType<IDisposable>())
            disposable.Dispose();
        _entries.Clear();
    }
}

/// <summary>
/// Rewrites an event's exception tree at read time using uploaded symbols:
/// managed frames get file:line from a matching portable PDB (by module MVID),
/// and Java frames/types are retraced with the project+release R8 mapping.
/// The stored event is never modified — symbols uploaded later still apply.
/// </summary>
public sealed class Symbolicator
{
    private readonly AvizoroDbContext _db;
    private readonly SymbolCache _cache;

    public Symbolicator(AvizoroDbContext db, SymbolCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>Returns the symbolicated tree and whether anything changed.</summary>
    public async Task<(ExceptionInfo? Exception, bool Changed)> SymbolicateAsync(
        ExceptionInfo? exception, string projectId, string release, CancellationToken ct = default)
    {
        if (exception is null)
            return (null, false);

        var mapping = string.IsNullOrWhiteSpace(release)
            ? null
            : await LoadMappingAsync(SymbolKinds.MappingKey(projectId, release), ct);

        var changed = false;
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e.Frames is { Count: > 0 } && await ResolveFramesAsync(e, ct))
                changed = true;

            if (mapping is not null && !string.IsNullOrEmpty(e.StackTrace))
            {
                var retraced = mapping.RetraceStackTrace(e.StackTrace);
                var type = mapping.RetraceClass(e.Type);
                if (retraced != e.StackTrace || type != e.Type)
                {
                    e.StackTrace = retraced;
                    e.Type = type;
                    changed = true;
                }
            }
        }

        return (exception, changed);
    }

    /// <summary>Fills missing file/line on managed frames and rebuilds the text trace.</summary>
    private async Task<bool> ResolveFramesAsync(ExceptionInfo e, CancellationToken ct)
    {
        var resolved = false;
        foreach (var frame in e.Frames!)
        {
            if (frame.LineNumber is not null || frame.ModuleVersionId is not { } mvid ||
                frame.MetadataToken is not { } token || frame.ILOffset is not { } il)
                continue;

            SourceLocation? location;
            try
            {
                var pdb = await LoadPdbAsync(mvid, ct);
                location = pdb?.Resolve(token, il);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or BadImageFormatException or InvalidOperationException)
            {
                location = null; // cache evicted mid-read, or an unreadable PDB
            }
            if (location is null)
                continue;

            frame.FileName = location.File;
            frame.LineNumber = location.Line;
            frame.ColumnNumber = location.Column;
            resolved = true;
        }

        if (resolved)
            e.StackTrace = Render(e.Frames!);

        return resolved;
    }

    /// <summary>.NET-style stack trace text from structured frames.</summary>
    public static string Render(IEnumerable<StackFrameInfo> frames)
    {
        var sb = new StringBuilder();
        foreach (var f in frames)
        {
            sb.Append("   at ").Append(f.Method);
            if (f.FileName is not null && f.LineNumber is not null)
                sb.Append(" in ").Append(f.FileName).Append(":line ").Append(f.LineNumber);
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    private async Task<PortablePdb?> LoadPdbAsync(Guid mvid, CancellationToken ct)
    {
        var key = mvid.ToString("N");
        if (_cache.TryGet<PortablePdb>("pdb:" + key, out var cached))
            return cached;

        var file = await _db.Symbols.AsNoTracking()
            .Where(s => s.Key == key && (s.Kind == SymbolKinds.PortablePdb || s.Kind == SymbolKinds.EmbeddedPdbAssembly))
            .OrderByDescending(s => s.UploadedAt)
            .FirstOrDefaultAsync(ct);

        var pdb = file is null ? null : Open(file);
        _cache.Set("pdb:" + key, pdb);
        return pdb;
    }

    private static PortablePdb? Open(SymbolFile file)
    {
        try
        {
            return file.Kind == SymbolKinds.EmbeddedPdbAssembly
                ? PortablePdb.FromAssemblyWithEmbeddedPdb(file.Content)
                : PortablePdb.FromPdb(file.Content);
        }
        catch
        {
            return null; // corrupt/unsupported file: symbolication is best-effort
        }
    }

    private async Task<ProguardMapping?> LoadMappingAsync(string key, CancellationToken ct)
    {
        if (_cache.TryGet<ProguardMapping>("map:" + key, out var cached))
            return cached;

        var content = await _db.Symbols.AsNoTracking()
            .Where(s => s.Kind == SymbolKinds.ProguardMapping && s.Key == key)
            .OrderByDescending(s => s.UploadedAt)
            .Select(s => s.Content)
            .FirstOrDefaultAsync(ct);

        var mapping = content is null ? null : ProguardMapping.Parse(Encoding.UTF8.GetString(content));
        _cache.Set("map:" + key, mapping);
        return mapping;
    }
}
