using System.Text;
using System.Text.RegularExpressions;

namespace Avizoro.Server.Symbols;

/// <summary>
/// Minimal R8/ProGuard <c>mapping.txt</c> reader and retracer. Understands the
/// standard format:
/// <code>
/// com.example.CartService -> a.b:
///     int count -> a
///     1:4:void checkout(int):20:23 -> a
///     void reset() -> b
/// </code>
/// and rewrites obfuscated Java frames (<c>at a.b.a(SourceFile:3)</c>) back to
/// <c>at com.example.CartService.checkout(CartService.java:22)</c>.
/// </summary>
public sealed partial class ProguardMapping
{
    private sealed record MethodMapping(string Name, int? ObfStart, int? ObfEnd, int? OrigStart, int? OrigEnd);

    private sealed class ClassMapping(string original)
    {
        public string Original { get; } = original;
        public Dictionary<string, List<MethodMapping>> Methods { get; } = new();
    }

    private readonly Dictionary<string, ClassMapping> _byObfuscated = new();

    public int ClassCount => _byObfuscated.Count;

    public static ProguardMapping Parse(string text)
    {
        var mapping = new ProguardMapping();
        ClassMapping? current = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
                continue;

            if (!char.IsWhiteSpace(line[0]))
            {
                var cls = ClassLine().Match(line);
                if (cls.Success)
                {
                    current = new ClassMapping(cls.Groups["orig"].Value);
                    mapping._byObfuscated[cls.Groups["obf"].Value] = current;
                }
                else
                {
                    current = null;
                }
                continue;
            }

            if (current is null)
                continue;

            var m = MethodLine().Match(line);
            if (!m.Success)
                continue; // a field, or something we don't need

            var entry = new MethodMapping(
                m.Groups["name"].Value,
                IntOrNull(m.Groups["obfStart"]), IntOrNull(m.Groups["obfEnd"]),
                IntOrNull(m.Groups["origStart"]), IntOrNull(m.Groups["origEnd"]));

            var obfName = m.Groups["obf"].Value;
            if (!current.Methods.TryGetValue(obfName, out var list))
                current.Methods[obfName] = list = new List<MethodMapping>();
            list.Add(entry);
        }

        return mapping;
    }

    /// <summary>Original name of an obfuscated class, or the input when unknown.</summary>
    public string RetraceClass(string obfuscated)
        => _byObfuscated.TryGetValue(obfuscated, out var c) ? c.Original : obfuscated;

    /// <summary>Retraces every recognizable Java frame in a stack trace; other lines pass through.</summary>
    public string RetraceStackTrace(string stackTrace)
    {
        var sb = new StringBuilder(stackTrace.Length);
        var lines = stackTrace.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var hadCr = line.EndsWith('\r');
            if (hadCr)
                line = line[..^1];

            sb.Append(RetraceLine(line));
            if (hadCr)
                sb.Append('\r');
            if (i < lines.Length - 1)
                sb.Append('\n');
        }
        return sb.ToString();
    }

    private string RetraceLine(string line)
    {
        var frame = JavaFrame().Match(line);
        if (!frame.Success)
            return ExceptionHeader().Replace(line, m => RetraceClass(m.Value), 1);

        var cls = frame.Groups["cls"].Value;
        if (!_byObfuscated.TryGetValue(cls, out var mapping))
            return line;

        var method = frame.Groups["method"].Value;
        int? lineNumber = int.TryParse(frame.Groups["line"].Value, out var n) ? n : null;

        var name = method;
        int? originalLine = lineNumber;
        if (mapping.Methods.TryGetValue(method, out var candidates))
        {
            var match = lineNumber is { } ln
                ? candidates.FirstOrDefault(c => c.ObfStart <= ln && ln <= c.ObfEnd) ?? candidates[0]
                : candidates[0];
            name = match.Name;

            if (lineNumber is { } obfLine && match.ObfStart is { } obfStart && match.OrigStart is { } origStart)
                originalLine = match.OrigEnd is { } origEnd && origEnd > origStart
                    ? origStart + (obfLine - obfStart)
                    : origStart;
        }

        var simpleName = mapping.Original[(mapping.Original.LastIndexOf('.') + 1)..];
        var outer = simpleName.Split('$')[0];
        var location = originalLine is { } l ? $"{outer}.java:{l}" : $"{outer}.java";
        return $"{frame.Groups["indent"].Value}at {mapping.Original}.{name}({location})";
    }

    private static int? IntOrNull(Group g) => g.Success && int.TryParse(g.Value, out var v) ? v : null;

    [GeneratedRegex(@"^(?<orig>\S+) -> (?<obf>\S+):$")]
    private static partial Regex ClassLine();

    // "    1:4:void checkout(int):20:23 -> a" (line ranges are optional)
    [GeneratedRegex(@"^\s+(?:(?<obfStart>\d+):(?<obfEnd>\d+):)?\S+ (?<name>[^\s(]+)\([^)]*\)(?::(?<origStart>\d+)(?::(?<origEnd>\d+))?)? -> (?<obf>\S+)$")]
    private static partial Regex MethodLine();

    // "	at a.b.c(SourceFile:12)" / "at a.b.c(Unknown Source)"
    [GeneratedRegex(@"^(?<indent>\s*)at (?<cls>[\w$.]+)\.(?<method>[\w$<>]+)\((?<file>[^:)]*)(?::(?<line>\d+))?\)\s*$")]
    private static partial Regex JavaFrame();

    // "a.b.c: message" at the start of a line (exception header / "Caused by:")
    [GeneratedRegex(@"(?<=^(?:Caused by: )?)[\w$]+(?:\.[\w$]+)+(?=:|$)")]
    private static partial Regex ExceptionHeader();
}
