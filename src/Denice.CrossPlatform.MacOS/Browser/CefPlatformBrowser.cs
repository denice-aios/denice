// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Denice.Abstractions.Contracts;
using Xilium.CefGlue;

namespace Denice.CrossPlatform.Browser;

/// <summary>Runs stock CEF offscreen in the application's main thread.</summary>
internal sealed partial class CefPlatformBrowser : IPlatformBrowser
{
    private readonly State state = new();
    private Client? client;
    private Application? application;

    private bool initialized;

    private CefPlatformBrowser()
    {
    }

    /// <inheritdoc />
    public bool IsClosed => state.Browser is null;

    /// <inheritdoc />
    public bool IsReady => state.Ready;

    /// <inheritdoc />
    public string? LastLoadError => state.LoadError;

    /// <inheritdoc />
    public void Start(Uri url, int width, int height, IPlatformBrowserFrameSink frames, IPlatformBrowserAudioSink audio, string cachePath, int debugPort)
    {
        if (initialized)
        {
            throw new InvalidOperationException("The browser has already started.");
        }

        state.Width = width;
        state.Height = height;
        state.Requested = url;
        state.Pending = true;
        application = new Application();
        string nativeLibrary = CefRuntimeLocator.FindLibrary() ?? throw new InvalidOperationException("The CEF native library was not deployed.");
        nint nativeHandle = NativeLibrary.Load(nativeLibrary);
        NativeLibrary.SetDllImportResolver(typeof(CefRuntime).Assembly, (name, assembly, path) => string.Equals(name, "libcef", StringComparison.Ordinal) ? nativeHandle : nint.Zero);
        CefRuntime.Load();
        #pragma warning disable S3251 // Approved optional browser audio hook, implemented only on macOS.
        StartAudio(audio);
        #pragma warning restore S3251
        var settings = new CefSettings
        {
            RootCachePath = cachePath,
            RemoteDebuggingPort = debugPort,
            LogFile = Path.Combine(cachePath, "chromium.log"),
            LogSeverity = debugPort > 0 ? CefLogSeverity.Verbose : CefLogSeverity.Warning,
            NoSandbox = true,
            WindowlessRenderingEnabled = true,
            MultiThreadedMessageLoop = false,
            ExternalMessagePump = true,
            BackgroundColor = new CefColor(0, 0, 0, 0),
            ResourcesDirPath = CefRuntimeLocator.GetResourceDirPath(),
        };
        ConfigurePlatform(settings);
        CefRuntime.Initialize(new CefMainArgs(["denice"]), settings, application, nint.Zero);
        initialized = true;
        try
        {
            CreateBrowser(url, frames, audio);
        }
        catch
        {
            if (IsClosed)
            {
                Shutdown();
            }
            else
            {
                RequestClose();
            }

            throw;
        }
    }

    /// <inheritdoc />
    public void Pump()
    {
        if (!initialized)
        {
            return;
        }

        CefRuntime.DoMessageLoopWork();
        #pragma warning disable S3251 // Approved optional browser audio hook, implemented only on macOS.
        PumpAudio();
        #pragma warning restore S3251
        if (state.Failure is { } message)
        {
            state.Failure = null;
            throw new InvalidOperationException(message);
        }
    }

    /// <inheritdoc />
    public void Navigate(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        state.Requested = url;
        state.Pending = true;
        state.Ready = false;
        state.LoadError = null;
        state.Browser?.GetMainFrame().LoadUrl(url.AbsoluteUri);
    }

    /// <inheritdoc />
    public void Resize(int width, int height)
    {
        state.Width = width;
        state.Height = height;
        state.Browser?.GetHost().WasResized();
    }

    /// <inheritdoc />
    public void RequestClose()
    {
        state.Closing = true;
        state.Browser?.GetHost().CloseBrowser(true);
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        if (!initialized)
        {
            return;
        }

        if (!IsClosed)
        {
            throw new InvalidOperationException("The browser must close before CEF shuts down.");
        }

        CefRuntime.Shutdown();
        GC.KeepAlive(client);
        GC.KeepAlive(application);
        #pragma warning disable S3251 // Approved optional browser audio hook, implemented only on macOS.
        FinishAudio();
        #pragma warning restore S3251
        initialized = false;
        client = null;
        application = null;
    }

    /// <inheritdoc />
    public void MouseMove(int x, int y, bool leave) => state.Browser?.GetHost().SendMouseMoveEvent(new CefMouseEvent(x, y, CefEventFlags.None), leave);

    /// <inheritdoc />
    public void MouseClick(int x, int y, int button, bool mouseUp, int clickCount) => state.Browser?.GetHost().SendMouseClickEvent(new CefMouseEvent(x, y, CefEventFlags.None), (CefMouseButtonType)button, mouseUp, clickCount);

    /// <inheritdoc />
    public void MouseWheel(int x, int y, int deltaX, int deltaY) => state.Browser?.GetHost().SendMouseWheelEvent(new CefMouseEvent(x, y, CefEventFlags.None), deltaX, deltaY);

