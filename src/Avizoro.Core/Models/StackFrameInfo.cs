using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Avizoro.Core.Models;

/// <summary>
/// One managed stack frame with the identifiers needed to symbolicate it on the
/// server. Release builds usually ship without PDBs, so <see cref="FileName"/> and
/// <see cref="LineNumber"/> are often missing on the device; the server recovers
/// them from an uploaded PDB using <see cref="ModuleVersionId"/> +
/// <see cref="MetadataToken"/> + <see cref="ILOffset"/>.
/// </summary>
public class StackFrameInfo
{
    /// <summary>Display name, e.g. <c>MyApp.CartService.Checkout(Int32)</c>.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Assembly/module name, e.g. <c>MyApp.dll</c>.</summary>
    public string? Module { get; set; }

    /// <summary>MVID of the module — identifies the exact build the PDB must match.</summary>
    public Guid? ModuleVersionId { get; set; }

    /// <summary>Metadata token of the method (0x06xxxxxx).</summary>
    public int? MetadataToken { get; set; }

    /// <summary>IL offset within the method, when known.</summary>
    public int? ILOffset { get; set; }

    public string? FileName { get; set; }

    public int? LineNumber { get; set; }

    public int? ColumnNumber { get; set; }

    /// <summary>
    /// Structured frames for <paramref name="exception"/>, or null when the runtime
    /// can't provide them. Never throws: frame capture is best-effort.
    /// </summary>
    public static List<StackFrameInfo>? Capture(Exception exception)
    {
        try
        {
            var frames = new StackTrace(exception, fNeedFileInfo: true).GetFrames();
            if (frames.Length == 0)
                return null;

            var result = new List<StackFrameInfo>(frames.Length);
            foreach (var frame in frames)
            {
                var method = frame.GetMethod();
                if (method is null)
                    continue;

                var ilOffset = frame.GetILOffset();
                var line = frame.GetFileLineNumber();
                var column = frame.GetFileColumnNumber();

                result.Add(new StackFrameInfo
                {
                    Method = DisplayName(method),
                    Module = SafeModuleName(method),
                    ModuleVersionId = SafeMvid(method),
                    MetadataToken = SafeToken(method),
                    ILOffset = ilOffset == StackFrame.OFFSET_UNKNOWN ? null : ilOffset,
                    FileName = frame.GetFileName(),
                    LineNumber = line == 0 ? null : line,
                    ColumnNumber = column == 0 ? null : column
                });
            }

            return result.Count == 0 ? null : result;
        }
        catch
        {
            return null;
        }
    }

    private static string DisplayName(MethodBase method)
    {
        var sb = new StringBuilder();
        if (method.DeclaringType is { } type)
            sb.Append(type.FullName ?? type.Name).Append('.');
        sb.Append(method.Name).Append('(');
        try
        {
            sb.AppendJoin(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        }
        catch
        {
            // Parameter metadata can be trimmed away; the name alone still helps.
        }
        return sb.Append(')').ToString();
    }

    private static string? SafeModuleName(MethodBase method)
    {
        try { return method.Module.Name; } catch { return null; }
    }

    private static Guid? SafeMvid(MethodBase method)
    {
        try { return method.Module.ModuleVersionId; } catch { return null; }
    }

    private static int? SafeToken(MethodBase method)
    {
        try { return method.MetadataToken; } catch { return null; }
    }
}
