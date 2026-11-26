// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Denice.CrossPlatform.Linux.Desktop;

namespace Denice.CrossPlatform.Linux.Recording;

/// <summary>Copies compositor textures into device-only CUDA storage on the matching GPU.</summary>
[SuppressMessage("Design", "CA2216", Justification = "The recorder drains frames and disposes the context explicitly before graphics shutdown.")]
[SuppressMessage("Usage", "CA2213", Justification = "The recorder borrows the desktop graphics owner, which outlives the recording context.")]
internal sealed class LinuxCudaContext : IDisposable
{
    private readonly LinuxGpuContext _graphics;
    private readonly System.Threading.Lock _gate = new();
    private readonly List<Transfer> _pending = [];
    private readonly HashSet<DeviceFrame> _frames = [];
    private nint _context;
    private bool _contextStackFailed;
    private Exception? _releaseFailure;

    private LinuxCudaContext(LinuxGpuContext graphics, nint context)
    {
        _graphics = graphics;
        _context = context;
    }

    /// <summary>Gets the borrowed GstCudaContext supplied to the encoder.</summary>
    internal nint Context => _context != 0 ? _context : throw new ObjectDisposedException(nameof(LinuxCudaContext));

    /// <summary>Gets the number of retained device allocations.</summary>
    internal int OutstandingFrames
    {
        get
        {
            lock (_gate)
            {
                return _frames.Count;
            }
        }
    }

