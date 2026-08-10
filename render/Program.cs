using Microsoft.Extensions.Configuration;
using Mirage.Infrastructure.Email;
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
  ["SocialMedia:Instagram"]="https://www.instagram.com/themiragehub",
  ["SocialMedia:X"]="https://x.com/themiragehub"}).Build();
var html = NewsletterEmailTemplate.Render("Ada","A title","an excerpt.","<p>Body.</p>", [],
  "https://www.themiragehub.com/n/1","https://www.themiragehub.com/u?token=x",
  new NewsletterAuthor("Chiamaka Obi", null), NewsletterEmailTemplate.SocialLinks(config), null,
  NewsletterEmailTemplate.LogoUrl(config));
Console.WriteLine($"td {html.Split("<td").Length-1}/{html.Split("</td>").Length-1} table {html.Split("<table").Length-1}/{html.Split("</table>").Length-1}");
Console.WriteLine($"logo imgs={html.Split("Asset_3Mirage").Length-1}  wordmarks={html.Split(">MIRAGE<").Length-1}  braces={(html.Contains("{{")?"BAD":"ok")}");
