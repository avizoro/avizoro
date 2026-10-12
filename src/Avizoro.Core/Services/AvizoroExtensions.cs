using Avizoro.Core.Enums;
using Avizoro.Core.Models;

namespace Avizoro.Core.Services;

/// <summary>Readable shortcuts over <see cref="IAvizoro.AddBreadcrumb"/> for the most common app activity.</summary>
public static class AvizoroExtensions
{
    /// <summary>
    /// Records something the user did, e.g. <c>kit.TrackAction("transfer_button_clicked")</c>.
    /// Prefer stable, snake_case names; never put personal data in the name.
    /// </summary>
    public static void TrackAction(this IAvizoro kit, string action, IDictionary<string, string>? data = null)
        => kit.AddBreadcrumb(action, BreadcrumbCategories.User, SeverityLevel.Information, data);

    /// <summary>
    /// Records a screen change that automatic page tracking can't see (tabs, popups,
    /// custom views). Pages are tracked automatically in the MAUI SDK.
    /// </summary>
    public static void TrackScreen(this IAvizoro kit, string screen, IDictionary<string, string>? data = null)
        => kit.AddBreadcrumb(screen, BreadcrumbCategories.Navigation, SeverityLevel.Information, data);
}
