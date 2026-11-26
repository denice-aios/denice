// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Engine;

/// <summary>The bundled desktop page. Its transparent background reveals the native GPU scene.</summary>
internal static class ApplicationPage
{
    /// <summary>The complete bundled HTML document.</summary>
    internal const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>DENICE</title>
          <style>
            :root { color-scheme: dark; font-family: -apple-system, BlinkMacSystemFont, sans-serif; }
            html, body { margin: 0; width: 100%; height: 100%; background: transparent; overflow: hidden; }
            body { color: #e7edf8; }
            header { position: absolute; top: 34px; left: 40px; right: 40px; display: flex; justify-content: space-between; align-items: center; }
            .brand { font-size: 18px; font-weight: 650; letter-spacing: .24em; }
            .status { color: #98b3cb; font-size: 12px; letter-spacing: .08em; }
            .status::before { content: ''; display: inline-block; width: 6px; height: 6px; border-radius: 50%; background: #7adaca; margin-right: 9px; }
            denice { display: block; position: absolute; inset: 90px 0 150px; pointer-events: none; }
            footer { position: absolute; bottom: 32px; left: 40px; right: 40px; display: flex; align-items: end; justify-content: space-between; gap: 20px; }
            h1 { margin: 0 0 9px; font-weight: 400; font-size: 28px; letter-spacing: -.04em; }
            p { margin: 0; font-size: 13px; color: #9daec5; }
            button { border: 1px solid #ffffff27; background: #131e32df; border-radius: 12px; padding: 12px 18px; color: #c8d8ec; font: inherit; cursor: pointer; }
            button:hover { background: #283d58; }
            #capabilities { position: absolute; left: 40px; top: 82px; font-size: 11px; color: #70869c; }
          </style>
        </head>
        <body>
          <header><span class="brand">DENICE</span><span class="status">HERE WITH YOU</span></header>
          <div id="capabilities"></div>
          <denice mode="background" aria-label="Breathing orb rendered by the native graphics service"></denice>
          <footer><div><h1>A moment to breathe.</h1><p>Your space. Your pace.</p></div><button id="sound" type="button">Play a gentle cue</button></footer>
          <script>
            const probe = document.createElement('canvas');
            const gl = probe.getContext('webgl2') || probe.getContext('webgl');
            const audioAvailable = !!window.AudioContext;
            document.querySelector('#capabilities').textContent = `WebGL ${gl ? 'available' : 'unavailable'} · Web Audio ${audioAvailable ? 'available' : 'unavailable'}`;
            if (gl) gl.getExtension('WEBGL_lose_context')?.loseContext();
            let audio;
            document.querySelector('#sound').addEventListener('click', async () => {
              audio ||= new AudioContext();
              await audio.resume();
              const tone = audio.createOscillator();
              const gain = audio.createGain();
              tone.frequency.value = 440;
              gain.gain.setValueAtTime(0, audio.currentTime);
              gain.gain.linearRampToValueAtTime(0.06, audio.currentTime + 0.04);
              gain.gain.exponentialRampToValueAtTime(0.001, audio.currentTime + 0.5);
              tone.connect(gain).connect(audio.destination);
              tone.start(); tone.stop(audio.currentTime + 0.55);
              document.querySelector('#sound').textContent = 'Cue played';
            });
          </script>
        </body>
        </html>
        """;
}
