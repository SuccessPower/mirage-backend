using System.Collections.Concurrent;
using System.Net;
using System.Reflection;

namespace Mirage.Infrastructure.Email;

// Loads the .html files under Email/Templates (embedded resources, see the csproj) and does
// simple {{TOKEN}} substitution — no conditionals/loops, since transactional emails don't need them.
internal static class TemplateEngine
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();
    private static readonly Assembly Assembly = typeof(TemplateEngine).Assembly;

    // Renders `templateName` with HTML-encoded token values, slots the CTA button HTML (already
    // safe markup, not encoded) in place of {{CTA_BLOCK}}, then wraps the result in layout.html.
    public static string RenderPage(string templateName, string preheader,
        IReadOnlyDictionary<string, string> tokens, string ctaBlock = "")
    {
        var content = Render(templateName, tokens).Replace("{{CTA_BLOCK}}", ctaBlock);

        var layoutTokens = new Dictionary<string, string>
        {
            ["PREHEADER"] = WebUtility.HtmlEncode(preheader),
            ["CONTENT"] = content,
            ["YEAR"] = DateTime.UtcNow.Year.ToString()
        };
        return Render("layout", layoutTokens, encodeValues: false);
    }

    public static string PrimaryButton(string href, string label, string color = "#6C4EF2") => $"""
        <tr><td style="padding:0 48px;" align="center">
          <table role="presentation" cellpadding="0" cellspacing="0">
            <tr><td style="border-radius:12px;background:linear-gradient(135deg,{color} 0%,#4F35CC 100%);box-shadow:0 12px 24px -8px {color}8C;">
              <a href="{WebUtility.HtmlEncode(href)}" style="display:inline-block;padding:15px 40px;font-family:-apple-system,'Helvetica Neue',Helvetica,Arial,sans-serif;font-size:15px;font-weight:600;color:#FFFFFF;letter-spacing:-0.1px;text-decoration:none;border-radius:12px;">{WebUtility.HtmlEncode(label)}</a>
            </td></tr>
          </table>
        </td></tr>
        <tr><td style="height:32px;line-height:32px;font-size:32px;">&nbsp;</td></tr>
        """;

    private static string Render(string templateName, IReadOnlyDictionary<string, string> tokens,
        bool encodeValues = true)
    {
        var template = Load(templateName);
        foreach (var (key, value) in tokens)
        {
            var replacement = encodeValues ? WebUtility.HtmlEncode(value) : value;
            template = template.Replace("{{" + key + "}}", replacement);
        }
        return template;
    }

    private static string Load(string name) => Cache.GetOrAdd(name, n =>
    {
        var resourceName = $"Mirage.Infrastructure.Email.Templates.{n}.html";
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Email template '{n}' was not found as an embedded resource.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
}
