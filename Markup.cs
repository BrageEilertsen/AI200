using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;

namespace Ai200Trainer;

/// <summary>
/// Renders the small amount of inline formatting the question bank uses.
/// </summary>
public static class Markup
{
    /// <summary>
    /// Turns `backtick spans` into &lt;code&gt; elements. Everything outside the backticks is
    /// HTML-encoded, so question text can safely contain characters like &lt;=&gt; (the pgvector
    /// cosine operator) or generic type parameters without being interpreted as markup.
    /// <para>
    /// An unmatched trailing backtick is treated as a literal character rather than opening a
    /// code span that swallows the rest of the sentence.
    /// </para>
    /// </summary>
    /// <summary>
    /// Strips backtick markers without adding markup, for places that cannot render elements —
    /// notably the text of an <c>&lt;option&gt;</c> inside a select, where a code span is not
    /// possible and the backticks would otherwise show up literally.
    /// </summary>
    public static string Plain(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Replace("`", string.Empty);

    public static MarkupString InlineCode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new MarkupString(string.Empty);

        if (!text.Contains('`'))
        {
            return new MarkupString(HtmlEncoder.Default.Encode(text));
        }

        var parts = text.Split('`');

        // An odd number of segments means the backticks are balanced.
        var balanced = parts.Length % 2 == 1;
        var builder = new StringBuilder(text.Length + 32);

        for (var i = 0; i < parts.Length; i++)
        {
            var encoded = HtmlEncoder.Default.Encode(parts[i]);
            var isCode = i % 2 == 1 && (balanced || i < parts.Length - 1);

            if (isCode)
            {
                builder.Append("<code>").Append(encoded).Append("</code>");
            }
            else
            {
                // Put the stray backtick back so the text reads as the author wrote it.
                if (i > 0 && !balanced && i == parts.Length - 1) builder.Append('`');
                builder.Append(encoded);
            }
        }

        return new MarkupString(builder.ToString());
    }
}
