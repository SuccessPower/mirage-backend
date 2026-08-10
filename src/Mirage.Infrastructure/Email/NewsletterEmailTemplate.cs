using System.Net;
using System.Text;

namespace Mirage.Infrastructure.Email;

/// <summary>
/// The Mirage Journal email. Deliberately table-based with inline styles: Outlook ignores flexbox, grid, and most
/// modern CSS, so the layout is built from nested tables and the modern touches (rounded corners, embedded prose
/// styling, the two-column mosaic collapse) are layered on as progressive enhancement.
/// </summary>
public static class NewsletterEmailTemplate
{
    private const string Ink = "#1b1424";
    private const string Plum = "#6c4bd1";
    private const string Muted = "#6f6880";
    private const string Paper = "#faf7ff";
    private const string Rule = "#e7e0f4";

    public static string Render(string displayName, string title, string excerpt, string contentHtml,
        IReadOnlyList<string> imageUrls, string newsletterUrl, string unsubscribeUrl)
    {
        var images = imageUrls.Where(IsHttpsUrl).Take(10).ToList();
        var hero = images.FirstOrDefault();
        var gallery = images.Skip(1).ToList();

        var body = new StringBuilder();
        body.Append($"""
          <tr><td style="padding:0 0 8px">
            <div style="font:700 11px/1 Helvetica,Arial,sans-serif;letter-spacing:.32em;text-transform:uppercase;color:{Plum}">The Mirage Journal</div>
          </td></tr>
          <tr><td style="padding:14px 0 0">
            <h1 style="margin:0;font:400 40px/1.1 Georgia,'Times New Roman',serif;color:{Ink};letter-spacing:-.4px">{Encode(title)}</h1>
          </td></tr>
          <tr><td style="padding:20px 0 0">
            <div style="width:56px;height:3px;background:{Plum};border-radius:3px;font-size:0;line-height:0">&nbsp;</div>
          </td></tr>
          <tr><td style="padding:22px 0 0;font:400 17px/1.7 Georgia,'Times New Roman',serif;color:#3c3450">
            <span style="color:{Ink};font-weight:700">Hello {Encode(displayName)},</span> {Encode(excerpt)}
          </td></tr>
        """);

        if (hero is not null)
            body.Append($"""
              <tr><td style="padding:30px 0 0">
                <img src="{Encode(hero)}" width="552" alt="" style="display:block;width:100%;max-width:552px;height:auto;border:0;border-radius:18px;outline:none;text-decoration:none" />
              </td></tr>
            """);

        body.Append($"""
          <tr><td class="prose" style="padding:30px 0 0;font:400 16px/1.85 Georgia,'Times New Roman',serif;color:#332c45">{contentHtml}</td></tr>
        """);

        if (gallery.Count > 0) body.Append(Mosaic(gallery));

        body.Append($"""
          <tr><td align="center" style="padding:38px 0 6px">{Button(newsletterUrl, "Read it on Mirage")}</td></tr>
          <tr><td align="center" style="padding:10px 0 0;font:400 13px/1.6 Helvetica,Arial,sans-serif;color:{Muted}">
            Like it, and leave a comment for the community.
          </td></tr>
        """);

        return Shell(excerpt, body.ToString(), unsubscribeUrl);
    }

    public static string PlatformManagerInvite(string inviteUrl) => Shell(
        "You have been invited to create and schedule editions of the Mirage Journal.",
        $"""
          <tr><td style="padding:0 0 8px">
            <div style="font:700 11px/1 Helvetica,Arial,sans-serif;letter-spacing:.32em;text-transform:uppercase;color:{Plum}">An invitation</div>
          </td></tr>
          <tr><td style="padding:14px 0 0">
            <h1 style="margin:0;font:400 38px/1.12 Georgia,'Times New Roman',serif;color:{Ink};letter-spacing:-.4px">Shape the stories<br />we send.</h1>
          </td></tr>
          <tr><td style="padding:20px 0 0">
            <div style="width:56px;height:3px;background:{Plum};border-radius:3px;font-size:0;line-height:0">&nbsp;</div>
          </td></tr>
          <tr><td style="padding:22px 0 0;font:400 17px/1.75 Georgia,'Times New Roman',serif;color:#3c3450">
            A Mirage platform administrator has invited you to join the editorial team as a <b>Platform Manager</b>.
            You will be able to compose editions with photography, schedule them to the minute, and watch how the
            community responds.
          </td></tr>
          <tr><td style="padding:26px 0 0">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{Paper};border:1px solid {Rule};border-radius:16px">
              <tr><td style="padding:22px 24px;font:400 15px/1.7 Helvetica,Arial,sans-serif;color:#463d5c">
                <b style="color:{Ink}">What you get</b><br />
                · A rich composer with photographs and blog-style formatting<br />
                · Scheduling that sends at the exact date and time you choose<br />
                · Delivery reports and engagement dashboards
              </td></tr>
            </table>
          </td></tr>
          <tr><td align="center" style="padding:34px 0 6px">{Button(inviteUrl, "Accept the invitation")}</td></tr>
          <tr><td align="center" style="padding:12px 0 0;font:400 13px/1.6 Helvetica,Arial,sans-serif;color:{Muted}">
            This invitation expires in seven days. Sign in with this email address to accept it.
          </td></tr>
        """);

