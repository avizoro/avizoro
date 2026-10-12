using System.Text.RegularExpressions;
using Avizoro.Core.Configuration;
using Avizoro.Core.Models;

namespace Avizoro.Core.Privacy;

/// <summary>
/// Removes personal data and secrets from an event before it leaves the device
/// (GDPR-friendly by default). Two layers:
/// <list type="bullet">
///   <item><b>By key</b> — values of tags, data and breadcrumb data whose key looks
///   sensitive (password, token, authorization, cookie, card…) become
///   <see cref="Filtered"/>, whatever they contain.</item>
///   <item><b>By pattern</b> — emails, JWTs, bearer tokens, credit-card numbers
///   (Luhn-checked) and IBANs are masked inside free text: messages, exception
///   messages, breadcrumb messages and string values.</item>
/// </list>
/// Stack traces, types and the explicit, opt-in <see cref="AvizoroEvent.UserId"/>
/// are left alone. Extra keys/patterns come from <see cref="AvizoroOptions"/>.
/// </summary>
public sealed partial class PiiScrubber
{
    public const string Filtered = "[Filtered]";

    /// <summary>Key fragments that mark a value as sensitive (case-insensitive substring match).</summary>
    public static readonly IReadOnlyList<string> DefaultSensitiveKeys =
    [
        "password", "passwd", "pwd", "secret", "token", "apikey", "api_key", "api-key",
        "authorization", "cookie", "session", "credential", "private_key", "privatekey",
        "creditcard", "credit_card", "cardnumber", "card_number", "cvv", "cvc", "ssn"
    ];

    private readonly string[] _sensitiveKeys;
    private readonly Regex[] _extraPatterns;

    public PiiScrubber(AvizoroOptions options)
    {
        _sensitiveKeys = DefaultSensitiveKeys.Concat(options.ScrubbingSensitiveKeys).Select(k => k.ToLowerInvariant()).ToArray();
        _extraPatterns = options.ScrubbingPatterns.ToArray();
    }

    public void Scrub(AvizoroEvent e)
    {
        e.Message = ScrubText(e.Message);

        for (var ex = e.Exception; ex is not null; ex = ex.InnerException)
            ex.Message = ScrubText(ex.Message) ?? string.Empty;

        foreach (var key in e.Tags.Keys.ToList())
            e.Tags[key] = IsSensitiveKey(key) ? Filtered : ScrubText(e.Tags[key]) ?? string.Empty;

        foreach (var key in e.Data.Keys.ToList())
        {
            if (IsSensitiveKey(key))
                e.Data[key] = Filtered;
            else if (e.Data[key] is string text)
                e.Data[key] = ScrubText(text);
        }

        foreach (var crumb in e.Breadcrumbs)
            Scrub(crumb);
    }

    public void Scrub(Breadcrumb crumb)
    {
        crumb.Message = ScrubText(crumb.Message) ?? string.Empty;
        foreach (var key in crumb.Data.Keys.ToList())
            crumb.Data[key] = IsSensitiveKey(key) ? Filtered : ScrubText(crumb.Data[key]) ?? string.Empty;
    }

    public bool IsSensitiveKey(string key)
    {
        var lower = key.ToLowerInvariant();
        foreach (var fragment in _sensitiveKeys)
        {
            if (lower.Contains(fragment, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public string? ScrubText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        text = BearerToken().Replace(text, "Bearer " + Filtered);
        text = Jwt().Replace(text, Filtered);
        text = Email().Replace(text, Filtered);
        text = Iban().Replace(text, Filtered);
        text = CardCandidate().Replace(text, m => PassesLuhn(m.Value) ? Filtered : m.Value);

        foreach (var pattern in _extraPatterns)
            text = pattern.Replace(text, Filtered);

        return text;
    }

    private static bool PassesLuhn(string candidate)
    {
        var digits = candidate.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (digits.Length is < 13 or > 19)
            return false;

        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var d = digits[digits.Length - 1 - i];
            if (i % 2 == 1)
            {
                d *= 2;
                if (d > 9)
                    d -= 9;
            }
            sum += d;
        }
        return sum % 10 == 0;
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/\-]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    // 13–19 digits, optionally grouped by spaces or dashes.
    [GeneratedRegex(@"\b\d(?:[ -]?\d){12,18}\b")]
    private static partial Regex CardCandidate();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]{4}){3,7}(?:[ ]?[A-Z0-9]{1,3})?\b")]
    private static partial Regex Iban();
}
