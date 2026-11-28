// Copyright (c) Denice.ai. All rights reserved.

using System.Text;

namespace Denice.Engine;

/// <summary>
/// The bundled startup and status page, served as a data URL so it loads without the local server. The status text
/// arrives in the URL fragment and is read by the page itself; changing only the fragment updates it in place.
/// </summary>
internal static class StartupPage
{
    /// <summary>The complete bundled HTML document; it shows the decoded fragment as text, never as markup.</summary>
    internal const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <title>DENICE</title>
          <style>
            :root { color-scheme: dark; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; }
            html, body { margin: 0; width: 100%; height: 100%; background: transparent; overflow: hidden; color: #e7edf8; }
            main { position: absolute; left: 40px; right: 40px; bottom: 40px; }
            .brand { font-size: 18px; font-weight: 650; letter-spacing: .24em; margin-bottom: 12px; }
            #status { font-size: 13px; color: #9daec5; }
          </style>
        </head>
        <body>
          <main><div class="brand">DENICE</div><div id="status">DENICE is starting.</div></main>
          <script>
            function show() {
              var text = location.hash.length > 1 ? decodeURIComponent(location.hash.substring(1)) : 'DENICE is starting.';
              document.getElementById('status').textContent = text;
            }
            window.addEventListener('hashchange', show);
            show();
          </script>
        </body>
        </html>
        """;

    /// <summary>Gets the page as a base64 data URL.</summary>
    internal static Uri Address { get; } = new("data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Html)));
}
