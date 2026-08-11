using System.Globalization;

namespace Ai200Trainer;

/// <summary>
/// Formats numbers for CSS and SVG attribute values.
/// <para>
/// These must always use a dot as the decimal separator. Razor renders interpolated
/// values with the current culture, so on a machine set to, say, Norwegian, a width of
/// 52.3 becomes "52,3%" and a circle radius of 27.5 becomes "27,5" — both of which
/// browsers reject outright, silently breaking every meter and ring in the app.
/// </para>
/// Display text (scores, dates, counts) deliberately still uses the user's culture;
/// only values destined for a stylesheet or an SVG attribute go through here.
/// </summary>
public static class Css
{
    public static string Num(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>A percentage clamped to 0–100, e.g. <c>"52.3%"</c>.</summary>
    public static string Pct(double value) =>
        Num(Math.Clamp(value, 0, 100)) + "%";

    public static string Px(double value) => Num(value) + "px";
}