    /// <summary>Creates the platform browser service.</summary>
    /// <returns>The browser capability.</returns>
    internal static IPlatformBrowser Create() => new CefPlatformBrowser();

    private static partial CefAudioHandler? CreateAudioHandler(State owner, IPlatformBrowserAudioSink sink);

    private static partial void ConfigurePlatform(CefSettings settings);

    private static partial void ConfigureCommandLine(CefCommandLine commandLine);

    private static partial void ForwardFrame(IPlatformBrowserFrameSink sink, CefPaintElementType type, CefAcceleratedPaintInfo info);

    private static void ForwardBuffer(IPlatformBrowserFrameSink sink, CefPaintElementType type, CefAcceleratedPaintInfo info, scoped in BrowserGpuBuffer buffer)
    {
        var visible = info.Extra.VisibleRectangle;
        sink.OnAcceleratedFrame(in buffer, (int)info.Format, info.Extra.CodedSize.Width, info.Extra.CodedSize.Height, visible.X, visible.Y, visible.Width, visible.Height, type == CefPaintElementType.Popup);
    }

#pragma warning disable S3251 // The approved optional browser audio hooks are implemented only on macOS; Windows and Linux intentionally omit them.
    /// <summary>Starts the browser's own audio output where a platform implements it (macOS); no-op elsewhere.</summary>
    /// <param name="audio">The audio sink given to Start.</param>
    partial void StartAudio(IPlatformBrowserAudioSink audio);

    /// <summary>Hands pending browser audio to the sink after each message pump where implemented (macOS); no-op elsewhere.</summary>
    partial void PumpAudio();

    /// <summary>Completes browser audio after CEF shutdown where implemented (macOS); no-op elsewhere.</summary>
    partial void FinishAudio();
#pragma warning restore S3251

    [SuppressMessage("Maintainability", "CA1508", Justification = "The native creation result is required even if the current binding throws for a null browser.")]
    private void CreateBrowser(Uri url, IPlatformBrowserFrameSink frames, IPlatformBrowserAudioSink audio)
    {
        client = new Client(state, frames, audio);
        var window = CefWindowInfo.Create();
        window.SetAsWindowless(nint.Zero, true);
        window.SharedTextureEnabled = true;
        window.RuntimeStyle = CefRuntimeStyle.Alloy;
        var settings = new CefBrowserSettings
        {
            WindowlessFrameRate = 60,
            BackgroundColor = new CefColor(0, 0, 0, 0),
        };
        CefBrowser? created = null;
        try
        {
            created = CefBrowserHost.CreateBrowserSync(window, client, settings, url.AbsoluteUri);
            if (created is null)
            {
                throw new InvalidOperationException("CEF did not create the native application browser.");
            }

            if (state.Browser is null)
            {
                state.Browser = created;
                created = null;
            }
            else if (ReferenceEquals(created, state.Browser))
            {
                created = null;
            }
        }
        finally
        {
            // OnAfterCreated retains its wrapper; release any independent synchronous result.
            created?.Dispose();
        }
    }

    private sealed class State
    {
        /// <summary>Gets or sets the live browser.</summary>
        internal CefBrowser? Browser { get; set; }

        /// <summary>Gets or sets a value indicating whether close was requested.</summary>
        internal bool Closing { get; set; }

        /// <summary>Gets or sets the viewport width.</summary>
        internal int Width { get; set; }

        /// <summary>Gets or sets the viewport height.</summary>
        internal int Height { get; set; }

        /// <summary>Gets or sets a callback failure for the main loop.</summary>
        internal string? Failure { get; set; }

        /// <summary>Gets or sets a value indicating whether the current main-document navigation loaded.</summary>
        internal bool Ready { get; set; }

        /// <summary>Gets or sets the failure of the current main-document navigation.</summary>
        internal string? LoadError { get; set; }

        /// <summary>Gets or sets the main-document URL most recently requested through Navigate or Start.</summary>
        internal Uri? Requested { get; set; }

        /// <summary>Gets or sets a value indicating whether the requested navigation has not started loading yet.</summary>
        internal bool Pending { get; set; }

        /// <summary>Whether a main-frame load result belongs to the requested navigation; fragments are ignored.</summary>
        /// <param name="url">The frame URL CEF reported.</param>
        /// <returns>Whether it is the current navigation.</returns>
        internal bool IsCurrent(string url) =>
            Requested is { } requested
            && string.Equals(WithoutFragment(url), WithoutFragment(requested.AbsoluteUri), StringComparison.Ordinal);

        /// <summary>Contains managed callback exceptions at the native boundary.</summary>
        /// <param name="callback">The synchronous managed callback.</param>
        internal void Invoke(Action callback)
        {
            try
            {
                callback();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Failure = error.ToString();
            }
        }

