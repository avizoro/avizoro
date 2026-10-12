using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Avizoro.Core.Models;

namespace Avizoro.Core.Diagnostics;

/// <summary>
/// Derives a stable grouping key ("fingerprint") for an event so that repeated
/// occurrences of the same problem collapse into a single issue.
///
/// For exceptions the key is <c>projectId + exceptionType + top in-app frame</c>:
/// frames from the runtime/framework (System.*, Microsoft.*, Android.*, java.*…)
/// are skipped so a crash inside LINQ called from two different places doesn't
/// merge into one issue, and compiler-generated async/lambda names are normalized
/// so recompiling doesn't split an issue. For everything else it falls back to
/// <c>projectId + eventType + a normalized message</c> (digits and GUIDs stripped)
/// so that "User 12 not found" and "User 34 not found" group together.
///
/// An event can override all of this with <see cref="AvizoroEvent.Fingerprint"/>.
/// </summary>
public static partial class EventFingerprint
{
    /// <summary>Placeholder in a custom fingerprint that expands to the default grouping key.</summary>
    public const string DefaultToken = "{{ default }}";

    /// <summary>Frame prefixes considered framework/runtime code (not "in-app").</summary>
    public static readonly IReadOnlyList<string> FrameworkPrefixes =
    [
        "System.", "Microsoft.", "Mono.", "Xamarin.", "Android.", "AndroidX.", "Java.", "Javax.",
        "Foundation.", "UIKit.", "ObjCRuntime.", "CoreFoundation.", "WinRT.", "Windows.",
        "Avizoro.", "Newtonsoft.", "SQLite.", "SQLitePCL.",
        "java.", "javax.", "android.", "androidx.", "kotlin.", "kotlinx.", "dalvik.", "com.android.", "sun.", "libcore."
    ];

    public sealed record Result(string Fingerprint, string Title, string? Culprit);

    public static Result Compute(AvizoroEvent e)
    {
        var (basis, title, culprit) = DefaultBasis(e);

        if (e.Fingerprint is { Count: > 0 } custom)
        {
            var parts = custom.Select(v => v == DefaultToken ? basis : $"custom:{v}");
            return new Result(Hash($"{e.ProjectId}\n{string.Join('\n', parts)}"), title, culprit);
        }

        return new Result(Hash(basis), title, culprit);
    }

    private static (string Basis, string Title, string? Culprit) DefaultBasis(AvizoroEvent e)
    {
        if (e.Exception is { } ex && !string.IsNullOrWhiteSpace(ex.Type))
        {
            var frame = TopFrame(ex.StackTrace);
            var culprit = frame is null ? null : ShortFrame(frame);
            return ($"{e.ProjectId}\n{ex.Type}\n{frame}", ShortTypeName(ex.Type), culprit);
        }

        var message = e.Message ?? string.Empty;
        var fallbackTitle = string.IsNullOrWhiteSpace(message)
            ? e.EventType.ToString()
            : Truncate(message, 120);

        return ($"{e.ProjectId}\n{e.EventType}\n{NormalizeMessage(message)}", fallbackTitle, null);
    }

    /// <summary>
    /// First in-app frame of a stack trace (falling back to the first frame when
    /// every frame is framework code), normalized: file/line suffix removed and
    /// compiler-generated async/lambda names collapsed to their method.
    /// </summary>
    public static string? TopFrame(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
            return null;

        string? first = null;
        foreach (var raw in stackTrace.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("at ", StringComparison.Ordinal))
                continue; // headers, "--- End of stack trace ---", "Caused by:" …

            line = NormalizeFrame(line[3..]);
            if (line.Length == 0)
                continue;

            first ??= line;
            if (!IsFrameworkFrame(line))
                return line;
        }

        return first ?? FirstNonEmptyLine(stackTrace);
    }

    public static bool IsFrameworkFrame(string frame)
    {
        foreach (var prefix in FrameworkPrefixes)
        {
            if (frame.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string NormalizeFrame(string frame)
    {
        // Drop the volatile " in <file>:line N" tail (managed) and "(File.java:12)"
        // line numbers (Java) so line-number churn doesn't split one problem.
        var inIndex = frame.IndexOf(" in ", StringComparison.Ordinal);
        if (inIndex >= 0)
            frame = frame[..inIndex];
        frame = JavaLocation().Replace(frame, "");

        // "Cart.<Checkout>d__5.MoveNext()" → "Cart.Checkout()"; "<Run>b__0_1" → "Run".
        frame = AsyncStateMachine().Replace(frame, "$1()");
        frame = Lambda().Replace(frame, "$1");
        return frame.Trim();
    }

    private static string? FirstNonEmptyLine(string text)
        => text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

    /// <summary>Compact "Type.Method" form of a frame, for display as the culprit.</summary>
    private static string ShortFrame(string frame)
    {
        var withoutArgs = frame;
        var paren = withoutArgs.IndexOf('(');
        if (paren >= 0)
            withoutArgs = withoutArgs[..paren];

        var parts = withoutArgs.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[^2]}.{parts[^1]}"
            : withoutArgs;
    }

    private static string ShortTypeName(string type)
    {
        var lastDot = type.LastIndexOf('.');
        return lastDot >= 0 && lastDot < type.Length - 1 ? type[(lastDot + 1)..] : type;
    }

    private static string NormalizeMessage(string message)
    {
        var noGuids = GuidRegex().Replace(message, "#");
        return DigitsRegex().Replace(noGuids, "#");
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static string Hash(string input)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsRegex();

    [GeneratedRegex(@"<([^>]+)>d__\d+\.MoveNext\(\)")]
    private static partial Regex AsyncStateMachine();

    [GeneratedRegex(@"<([^>]+)>b__[\d_]+")]
    private static partial Regex Lambda();

    [GeneratedRegex(@"\((?:[^():]+\.(?:java|kt)|SourceFile|Unknown Source|Native Method):?\d*\)$")]
    private static partial Regex JavaLocation();
}