    // Two-up photo mosaic that stacks to one column on narrow screens via the .col class in the head styles.
    private static string Mosaic(IReadOnlyList<string> images)
    {
        var mosaic = new StringBuilder("""<tr><td style="padding:26px 0 0"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>""");
        for (var index = 0; index < images.Count; index++)
        {
            if (index > 0 && index % 2 == 0) mosaic.Append("""</tr><tr><td colspan="2" height="12" style="font-size:0;line-height:0">&nbsp;</td></tr><tr>""");
            var pad = index % 2 == 0 && index + 1 < images.Count ? "padding-right:6px" : "padding-left:6px";
            if (images.Count == 1) pad = "padding:0";
            mosaic.Append($"""<td class="col" width="50%" valign="top" style="{pad}"><img src="{Encode(images[index])}" width="270" alt="" style="display:block;width:100%;height:auto;border:0;border-radius:14px;outline:none;text-decoration:none" /></td>""");
        }
        if (images.Count % 2 == 1) mosaic.Append("""<td class="col" width="50%">&nbsp;</td>""");
        return mosaic.Append("</tr></table></td></tr>").ToString();
    }

    // Bulletproof button: VML fills the shape for Outlook, everyone else gets the padded anchor.
    private static string Button(string url, string label) => $"""
      <!--[if mso]><v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{Encode(url)}" style="height:48px;v-text-anchor:middle;width:250px" arcsize="50%" stroke="f" fillcolor="{Plum}"><w:anchorlock/><center style="color:#ffffff;font-family:Helvetica,Arial,sans-serif;font-size:15px;font-weight:bold">{Encode(label)}</center></v:roundrect><![endif]-->
      <!--[if !mso]><!--><a href="{Encode(url)}" style="display:inline-block;background:{Plum};color:#ffffff;text-decoration:none;font:700 15px/1 Helvetica,Arial,sans-serif;padding:17px 34px;border-radius:999px">{Encode(label)}</a><!--<![endif]-->
    """;

    // Kept token-substituted rather than interpolated: CSS braces and raw-string interpolation do not mix cleanly.
    private static readonly string HeadStyles = """
          body,table,td,a{-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%}
          img{-ms-interpolation-mode:bicubic}
          .prose p{margin:0 0 18px}
          .prose h2{font:400 27px/1.25 Georgia,'Times New Roman',serif;color:@ink@;margin:34px 0 14px;letter-spacing:-.2px}
          .prose h3{font:700 19px/1.35 Helvetica,Arial,sans-serif;color:@ink@;margin:28px 0 10px}
          .prose a{color:@plum@}
          .prose img{max-width:100%;height:auto;border-radius:14px;margin:10px 0}
          .prose ul,.prose ol{margin:0 0 18px;padding-left:22px}
          .prose li{margin:0 0 8px}
          .prose blockquote{margin:26px 0;padding:4px 0 4px 22px;border-left:3px solid @plum@;font:italic 400 19px/1.6 Georgia,serif;color:#463c60}
          .prose hr{border:0;border-top:1px solid @rule@;margin:30px 0}
          @media only screen and (max-width:620px){
            .frame{width:100% !important;border-radius:0 !important}
            .pad{padding:32px 24px !important}
            .col{display:block !important;width:100% !important;padding:0 0 12px !important}
          }
        """.Replace("@ink@", Ink).Replace("@plum@", Plum).Replace("@rule@", Rule);

    private static string Shell(string preheader, string bodyRows, string? unsubscribeUrl = null)
    {
        var footerNote = unsubscribeUrl is null ? string.Empty : $"""
          <div style="padding-top:12px">You are receiving this because you subscribed to the Mirage Journal.<br />
            <a href="{Encode(unsubscribeUrl)}" style="color:{Plum};text-decoration:underline">Unsubscribe</a> ·
            <a href="{Encode(unsubscribeUrl)}" style="color:{Muted};text-decoration:underline">Manage preferences</a>
          </div>
        """;
        return $"""
      <!doctype html>
      <html lang="en" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
      <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width,initial-scale=1" />
        <meta name="x-apple-disable-message-reformatting" />
        <meta name="color-scheme" content="light" />
        <meta name="supported-color-schemes" content="light" />
        <!--[if mso]><xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml><![endif]-->
        <style>
        {HeadStyles}
        </style>
      </head>
      <body style="margin:0;padding:0;background:#efeaf8">
        <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;height:0;width:0">{Encode(preheader)}</div>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#efeaf8">
          <tr><td align="center" style="padding:34px 12px">
            <table role="presentation" class="frame" width="640" cellpadding="0" cellspacing="0" border="0" style="width:640px;max-width:640px;background:#ffffff;border-radius:24px;overflow:hidden;box-shadow:0 20px 60px rgba(41,26,74,.14)">
              <tr><td style="background:{Ink};padding:22px 44px">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
                  <td style="font:700 17px/1 Georgia,serif;color:#ffffff;letter-spacing:.22em">MIRAGE</td>
                  <td align="right" style="font:700 10px/1 Helvetica,Arial,sans-serif;letter-spacing:.26em;text-transform:uppercase;color:#b9a4f5">Faith · Love · Becoming</td>
                </tr></table>
              </td></tr>
              <tr><td class="pad" style="padding:44px 44px 48px">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">{bodyRows}</table>
              </td></tr>
              <tr><td style="background:{Paper};border-top:1px solid {Rule};padding:28px 44px;font:400 12px/1.7 Helvetica,Arial,sans-serif;color:{Muted}" align="center">
                <div style="font:700 12px/1 Georgia,serif;color:{Ink};letter-spacing:.2em">MIRAGE</div>
                <div style="padding-top:10px">A faith-integrated home for relationships worth building.</div>
                {footerNote}
              </td></tr>
            </table>
          </td></tr>
        </table>
      </body>
      </html>
    """;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static bool IsHttpsUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
