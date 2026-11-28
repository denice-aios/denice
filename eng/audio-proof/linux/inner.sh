#!/bin/bash
# Runs inside the proof container as the unprivileged user: starts Xvfb, a session bus and the selected audio server
# (pipewire | pulseaudio | none), creates the null sinks, records their monitors, runs the proof test and compares.
# Every sink is a null sink, so no audio reaches any device.
set -u
SERVER=${DENICE_AUDIO_SERVER:-pipewire}
OUT=/out
RID=linux-x64; [ "$(uname -m)" = aarch64 ] && RID=linux-arm64
export PATH=/dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export XDG_RUNTIME_DIR=/tmp/xdg-$(id -u) HOME=/tmp/home DOTNET_CLI_HOME=/tmp/home NUGET_PACKAGES=/nuget/
mkdir -p -m 700 "$XDG_RUNTIME_DIR" "$HOME"
Xvfb :99 -screen 0 1280x800x24 -nolisten tcp >"$OUT/xvfb.log" 2>&1 &
export DISPLAY=:99
eval "$(dbus-launch --sh-syntax)"
SINK="format=float32le rate=48000 channels=2"
case "$SERVER" in
  pipewire)
    mkdir -p "$HOME/.config/wireplumber/bluetooth.lua.d"
    # No bluetooth in the container: its monitor needs logind, whose failure ends WirePlumber's start-up.
    printf 'bluez_monitor.enable = function() end\nbluez_midi_monitor.enable = function() end\n' >"$HOME/.config/wireplumber/bluetooth.lua.d/80-denice-no-bluetooth.lua"
    pipewire >"$OUT/pipewire.log" 2>&1 &
    sleep 1; wireplumber >"$OUT/wireplumber.log" 2>&1 &
    sleep 1; pipewire-pulse >"$OUT/pipewire-pulse.log" 2>&1 & ;;
  pulseaudio)
    pulseaudio --daemonize=no --exit-idle-time=-1 --log-target=stderr >"$OUT/pulseaudio.log" 2>&1 & ;;
  none)
    # ALSA stage: Chromium's playback opens are redirected and never reach this device; DENICE's own ALSA output opens
    # "default", which the file plugin writes to $OUT/alsa-mix.raw (interleaved float32 stereo) in front of the null device.
    printf 'pcm.!default { type plug slave.pcm "denicefile" }\npcm.denicefile { type file slave.pcm "null" file "%s" format "raw" }\nctl.!default { type hw card 0 }\n' "$OUT/alsa-mix.raw" >"$HOME/.asoundrc" ;;
esac
if [ "$SERVER" != none ]; then
  for i in $(seq 1 50); do pactl info >/dev/null 2>&1 && break; sleep 0.2; done
  pactl info >"$OUT/server-info.txt" 2>&1
  pactl list modules short >"$OUT/modules-before.txt" 2>&1
  pactl load-module module-null-sink sink_name=denice_out sink_properties=device.description=denice_out $SINK >/dev/null
  pactl load-module module-null-sink sink_name=denice_explicit sink_properties=device.description=denice_explicit $SINK >/dev/null
  pactl set-default-sink denice_out
  parec -d denice_out.monitor --format=float32le --rate=48000 --channels=2 --raw >"$OUT/out-sink.f32" 2>"$OUT/parec-out.log" &
  P1=$!
  parec -d denice_explicit.monitor --format=float32le --rate=48000 --channels=2 --raw >"$OUT/explicit-sink.f32" 2>"$OUT/parec-explicit.log" &
  P2=$!
  ( while true; do echo "--- $(pactl info | grep "Default Sink")"; pactl list sink-inputs | grep -E 'Sink Input #|Sink:|application.name|media.name|node.name' ; pactl list sinks short; sleep 0.5; done ) >"$OUT/sink-inputs.log" 2>&1 &
  P3=$!
fi
cd /src
perl -e 'alarm 1200; exec @ARGV' dotnet build tests/Denice.CrossPlatform.Tests/Denice.CrossPlatform.Tests.csproj -c Release -r $RID -p:DeniceAudioRedirectionProof=true >"$OUT/build.log" 2>&1
echo "build exit=$?" >"$OUT/build-exit.txt"
# The Linux packaging publishes icudtl.dat, the .pak files and locales beside libcef.so (3ab198f); the build must show it.
CEFDIR=tests/Denice.CrossPlatform.Tests/bin/Release/net10.0/$RID/CEF
[ -f "$CEFDIR/icudtl.dat" ] && [ -f "$CEFDIR/locales/en-US.pak" ] || echo "CEF resources are not beside libcef.so in $CEFDIR" >>"$OUT/build.log"
DENICE_AUDIO_PROOF_OUT=$OUT/evidence perl -e 'alarm 1500; exec @ARGV' dotnet test tests/Denice.CrossPlatform.Tests/Denice.CrossPlatform.Tests.csproj \
  -c Release -r $RID -p:DeniceAudioRedirectionProof=true --no-build --filter Category=AudioRedirectionProof \
  --logger "console;verbosity=detailed" --diag "$OUT/diag/vstest.log" --blame >"$OUT/test.log" 2>&1
echo "test exit=$?" | tee "$OUT/test-exit.txt"
if [ "$SERVER" = none ]; then
  python3 /p/compare_alsa.py "$OUT" | tee "$OUT/compare-alsa.txt"
fi
if [ "$SERVER" != none ]; then
  sleep 0.5; kill $P1 $P2 $P3 2>/dev/null
  pactl list modules short >"$OUT/modules-after.txt" 2>&1
  python3 /p/compare.py "$OUT" | tee "$OUT/compare.txt"
fi