        private static string WithoutFragment(string url) => url.IndexOf('#', StringComparison.Ordinal) is var hash and >= 0 ? url[..hash] : url;
    }

    private sealed class Application : CefApp
    {
        protected override void OnBeforeCommandLineProcessing(string processType, CefCommandLine commandLine)
        {
            commandLine.AppendSwitch("single-process");
            commandLine.AppendSwitch("in-process-gpu");
            commandLine.AppendSwitch("disable-gpu-sandbox");
            ConfigureCommandLine(commandLine);
        }
    }

    private sealed class Client : CefClient
    {
        private readonly Renderer renderer;
        private readonly Lifetime lifetime;
        private readonly CefAudioHandler? audio;
        private readonly Loading loading;

        /// <summary>Initializes a new instance of the <see cref="Client"/> class.</summary>
        /// <param name="owner">The shared browser state.</param>
        /// <param name="frames">The synchronous GPU consumer.</param>
        /// <param name="audio">The audio capture consumer.</param>
        internal Client(State owner, IPlatformBrowserFrameSink frames, IPlatformBrowserAudioSink audio)
        {
            renderer = new Renderer(owner, frames);
            lifetime = new Lifetime(owner);
            this.audio = CreateAudioHandler(owner, audio);
            loading = new Loading(owner);
        }

        protected override CefRenderHandler GetRenderHandler() => renderer;

        protected override CefLifeSpanHandler GetLifeSpanHandler() => lifetime;

        protected override CefAudioHandler GetAudioHandler() => audio!;

        protected override CefLoadHandler GetLoadHandler() => loading;
    }

    private sealed class Lifetime(State owner) : CefLifeSpanHandler
    {
        protected override void OnAfterCreated(CefBrowser browser) => owner.Browser = browser;

        protected override void OnBeforeClose(CefBrowser browser)
        {
            owner.Browser?.Dispose();
            owner.Browser = null;
        }
    }

    private sealed class Loading(State owner) : CefLoadHandler
    {
        protected override void OnLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
        {
            // Every main-document load that starts is the current navigation, whether DENICE or the page started it.
            if (frame.IsMain)
            {
                owner.Pending = false;
                owner.Ready = false;
            }
        }

        protected override void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
        {
            // A load that ends before the requested one has started belongs to the previous document. A load failure is
            // not fatal: the desktop follows it with the application session. A bundled data URL loads with status zero.
            if (!frame.IsMain || owner.Pending)
            {
                return;
            }

            bool bundled = frame.Url.StartsWith("data:", StringComparison.Ordinal);
            owner.Ready = httpStatusCode is >= 200 and < 400 || (bundled && httpStatusCode == 0);
            owner.LoadError = owner.Ready ? null : owner.LoadError ?? string.Create(CultureInfo.InvariantCulture, $"The application page returned HTTP {httpStatusCode}.");
        }

        protected override void OnLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl)
        {
            // A superseded navigation reports Aborted. Before the requested load starts, only its own failure counts.
            if (frame.IsMain && errorCode != CefErrorCode.Aborted && (!owner.Pending || owner.IsCurrent(failedUrl)))
            {
                owner.Pending = false;
                owner.Ready = false;
                owner.LoadError = string.Create(CultureInfo.InvariantCulture, $"The application page could not load: {errorText} ({(int)errorCode}).");
            }
        }
    }

    private sealed class Renderer(State owner, IPlatformBrowserFrameSink sink) : CefRenderHandler
    {
        private CefRectangle popup;
        private bool popupVisible;

        protected override CefAccessibilityHandler GetAccessibilityHandler() => null!;

        protected override void GetViewRect(CefBrowser browser, out CefRectangle rect) => rect = new CefRectangle(0, 0, owner.Width, owner.Height);

        protected override bool GetScreenInfo(CefBrowser browser, CefScreenInfo screenInfo) => false;

        protected override void OnPopupShow(CefBrowser browser, bool show)
        {
            popupVisible = show;
            owner.Invoke(() => sink.OnPopup(show, popup.X, popup.Y, popup.Width, popup.Height));
        }

        protected override void OnPopupSize(CefBrowser browser, CefRectangle rect)
        {
            popup = rect;
            owner.Invoke(() => sink.OnPopup(popupVisible, rect.X, rect.Y, rect.Width, rect.Height));
        }

        protected override void OnPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, nint buffer, int width, int height)
        {
            owner.Failure = "CEF produced CPU pixels instead of the required GPU buffer.";
        }

        protected override void OnAcceleratedPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, CefAcceleratedPaintInfo info)
        {
            if (owner.Closing)
            {
                return;
            }

            try
            {
                ForwardFrame(sink, type, info);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                owner.Failure = error.ToString();
            }
        }

        protected override void OnScrollOffsetChanged(CefBrowser browser, double x, double y)
        {
        }

        protected override void OnImeCompositionRangeChanged(CefBrowser browser, CefRange selectedRange, CefRectangle[] characterBounds)
        {
        }
    }
}
