#!/usr/bin/env bash
# Copyright (c) Denice.ai. All rights reserved.
# Run the actual install-layout entry; inventory results cannot substitute for application execution.
set -euo pipefail
# Validation uses the same Python standard-library XML pattern as coverage-gate.sh.
# It may run on the build host without opening a display or loading native libraries.
fixture_validation() {
  command -v python3 >/dev/null || { echo 'Fixture validation requires the existing Python 3 tooling.' >&2; return 2; }
  python3 - "$@" <<'PYTHON'
import collections
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import sys
import tarfile
import tempfile
import xml.etree.ElementTree as ET

EXPECTED = {
    'Denice.CrossPlatform.Tests.DesktopGpuTests.HardwareEncoderFinalizesGpuFramesWithOriginalTimestamps',
    'Denice.CrossPlatform.Tests.DesktopGpuTests.HardwareWindowRendersDistinctFramebuffersAndPresents',
    'Denice.CrossPlatform.Tests.DesktopGpuTests.RecordingDestinationCreatesExclusivelyAndPreservesExistingBytes',
}
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def require(ok, message):
    if not ok:
        raise ValueError(message)

def names(actual):
    require(collections.Counter(actual) == collections.Counter(EXPECTED), 'Expected exactly the Linux renderer, recording-destination and hardware encoder cases.')

def discovery(path):
    text = Path(path).read_text()
    require(not re.search(r'Test Run Aborted|TestPlatformException|fatal error|No test matches|^\s*Error\b|exited with error', text, re.I | re.M), 'Discovery reported an error regardless of process exit status.')
    names(line.strip() for line in text.splitlines() if line.strip().startswith('Denice.CrossPlatform.Tests.DesktopGpuTests.'))

def results(directory):
    path = Path(directory) / 'hardware.trx'
    require(path.is_file() and not path.is_symlink(), 'Missing regular hardware.trx.')
    video = Path(directory) / 'hardware-fixtures/linux-hardware.mp4'
    require(video.is_file() and not video.is_symlink() and video.stat().st_size > 0, 'Missing retained Linux hardware encoder MP4; decoding remains separate.')
    report = ET.parse(path).getroot()
    summary = report.find('t:ResultSummary', NS)
    require(summary is not None and summary.get('outcome') == 'Completed', 'Hardware test run did not complete.')
    rows = report.findall('t:Results/t:UnitTestResult', NS)
    names(row.get('testName') for row in rows)
    require(all(row.get('outcome') == 'Passed' for row in rows), 'A required Linux case failed or was skipped.')
    counters = summary.find('t:Counters', NS)
    require(counters is not None and all(int(counters.get(key, '-1')) == len(EXPECTED) for key in ('total', 'executed', 'passed')), 'Linux execution counters are incomplete.')

def inventory(root):
    root = Path(root)
    require(root.is_dir() and not root.is_symlink(), 'Fixture root must be a regular directory.')
    files = {}
    for directory, dirs, leaves in os.walk(root, followlinks=False):
        for name in dirs + leaves:
            path = Path(directory) / name
            mode = path.lstat().st_mode
            require(stat.S_ISDIR(mode) or stat.S_ISREG(mode), 'Fixture contains a link or special file: ' + str(path))
        for name in leaves:
            path = Path(directory) / name
            files[path.relative_to(root).as_posix()] = path
    return files

def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(chunk)
    return result.hexdigest()

def seal(root):
    files = inventory(root)
    entries = [{'path': name, 'sha256': digest(path)} for name, path in sorted(files.items()) if name != 'payload-sha256.json']
    (Path(root) / 'payload-sha256.json').write_text(json.dumps(entries, indent=2) + '\n')

def verify(root, commit):
    require(re.fullmatch('[0-9a-fA-F]{40}', commit) is not None, 'Expected a full source commit.')
    files = inventory(root)
    require('payload-sha256.json' in files, 'Missing fixture hash manifest.')
    entries = json.loads(files['payload-sha256.json'].read_text())
    require(isinstance(entries, list) and entries, 'Fixture manifest must be a nonempty array.')
    listed = set()
    for entry in entries:
        require(isinstance(entry, dict), 'Invalid fixture manifest entry.')
        name, sha = entry.get('path'), entry.get('sha256')
        require(isinstance(name, str) and name and not name.startswith('/') and '\\' not in name and ':' not in name and all(part not in ('', '.', '..') for part in name.split('/')), 'Invalid relative fixture path.')
        require(name != 'payload-sha256.json' and name not in listed, 'Duplicate/self-referencing fixture path.')
        require(isinstance(sha, str) and re.fullmatch('[0-9a-fA-F]{64}', sha) is not None, 'Invalid fixture SHA256.')
        require(name in files and digest(files[name]) == sha.lower(), 'Fixture file absent or hash differs: ' + name)
        listed.add(name)
    require(listed == set(files) - {'payload-sha256.json'}, 'Fixture hash manifest must cover the exact file set.')
    source = files['fixture-source.txt'].read_text().splitlines()
    require([line for line in source if line.startswith('fixture_commit=')] == ['fixture_commit=' + commit], 'Fixture source commit mismatch.')
    require([line for line in source if line.startswith('media_commit=')] == ['media_commit=' + commit], 'Native media source commit mismatch.')
    require('rid=linux-x64' in source, 'Fixture architecture is not linux-x64.')
    version = json.loads(files['global.json'].read_text())['sdk']['version']
    require(isinstance(version, str) and re.fullmatch(r'\d+\.\d+\.\d+', version) is not None, 'Invalid SDK pin.')
    required = {'fixture-source.txt', 'global.json', 'dotnet/dotnet', 'dotnet/sdk/' + version + '/vstest.console.dll',
        'tests/Denice.CrossPlatform.Tests.dll', 'tests/Denice.CrossPlatform.Tests.runtimeconfig.json', 'tests/testhost.dll',
        'tests/xunit.runner.visualstudio.testadapter.dll', 'tests/libSkiaSharp.so', 'tests/libhostfxr.so',
        'tests/libhostpolicy.so', 'tests/libcoreclr.so', 'tests/media/build-manifest.json'}
    require(required <= listed, 'Required SDK, test adapter, runtime, or media files are missing.')
    require(os.access(files['dotnet/dotnet'], os.X_OK), 'Staged dotnet lost executable permissions.')

def extract(archive, destination):
    destination = Path(destination)
    require(not destination.exists(), 'Fixture extraction destination must be new.')
    with tarfile.open(archive, 'r:gz') as package:
        members, paths = package.getmembers(), set()
        for member in members:
            path = PurePosixPath(member.name)
            require(not path.is_absolute() and '..' not in path.parts and '\\' not in member.name, 'Archive path escapes payload.')
            require(member.isdir() or member.isfile(), 'Archive links and special files are rejected.')
            if str(path) == '.':
                require(member.isdir(), 'Archive root must be a directory.')
                continue
            require(str(path) not in paths, 'Duplicate archive path.')
            paths.add(str(path))
        destination.mkdir(parents=True)
        for member in members:
            target = destination / member.name
            if member.isdir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with package.extractfile(member) as source, target.open('xb') as output:
                    shutil.copyfileobj(source, output)
                target.chmod(member.mode & 0o777)

def self_test():
    with tempfile.TemporaryDirectory(prefix='denice-linux-fixtures-') as temp:
        root = Path(temp)
        report = root / 'hardware.trx'
        video = root / 'hardware-fixtures/linux-hardware.mp4'
        video.parent.mkdir()
        video.write_bytes(b'synthetic fixture, not video evidence')
        cases = ''.join('<UnitTestResult testName="' + name + '" outcome="Passed" />' for name in sorted(EXPECTED))
        valid = '<TestRun xmlns="' + NS['t'] + '"><Results>' + cases + '</Results><ResultSummary outcome="Completed"><Counters total="3" executed="3" passed="3" /></ResultSummary></TestRun>'
        report.write_text(valid)
        results(root)
        def rejected(action):
            try:
                action()
            except (ValueError, KeyError, ET.ParseError, FileNotFoundError):
                return
            raise AssertionError('Invalid fixture evidence was accepted.')
        video.unlink()
        rejected(lambda: results(root))
        video.write_bytes(b'synthetic fixture, not video evidence')
        for invalid in ('broken XML', valid.replace('outcome="Passed"', 'outcome="NotExecuted"'), valid.replace('executed="3"', 'executed="0"'), valid.replace(sorted(EXPECTED)[0], 'UnknownTest'), valid.replace(sorted(EXPECTED)[0], sorted(EXPECTED)[1]), valid.replace(cases, '')):
            report.write_text(invalid)
            rejected(lambda: results(root))
        log = root / 'discovery.txt'
        log.write_text('\n'.join('    ' + name for name in sorted(EXPECTED)))
        discovery(log)
        log.write_text(log.read_text() + '\nTest Run Aborted')
        rejected(lambda: discovery(log))
        commit = 'a' * 40
        payload = root / 'payload'
        payload.mkdir()
        files = ['dotnet/dotnet', 'dotnet/sdk/10.0.400/vstest.console.dll', 'tests/Denice.CrossPlatform.Tests.dll',
            'tests/Denice.CrossPlatform.Tests.runtimeconfig.json', 'tests/testhost.dll', 'tests/xunit.runner.visualstudio.testadapter.dll',
            'tests/libSkiaSharp.so', 'tests/libhostfxr.so', 'tests/libhostpolicy.so', 'tests/libcoreclr.so', 'tests/media/build-manifest.json']
        for name in files:
            path = payload / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('fixture')
        (payload / 'dotnet/dotnet').chmod(0o755)
        (payload / 'fixture-source.txt').write_text('fixture_commit=' + commit + '\nmedia_commit=' + commit + '\nrid=linux-x64\n')
        (payload / 'global.json').write_text('{"sdk":{"version":"10.0.400"}}')
        seal(payload)
        verify(payload, commit)
        rejected(lambda: verify(payload, 'b' * 40))
        original = (payload / 'fixture-source.txt').read_text()
        (payload / 'fixture-source.txt').write_text(original.replace('media_commit=' + commit, 'media_commit=' + 'b' * 40))
        seal(payload)
        rejected(lambda: verify(payload, commit))
        (payload / 'fixture-source.txt').write_text(original)
        seal(payload)
        (payload / 'tests/testhost.dll').write_text('changed bytes')
        rejected(lambda: verify(payload, commit))
        (payload / 'tests/testhost.dll').unlink()
        rejected(lambda: verify(payload, commit))
        (payload / 'tests/testhost.dll').write_text('fixture')
        extra = payload / 'unlisted'
        extra.write_text('extra')
        rejected(lambda: verify(payload, commit))
        extra.unlink()
        extra.symlink_to(root)
        rejected(lambda: verify(payload, commit))
        extra.unlink()
        manifest = payload / 'payload-sha256.json'
        entries = json.loads(manifest.read_text())
        manifest.write_text(json.dumps(entries + entries[:1]))
        rejected(lambda: verify(payload, commit))
        manifest.write_text('[]')
        rejected(lambda: verify(payload, commit))
        archive = root / 'bad.tar.gz'
        with tarfile.open(archive, 'w:gz') as package:
            item = tarfile.TarInfo('../outside')
            item.size = 1
            package.addfile(item, io.BytesIO(b'x'))
        rejected(lambda: extract(archive, root / 'out'))
        with tarfile.open(archive, 'w:gz') as package:
            item = tarfile.TarInfo('linked')
            item.type, item.linkname = tarfile.SYMTYPE, '/tmp'
            package.addfile(item)
        rejected(lambda: extract(archive, root / 'linked-out'))
        seal(payload)
        with tarfile.open(archive, 'w:gz') as package:
            package.add(payload, arcname='.')
        extract(archive, root / 'extracted')
        verify(root / 'extracted', commit)
    print('Linux fixture validation checks passed; synthetic data does not exercise a GPU or encoder.')

mode, args = sys.argv[1], sys.argv[2:]
try:
    {'--fixture-seal': seal, '--fixture-verify': verify, '--fixture-extract': extract,
     '--fixture-discovery': discovery, '--fixture-results': results, '--fixture-self-test': self_test}[mode](*args)
except (ValueError, KeyError, OSError, ET.ParseError, TypeError) as error:
    sys.exit('Linux fixture validation failed: ' + str(error))
PYTHON
}
case "${1:-}" in
  --fixture-*) fixture_validation "$@"; exit $? ;;
