# Compares the mixer's drained output (evidence/mix.f32) with what DENICE's output stream delivered to the default null
# sink (out-sink.f32, recorded by parec from its monitor), and checks that the explicit-device null sink stayed silent.
# Same ramp rule as RampAnalysis.cs: sample i of a ramp is 0.5 * (i + 1) / 19200.
import array, json, os, sys

N, STEP, TOL = 19200, 0.5 / 19200, 2e-6


def load(path):
    data = array.array('f')
    with open(path, 'rb') as f:
        data.frombytes(f.read()[: os.path.getsize(path) // 8 * 8])
    return data[0::2], data[1::2]


def runs(x):
    found, p = [], 0
    while p < len(x) - 1:
        v = x[p]
        idx = v / STEP - 1
        if v > 0 and abs(idx - round(idx)) < 0.05 and abs(x[p + 1] - v - STEP) < TOL:
            first, n = int(round(idx)), 1
            while p + n < len(x) and first + n < N and abs(x[p + n] - STEP * (first + n + 1)) < TOL:
                n += 1
            if n >= 64:
                found.append((p, first, n))
            p += n
        else:
            p += 1
    return found


out = sys.argv[1]
mix = load(os.path.join(out, 'evidence', 'mix.f32'))
sink = load(os.path.join(out, 'out-sink.f32'))
explicit = load(os.path.join(out, 'explicit-sink.f32'))
report = {}
for name, (l, r) in (('mixer dump', mix), ('default sink monitor', sink)):
    report[name] = {'frames': len(l), 'left runs (first index, length)': [(a, b) for _, a, b in runs(l)],
                    'right runs (first index, length)': [(a, b) for _, a, b in runs(r)]}
mixRuns, sinkRuns = runs(mix[0]) + runs(mix[1]), runs(sink[0]) + runs(sink[1])
report['every mixer ramp reached the default sink unchanged'] = sorted((a, b) for _, a, b in mixRuns) == sorted((a, b) for _, a, b in sinkRuns)
report['explicit sink frames'] = len(explicit[0])
report['explicit sink peak'] = max([abs(v) for v in explicit[0]] + [abs(v) for v in explicit[1]] + [0.0])
print(json.dumps(report, indent=1))
