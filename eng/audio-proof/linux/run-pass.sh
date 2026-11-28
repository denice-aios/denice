#!/bin/bash
# Host side of stages L1/L2: copies the checkout (no bin/obj/.git) and runs one pass in the test-only container.
# Usage (from the repository root): bash eng/audio-proof/linux/run-pass.sh <pipewire|pulseaudio|alsa> <out-dir> [baseline 0|1] [media reference .f32]
# The optional media reference is an unredirected baseline sink capture; the media-element row is judged against its fade.
set -u
PASS=$1 OUT=$2 BASELINE=${3:-0} REFERENCE=${4:-}
SERVER=$PASS; [ "$PASS" = alsa ] && SERVER=none
ROOT=$(pwd)
SRC=$OUT/src
rm -rf "$OUT" && mkdir -p "$SRC" "$OUT/run"
rsync -a --exclude bin --exclude obj --exclude .git --exclude TestResults --exclude handoff "$ROOT/" "$SRC/"
chmod 777 "$OUT/run"
cp -R "$ROOT/eng/audio-proof/linux" "$OUT/p"
docker volume create denice-audio-proof-nuget >/dev/null
REF_ARGS=() REF_ENV=""
if [ -n "$REFERENCE" ]; then
  cp "$REFERENCE" "$OUT/media-reference.f32"
  REF_ARGS=(-v "$OUT/media-reference.f32":/ref/media-reference.f32:ro -e DENICE_AUDIO_PROOF_MEDIA_REFERENCE=/ref/media-reference.f32)
  REF_ENV="DENICE_AUDIO_PROOF_MEDIA_REFERENCE=/ref/media-reference.f32"
fi
perl -e 'alarm 2400; exec @ARGV' docker run --rm --shm-size=1g \
  -e DENICE_AUDIO_SERVER=$SERVER -e DENICE_AUDIO_PROOF_BASELINE=$BASELINE -e REF_ENV="$REF_ENV" "${REF_ARGS[@]}" \
  -v "$SRC":/src -v "$OUT/run":/out -v "$OUT/p":/p:ro -v denice-audio-proof-nuget:/nuget \
  denice-audio-proof:$PASS bash -c 'chmod 777 /nuget; useradd -m -u 1500 tester; chown -R tester /src; su tester -c "$REF_ENV DENICE_AUDIO_SERVER=$DENICE_AUDIO_SERVER DENICE_AUDIO_PROOF_BASELINE=$DENICE_AUDIO_PROOF_BASELINE bash /p/inner.sh"'
echo "container exit=$?"
