using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;

namespace Ai200Trainer;

/// <summary>One top-level section of a cram sheet, used to build the jump links.</summary>
public sealed record CramSection(string Anchor, string Title);

/// <summary>
/// Renders the small markdown subset the cram sheets are written in, so each exam can ship
/// its own <c>cram.md</c> instead of the content being baked into a Razor page.
/// <para>
/// Supported, and deliberately nothing more:
/// <list type="bullet">
///   <item><c>## Title | 20–25%</c> — a section, with an optional weight after a pipe</item>
///   <item><c>### Subtitle</c> — a heading inside a section</item>
///   <item><c>- item</c> — a fact in the current list</item>
///   <item><c>**bold**</c> and <c>`code`</c> inline</item>
/// </list>
/// Everything is HTML-encoded before any markup is added, so content can contain
/// characters like &lt;=&gt; safely.
/// </para>
/// </summary>
public static class CramSheet
{
    /// <summary>Section headings in order, for the table of contents.</summary>
    public static IReadOnlyList<CramSection> Sections(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return [];

        var sections = new List<CramSection>();
        foreach (var line in source.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').Trim();
            if (!trimmed.StartsWith("## ") || trimmed.StartsWith("### ")) continue;

            var (title, _) = SplitWeight(trimmed[3..].Trim());
            sections.Add(new CramSection(Anchor(title), title));
        }
        return sections;
    }

    public static MarkupString Render(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return new MarkupString(string.Empty);

        var html = new StringBuilder();
        var inList = false;
        var inSection = false;

        foreach (var raw in source.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();

            if (line.Length == 0) continue;

            if (line.StartsWith("### "))
            {
                CloseList(html, ref inList);
                html.Append("<h3>").Append(Inline(line[4..].Trim())).Append("</h3>");
            }
            else if (line.StartsWith("## "))
            {
                CloseList(html, ref inList);
                if (inSection) html.Append("</section>");

                var (title, weight) = SplitWeight(line[3..].Trim());
                html.Append("<section class=\"cram-section\" id=\"").Append(Anchor(title)).Append("\">")
                    .Append("<h2>").Append(Inline(title));

                if (weight is { Length: > 0 })
                {
                    html.Append("<span class=\"w\">").Append(Inline(weight)).Append("</span>");
                }

                html.Append("</h2>");
                inSection = true;
            }
            else if (line.StartsWith("- "))
            {
                if (!inList) { html.Append("<ul class=\"facts\">"); inList = true; }
                html.Append("<li>").Append(Inline(line[2..].Trim())).Append("</li>");
            }
            else
            {
                CloseList(html, ref inList);
                html.Append("<p class=\"muted\">").Append(Inline(line)).Append("</p>");
            }
        }

        CloseList(html, ref inList);
        if (inSection) html.Append("</section>");

        return new MarkupString(html.ToString());
    }

    private static void CloseList(StringBuilder html, ref bool inList)
    {
        if (!inList) return;
        html.Append("</ul>");
        inList = false;
    }

    private static (string Title, string? Weight) SplitWeight(string heading)
    {
        var pipe = heading.IndexOf('|');
        return pipe < 0
            ? (heading, null)
            : (heading[..pipe].Trim(), heading[(pipe + 1)..].Trim());
    }

    /// <summary>Slug for the section id, matching what the table of contents links to.</summary>
    public static string Anchor(string title)
    {
        var slug = new StringBuilder(title.Length);
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) slug.Append(c);
            else if (c is ' ' or '-' && slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        return slug.ToString().Trim('-');
    }

    /// <summary>Encodes, then applies <c>**bold**</c> and <c>`code`</c>.</summary>
    private static string Inline(string text)
    {
        var encoded = HtmlEncoder.Default.Encode(text);

        encoded = Wrap(encoded, '`', "<code>", "</code>");
        encoded = WrapDouble(encoded, "**", "<b>", "</b>");

        return encoded;
    }

    private static string Wrap(string text, char marker, string open, string close)
    {
        if (!text.Contains(marker)) return text;

        var parts = text.Split(marker);
        var balanced = parts.Length % 2 == 1;
        var builder = new StringBuilder(text.Length + 32);

        for (var i = 0; i < parts.Length; i++)
        {
            var isInside = i % 2 == 1 && (balanced || i < parts.Length - 1);
            if (isInside) builder.Append(open).Append(parts[i]).Append(close);
            else builder.Append(parts[i]);
        }

        return builder.ToString();
    }

    private static string WrapDouble(string text, string marker, string open, string close)
    {
        if (!text.Contains(marker)) return text;

        var parts = text.Split(marker);
        var balanced = parts.Length % 2 == 1;
        var builder = new StringBuilder(text.Length + 32);

        for (var i = 0; i < parts.Length; i++)
        {
            var isInside = i % 2 == 1 && (balanced || i < parts.Length - 1);
            if (isInside) builder.Append(open).Append(parts[i]).Append(close);
            else builder.Append(parts[i]);
        }

        return builder.ToString();
    }
}
