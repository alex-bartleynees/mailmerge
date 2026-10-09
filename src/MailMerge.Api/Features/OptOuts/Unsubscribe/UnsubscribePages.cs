using System.Net;

namespace MailMerge.Api.Features.OptOuts.Unsubscribe;

/// <summary>Plain server-rendered pages: recipients may be on any device, and this must work without JavaScript.</summary>
internal static class UnsubscribePages
{
    public static string Confirm(UnsubscribeHandler.Page page) => Layout(page.ShopName,
        page.AlreadyUnsubscribed
            ? $"<h1>You're already unsubscribed</h1><p>{E(page.MaskedEmail)} won't get these emails from {E(page.ShopName)}.</p>"
            : $"""
               <h1>Unsubscribe?</h1>
               <p>Stop emails from {E(page.ShopName)} to {E(page.MaskedEmail)}.</p>
               <form method="post"><button type="submit">Unsubscribe</button></form>
               """);

    public static string Done(UnsubscribeHandler.Page page) => Layout(page.ShopName,
        $"<h1>You're unsubscribed</h1><p>{E(page.MaskedEmail)} won't get these emails from {E(page.ShopName)} again.</p>");

    public static string Invalid(string message) => Layout("Unsubscribe", $"<h1>Link not recognised</h1><p>{E(message)}</p>");

    private static string Layout(string title, string body) => $$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="robots" content="noindex"><title>{{E(title)}}</title>
        <style>
          body{font:16px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif;color:#222;background:#f6f6f4;margin:0}
          main{max-width:28rem;margin:12vh auto;padding:2rem;background:#fff;border-radius:12px;box-shadow:0 1px 4px #0001}
          h1{font-size:1.4rem;margin:0 0 .5rem}
          button{font:inherit;padding:.6rem 1.4rem;border:0;border-radius:8px;background:#222;color:#fff;cursor:pointer}
        </style></head>
        <body><main>{{body}}</main></body></html>
        """;

    private static string E(string s) => WebUtility.HtmlEncode(s);
}