    /// <summary>Gets a native-release failure that must fail recording finalization.</summary>
    internal Exception? ReleaseFailure
    {
        get
        {
            lock (_gate)
            {
                return _releaseFailure;
            }
        }
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "S3877", Justification = "Outstanding native references must prevent context teardown.")]
    public void Dispose()
    {
        lock (_gate)
        {
            if (_context == 0)
            {
                return;
            }

            if (_frames.Count != 0 || _contextStackFailed)
            {
                throw new InvalidOperationException("CUDA context teardown requires released frames and a valid context stack.");
            }

            _graphics.MakeCurrent();
            Push();
            try
            {
                foreach (Transfer transfer in _pending)
                {
                    transfer.Dispose();
                }

                _pending.Clear();
            }
            finally
            {
                Pop();
            }

            GstNative.Unref(_context);
            _context = 0;
        }
    }

    /// <summary>Selects the CUDA device associated with the current compositor GL context.</summary>
    /// <param name="graphics">The current graphics owner.</param>
    /// <returns>The device-matched CUDA context.</returns>
    internal static LinuxCudaContext Create(LinuxGpuContext graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        graphics.MakeCurrent();
        Require(CudaNative.Initialize(0), "initialize the CUDA driver");
        Require(CudaNative.GetGlDevices(out uint count, out int device, 1, 1), "identify the compositor CUDA device");
        if (count != 1 || device < 0)
        {
            throw new NotSupportedException("CUDA recording requires one device matching the compositor GL context.");
        }

        if (GstNative.LoadCudaDriver() == 0)
        {
            throw new NotSupportedException("The host CUDA driver does not provide GStreamer's required API.");
        }

        nint context = GstNative.NewCudaContext((uint)device);
        return context != 0 ? new LinuxCudaContext(graphics, context)
            : throw new NotSupportedException("GStreamer could not create the compositor's CUDA context.");
    }

    /// <summary>Copies one completed texture without mapping pixel storage into CPU memory.</summary>
    /// <param name="texture">The immutable producer image.</param>
    /// <returns>The owned, tightly packed RGBA device allocation.</returns>
    internal DeviceFrame CopyTexture(LinuxOwnedTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        int width = texture.Width;
        int height = texture.Height;
        nuint pitch = checked((nuint)width * 4);
        nuint bytes = checked(pitch * (nuint)height);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_context == 0, this);
            if (_contextStackFailed || _pending.Count != 0 || _releaseFailure is not null)
            {
                throw new InvalidOperationException("A prior CUDA transfer requires cleanup before another frame.");
            }

            Push();
            try
            {
                var transfer = new Transfer(texture);
                _pending.Add(transfer);
                try
                {
                    Require(CudaNative.RegisterImage(out nint resource, texture.Texture, 0x0DE1, 1), "register the composition texture");
                    transfer.Resource = resource;
                    Require(CudaNative.MapResources(1, in resource, 0), "map the composition texture");
                    transfer.Mapped = true;
                    Require(CudaNative.GetMappedArray(out nint array, transfer.Resource, 0, 0), "get the mapped texture array");
                    Require(CudaNative.Allocate(out ulong device, bytes), "allocate device-only recording storage");
                    transfer.Device = device;
                    var copy = new CudaNative.Copy2D
                    {
                        SourceType = 3,
                        SourceArray = array,
                        DestinationType = 2,
                        DestinationDevice = transfer.Device,
                        DestinationPitch = pitch,
                        WidthBytes = pitch,
                        Height = (nuint)height,
                    };
                    Require(CudaNative.Copy(in copy), "copy the texture into CUDA storage");
                    transfer.ReleaseSource();
                    var frame = new DeviceFrame(this, transfer.Device, width, height);
                    transfer.Device = 0;
                    _ = _pending.Remove(transfer);
                    _ = _frames.Add(frame);
                    return frame;
                }
                catch
                {
                    // If synchronization/unmapping fails, Dispose throws before
                    // releasing the lease. The pending transaction stays owned.
                    transfer.Dispose();
                    _ = _pending.Remove(transfer);
                    throw;
                }
            }
            finally
            {
                Pop();
            }
        }
    }

    private static void Require(int status, string operation)
    {
        if (status != 0)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"CUDA could not {operation}; driver status {status}."));
        }
    }

    private void Push()
    {
        if (_contextStackFailed)
        {
            throw new InvalidOperationException("CUDA context-stack failure retains native resources until process teardown.");
        }

        if (GstNative.PushCudaContext(Context) == 0)
        {
            throw new InvalidOperationException("GStreamer could not activate the recording CUDA context.");
        }
    }

    private void Pop()
    {
        nint expected = GstNative.GetCudaContextHandle(Context);
        if (GstNative.PopCudaContext(out nint popped) == 0 || popped != expected)
        {
            _contextStackFailed = true;
            throw new InvalidOperationException("GStreamer could not restore the preceding CUDA context.");
        }
    }

    private void Release(DeviceFrame frame)
    {
        lock (_gate)
        {
            Push();
            try
            {
                Require(CudaNative.Synchronize(), "complete device-frame consumers");
                Require(CudaNative.Free(frame.Pointer), "release device recording storage");
                frame.Pointer = 0;
                _ = _frames.Remove(frame);
            }
            finally
            {
                Pop();
            }
        }
    }

    /// <summary>Owns a device-only allocation until all encoder references have been released.</summary>
    [SuppressMessage("Design", "CA2216", Justification = "The recording owner releases device frames after native consumers complete.")]
    [SuppressMessage("Usage", "CA2213", Justification = "The frame borrows its owning context, which tracks the outstanding frame count.")]
    internal sealed class DeviceFrame : IDisposable
    {
        private readonly LinuxCudaContext _owner;
        private bool _nativeOwned;

        /// <summary>Initializes a new instance of the <see cref="DeviceFrame"/> class.</summary>
        /// <param name="owner">The owning context.</param>
        /// <param name="pointer">The device pointer.</param>
        /// <param name="width">The pixel width.</param>
        /// <param name="height">The pixel height.</param>
        internal DeviceFrame(LinuxCudaContext owner, ulong pointer, int width, int height)
        {
            _owner = owner;
            Pointer = pointer;
            Width = width;
            Height = height;
        }

        /// <summary>Gets the borrowed GstCudaContext shared with the encoder.</summary>
        internal nint Context => _owner.Context;

        /// <summary>Gets or sets the owned device pointer.</summary>
        internal ulong Pointer { get; set; }

        /// <summary>Gets the pixel width.</summary>
        internal int Width { get; }

        /// <summary>Gets the pixel height.</summary>
        internal int Height { get; }

        /// <inheritdoc />
        [SuppressMessage("Design", "S3877", Justification = "Native memory ownership must prevent early release of its device allocation.")]
        public void Dispose()
        {
            lock (_owner._gate)
            {
                if (_nativeOwned)
                {
                    throw new InvalidOperationException("The device frame belongs to GStreamer until its native reference is released.");
                }

                if (Pointer != 0)
                {
                    _owner.Release(this);
                }
            }
        }

        /// <summary>Reserves this allocation for exactly one native memory wrapper.</summary>
        internal void BeginNativeOwnership()
        {
            lock (_owner._gate)
            {
                ObjectDisposedException.ThrowIf(Pointer == 0, this);
                if (_nativeOwned)
                {
                    throw new InvalidOperationException("The device frame already belongs to GStreamer.");
                }

                _nativeOwned = true;
            }
        }

        /// <summary>Returns ownership when native wrapping fails before transfer.</summary>
        internal void CancelNativeOwnership()
        {
            lock (_owner._gate)
            {
                _nativeOwned = false;
            }
        }

        /// <summary>Releases a frame after its final native memory reference completes.</summary>
        internal void ReleaseNativeOwnership()
        {
            lock (_owner._gate)
            {
                _nativeOwned = false;
                _owner.Release(this);
            }
        }

        /// <summary>Records a failed native callback while retaining the device allocation in its owner.</summary>
        /// <param name="error">The release failure.</param>
        internal void RecordReleaseFailure(Exception error)
        {
            lock (_owner._gate)
            {
                _owner._releaseFailure ??= error;
            }
        }
    }

    [SuppressMessage("Design", "CA2216", Justification = "The context retains failed transactions and explicitly drains them before teardown.")]
    private sealed class Transfer(LinuxOwnedTexture texture) : IDisposable
    {
        private readonly IDisposable _lease = texture.Retain();

        internal nint Resource { get; set; }

        internal ulong Device { get; set; }

        internal bool Mapped { get; set; }

        public void Dispose()
        {
            ReleaseSource();
            if (Device != 0)
            {
                Require(CudaNative.Free(Device), "release incomplete device recording storage");
                Device = 0;
            }
        }

        internal void ReleaseSource()
        {
            if (Resource != 0)
            {
                Require(CudaNative.Synchronize(), "complete texture transfer");
                if (Mapped)
                {
                    nint resource = Resource;
                    Require(CudaNative.UnmapResources(1, in resource, 0), "unmap the composition texture");
                    Mapped = false;
                }

                Require(CudaNative.UnregisterResource(Resource), "unregister the composition texture");
                Resource = 0;
            }

            _lease.Dispose();
        }
    }
}
