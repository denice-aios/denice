// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Recording;

/// <summary>Selects a concrete H.264 factory on the CUDA device that owns the compositor frames.</summary>
[SuppressMessage("Design", "CA2216", Justification = "The pipeline owner releases its retained element reference after stopping native processing.")]
internal sealed class GstHardwareEncoder : IDisposable
{
    private nint _element;

    private GstHardwareEncoder(nint element, string factoryName)
    {
        _element = element;
        FactoryName = factoryName;
    }

    /// <summary>Gets the borrowed element pointer retained by this owner.</summary>
    internal nint Element => _element != 0 ? _element : throw new ObjectDisposedException(nameof(GstHardwareEncoder));

    /// <summary>Gets the actual selected plugin factory name.</summary>
    internal string FactoryName { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_element != 0)
        {
            GstNative.Unref(_element);
            _element = 0;
        }
    }

    /// <summary>Discovers a packaged device-specific encoder compatible with the requested CUDA frame layout.</summary>
    /// <param name="cuda">The exact context that owns every input frame.</param>
    /// <param name="width">The recording width.</param>
    /// <param name="height">The recording height.</param>
    /// <returns>The retained encoder, with its shared CUDA context supplied.</returns>
    internal static GstHardwareEncoder Create(LinuxCudaContext cuda, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(cuda);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        nint input = CreateInputCaps(width, height);
        nint output = GstNative.CapsFromString("video/x-h264");
        nint features = GstNative.GetPluginFeatures(GstNative.GetRegistry(), "nvcodec");
        try
        {
            if (input == 0 || output == 0)
            {
                throw new InvalidOperationException("GStreamer could not describe the recording media caps.");
            }

            return Select(cuda, features, input, output);
        }
        finally
        {
            if (features != 0)
            {
                GstNative.FreePluginFeatures(features);
            }

            if (output != 0)
            {
                GstNative.UnrefMiniObject(output);
            }

            if (input != 0)
            {
                GstNative.UnrefMiniObject(input);
            }
        }
    }

    /// <summary>Creates the exact shared source and hardware-selection input caps.</summary>
    /// <param name="width">The recording width.</param>
    /// <param name="height">The recording height.</param>
    /// <returns>The owned caps reference.</returns>
    internal static nint CreateInputCaps(int width, int height)
    {
        string description = string.Create(
            CultureInfo.InvariantCulture,
            $"video/x-raw(memory:CUDAMemory),format=RGBA,width={width},height={height},framerate=60/1,interlace-mode=progressive");
        nint caps = GstNative.CapsFromString(description);
        return caps != 0 ? caps : throw new InvalidOperationException("GStreamer could not describe the recording input caps.");
    }

    /// <summary>Requires the encoder's context query to return the exact input-memory context.</summary>
    /// <param name="cuda">The context used for every device frame.</param>
    /// <remarks>Call after READY and before the first frame, then after any pipeline state reconstruction.</remarks>
    internal void RequireContext(LinuxCudaContext cuda)
    {
        const string contextType = "gst.cuda.context";
        nint query = GstNative.NewContextQuery(contextType);
        if (query == 0)
        {
            throw new InvalidOperationException("GStreamer could not allocate a CUDA context query.");
        }

        try
        {
            if (GstNative.QueryElement(Element, query) == 0)
            {
                throw new NotSupportedException("The selected encoder did not report its active CUDA context.");
            }

            GstNative.ParseContextQuery(query, out nint context);
            nint structure = context == 0 ? 0 : GstNative.ContextStructure(context);
            nint value = structure == 0 ? 0 : GstNative.StructureValue(structure, contextType);
            if (value == 0 || GObjectNative.GetObject(value) != cuda.Context)
            {
                throw new NotSupportedException("The selected encoder does not share the input CUDA context; CPU fallback is rejected.");
            }
        }
        finally
        {
            GstNative.UnrefMiniObject(query);
        }
    }

    private static GstHardwareEncoder Select(LinuxCudaContext cuda, nint features, nint input, nint output)
    {
        uint device = GstProperties.ReadUInt(cuda.Context, "cuda-device-id", requireReadOnly: false)
            ?? throw new NotSupportedException("The input CUDA context did not identify its device.");
        nuint factoryType = GstNative.ElementFactoryType();
        for (nint node = features; node != 0; node = Marshal.ReadIntPtr(node, 8))
        {
            nint feature = Marshal.ReadIntPtr(node);
            if (GObjectNative.IsInstance(feature, factoryType) == 0
                || GstNative.FactoryAcceptsInput(feature, input) == 0
                || GstNative.FactoryProducesOutput(feature, output) == 0)
            {
                continue;
            }

            string name = ReadName(feature);
            nint element = GstRuntime.CreateElement(name, "libgstnvcodec.so", "hardware-video-encoder");
            bool retained = false;
            try
            {
                // Auto-selection exposes a writable device property. It can
                // silently choose another GPU when input memory is unsupported.
                if (GstProperties.ReadUInt(element, "cuda-device-id", requireReadOnly: true) != device)
                {
                    continue;
                }

                SupplyContext(element, cuda.Context);
                _ = GstNative.RefSink(element);
                retained = true;
                return new GstHardwareEncoder(element, name);
            }
            finally
            {
                if (!retained)
                {
                    GstNative.Unref(element);
                }
            }
        }

        throw new NotSupportedException("No packaged hardware H.264 encoder accepts RGBA CUDA memory on the compositor device.");
    }

    private static string ReadName(nint feature)
    {
        nint name = GstNative.GetObjectName(feature);
        try
        {
            return Marshal.PtrToStringUTF8(name) ?? throw new InvalidOperationException("GStreamer returned an unnamed encoder factory.");
        }
        finally
        {
            GstNative.Free(name);
        }
    }

    private static void SupplyContext(nint element, nint cuda)
    {
        nint context = GstNative.NewCudaContextMessage(cuda);
        if (context == 0)
        {
            throw new InvalidOperationException("GStreamer could not share the recording CUDA context.");
        }

        try
        {
            GstNative.SetContext(element, context);
        }
        finally
        {
            GstNative.UnrefMiniObject(context);
        }
    }
}
