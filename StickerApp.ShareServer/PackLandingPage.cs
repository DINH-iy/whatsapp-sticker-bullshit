using System.Text.Encodings.Web;

namespace StickerApp.ShareServer;

public static class PackLandingPage
{
    public static string Render(PackRepository.PackDetails pack)
    {
        var name = HtmlEncoder.Default.Encode(pack.Name);
        var size = pack.Bytes >= 1024 * 1024
            ? $"{(pack.Bytes / 1024d / 1024d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} MB"
            : $"{Math.Max(1, Math.Ceiling(pack.Bytes / 1024d))} KB";
        var imageLabel = pack.ImageCount == 1 ? "image" : "images";
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width,initial-scale=1">
                <title>{{name}} · Shared sticker pack</title>
                <style>
                    :root { color-scheme: dark; font-family: system-ui, sans-serif; }
                    body { margin: 0; background: #101114; color: #e7e9ee; padding: 48px 20px; }
                    main { max-width: 560px; margin: auto; padding: 28px; background: #191b20; border: 1px solid #383c46; border-radius: 14px; }
                    h1 { font-size: 28px; overflow-wrap: anywhere; }
                    p { line-height: 1.7; color: #a8aebc; }
                    a { color: #cfabff; }
                    .download { display: inline-flex; align-items: center; min-height: 44px; padding: 0 18px; border-radius: 8px; background: #70449e; color: white; text-decoration: none; font-weight: 600; }
                    code { overflow-wrap: anywhere; font-size: 12px; }
                    summary { cursor: pointer; margin-top: 24px; }
                </style>
            </head>
            <body>
                <main>
                    <p>Shared sticker pack</p>
                    <h1>{{name}}</h1>
                    <p>{{pack.ImageCount}} {{imageLabel}} · {{(pack.Animated ? "Animated" : "Static")}} · {{size}}</p>
                    <a class="download" href="/p/{{pack.Sha256}}/download">Download pack</a>
                    <p>Save the file, then open Stickers and choose <strong>Import pack</strong>. It will appear as a new pack.</p>
                    <p>This file is for the Stickers app. It is not a direct WhatsApp install link.</p>
                    <details>
                        <summary>SHA-256 fingerprint</summary>
                        <p><code>{{pack.Sha256}}</code></p>
                        <p>A fingerprint can detect a changed file. It does not verify who created the pack.</p>
                    </details>
                </main>
            </body>
            </html>
            """;
    }
}