esac
if [[ $# -lt 2 || $# -gt 3 || ( $# -eq 3 && $3 != --hardware-fixtures ) ]]; then
  echo 'usage: run-linux-desktop-smoke.sh PAYLOAD_DIRECTORY OUTPUT_DIRECTORY [--hardware-fixtures]' >&2
  exit 2
fi
if [[ $(uname -s) != Linux ]]; then echo 'Linux is required.' >&2; exit 2; fi
payload=$1
output=$2
mkdir -p "$output"
output=$(cd "$output" && pwd)
if [[ ${3:-} == --hardware-fixtures ]]; then
  [[ $(uname -m) == x86_64 ]] || { echo 'Isolated Linux fixture execution requires x64; ARM64 builds are separate.' >&2; exit 2; }
  payload=$(cd "$payload" && pwd)
  [[ ! -e "$output/hardware.trx" ]] || { echo 'Fixture output must be fresh.' >&2; exit 2; }
  command -v timeout >/dev/null
  cp "$payload/fixture-source.txt" "$payload/payload-sha256.json" "$output/"
  preserve_fixture_output() {
    status=$?
    trap - EXIT
    printf 'exit_code=%s\n' "$status" > "$output/hardware-fixture-exit.txt"
    if [[ -d "$payload/tests/hardware-fixture-output" ]]; then
      mkdir -p "$output/hardware-fixtures"
      cp -R "$payload/tests/hardware-fixture-output/." "$output/hardware-fixtures/" || status=1
    fi
    exit "$status"
  }
  trap preserve_fixture_output EXIT
  printf '%s\n' 'Scope: X11/EGL/Skia renderer/presentation, exclusive recording destination and hardware encoder EOS/artifact creation.' \
    'CEF DMA-BUF import, displayed scanout and audio: NOT_TESTED. Independent decoded MP4 colors/timestamps: PENDING.' > "$output/linux-fixture-scope.txt"
  (cd "$payload/tests" && timeout --kill-after=5s 300s env GST_PLUGIN_SYSTEM_PATH_1_0= GST_PLUGIN_PATH_1_0="$payload/tests/media/lib/gstreamer-1.0" "$payload/dotnet/dotnet" vstest Denice.CrossPlatform.Tests.dll \
    --TestCaseFilter:Category=GpuHardware --TestAdapterPath:. "--ResultsDirectory:$output" \
    '--Logger:trx;LogFileName=hardware.trx') > "$output/hardware-fixtures.stdout.txt" 2> "$output/hardware-fixtures.stderr.txt"
  mkdir -p "$output/hardware-fixtures"
  cp -R "$payload/tests/hardware-fixture-output/." "$output/hardware-fixtures/"
  fixture_validation --fixture-results "$output"
  echo 'Isolated linux-x64 graphics, destination and encoder finalization fixtures passed. MP4 decode remains pending; CEF import and audio remain untested.'
  exit 0
fi
video="$output/denice-orb.mp4"
if [[ -e "$video" ]]; then echo 'The recording destination already exists.' >&2; exit 2; fi
shopt -s nullglob
entries=("$payload"/versions/*/denice-v*-linux-x64)
if [[ ${#entries[@]} -ne 1 || ! -x ${entries[0]} ]]; then echo 'Expected exactly one executable linux-x64 versioned entry.' >&2; exit 2; fi
entry=${entries[0]}
if [[ ! -f $(dirname "$entry")/Denice.Runtime.dll ]]; then echo 'Packaged Runtime is missing beside Rust entry.' >&2; exit 2; fi
command -v timeout >/dev/null
timeout --kill-after=5s 120s "$entry" gui --duration 2.5 --record "$video" > "$output/application.stdout.txt" 2> "$output/application.stderr.txt"
if [[ ! -s "$video" ]]; then echo 'DENICE did not produce a nonempty MP4.' >&2; exit 1; fi
echo 'DENICE exited zero and produced an MP4. Playback, GPU use, native-buffer import, frame rate and audio synchronization still require validation.'
