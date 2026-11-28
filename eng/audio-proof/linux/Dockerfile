# Test-only container for the Linux browser-audio redirection proof (stage L1). Nothing here ships.
# Build: docker build --build-arg SERVER=pipewire -t denice-audio-proof:pipewire eng/audio-proof/linux
#        docker build --build-arg SERVER=pulseaudio -t denice-audio-proof:pulseaudio eng/audio-proof/linux
#        docker build --build-arg SERVER=none -t denice-audio-proof:alsa eng/audio-proof/linux
FROM ubuntu:24.04
ARG SERVER=pipewire
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update -qq && apt-get install -y -qq --no-install-recommends \
      curl ca-certificates libicu74 python3 xvfb xauth dbus dbus-x11 \
      pulseaudio-utils alsa-utils libasound2t64 libasound2-plugins \
      speech-dispatcher speech-dispatcher-espeak-ng \
      libnss3 libnspr4 libatk1.0-0t64 libatk-bridge2.0-0t64 libcups2t64 libdrm2 libxkbcommon0 libxcomposite1 \
      libxdamage1 libxrandr2 libgbm1 libpango-1.0-0 libcairo2 libxshmfence1 libxfixes3 libx11-6 libxext6 libxcb1 \
      libglib2.0-0t64 libdbus-1-3 libexpat1 libegl1 libgles2 libgl1 libudev1 \
 && case "$SERVER" in \
      pipewire) apt-get install -y -qq --no-install-recommends pipewire pipewire-pulse wireplumber ;; \
      pulseaudio) apt-get install -y -qq --no-install-recommends pulseaudio ;; \
      none) true ;; \
    esac \
 && rm -rf /var/lib/apt/lists/*
RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --version 10.0.400 --install-dir /dotnet >/dev/null
ENV PATH=/dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DENICE_AUDIO_SERVER=$SERVER
