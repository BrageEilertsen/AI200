using Microsoft.AspNetCore.Components;

namespace Ai200Trainer.Components.Shared;

/// <summary>
/// Inline 16px stroke icons. Kept as markup constants so they inherit currentColor
/// and need no icon font or external request.
/// </summary>
public static class Icons
{
    private const string Open =
        """<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">""";

    private static MarkupString Svg(string body) => new(Open + body + "</svg>");

    public static MarkupString Dashboard => Svg(
        """<rect x="2" y="2" width="5" height="5" rx="1"/><rect x="9" y="2" width="5" height="5" rx="1"/><rect x="2" y="9" width="5" height="5" rx="1"/><rect x="9" y="9" width="5" height="5" rx="1"/>""");

    public static MarkupString Practice => Svg(
        """<circle cx="8" cy="8" r="6"/><circle cx="8" cy="8" r="2.5"/>""");

    public static MarkupString Exam => Svg(
        """<circle cx="8" cy="9" r="5.5"/><path d="M8 6.5V9l1.75 1.25M6 1.5h4"/>""");

    public static MarkupString Browse => Svg(
        """<path d="M2.5 4h11M2.5 8h11M2.5 12h7"/>""");

    public static MarkupString Cram => Svg(
        """<path d="M8.5 1.5 3 9h4l-.5 5.5L13 7H9l-.5-5.5Z"/>""");

    public static MarkupString Stats => Svg(
        """<path d="M2 14h12M4.5 11.5V7M8 11.5V3M11.5 11.5V8.5"/>""");

    public static MarkupString External => Svg(
        """<path d="M6.5 3H3.5A1.5 1.5 0 0 0 2 4.5v8A1.5 1.5 0 0 0 3.5 14h8a1.5 1.5 0 0 0 1.5-1.5v-3M9.5 2H14v4.5M14 2 7.5 8.5"/>""");

    public static MarkupString Hint => Svg(
        """<path d="M6 12.5h4M6.5 14.5h3M8 1.5a4.5 4.5 0 0 0-2.6 8.17c.37.27.6.69.6 1.15v.18h4v-.18c0-.46.23-.88.6-1.15A4.5 4.5 0 0 0 8 1.5Z"/>""");

    public static MarkupString Check => Svg("""<path d="M3 8.5 6.25 12 13 4.5"/>""");

    public static MarkupString Cross => Svg("""<path d="M4 4l8 8M12 4l-8 8"/>""");
}
