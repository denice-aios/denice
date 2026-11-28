// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// The page of the Windows and Linux browser-audio redirection proofs (windows-linux-audio-proof.md, "Cases and expected
/// evidence"). Every sound is a deterministic ramp, left channel or right channel only: sample i of a ramp is
/// 0.5 × (i + 1) / 19200, so the first value the mixer receives names the exact source sample. Each case is a function
/// the driver awaits through the DevTools protocol with a user gesture; page speech events are returned with page times.
/// </summary>
internal static class AudioProofPage
{
    /// <summary>The samples per ramp (0.4 s at 48 kHz).</summary>
    internal const int RampSamples = 19200;

    /// <summary>The page.</summary>
    internal const string Html = """
        <!doctype html><html><head><meta charset="utf-8"><title>DENICE audio proof</title></head><body><script>
        const N = 19200, t0 = performance.now(), now = () => Math.round(performance.now() - t0);
        const sleep = ms => new Promise(r => setTimeout(r, ms));
        const within = (p, ms, what) => Promise.race([p, sleep(ms).then(() => { throw new Error(what + ' timed out'); })]);
        function ramp(ctx, channel) {
          const b = ctx.createBuffer(2, N, 48000), d = b.getChannelData(channel);
          for (let i = 0; i < N; i++) d[i] = 0.5 * (i + 1) / N;
          return b;
        }
        function play(ctx, channel, when) {
          const s = ctx.createBufferSource(); s.buffer = ramp(ctx, channel); s.connect(ctx.destination);
          const done = new Promise(r => s.onended = r); s.start(when); return within(done, 15000, 'ramp');
        }
        async function heard(ctx, end) {
          for (let i = 0; i < 500; i++) { const t = ctx.getOutputTimestamp(); if (t.contextTime >= end) return t.contextTime; await sleep(10); }
          return -1;
        }
        function context() { return new AudioContext({ sampleRate: 48000 }); }
        function info(c) { return { state: c.state, rate: c.sampleRate, baseLatency: c.baseLatency, outputLatency: c.outputLatency }; }
        function wav() {
          const bytes = N * 8, b = new ArrayBuffer(44 + bytes), v = new DataView(b);
          const w = (o, s) => { for (let i = 0; i < s.length; i++) v.setUint8(o + i, s.charCodeAt(i)); };
          w(0, 'RIFF'); v.setUint32(4, 36 + bytes, true); w(8, 'WAVE'); w(12, 'fmt '); v.setUint32(16, 16, true);
          v.setUint16(20, 3, true); v.setUint16(22, 2, true); v.setUint32(24, 48000, true); v.setUint32(28, 384000, true);
          v.setUint16(32, 8, true); v.setUint16(34, 32, true); w(36, 'data'); v.setUint32(40, bytes, true);
          for (let i = 0; i < N; i++) { v.setFloat32(44 + i * 8, 0, true); v.setFloat32(48 + i * 8, 0.5 * (i + 1) / N, true); }
          return URL.createObjectURL(new Blob([b], { type: 'audio/wav' }));
        }
        window.cases = {
          async midi() {
            if (!navigator.requestMIDIAccess) return { reachable: false, error: 'navigator.requestMIDIAccess is undefined' };
            try { const m = await within(navigator.requestMIDIAccess(), 10000, 'requestMIDIAccess'); return { reachable: true, inputs: m.inputs.size, outputs: m.outputs.size, outputNames: [...m.outputs.values()].map(o => o.name) }; }
            catch (e) { return { reachable: false, error: e.name + ': ' + e.message }; }
          },
          async cold() { const c = window.c1 = context(); await play(c, 0, c.currentTime + 1.0); return info(c); },
          async silenceSame() { await sleep(3000); await play(c1, 0, c1.currentTime + 0.05); return info(c1); },
          async silenceNew() { await sleep(3000); const c = window.c2 = context(); await play(c, 0, c.currentTime + 0.3); return info(c); },
          async concurrent() {
            const bg = window.c3 = context(), o = bg.createOscillator(), g = bg.createGain(), m = bg.createChannelMerger(2);
            o.frequency.value = 500; g.gain.value = 0.2; o.connect(g); g.connect(m, 0, 0); m.connect(bg.destination);
            const start = bg.currentTime + 0.1; o.start(start); o.stop(start + 3.5); const bgDone = new Promise(r => o.onended = r);
            await sleep(700);
            const c = window.c4 = context(), at = c.currentTime + 0.1; await play(c, 1, at); await heard(c, at + N / 48000 + 0.02);
            const a = window.a1 = new Audio(wav());
            const media = await within(new Promise((res, rej) => { a.onended = () => res({ duration: a.duration }); a.onerror = () => rej(new Error('media error ' + (a.error && a.error.code))); a.play().catch(rej); }), 15000, 'media element');
            await within(bgDone, 15000, 'background tone');
            return { context: info(c), media };
          },
          async devices() {
            let permission = 'granted';
            try { const s = await within(navigator.mediaDevices.getUserMedia({ audio: true }), 10000, 'getUserMedia'); s.getTracks().forEach(t => t.stop()); }
            catch (e) { permission = e.name + ': ' + e.message; }
            const d = await within(navigator.mediaDevices.enumerateDevices(), 10000, 'enumerateDevices');
            return { permission, devices: d.map(x => ({ kind: x.kind, label: x.label, id: x.deviceId.slice(0, 16) })) };
          },
          async explicit(label) {
            const outs = (await navigator.mediaDevices.enumerateDevices()).filter(x => x.kind === 'audiooutput');
            const t = label ? outs.find(x => x.label.includes(label)) : outs.find(x => !['default', 'communications'].includes(x.deviceId));
            if (!t) return { played: false, error: 'no audiooutput labelled ' + label, outputs: outs.map(x => x.label) };
            const c = window.c5 = new AudioContext({ sampleRate: 48000, sinkId: t.deviceId });
            await sleep(500); await play(c, 1, c.currentTime + 0.2);
            return { played: true, label: t.label, sinkId: t.deviceId.slice(0, 16), context: info(c) };
          },
          async speech() {
            const r = { voices: speechSynthesis.getVoices().length, events: [] };
            const say = text => { const u = new SpeechSynthesisUtterance(text);
              for (const n of ['start', 'end', 'boundary', 'pause', 'resume', 'error']) u.addEventListener(n, e => r.events.push({ t: now(), n, u: text.split(' ')[0], ci: e.charIndex, err: e.error }));
              const done = new Promise(res => { u.addEventListener('end', res); u.addEventListener('error', res); }); speechSynthesis.speak(u); return done; };
            try {
              r.startedAt = now(); await within(say('one two three four five'), 20000, 'utterance');
              const second = say('pause alpha bravo charlie delta echo foxtrot golf hotel'); await sleep(900);
              speechSynthesis.pause(); r.pausedAt = now(); await sleep(1200); speechSynthesis.resume(); r.resumedAt = now();
              await within(second, 20000, 'paused utterance');
              say('cancel india juliet kilo lima mike november oscar papa'); await sleep(900); speechSynthesis.cancel(); r.cancelledAt = now(); await sleep(800);
            } catch (e) { r.error = e.message; }
            r.endedAt = now(); return r;
          },
          async navCold() { const c = window.c1 = context(); await play(c, 0, c.currentTime + 1.0); return info(c); },
          async final() { const c = window.c1, start = c.currentTime + 0.2; await play(c, 0, start); return { heardAt: await heard(c, start + N / 48000), end: start + N / 48000 }; },
        };
        </script></body></html>
        """;
}
