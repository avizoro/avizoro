using IntelligenceKit.Core.Enums;
using IntelligenceKit.Core.Models;

namespace IntelligenceKit.Core.Services;

/// <summary>Readable shortcuts over <see cref="IIntelligenceKit.AddBreadcrumb"/> for the most common app activity.</summary>
public static class IntelligenceKitExtensions
{
    /// <summary>
    /// Records something the user did, e.g. <c>kit.TrackAction("transfer_button_clicked")</c>.
    /// Prefer stable, snake_case names; never put personal data in the name.
    /// </summary>
    public static void TrackAction(this IIntelligenceKit kit, string action, IDictionary<string, string>? data = null)
        => kit.AddBreadcrumb(action, BreadcrumbCategories.User, SeverityLevel.Information, data);

    /// <summary>
    /// Records a screen change that automatic page tracking can't see (tabs, popups,
    /// custom views). Pages are tracked automatically in the MAUI SDK.
    /// </summary>
    public static void TrackScreen(this IIntelligenceKit kit, string screen, IDictionary<string, string>? data = null)
        => kit.AddBreadcrumb(screen, BreadcrumbCategories.Navigation, SeverityLevel.Information, data);
}
