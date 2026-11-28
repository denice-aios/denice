# Prints the measured facts of one L1 pass from its run directory (evidence/report.json, calls.log, compare, logs).
import json, os, re, subprocess, sys

run = sys.argv[1]
ev = os.path.join(run, 'evidence')
r = json.load(open(os.path.join(ev, 'report.json')))
print('test-exit:', open(os.path.join(run, 'test-exit.txt')).read().strip())
for c in r['cases']:
    print(f"  case {c['case']}: channel {c['channel']} firstIndex {c['firstIndex']} contiguous {c['contiguous']} accept {c['accept']}")
m = r['mixer']
print('mixer failure:', m['failure'], '| cycles', m['cycles'], '| drained frames', m['drainedFrames'], '| sink stopped', m['sinkStopped'])
print('cpu paints:', r['cpuPaints'], '| page clock', r['pageClock'])
facts = {k: v for k, v in r['facts'].items() if k != 'alsaSequences'}
print('facts:', facts)
print('counters:', r['counters'].replace('\n', '; '))
print('speech in mix (page ms):', [(round(s['startPageMs']), round(s['endPageMs'])) for s in r['speechInMix']])
pr = r['pageResults']
print('midi:', pr.get('midi'))
print('devices:', pr.get('devices'))
print('explicit:', pr.get('explicit'))
print('speech:', pr.get('speech'))
print('final:', pr.get('final'))
calls = open(os.path.join(ev, 'calls.log')).read().splitlines()
for line in calls:
    if re.search(r'server rate|redirected pa_stream_connect|UNREDIRECTED|connect_record|disconnect 0x|stale|unloaded|loaded speech', line):
        print('call:', line[:260])
first = next((l.split()[2] for l in calls if l.startswith('redirected pa_stream_connect_playback')), None)
if first:
    print('sequence of first stream:', ' | '.join(l[:90] for l in calls if first in l))
if os.path.exists(os.path.join(run, 'out-sink.f32')):
    out = subprocess.run([sys.executable, os.path.join(os.path.dirname(__file__), 'compare.py'), run], capture_output=True, text=True).stdout
    c = json.loads(out)
    print('sink monitor runs L:', c['default sink monitor']['left runs (first index, length)'])
    print('sink monitor runs R:', c['default sink monitor']['right runs (first index, length)'])
    print('mixer ramps == sink ramps:', c['every mixer ramp reached the default sink unchanged'], '| explicit sink peak:', c['explicit sink peak'])
si = os.path.join(run, 'sink-inputs.log')
if os.path.exists(si):
    t = open(si).read()
    print('default sinks seen:', sorted(set(re.findall(r'Default Sink: (\S+)', t))))
    print('sink-input applications:', sorted(set(re.findall(r'application.name = "([^"]+)"', t))))
    print('sink-input sinks:', sorted(set(re.findall(r'\tSink: (\d+)', t))))
    print('sinks:', sorted(set(re.findall(r'^(\d+)\t(\S+)\t', t, re.M))))
