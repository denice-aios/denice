# ALSA pass (no server, defined but not yet run): compares the mixer's drained output (evidence/mix.f32) with what
# DENICE's own ALSA output wrote through the ALSA file plugin (alsa-mix.raw, interleaved float32 stereo). Chromium writes
# 16-bit samples on this path, so a ramp sample is the page's float32 value quantized as Chromium quantizes it (positive
# x 32767, negative x 32768, rounded half to even) and read back as DENICE reads it (/ 32768), compared exactly; the same
# rule as RampAnalysis.Precision.Signed16 and PcmMath.ChromiumFloatToS16.
import array, json, os, struct, sys

N = 19200


def f32(x):
    return struct.unpack('<f', struct.pack('<f', x))[0]


def quantized(i):
    v = f32(0.5 * (i + 1) / N)
    s = round(f32(v * 32767 if v >= 0 else v * 32768))  # float32 product, then round half to even
    return f32(max(-32768, min(32767, s)) / 32768)


RAMP = [quantized(i) for i in range(N)]
INDEX = {}
for i, v in enumerate(RAMP):
    INDEX.setdefault(v, []).append(i)


def load(path):
    data = array.array('f')
    with open(path, 'rb') as f:
        data.frombytes(f.read()[: os.path.getsize(path) // 8 * 8])
    return data[0::2], data[1::2]


def runs(x):
    found, p = [], 0
    while p < len(x):
        best = (p, 0, 0)
        for first in INDEX.get(x[p], []) if x[p] != 0 else []:
            n = 0
            while p + n < len(x) and first + n < N and x[p + n] == RAMP[first + n]:
                n += 1
            if n > best[2]:
                best = (p, first, n)
        if best[2] >= 64:
            found.append(best)
            p += best[2]
        else:
            p += 1
    return found


out = sys.argv[1]
mix = load(os.path.join(out, 'evidence', 'mix.f32'))
device = load(os.path.join(out, 'alsa-mix.raw'))
report = {}
for name, (l, r) in (('mixer dump', mix), ('ALSA file plugin', device)):
    report[name] = {'frames': len(l), 'left runs (first index, length)': [(a, b) for _, a, b in runs(l)],
                    'right runs (first index, length)': [(a, b) for _, a, b in runs(r)]}
print(json.dumps(report, indent=1))
