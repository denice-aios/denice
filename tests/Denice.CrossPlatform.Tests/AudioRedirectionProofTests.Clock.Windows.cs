// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Denice.CrossPlatform.Browser;
using Denice.CrossPlatform.Windows.Browser;
using Denice.TestHelpers;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// The redirected WASAPI clock across Stop, Reset and Start (part of the W1 proof), compiled only with DeniceAudioRedirectionProof. Chromium
/// (media/audio/win/audio_low_latency_output_win.cc) sets its written-frame count to the endpoint buffer size at every
/// Start and computes the output delay as written frames minus the played position, clamped at zero. These cases pin the
/// two facts that delay depends on: Reset returns the position to 0, and written minus position equals the frames still
/// queued (the padding), so the page's output clock cannot pass a sample that has not been mixed.
/// </summary>
[SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: the WASAPI client takes unmanaged buffers and formats.")]
public sealed unsafe partial class AudioRedirectionProofTests
{
    private const int Frames = 1920;

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void ResetReturnsThePositionToZeroAndDelayMatchesTheQueue()
    {
        using var client = new WasapiRedirectedClient(SystemServicesBuilder.Real().Build().Clock, "test-endpoint");
        Initialize(client);

        // First run: Chromium writes 1920 frames, the mixer takes 1440, 480 stay queued.
        Assert.Equal(0, client.Start());
        Write(client, Frames, 0.25f);
        Mix(client, 1440);
        Assert.Equal(1440ul, Position(client));
        Assert.Equal(480u, Padding(client));

        // Stop holds the position; Reset discards the queue and returns the position to 0.
        Assert.Equal(0, client.Stop());
        Assert.Equal(1440ul, Position(client));
        Assert.Equal(0, client.Reset());
        Assert.Equal(0ul, Position(client));
        Assert.Equal(0u, Padding(client));

        // Second run: positions count from the new Start, and written minus position is exactly what is still queued.
        Assert.Equal(0, client.Start());
        Write(client, Frames, 0.5f);
        Mix(client, 1200);
        ulong position = Position(client);
        Assert.Equal(1200ul, position);
        Assert.Equal((ulong)Frames - position, Padding(client));
        Mix(client, 720);
        Assert.Equal((ulong)Frames, Position(client));
        Assert.Equal(0u, Padding(client));
    }

    private static void Initialize(WasapiRedirectedClient client)
    {
        // WAVEFORMATEX: IEEE float, 2 channels, 48 kHz, 32 bits; event-driven shared mode with a 40 ms buffer.
        byte* format = stackalloc byte[18];
        *(ushort*)format = 3;
        *(ushort*)(format + 2) = 2;
        *(uint*)(format + 4) = MixerCore.SampleRate;
        *(uint*)(format + 8) = MixerCore.SampleRate * 8;
        *(ushort*)(format + 12) = 8;
        *(ushort*)(format + 14) = 32;
        *(ushort*)(format + 16) = 0;
        Assert.Equal(0, client.Initialize(0, 0x00040000, 400_000, 0, (nint)format, null));
    }

    private static void Write(WasapiRedirectedClient client, int frames, float level)
    {
        nint data;
        Assert.Equal(0, client.GetBuffer((uint)frames, &data));
        new Span<float>((void*)data, frames * 2).Fill(level);
        Assert.Equal(0, client.ReleaseBuffer((uint)frames, 0));
    }

    private static void Mix(WasapiRedirectedClient client, int frames)
    {
        float[] left = new float[frames];
        float[] right = new float[frames];
        client.MixInto(left, right, 0, 0);
    }

    private static ulong Position(WasapiRedirectedClient client)
    {
        ulong position;
        ulong counter;
        Assert.Equal(0, client.GetPosition(&position, &counter));
        return position;
    }

    private static uint Padding(WasapiRedirectedClient client)
    {
        uint frames;
        Assert.Equal(0, client.GetCurrentPadding(&frames));
        return frames;
    }
}
