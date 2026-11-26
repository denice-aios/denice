# DENICE desktop prototype

The macOS prototype renders the native Metal orb beneath transparent Chromium HTML inside the existing Rust-hosted .NET process. MP4 recording includes the composed client screen and the page audio. The final native-input demonstration is `handoff/prototype-mvp/denice-orb.mp4`, lasting 4.542104 seconds, with poster `handoff/prototype-mvp/denice-orb.png`. Measured audio-to-visual offsets are 67 ms early and 126 ms late across two cues; exact synchronization is not claimed. Physical-resolution capture remains below 60 distinct frames per second.

## Run it

Work is isolated on branch `prototype-mvp` in `../denice-prototype-mvp`. The original checkout remains unchanged. The packaged development application is `.private/prototype-mvp/Denice.app`.

```sh
env -u DYLD_LIBRARY_PATH .private/prototype-mvp/Denice.app/Contents/MacOS/denice-entry gui --record /tmp/denice-new.mp4
```

The output path must not exist. Close the native window to finalize the recording. Omit `--record` for normal interactive rendering. Add `--duration 2.5` for an automatic demonstration; this timer starts after the page is ready, while recording retains startup too.

Direct developer execution removes an inherited library override from this child invocation only. The existing Rust version launcher applies that cleanup to its macOS child. No shell environment or system security setting is changed.

## Implementation

The existing Rust entry hosts the existing .NET runtime. C# owns the Cocoa window, SkiaSharp Metal scene, CEF browser callbacks, and AVFoundation recorder. No new first-party compilation project, language, custom Chromium build, or manual C++ virtual dispatch table was added. Dependencies supply their existing managed and native binaries.

The GPU orb sits behind a transparent Chromium page. Chromium supplies borrowed IOSurfaces through accelerated offscreen rendering; the renderer copies each into owned GPU storage and completes its GPU read before returning from the callback. The native scene and browser image are composed into one presentation surface shared by the window and recorder. The orb uses 2,000 GPU-batched particle sprites and retains a spherical breathing shape. It does not use WebGL. The literal `<denice mode="background">` remains in the page; it is not registered through `customElements.define`, which requires a hyphenated name.

One CEF output stream supplies playback and the recorder's 48 kHz stereo PCM tap. The recorder encodes H.264 and AAC in MP4. It starts with the first native frame before browser startup and retains one timeline through startup and page readiness. It records initial physical client-area pixel dimensions, proportionally fitting the complete visible presentation when the window resizes. Native titlebar chrome is outside this client capture.

The video timeline holds the last completed screen image through missed recording slots, including synchronous startup and encoder backpressure. Runtime statistics separately report rendered frames, newly captured frames accepted by the recorder, and encoded samples including holds. A 60 FPS encoded stream does not imply 60 distinct captures per second. Audio records explicit silence during inactive intervals so stream restarts retain their elapsed timing. Captured PCM buffering and encoder catch-up are bounded. One-second MP4 fragments retain recoverable footage after a later failure.

Existing output and recording-start failures return an error. A later recording failure is reported while the application continues. Normal shutdown drains and finalizes recording before its resources are released.

## Dependencies

| Dependency | Version | Purpose |
|---|---|---|
| CefGlue.Next.Core | 156.8078.80 | Managed binding to the CEF C API. |
| cef.runtime.osx-x64 | 156.0.15 | Stock CEF framework matching Chromium 156.0.8078.80. |
| SkiaSharp | 4.157.0 | Existing C# GPU graphics binding. |
| SkiaSharp.NativeAssets.macOS | 4.157.0 | Native Metal-capable Skia binary. |

AVFoundation, CoreMedia, CoreVideo, Cocoa, and Metal are system frameworks. Managed binding source evidence is pinned to Deon-Berlin/CefGlue commit `7b75d2f3cb1da982bad84daceb7cdad8e72219d5`; native CEF evidence is commit `cf671c072925b27823a61181152f4f288e8e4306`.

The native NuGet build targets assume an obsolete flat framework path. Their build assets are excluded, and the existing macOS project explicitly copies the actual complete framework tree into build and publish outputs.

## Settings and build

`browser.profile` selects the browser cache directory beneath DENICE's user directory. `browser.debugPort` controls the localhost diagnostic endpoint and defaults to zero, disabled. No `.env` edits are required. Diagnostic port 9223 was used during validation and has been reset through Settings to the default zero.

```sh
dotnet publish src/Denice.Runtime/Denice.Runtime.csproj -c Debug -r osx-x64 --self-contained --no-restore -m:1 -o .private/prototype-mvp/Denice.app/Contents/MacOS -v:q
```

The existing bundle contains the Rust executable and Info.plist. Rust rebuilds use `cargo build -p denice-entry --locked` from `rust`; its executable is copied into `Contents/MacOS`. This ignored development bundle is not a notarized distribution.

## Runtime evidence

After the page loaded, process inspection found hostfxr, CoreCLR, SkiaSharp, and CEF loaded in the Rust entry's PID, with no descendants at that inspection. This establishes the observed prototype arrangement, not every possible Chromium service configuration. See `handoff/prototype-mvp/validation/process-colocation.json`.

After actual native window resize, CoreGraphics mouse movement and down/up events activated the page cue button. Its text changed to `Cue played`. CDP only read DOM state for this native-input test. Existing event-posting permission was used; security settings were unchanged. See `handoff/prototype-mvp/validation/native-input.json`.

The audio regression used temporary visible markers alongside two actual page cues. Both audio and video tracks begin at zero and decode completely. Marker starts were 1.616667 and 4.883333 seconds; decoded audio onsets were 1.549833 and 5.009500 seconds. First-packet audio anchoring includes capture-to-callback latency, and displayed frames add rendering latency. The normal final demonstration has no injected markers.

The measured regression captured 2048 by 1450 pixels over 7.3167 seconds. It rendered 184 frames, accepted 170 new captured images, and encoded 439 samples including held images. Earlier 1024 by 768 runs approached 59 distinct frames per second; that lower-resolution result is not the current physical-resolution result.

## Checks

Strict application publish passed with analyzers enabled. Runtime, Settings/SystemServices, native recorder, and analyzer tests passed. Native recorder coverage includes delayed first audio, sparse startup video, stream silence, bounded PCM backpressure, a ten-second held-frame interval, existing destination preservation, and zero-frame failure. Windows and Linux platform assemblies build, while their new desktop entry remains explicitly unavailable. Rust loader process-environment tests and Clippy passed. `git diff --check` passed.

DN0101 now permits explicitly recognized graphics/media native imports only in per-OS capability implementations. Filesystem calls, unknown native imports, and arbitrary sibling namespaces retain their restrictions. Regression tests cover both allowed and rejected calls. No global analyzer disable was introduced.

Relevant logs are retained under `handoff/prototype-mvp/validation`. Failed and intermediate media remain evidence rather than user-facing demonstrations.

## Resolved runtime failures

Unbundled startup exited 133 inside CEF initialization. The pinned CEF macOS startup computes an app-bundle-derived child path even with single-process enabled. Packaging the existing executable in `Denice.app/Contents/MacOS` resolved that check without adding a helper executable.

Bundled startup then exited 139 in Apple's ImageIO at address `0x0bad4007`. The inherited `DYLD_LIBRARY_PATH=/usr/local/lib` caused ImageIO to load the wrong PNG library. A valid two-pixel PNG reproduced the failure in Apple's `sips` tool. Removing that variable from the child environment resolved it. Vendor binaries were unchanged. Ad-hoc signing did not resolve this failure. LLDB attachment was denied; security settings were not changed.

The CEF public audio header describes Unix timestamps, but the pinned `audio_capturer.cc` implementation forwards monotonic `TimeTicks` milliseconds. The recorder uses a shared elapsed-time anchor instead of UTC. A sparse startup video interval then reproduced AVFoundation -11800 with underlying OSStatus -16341 in a native fixture without Chromium. Holding the last completed image through missing recording slots resolved that case. Explicit silent AAC samples subsequently preserved inactive-stream gaps that timestamp holes alone did not retain.

## Remaining product work

This is a macOS prototype. Windows and Linux GUI implementations remain unimplemented. CEF single-process operation is experimental and is not a supported-production guarantee; its proxy-resolver warning persists while the loopback application runs. Production lifecycle hardening, a bundled startup page before server readiness, server recovery, and microphone capture remain unimplemented. The logical HTML viewport stays 1024 by 768 while the final window composition scales on resize. Physical-resolution recording is below its 60 distinct FPS target, and the measured audio/display latency remains.


## Final demonstration

`handoff/prototype-mvp/denice-orb.mp4` is the untrimmed native-input recording. It lasts 4.542104 seconds and fully decodes. H.264 video is 2048 by 1450 pixels, starts at zero, and contains 271 encoded samples over 4.516667 seconds. AAC is 48 kHz stereo, starts at zero, and lasts 4.542104 seconds. Its audible page cue occurs approximately 2.145–2.361 seconds into the file and peaks at -33.48 dBFS. The page reports `Cue played` after native input. `denice-orb.png` is extracted from this video at three seconds.

The run rendered 82 frames and accepted 61 new captured images. The 271 encoded samples include holds; they are not 271 distinct captures. The native process exited zero. The earlier silent short take is retained under validation and is not this deliverable. Final Runtime tests passed after the last source changes, and the diagnostic endpoint is now disabled by default again.


## Windows/Linux portability preparation

This work is isolated in `../denice-windows-linux-prototype` on `prototype-mvp-windows-linux`. The starting tree copies the prototype baseline, including its uncommitted source, without changing the macOS worktree. Strict Debug builds targeting `linux-x64` and `win-x64` pass. These builds still contain unavailable desktop implementations; they are not graphics execution evidence.

The existing layout command is `eng/build-install-layout.sh <rid> <out>`. Its entry is `<out>/versions/<version>/denice-v<version>-<rid>[.exe]`, beside `Denice.Runtime.dll`. The existing execution command is `gui --duration <seconds> --record <new-file.mp4>`. This contract has been sent to the infrastructure agent. A later recording error previously printed an error while returning zero when desktop shutdown succeeded. The portability tree changes that final result to nonzero while retaining an already nonzero desktop result.

### Decisions needed for native implementation

The current frame callback carries one native handle. Pinned CEF Linux frames instead provide up to four DMA-BUF plane descriptors, each with file descriptor, stride, offset and size, plus a modifier. The pinned CefGlue Linux implementation exposes those values and throws from `SharedTexture`. Copying the macOS browser callback to Linux would therefore fail before rendering. The application needs an explicit Linux plane descriptor path; hiding a managed object or a file-descriptor list behind the existing surface pointer is not proposed.

Stock `cef.runtime.win-x64` and `cef.runtime.linux-x64` version `156.0.15` exist in NuGet, matching the current macOS CEF version. Stock `SkiaSharp.NativeAssets.Win32` and `SkiaSharp.NativeAssets.Linux` version `4.157.0` also exist. None has been added yet. Windows COM graphics/media binding and Linux encoding backend selection remain pending. These are missing application implementations, not GitHub account problems.

The reusable code is the Rust/CoreCLR host, Engine application server and page, CLI, Settings, desktop lifetime contract, and Skia drawing logic. Native window creation, accelerated frame import, presentation, and recording must be supplied per platform. The common browser lifecycle and drawing logic should have one authored source rather than copied implementations. Audio capture must follow the outcome of the active macOS browser-capture proof; the known lossy CefAudioHandler must not be presented as complete audio support on another platform.

Source evidence: pinned CEF `include/internal/cef_types_linux.h` at `cf671c072925b27823a61181152f4f288e8e4306`; pinned CefGlue `CefGlue/Platform/Linux/CefAcceleratedPaintInfoLinuxImpl.cs` at `7b75d2f3cb1da982bad84daceb7cdad8e72219d5`; native package version indexes at `api.nuget.org/v3-flatcontainer`.

### Recording-result regression validation

`dotnet test tests/Denice.Runtime.Tests/Denice.Runtime.Tests.csproj -c Debug -m:1 --filter FullyQualifiedName~DesktopCliTests -v:q` passed the success, recording-failure and existing-desktop-failure cases with analyzers enabled. The helper lives in `Denice.TestHelpers`, matching the enforced fake ownership. This validates the CLI result, not a native encoder.

## Proposed Windows/Linux frame and backend amendment — awaiting approval

The recommendation keeps CEF, Skia, the Rust/.NET host, and existing per-OS projects. It adds a bounded GPU-buffer descriptor to the existing frame callback. It does not add a compilation project or a custom Chromium build. The source below is a proposed contract, not compiled implementation.

```csharp
public readonly record struct BrowserGpuPlane(
    int FileDescriptor, uint Stride, ulong Offset, ulong Size);

public readonly ref struct BrowserGpuBuffer
{
    public BrowserGpuBuffer(nint sharedTexture)
    {
        if (sharedTexture == 0)
            throw new ArgumentException("A shared texture is required.", nameof(sharedTexture));
        SharedTexture = sharedTexture;
        Planes = default;
        Modifier = 0;
    }

    public BrowserGpuBuffer(ReadOnlySpan<BrowserGpuPlane> planes, ulong modifier)
    {
        if (planes.IsEmpty || planes.Length > 4)
            throw new ArgumentException("One to four planes are required.", nameof(planes));
        SharedTexture = 0;
        Planes = planes;
        Modifier = modifier;
    }

    public nint SharedTexture { get; }
    public ReadOnlySpan<BrowserGpuPlane> Planes { get; }
    public ulong Modifier { get; }
    public bool IsNativePixmap => !Planes.IsEmpty;
}

public interface IPlatformBrowserFrameSink
{
    void OnAcceleratedFrame(
        scoped in BrowserGpuBuffer buffer,
        int format, int width, int height,
        int visibleX, int visibleY, int visibleWidth, int visibleHeight,
        bool popup);

    void OnPopup(bool visible, int x, int y, int width, int height);
}
```

The new value types belong in the existing `Denice.Abstractions/Contracts` directory. The sole existing signature change replaces `nint surface` with `scoped in BrowserGpuBuffer buffer`; dimensions, CEF format, popup identity, and existing popup notifications keep their meaning. The existing `IPlatformDesktop`, `IPlatformBrowser`, and audio interfaces are unchanged by this amendment. A default buffer is invalid and the importer must reject it. Consumers validate descriptor bounds, supported CEF format, dimensions, and platform representation before native import.

macOS and Windows pass a shared native texture handle. Linux copies descriptor metadata into a stack-local span of at most four planes and passes the modifier. The descriptors and underlying native resource remain borrowed for the callback only. The consumer copies into its own GPU storage and completes the source GPU read before returning. It does not close borrowed descriptors or retain their numeric values for later use. The ref struct and scoped input prevent storing the descriptor span, while native resource lifetime still requires explicit completion. Unsupported format/modifier combinations fail visibly rather than selecting CPU pixel callbacks.

### Recommended Windows backend

Use stock `Vortice.Direct3D11` and `Vortice.MediaFoundation` 3.8.3 in `Denice.CrossPlatform.Windows`. Direct3D11 brings Vortice.DXGI; both bring the stock SharpGen.Runtime 2.4.2-beta dependency. Their published NuGet manifests contain net10.0 assets and MIT licenses, pinned to repository commit `9e609cb9439c9872aa1b339f177e40ec96f77239`. This uses vendor-generated COM bindings, not first-party vtables. The beta transitive version is disclosed rather than silently treated as a stable dependency.

`Windows.Desktop` owns the Win32 window, a D3D11 hardware device, ANGLE/EGL context, Skia GPU scene and presentation. `Windows.Browser` maps each CEF shared texture to the same adapter/device, opens it through the appropriate D3D11 shared-resource API and copies it to owned storage before returning. Skia renders through ANGLE onto owned D3D11-backed textures, preserving the existing scene instead of rewriting the orb for Direct2D. `Windows.Recording` uses Media Foundation with the same DXGI device manager, GPU conversion to the encoder's NV12 format, texture-backed samples, H.264/AAC and MP4 finalization. Win32 and EGL C entry points remain bounded native imports in the existing platform owner.

This is a supported-API design, not a completed compatibility proof. The first run must establish that the deployed ANGLE exports support D3D texture client buffers, its device matches the CEF frame adapter, Media Foundation is installed in the guest, and an actual hardware H.264 encoder accepts the texture format. No WARP or software-encoder fallback counts as a GPU proof. The CEF native package must be inspected after approval to establish whether its ANGLE DLLs can be reused as deployed; a missing required runtime is a reported packaging decision rather than an unapproved download.

Evidence: [Vortice source](https://github.com/amerkoleci/Vortice.Windows), [Media Foundation DXGI surface buffers](https://learn.microsoft.com/en-us/windows/win32/api/mfapi/nf-mfapi-mfcreatedxgisurfacebuffer), [Sink Writer DXGI device manager](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-sink-writer-d3d-manager), [ANGLE D3D texture import header](https://chromium.googlesource.com/angle/angle/+/master/include/EGL/eglext_angle.h). Microsoft lists the DXGI buffer/device-manager facilities from Windows 8 and Server 2012; actual guest media features and encoder availability still require tests.

### Recommended Linux backend

Use an X11 desktop for the first Linux x64 proof, EGL/OpenGL ES with the existing Skia renderer, and GStreamer 1.x in-process for encoding/muxing. `Linux.Desktop` owns the X11 window, EGL context and compositor. `Linux.Browser` imports all CEF DMA-BUF planes with `EGL_EXT_image_dma_buf_import` plus the modifier extension, maps the supported CEF color types to explicit DRM formats, and copies into an owned GL texture before returning. The EGL implementation must accept the actual format/modifier tuple reported by CEF; GPU enumeration is insufficient. This initially establishes X11 support, not Wayland or Linux ARM64 coverage.

`Linux.Recording` wraps owned GL textures in GstGLMemory using a shared GL context and feeds them to an in-process GStreamer appsrc. The GitHub NVIDIA T4 proof uses `nvh264enc` with negotiated GLMemory, followed by `h264parse` and `mp4mux`. Timestamped interleaved float PCM from the eventual approved browser-capture path uses a separate appsrc, `avenc_aac`, `aacparse` and the same muxer. Bounded queues, GPU fences, release callbacks, one media time origin, and end-of-stream draining retain the existing recording lifetime contract. No subprocess media pipeline or desktop-wide audio loopback is substituted.

This adds GStreamer core/app/video/GL libraries, the nvcodec plugin, H.264/AAC parsers, libav AAC encoder and isomp4 muxer to the Linux runtime requirements. The exact distribution package revisions must be pinned against the selected runner image after its package inventory; the image does not currently have a proven plugin set. This proposal chooses the backend family and element set, not an invented package version. C# calls the published C API within the existing Linux project; this requires bounded analyzer recognition of those native entry points, not disabling analyzers or adding a C/C++ shim. The first execution must check GL context sharing, GLMemory negotiation and actual NVENC encoding. A GStreamer pipeline that downloads frames or silently selects a CPU encoder does not establish this path. A self-hosted Linux GPU without NVENC needs a separately verified hardware encoder element; the T4 proof does not establish all GPU vendors.

Evidence: [EGL DMA-BUF import](https://registry.khronos.org/EGL/extensions/EXT/EGL_EXT_image_dma_buf_import.txt), [modifier and fourth-plane support](https://registry.khronos.org/EGL/extensions/EXT/EGL_EXT_image_dma_buf_import_modifiers.txt), [GstGLMemory texture wrapping](https://gstreamer.freedesktop.org/documentation/gl/gstglmemory.html), [GstGLContext](https://gstreamer.freedesktop.org/documentation/gl/gstglcontext.html), [nvh264enc GLMemory input](https://gstreamer.freedesktop.org/documentation/nvcodec/nvh264enc.html), [AAC input format](https://gstreamer.freedesktop.org/documentation/libav/avenc_aac.html), [MP4 audio/video muxing](https://gstreamer.freedesktop.org/documentation/isomp4/mp4mux.html).

### Execution order after approval

First package and load the stock platform binaries. Next prove the native window, hardware context and one actual CEF accelerated frame import. Then reuse the existing orb/transparent composition and prove texture-backed video encoding. Finally attach the approved browser-audio stream and decode the recorded audio across silence. Each step reports actual adapter/renderer/encoder identity, callback/import counts and distinct frame counts through existing run logs. The CI test retains its NOT_VERIFIED labels until corresponding evidence exists. Audio architecture remains pending the existing macOS capture experiment; this amendment does not approve a substitute capture route. Configuration exposed to users belongs in Settings, and GStreamer context selection is performed through APIs rather than requiring environment variables.

## Six-platform native dependency audit — package changes withheld

The repository's supported packaging RIDs are `osx-x64`, `osx-arm64`, `win-x64`, `win-arm64`, `linux-x64`, and `linux-arm64`, explicitly enumerated by `eng/build-install-layout.sh`. That script maps them to x86_64/aarch64 Rust targets and publishes the corresponding self-contained .NET Runtime. Existing CI builds both architectures on each OS. Its ordinary test coverage is native macOS ARM64 plus macOS x64 under Rosetta, native Windows x64 with Windows ARM64 build-only, and native Linux ARM64 with Linux x64 build-only in the main build leg. Release verification has separate Linux x64/ARM64 jobs. Those are CLI/install/update tests, not proof of GPU desktop support.

All six CEF runtime packages exist at exactly `156.0.15`. This was established from NuGet flat-container indexes and archive contents, not inferred from names. HTTP byte-range reads of each archive's native payload also established the actual CPU type.

| Supported RID | CEF package 156.0.15 and native CPU | Skia package 4.157.0 asset | Current prototype consequence |
|---|---|---|---|
| osx-x64 | cef.runtime.osx-x64; Mach-O CPU 0x01000007 | NativeAssets.macOS; universal dylib x86_64 slice | Current macOS implementation targets this runtime. |
| osx-arm64 | cef.runtime.osx-arm64; Mach-O CPU 0x0100000c | NativeAssets.macOS; universal dylib arm64 slice | Mac project currently hardcodes x64 CEF; native ARM64 packaging must select this runtime. |
| win-x64 | cef.runtime.win-x64; PE machine 0x8664 | NativeAssets.Win32; runtimes/win-x64/native/libSkiaSharp.dll | Desktop implementation and native backend remain missing. |
| win-arm64 | cef.runtime.win-arm64; PE machine 0xaa64 | NativeAssets.Win32; runtimes/win-arm64/native/libSkiaSharp.dll | Same backend must use native ARM64 dependencies and receive ARM64 execution coverage. |
| linux-x64 | cef.runtime.linux-x64; ELF machine 62 | NativeAssets.Linux; runtimes/linux-x64/native/libSkiaSharp.so | Desktop, DMA-BUF import and encoder implementation remain missing. |
| linux-arm64 | cef.runtime.linux-arm64; ELF machine 183 | NativeAssets.Linux; runtimes/linux-arm64/native/libSkiaSharp.so | Same source can target ARM64, but native graphics/media plugins and actual GPU path need ARM64 validation. |

The macOS Skia dylib was inspected with both `file` and `lipo -archs`; it contains x86_64 and arm64. Its linked dependencies shown by `otool -L` are Apple system frameworks and system libraries. A Rosetta x64 process on Apple Silicon is not a native ARM64 result; ARM64 Rust entry, CoreCLR, CEF and Skia must all align in one process.

### Missing closure beyond CEF and Skia

The proposed Windows ANGLE reuse was contradicted by the pinned archives: neither Windows x64 nor Windows ARM64 CEF package includes `libEGL.dll` or `libGLESv2.dll`. Both contain `libcef.dll`, `chrome_elf.dll`, `d3dcompiler_47.dll`, `dxcompiler.dll`, Vulkan/SwiftShader files, locale/resources and snapshots. The proposed Skia-on-ANGLE bridge therefore needs a separate compatible native ANGLE distribution for both Windows architectures, or a revised graphics bridge. No such extra runtime is approved, installed or claimed available by this audit.

The Vortice recommendation adds managed Direct3D11/Media Foundation bindings and transitive SharpGen.Runtime, DXGI and DirectX packages. It does not supply an ANGLE runtime, a Windows GPU driver, Media Foundation features, or a hardware encoder. Those remain native guest/OS requirements. The full PE import dependency closure for the vendor DLLs has not yet been established.

The Linux proposal adds EGL/GLES/X11 driver libraries plus GStreamer core/app/video/GL, H.264 encoder/parser, AAC encoder/parser and MP4 muxer libraries/plugins for each architecture. A particular distribution's plugin revisions and native dependency closure have not been pinned or tested. The NVIDIA `nvh264enc` proposal targets the GitHub T4 experiment; it is not a universal Linux ARM64 or all-GPU-vendor solution. A self-hosted ARM64 GPU requires a supported encoder and verified texture-sharing path for that specific driver. `libcef.so` also has system library and glibc prerequisites; the Rust loader's glibc 2.17 target does not establish CEF's minimum glibc compatibility.

CEF's macOS archives include the framework, sandbox/Vulkan/SwiftShader libraries and cefclient helper app templates. Linux archives include `chrome-sandbox`, `libcef.so`, `libvulkan.so.1`, SwiftShader and resources. These auxiliary native files are part of the package inventory, not evidence that DENICE's eventual process model or sandbox deployment is complete. No sandbox or host security settings were changed.

Result: CEF and Skia have native assets for all six supported RIDs at aligned pinned versions. The entire desktop GPU/media dependency set is not yet aligned or complete. Package additions remain withheld. x64 runner experiments can establish x64 behavior only; their results must not be reported as ARM64 support.

Primary package evidence uses `https://api.nuget.org/v3-flatcontainer/<package>/index.json` and the exact `156.0.15` or `4.157.0` package archive. The CEF payload header audit retrieved only byte ranges rather than downloading or installing the six archives.

The Windows/Linux Skia payload headers were also inspected through archive byte ranges: Win32 package x64 reports PE 0x8664 and ARM64 reports 0xaa64; Linux x64 reports ELF machine 62 and ARM64 reports 183. Thus these are native architecture payloads, not architecture labels alone.

NuGet transitive metadata: `CefGlue.Next.Core` 156.8078.80 has an empty net10.0 dependency group and does not select a CEF native runtime automatically. `SkiaSharp` 4.157.0's net10.0 group already references `SkiaSharp.NativeAssets.macOS` and `SkiaSharp.NativeAssets.Win32` 4.157.0. A redundant explicit Windows NativeAssets reference is therefore not necessary just to obtain those assets. Linux NativeAssets must be selected explicitly. NativeAssets.Linux itself declares no further net10.0 NuGet dependencies; that does not eliminate its ELF/system-library prerequisites.


## Existing release-chain integration audit — review only

This proposal uses the current native platform assemblies and release machinery. No package references, graphics implementation, loader or deployment format were changed by this audit.

### Ownership and build chain

1. `Denice.Runtime.csproj` already passes `DenicePlatformRid=$(RuntimeIdentifier)` to its Engine project reference. `PlatformImplementation.targets` selects exactly one Windows/Linux/macOS implementation using that surviving global property. Native asset selection must use the same target RID; selecting by the build host would break cross-published ARM64 packages.
2. Each existing per-OS csproj owns its stock native dependencies and build/stage targets. The macOS csproj already demonstrates `GeneratePathProperty`, recursive `Content`, `Link`, and `CopyToPublishDirectory=PreserveNewest`. Replace its hardcoded x64 CEF selection with RID selection. Apply the same mechanism to the Windows and Linux CEF assets, resources, locale files, architecture-specific graphics libraries and media plugins.
3. Shared authored CEF lifecycle/Skia scene code can use the existing linked `Compile Include` convention demonstrated by Linux's POSIX implementation. Keep native import, window, device, texture synchronization and encoder code in their current OS assembly. This does not require another managed project, first-party language or runtime loader.
4. Dependency source builds, where needed for Linux, run as a prerequisite in the existing build scripts/MSBuild chain and stage their pinned outputs before `dotnet publish`. Their cache key includes target RID, upstream version, source checksum, toolchain and enabled plugin set. Stage only runtime libraries/plugins and notices, not the build SDK, headers or arbitrary host libraries. These outputs enter the same `versions/<version>` tree as the self-contained .NET runtime and Rust entry.
5. `eng/build-install-layout.sh` continues to produce the existing root loader and versioned Rust entry. `eng/ci-stage-layout.sh` invokes the existing recursive SHA256SUMS writer, cosigns that manifest and archives the layout. Native dependencies therefore receive the existing update integrity coverage automatically; no parallel package/update protocol is needed.
6. macOS nested CEF Mach-O files must have valid signatures before the enclosing framework/app and manifest are finalized. Existing verification checks every Mach-O, but the current CI signing step explicitly signs the loader app and Rust entry, not a newly staged CEF framework. Extend that existing signing owner rather than adding another signing system. The real signed install layout must run the GUI; a separately assembled prototype `.app` is not release-layout proof.
7. Windows vendor DLLs are covered by the version manifest; the existing Authenticode step signs DENICE-owned executables/assemblies. Retain upstream notices and inspect vendor dependency imports. Linux must inspect all staged ELF files recursively. The current `check-glibc.sh` uses top-level `lib*.so` as the .NET baseline: adding vendor libraries there could alter that baseline, and nested plugins are skipped. Preserve the actual .NET baseline and separately validate the entire native payload against the declared desktop OS floor.

### Windows bridge now identified for both architectures

`Avalonia.Angle.Windows.Natives` **2.1.27548.20260419** is a standalone native package with an empty managed dependency group. It does not add the Avalonia UI framework. Its actual `av_libglesv2.dll` payloads were inspected: Windows x64 is PE 0x8664; Windows ARM64 is PE 0xaa64. Both carry EGL implementation symbols and the D3D texture client-buffer extension. Direct inspection of the x64 export table confirms the stock ABI uses `EGL_GetProcAddress`, `EGL_Initialize`, `EGL_CreatePbufferFromClientBuffer`, `EGL_CreateDeviceANGLE` and `EGL_GetPlatformDisplayEXT`; the imports must bind those exact names instead of the ordinary lowercase EGL API names. Skia receives the resulting GL function resolver. The direct DLL import tables on both architectures list USER32, KERNEL32, DXGI and the Windows synchronization API set; dynamic driver/shader loading still requires a runtime check. Both PE headers declare Windows 10 OS/subsystem fields. Those header fields are a binary minimum, not an exhaustive supported-OS guarantee. Its combined EGL/GLES library is usable as a stock bridge candidate for the existing Skia renderer, alongside the proposed Vortice bindings. This replaces the disproved assumption that CEF itself ships public EGL/GLES DLLs. Runtime import, adapter matching and hardware operation still require a real Windows run.

The package's LICENSE contains the ANGLE redistribution terms and must accompany the distributed binaries. Package provenance identifies AvaloniaUI/angle commit `1c89805903c1482166356d3b950d474973180e61`. The exact pinned archive is [NuGet native package](https://api.nuget.org/v3-flatcontainer/avalonia.angle.windows.natives/2.1.27548.20260419/avalonia.angle.windows.natives.2.1.27548.20260419.nupkg). Vortice remains a separate managed binding dependency; Direct3D, DXGI and Media Foundation are Windows facilities, while hardware drivers and a usable encoder are machine prerequisites. Do not bundle a GPU driver or substitute WARP and report hardware success.

### Linux distributable media build and device selection

Recommend a **GStreamer 1.28.9** pinned stock-source build for Linux x64 and ARM64 through its existing upstream Cerbero build aggregator. The upstream Linux configuration explicitly handles x86_64 and aarch64. This is a dependency build inside DENICE's existing build chain, not another first-party language or project. The official download page supplies Linux distribution packages rather than a universal Linux SDK archive; a controlled source build avoids making the shipped application depend on whatever optional plugin set a user happened to install.

Bundle the selected GStreamer core/app/video/GL libraries and their non-driver dependencies, app/opengl/video conversion, H.264 parser, AAC parser/encoder and MP4 muxer plugins. Include the nvcodec and VA plugins for capability selection. Do not bundle all upstream SDK plugins: the stock bad-plugin recipe enables GPL options and unrelated plugins by default. Pin the selected plugin graph and build flags, disable unrelated/GPL components, and retain the applicable source/license notices. The upstream FFmpeg recipe used for libav identifies FFmpeg 7.1 with LGPLv2.1-or-later; this does not license every optional codec configuration automatically.

Keep the system's kernel/display/GPU drivers, EGL vendor implementation, X11 session, VA driver or NVIDIA CUDA/NVENC driver libraries as machine prerequisites. A shipped media library cannot manufacture missing GPU passthrough, a graphical session, encode hardware or device permission. Application plugin discovery and context selection should use explicit paths/APIs; users must not edit environment files.

Select a recording path by the actual GPU and negotiated memory capabilities, not by `x64` versus `arm64` or a T4 name. NVIDIA can use `nvh264enc` with GLMemory. A VA device can use `vah264enc` with VAMemory/NV12, requiring a separately proven GPU transfer/conversion path from the compositor. `gldownload` advertises DMA-BUF output, but that alone does not prove compatible modifiers or a zero-copy VA handoff. V4L2/SoC encoding requires device-specific discovery and is not certified by having an ARM64 build. If no supported GPU path is available, return a clear capability failure; CPU fallback must not masquerade as the hardware proof. This yields one capability-driven implementation with platform-native dependencies, not a universal encoder that all hardware is guaranteed to expose.

Primary evidence: [Cerbero distribution builds](https://gstreamer.freedesktop.org/documentation/installing/building-from-source-using-cerbero.html), [pinned Linux architecture configuration](https://github.com/GStreamer/cerbero/blob/1.28.9/config/linux.config), [pinned plugin recipe](https://github.com/GStreamer/cerbero/blob/1.28.9/recipes/gst-plugins-bad-1.0.recipe), [pinned FFmpeg recipe](https://github.com/GStreamer/cerbero/blob/1.28.9/recipes/ffmpeg.recipe), [NVENC inputs](https://gstreamer.freedesktop.org/documentation/nvcodec/nvh264enc.html), [VA encoder inputs](https://gstreamer.freedesktop.org/documentation/va/vah264enc.html), [GL-to-DMA-BUF advertised caps](https://gstreamer.freedesktop.org/documentation/opengl/gldownload.html).

### Remaining decisions versus execution checks

The reviewable decisions are the frame-contract amendment above, Windows stock Vortice plus ANGLE dependencies, and Linux stock GStreamer/plugin build plus its supported desktop/system prerequisites. No new loader, project or deployment framework is proposed. Package approval remains withheld. Browser audio capture is still its own pending experimental result and is not solved by selecting a muxer.

After those decisions, dependency resolution, native payload staging, source sharing, per-OS implementation, signing and ordinary CI build checks are implementation work within the approved design. Hardware testing must separately establish all six native targets, actual imported CEF frames, selected hardware encoder, continuous recorded audio and shutdown/recovery. Native asset availability and successful compilation alone do not establish those outcomes.


Direct ELF audit of both pinned `cef.runtime.linux-x64` and `cef.runtime.linux-arm64` 156.0.15 archives found a maximum required symbol version of **GLIBC_2.25** in `libcef.so`'s `.gnu.version_r` section. Its DT_NEEDED entries include GLib/GObject/GIO, NSS/NSPR, ATK/AT-SPI, DBus, CUPS, X11/Xcomposite/Xdamage/Xext/Xfixes/Xrandr/XCB, GBM, xkbcommon, expat, Cairo/Pango, udev, ALSA and core system libraries. This is a lower bound for that binary, not the minimum for the complete DENICE release or all its system libraries. In particular, it does not make a glibc 2.17 machine a supported desktop target merely because the Rust entry targets 2.17.

### Linux media dependency prerequisite implementation

`eng/build-linux-media.sh` now prepares a selected stock GStreamer 1.28.9 runtime for native `linux-x64` and `linux-arm64` builders. Its interface is `bash eng/build-linux-media.sh --rid <rid> --output <prefix> [--work-dir <cache>] [--toolchain-notices <directory>]`. The existing Linux platform publish owner must recursively copy the resulting prefix to `media/`; libraries are in `media/lib/` and plugins in `media/lib/gstreamer-1.0/`. The script does not install host packages, change a daemon, or write user environment configuration. It can install build-only packages inside its disposable container.

The build pins the Cerbero 1.28.9 archive by SHA256 and uses its unchanged GLib, ORC, zlib and bzip2 dependency recipes, including their transitive recipes and checksums. Cerbero extracts its pinned FFmpeg 7.1 source and stock Meson port patch. Shell commands invoke Meson directly with AAC encoding selected and GPL/nonfree/version3 disabled. GStreamer core/base/good/bad/libav tarballs, libva 2.22.0 and libdrm 2.4.124 have explicit SHA256 pins. The script contains shell build/staging logic and declarative upstream `.cbc` assignments; it introduces no authored Python program, recipe subclass or application project.

The staged plugin list contains coreelements, app, opengl, videoconvertscale, audioconvert, audioresample, volume, typefindfunctions, audioparsers, isomp4, videoparsersbad, codectimestamper, nvcodec, va and libav. FFmpeg's AAC encoder is selected explicitly. Plugin and API-library roots drive a recursive DT_NEEDED walk. All non-driver dependencies must resolve from the built prefix or compiler runtime; unresolved dependencies stop publication. The script copies ELF payloads by their needed names, assigns `$ORIGIN` runpaths, checks the target ELF machine and every required GLIBC symbol against 2.27, and runs loader inspection without the build-prefix search path. GCC runtime notices are discovered from Debian/RPM compiler-package locations, with an explicit override for a separately provisioned toolchain. License notices, pinned source archives, Cerbero runtime recipes and patches accompany the output. A completed output has dependency and source inventories, a build manifest and checksums; incomplete output is not published.

Libva and libdrm here are the upstream userspace dispatch libraries. Kernel/display/vendor GPU drivers, the EGL/GLES implementation, X11/XCB session libraries, VA DRI drivers and NVIDIA CUDA/NVENC facilities remain host prerequisites. Libva searches the conventional multiarch, `/usr/lib64/dri` and `/usr/lib/dri` driver directories. A site using a different vendor layout still needs a supported device configuration through Settings. The application must load media libraries by the published paths and use `gst_registry_fork_set_enabled(FALSE)` plus `gst_registry_scan_path` for the published plugin directory. The build performs process-local `gst-inspect` smoke checks for appsrc/appsink, GL upload/conversion/download, H.264/AAC parsers, `avenc_aac` and `mp4mux`; these commands do not establish a usable NVIDIA/VA encoder or GPU-memory interoperability.

A newer GitHub runner can host a matching-architecture container while keeping the media ABI floor below 2.27. Registry manifests retrieved during this implementation pin `quay.io/pypa/manylinux2014_x86_64@sha256:8a79ff36ce155d8f242dc8013d73c5e8bbe937defb15de7d7afb342c1ab537f4` and `quay.io/pypa/manylinux2014_aarch64@sha256:382236bfd11d86894d9792a6fa5286190b8dd3d075a7032284e123c288711e4f`. The script automatically enters the matching pinned glibc 2.17 image when the Linux host has a newer glibc. It installs EGL/GLES/X11/XCB development packages and command-line prerequisites inside that disposable container, selects its Python 3.11 installation, and installs `distro` 1.9.0 using a pinned wheel hash. It mounts the repository read-only and writes only to the selected cache/output mounts, running the source build as the caller's uid. The container path has not executed here. RPM package versions are included in the cache key and output inventory; the resolved package installation is not a separately digest-pinned derived image, so byte-for-byte build reproducibility remains unestablished. Libdrm is built from source because the base distribution's headers can predate GStreamer's required 2.4.98. The resulting prefix enters the existing Linux publish step. Using the x64 image on an ARM runner or emulation does not establish native ARM64 runtime support.

Available-host checks: `bash -n` passes; every explicitly selected core/base/good/bad/libav Meson option name matches the pinned upstream source; the downloaded Cerbero/libva/libdrm hashes match the pins; invocation on this Intel Mac stops with `Build linux-x64 on matching native Linux hardware.` No Linux compilation, ELF payload audit, loader smoke check, six-RID publish or hardware execution has run here. The Docker daemon remains unavailable and was not started. The script's checks are execution gates, not claims that their eventual results already passed.

## Dependency integration implementation — current authorization

Dependency selection and incorporation into the existing platform assemblies and build chain are now authorized. This supersedes the earlier package hold in the audit history above. The work continues across all six supported RIDs; native package availability is not desktop runtime certification.

`eng/desktop-native.targets` is the single dependency/content owner imported by each existing OS project. Central package versions now include CEF 156.0.15 for all six RIDs, Skia 4.157.0 with Linux NativeAssets explicitly selected, Vortice.Direct3D11/MediaFoundation 3.8.3 and Avalonia.Angle.Windows.Natives 2.1.27548.20260419. Existing transitive Skia Windows/macOS packages are reused. CEF resources, CEF/ANGLE licenses and Skia native third-party notices use the existing Content/CopyToPublish mechanism.

The first ARM64 publish exposed NETSDK1152 because the SDK creates a flattened Runtime-to-platform project reference after evaluation. That generated reference did not inherit Engine's AdditionalProperties and selected host-x64 content alongside ARM64 content. `DeniceThreadPlatformRid` in the existing Directory.Build.targets now runs after IncludeTransitiveProjectReferences and passes the target RID through those generated references. Duplicate-file detection remains enabled. Windows ARM64 publish then exited successfully, followed by renewed Windows x64 and both macOS RID publishes with matching native payload architectures. Linux x64 publish also completed with matching CEF/Skia architecture; Linux ARM64 is still being validated.

The existing build-install-layout script now invokes `eng/check-desktop-native.sh` against its published folder. This checks native file headers, auxiliary CEF libraries, browser resources and license files. A negative check using an actual x64 CEF binary in an ARM64 fixture returned the expected architecture failure. Hardware rendering, capture and encoding are deliberately separate execution checks.

The glibc gate now reads the .NET native runtime-pack file list from Denice.Runtime.deps.json instead of treating every top-level lib*.so as .NET. It additionally scans nested ELF libraries, so new browser/media plugins cannot silently raise the baseline. Inspection of the published Linux x64 runtime found .NET's maximum required symbol version is 2.27; its CEF plus Skia requirement also tops out at 2.27. The full Rust-entry install-layout check remains to be run.

The pinned CEF macOS binary declares macOS 13.0 in LC_BUILD_VERSION. Packaged libcoreclr declares 12.0. This mismatch already existed in the prototype's selected CEF version; the desktop cannot be described as macOS-12-compatible. The native CLI runtime and full desktop have different effective dependency floors until their support requirements are reconciled.

Windows graphics implementation has begun inside Denice.CrossPlatform.Windows.Desktop. `AngleNative` binds the stock EGL_* exported names. `WindowsGpuContext` owns a hardware D3D11 device, imports that device into ANGLE, and creates the Skia GLES context with deterministic reverse-order disposal. It has no WARP fallback. This foundation still needs compilation validation, window/presentation wiring and actual CEF shared-texture import. It is not a completed desktop implementation or a hardware test result.

The media prerequisite now supports `--verify-output` before any native-host, compiler, glibc or Docker check. This lets the existing ARM64 Linux release runner consume an x64 media artifact built on an ordinary native x64 dependency runner. The verifier requires the requested RID, the current script's SHA256 in `build_script_sha256`, the required API libraries/plugins and notices, and exact agreement between the file inventory and `SHA256SUMS`. It rejects missing, extra, duplicate, modified or unsafe-path records and symlinks/special files. Default invocation also verifies and reuses an existing output before checking the build host; a missing output still requires matching native Linux compilation. The same plugin/API root arrays drive staging and verification. Freshly staged output passes this verifier before its final move.

Synthetic artifacts on the Mac exercised both `linux-x64` and `linux-arm64` verification and default cache reuse. Wrong RID, modified payload, unlisted file, missing required plugin despite regenerated hashes, stale script hash, traversal record, duplicate checksum record and symlink cases were rejected. These fixtures test artifact-verification behavior only; they do not constitute Linux ELF, compilation or hardware evidence. The existing trusted CI artifact/signature chain remains responsible for artifact provenance; a checksum file alone is not a publisher signature.

The Linux ARM64 publish completed successfully. All six direct self-contained publishes now contain the target CEF and Skia native architecture. Windows x64 and ARM64 also republished with the new D3D11/ANGLE/Skia context code and passed the native payload checks. The native interop boundary analyzer tests passed, including the Windows EGL export allowance and rejection in other platform assemblies or filesystem namespaces. These direct Linux publish folders omit the optional media prefix; they do not establish GStreamer build or runtime success.

Ordinary CI now builds Linux media on native ubuntu-24.04 x64 and ubuntu-24.04-arm workers, uploads each checked media prefix, and passes those artifacts to the existing release-layout job. The existing ARM64 release worker can package both RIDs without running the x64 media binaries. No workflow has been dispatched or pushed from this worktree. Container execution and real media build remain to be tested on Linux.

The shared orb scene has been extracted into `OrbScene.cs` and compiled into each platform assembly through the existing linked-source convention. The macOS agent identified the scene as stable; physical-size surfaces, aspect-fit, Metal presentation and encoder copying remain platform-owned. The source algorithms are unchanged, and the sprite remains GPU-backed on the borrowed owning context. The macOS agent's experimental browser/audio code was not copied into a shared path.

The existing macOS CI signing step now signs CEF's nested Mach-O libraries and seals its framework before manifests are generated. It preserves the existing runtime entry entitlements and .NET/Skia vendor signatures. Applying the same inside-out order with an ad-hoc identity to an isolated ARM64 framework copy passed `codesign --verify --deep --strict`. Developer-ID signing, notarization and hardened desktop launch have not run here. The renewed macOS x64 publish contains the Skia notices, and both macOS native payload checks pass.

The shared scene builds passed on Windows ARM64, macOS x64 and Linux ARM64 with the normal analyzer gate. A self-contained Linux ARM64 publish using an explicitly synthetic media fixture staged its complete directory unchanged under `media/`; recursive diff matched the fixture. This establishes the existing Content/CopyToPublish path, not native Linux media behavior. The remaining backend work is Windows/Linux window and input ownership, CEF GPU import into owned surfaces, presentation, hardware encoder selection and audio integration. The Linux multi-plane frame-contract amendment remains recorded separately; no opaque-pointer encoding of plane metadata was introduced.

The complete native-layout checker now requires the Linux media prefix, reuses the media builder's artifact verifier for the plugin/library/notice inventory, and checks each staged media shared object's architecture. Earlier direct Linux publish checks established CEF/Skia only; those folders intentionally omit media and no longer pass the complete-layout gate. Manual smoke checkout and selected CI artifact must correspond to the same media-builder source hash. Native Linux compilation remains required before this full gate can pass with actual media binaries.

### Linux source-build audit before the first CI execution

Inspection of the scripts extracted from the pinned manylinux image confirmed that its `fixup-mirrors` redirects CentOS 7 and ARM64 altarch repositories to the CentOS vault. The media container now invokes that upstream helper and restricts graphics/build-package installation to `base,updates`; it does not depend on an unrelated EPEL mirror. ARM64's image setup does not install EPEL, so `jq` is now fetched as the architecture-specific upstream 1.8.1 static binary with a SHA256 taken from the upstream release metadata. The tagged jq workflow builds those binaries with `--enable-all-static`. This avoids relying on an unavailable CentOS base `jq` package or on the container's old glibc for a dynamically linked host tool. The container also installs `flex`, `bison` and gettext development tools; GStreamer's parser source explicitly requires flex.

The bad-plugin Meson invocation now explicitly enables `gl`. Its previous `auto_features=disabled` selection otherwise disabled the `gstreamer-gl` dependencies used to compile nvcodec's GL integration. The host-graphics preflight now checks `x11-xcb` and `xcb-dri3`; libva's X11 source links both, and `libxcb-dri3.so.0` is included in the X11 host-library boundary. Direct Meson builds explicitly select the same GCC/G++ executables whose identity the build cache records. A file lock serializes shared-cache source downloads, extraction and builds. The FFmpeg corresponding-source archive excludes its generated Git object database while retaining the patched source and separate upstream patch files.

Source inspection confirms that Cerbero's FFmpeg extraction applies its pinned Meson patches, and that the patched Meson configuration selects an explicitly enabled `aac_encoder` even when the encoder group defaults are disabled. The gst-libav configuration requires libavcodec, libavfilter, libavformat and libavutil; those library options remain enabled in the pinned FFmpeg port. NVIDIA/VA hardware success is still not inferred from these options.

Audit validation consists of shell syntax, selected Meson option names against the pinned core/base/good/bad/libav sources, pinned image script/config inspection, CentOS x64/ARM64 vault metadata endpoint responses, and jq release metadata/workflow inspection. A local attempt to download the GitHub release checksum asset encountered `Could not resolve host: release-assets.githubusercontent.com`; the binary download itself has not been validated on this host. The script retains checksum verification at execution. The first native CI run must still execute container package installation, upstream downloads/bootstrap, compilation, complete ELF closure/glibc audits and plugin smoke tests. Any unexpected optional host dependency discovered by the ELF walk stops publication rather than being copied without provenance. RPM resolution is recorded, but no byte-for-byte reproducibility or hardware result is claimed.

The GPU-frame interface amendment was approved as documented. BrowserGpuPlane and BrowserGpuBuffer now live in the existing Contracts directory, and IPlatformBrowserFrameSink replaces its native surface parameter with scoped in BrowserGpuBuffer. The existing rectangle/color arguments remain unchanged. The macOS callback wraps its borrowed IOSurface and the desktop rejects invalid/pixmap input. The pinned CefGlue assembly reflection confirms exact plane widths: int descriptor, uint stride, ulong offset and size, ulong modifier. No source descriptor ownership is transferred.

Windows window/context code now builds on x64 with the analyzer gate: Win32 window/message pumping, initial mouse forwarding, checked per-monitor DPI awareness before window creation, ANGLE window surface creation and Skia framebuffer wrapping/presentation. Logical dimensions now belong to shared OrbScene. Remaining input acceptance includes pointer leave/capture and wheel handling. Window/display/recording execution has not run on Windows.

WindowsSharedFrame remains disconnected from CEF. Its factory cleans up partial COM acquisitions and owns a separate immediate-context reference. It reopens each NT handle with OpenSharedResource1, validates layout and copies into owned GPU storage. Flush only submits work; an EVENT query completing with S_OK is the normal release condition. The arbitrary timeout exception was removed: a still-live device with unresolved work must not return the borrowed source to CEF. Confirmed removed/reset-device errors take a separate invalid-resource teardown path. Handling a live-device query failure without indefinite wait is still unresolved and must be settled before wiring. No fail-fast policy has been adopted. References: Microsoft ID3D11DeviceContext::GetData and ::Flush, handling-device-lost-scenarios, and DXGKDDI_RESETFROMTIMEOUT.

A Windows-only opt-in hardware test now uses the existing Denice.CrossPlatform.Tests project with `-p:DeniceDesktopGpuTests=true --filter Category=GpuHardware`. It creates the native window and D3D11/ANGLE/Skia context, draws and presents two GPU frames, reads pixels solely to assert distinct output, reports the selected adapter and closes the window. It is excluded from ordinary tests and does not claim browser import, audio or encoder coverage. Its x64 cross-build passed; hardware execution remains pending. Window pointer leave/capture, wheel translation and monitor-DPI rectangle handling were added after that initial compile and are undergoing ARM64 build validation.

The updated Windows ARM64 hardware-test cross-build passed with zero warnings/errors, including window input/DPI additions and the approved frame descriptor contracts. This remains compile-only evidence.

Native interop review found that the desktop analyzer allowed Apple framework and Objective-C imports from Linux or Windows capability namespaces. The Apple library allowlist now applies only to the macOS assembly; the Windows allowlist remains scoped to Windows. Regression tests first reproduced the cross-OS acceptance, then the complete OsDivergentFilesystemOnlyInCrossPlatformAnalyzerTests class passed after the correction. Linux native entry points must receive their own bounded allowlist as their implementation is introduced. This check concerns platform ownership, not hardware execution.

### Desktop integration review during platform implementation

The shared CEF renderer catches callback exceptions and records them for the application loop; an additional Windows callback catch is unnecessary. Review of WindowsDesktop.Run found that using declarations would release window and GPU resources when CloseBrowser throws, despite outstanding browser callbacks. The Windows owner is correcting ownership and preserving bounded failure reporting. Shared CEF initialization also needs rollback coverage for failures after Initialize and before CreateBrowserSync. These are review findings, not completed fixes.

WindowsSharedFrame currently retries every unresolved GetData status, including a failed query on a live device. It does not return the borrowed texture early, but can hang indefinitely. Desktop execution must remain gated pending the failure-policy decision; compilation does not establish runtime readiness.

The Linux hardware test compares readback of two composed framebuffers and calls EGL presentation. Its assertions do not cover CEF DMA-BUF import, displayed scanout, audio, or encoding. The managed DMA-BUF tests cover attribute construction and boundary validation only. Hardware acceptance requires execution of the actual application on Linux with its media dependencies.

### Linux desktop implementation checkpoint

The Linux desktop factory now constructs the X11/EGL/GLES desktop and shared CEF browser lifecycle. Native graphics creation finishes before CEF initialization, so the temporary Xlib startup error handler is restored before Chromium can use Xlib. Window events are filtered to the owned window, including the distinct ConfigureNotify/DestroyNotify window field. Closing a browser must complete before GPU resources are released; a close timeout is an explicit failure and retains callback resources. This is not a recovery-success claim.

The DMA-BUF importer passes every plane and both modifier words, validates the public CEF field widths, and copies imported images into owned Skia GPU surfaces. Deferred Skia reads are submitted in cleanup before GL completion; a failed submission abandons deferred context work while still waiting for already-submitted GL work. Imported texture wrappers and EGL images are released only after that completion path. Borrowed descriptors are never closed. Plane-size validation does not assume that CEF's plane byte size includes its offset in a shared allocation.

Strict linux-x64 and linux-arm64 hardware-test cross-builds passed. The ARM64 build completed with zero warnings and errors after desktop startup-failure handling and renderer reporting were added. Pure managed DMA-BUF boundary tests passed on the macOS host against the linux-x64 target; this exercises attribute construction, not native import. The platform-native analyzer tests passed with Linux-specific allowed entry points and cross-platform rejection cases. The opt-in GPU test composes and reads back distinct Skia frames and presents them through EGL, but has not executed on Linux.

No Linux window, CEF DMA-BUF import, GPU encoder, audio recording, or displayed-frame-rate result is claimed. The desktop currently rejects recording requests explicitly while the approved GStreamer texture pipeline and browser-audio integration are unfinished. The existing pinned media-prefix builder remains the single source of packaged GStreamer dependencies.

### Linux plugin bootstrap proposal

GStreamer default discovery may load host plugins on a cold registry even with --gst-disable-registry-update. The Linux implementation will verify selected factory provenance, but that alone does not prevent initial host discovery. GLib documents environment writes as safe only before other threads exist; managed startup already has CoreCLR threads.

Proposed startup change: in the existing Linux Rust entry, before loading hostfxr, set only the current process GST_PLUGIN_SYSTEM_PATH_1_0 to an empty value and GST_PLUGIN_PATH_1_0 to the version directory media/lib/gstreamer-1.0. These values are derived from the installed layout, require no user environment files, and do not modify the shell or system configuration. Keep native library resolution inside the existing media resolver, disable scanner forking through GStreamer APIs, and verify selected plugin paths. No DYLD_LIBRARY_PATH or LD_LIBRARY_PATH change is proposed. Other operating systems retain their current startup behavior. This startup-environment change is proposed, not implemented; approval is pending.

The focused native-boundary analyzer class passed after adding Linux-only plugin-provenance and shared-context entry points. Tests include rejection of an unknown GStreamer symbol and of the Linux plugin API from the Windows assembly. This validates the analyzer boundary, not Linux plugin execution.

Shared CEF compatibility checkpoint: the portability worktree macOS assembly built successfully for osx-x64 after the OnAfterCreated ownership and post-initialization rollback changes, with normal analyzers and shared compilation disabled. This is compile evidence only; the separate macOS runtime worktree retains its own audio diagnostics and lifecycle fixes. Linux context-import compilation also passed after rebuilding the analyzer assembly; the focused symbol-boundary tests passed. Platform builds now coordinate their restore/build turns to avoid concurrently replacing shared project.assets files.


### Windows recorder ownership and hardware fixture checkpoint

Windows recorder teardown no longer treats a full semaphore as proof that allocator callbacks have returned. A pending cleanup task strongly retains the callback until the pinned SharpGen native reference count returns to its initial owner-only reference. It then disposes the callback and semaphore. Native references that remain outstanding retain this bounded owner without blocking the UI or forcing native release. The pinned SharpGen callback wrapper uses a weak managed GCHandle, so native COM reference counting alone is insufficient to retain the managed callback.

The existing MfNative file now owns balanced COM and Media Foundation initialization for both recorder and decoding fixture. It accepts successful S_OK/S_FALSE COM initialization, rejects an incompatible apartment, restores COM ownership when Media Foundation startup fails, and enforces same-thread teardown. No test-specific native-boundary exemption or new dependency was introduced. Reflection of Vortice 3.8.3 confirms SetAllocator, WriteSample, Finalize, NotifyEndOfSegment and GetTransformForStream expose checked void-returning wrappers, not unchecked Result returns.

The existing opt-in Windows hardware test file now checks known red, green, blue and yellow corners for both BGRA and RGBA D3D11 texture imports, then checks the composed texture again through the physical-size window framebuffer. A separate hardware encoder fixture uses the actual GPU converter and hardware H.264 recorder, schedules 60 samples from two distinct patterns with held frames, finalizes the MP4, decodes every sample through Media Foundation, and checks dimensions, timestamps, end-of-stream and pattern orientation. The sample count is not a distinct-frame-rate result. These hardware fixtures have compiled but have not run on Windows.

Managed callback regressions passed on the macOS host using Windows platform selection. They exercise disposal after a native reference remains past Invoke, and forced garbage collection while only asynchronous cleanup and simulated native ownership remain. Strict win-x64 and win-arm64 builds of the complete hardware test project passed with zero warnings/errors. The source check passed. The Windows desktop completion-safety gate remains in place, and product recording still rejects incomplete audio integration.

Commands: `dotnet test tests/Denice.CrossPlatform.Tests/Denice.CrossPlatform.Tests.csproj -c Debug -p:DenicePlatformRid=win-x64 -p:DeniceDesktopGpuTests=true -m:1 --filter FullyQualifiedName~AllocatorCleanup -v:q`; `dotnet build tests/Denice.CrossPlatform.Tests/Denice.CrossPlatform.Tests.csproj -c Debug -r <win-x64|win-arm64> -p:DeniceDesktopGpuTests=true -m:1 -v:q`.

Ownership sources: [pinned SharpGen CallbackBase](https://github.com/SharpGenTools/SharpGenTools/blob/6990bcafe124a4c22515ad19cee5a081da8db67b/SharpGen.Runtime/CallbackBase.cs), [pinned native COM forwarding](https://github.com/SharpGenTools/SharpGenTools/blob/6990bcafe124a4c22515ad19cee5a081da8db67b/SharpGen.Runtime/COM/ComObjectVtbl.cs), and [Microsoft tracked-sample allocator lifetime](https://learn.microsoft.com/en-us/windows/win32/api/mfidl/nf-mfidl-imftrackedsample-setallocator).


### Linux media first-run review refreshed — 2026-11-26

The media builder now requires libgstcuda alongside the existing GStreamer API roots because the recorder consumes its public context and memory APIs. Bash syntax validation and git diff --check passed. A fresh isolated-index patch containing only the existing linux-media CI job and eng/build-linux-media.sh applies to baseline 38b5660c71022054936eae1f3041321b818179c9. No working index was changed, branch published, or workflow run. Builder SHA-256: 17fa50f84c1b406a4e8a6ea346c2f4657afd345ec185e90988e12bb0ea2ad8ea. The proposed CI job uses ordinary ubuntu-24.04 and ubuntu-24.04-arm runners with 90-minute timeouts. Actual dependency compilation and media execution remain untested.

### Linux recording ownership and CUDA transfer checkpoint

The product still rejects recording pending audio integration. LinuxVideoRecorder now builds a hardware-video pipeline for the opt-in validation fixture. No Linux GPU execution result is claimed.

GstRuntime creates selected elements only after comparing the factory's plugin filename with its expected file under the packaged media prefix, both before and after feature loading. It creates the element from the loaded factory reference. Initial registry isolation remains pending the separately recorded native-entry bootstrap proposal. Plugin-path checks do not establish that initialization avoided scanning host plugin directories.

GstSharedContext now creates a GStreamer-owned GL context sharing objects with the wrapped compositor context. The wrapped compositor context alone has no GStreamer GL worker thread. LinuxOwnedTexture retains completed producer images, rejects further rendering or disposal while leased, and releases native textures only on the graphics owner. Its consumer leases perform no GL calls when released.

Pinned GStreamer 1.28.9 source contains CPU-copy fallback in both nvcodec GL interop and cudaupload. Directly feeding GLMemory or forcing CUDAMemory output caps does not exclude that fallback. LinuxCudaContext instead selects the CUDA device associated with the actual GL compositor, registers its completed RGBA texture through the public host CUDA driver API, and copies into device-only CUDA storage. It uses cuMemcpy2DUnaligned_v2 for the tightly packed destination; no CPU pixel mapping or private GStreamer helper is used. Incomplete transfers retain their native registration, device allocation and texture lease if cleanup fails. Completed device frames remain strongly tracked through context-pop failure, and teardown rejects outstanding ownership. Context-pop identity is compared with the pushed GstCudaContext's native handle.

GstCudaMemory wraps each allocation through public gst_cuda_allocator_alloc_wrapped with ordinary RGBA GstVideoInfo and the same GstCudaContext. Native ownership prevents a duplicate wrap or premature managed disposal. The native destroy callback catches release failures, preserves the allocation in its context owner and records the failure for recording finalization. GstHardwareEncoder selects a concrete matching device factory, supplies the same GstCudaContext and queries the encoder context after READY. LinuxVideoRecorder propagates these release failures. The pinned auto-GPU encoder can choose a different device when the upstream device is unsupported, so automatic selection alone is insufficient.

The VA encoder's shared importer also copies through mapped CPU video frames after failed native import. The public gst_va_dmabuf_memories_setup API can perform explicit import first; the importer accepts an already-imported surface without copying only when its GstVaDisplay identity matches. EGL DMA-BUF export, RGBA-to-NV12 GPU postprocessing, modifier compatibility and actual VA execution remain unimplemented. NVIDIA is one capability-specific path, not a claim of universal Linux or ARM64 support.

The platform-native analyzer class passed with exact CUDA/GStreamer entrypoints, unknown-symbol rejection and cross-OS rejection. Managed DMA-BUF boundary tests and CUDA_MEMCPY2D layout tests passed on the macOS host using Linux platform selection. The CUDA descriptor test checks the public 128-byte 64-bit layout and each field offset; it does not exercise a GPU. The strict Linux ARM64 hardware-test cross-build completed with zero warnings and errors. This is compile-only evidence. The media builder now requires libgstcuda as a packaged API root; the host continues to supply libcuda and its GPU driver.

Source evidence: [GStreamer GL memory ownership](https://github.com/GStreamer/gstreamer/blob/1.28.9/subprojects/gst-plugins-base/gst-libs/gst/gl/gstglbasememory.c), [NVENC input and fallback](https://github.com/GStreamer/gstreamer/blob/1.28.9/subprojects/gst-plugins-bad/sys/nvcodec/gstnvencoder.cpp), [CUDA upload fallback](https://github.com/GStreamer/gstreamer/blob/1.28.9/subprojects/gst-plugins-bad/sys/nvcodec/gstcudamemorycopy.c), [public CUDA memory wrapping](https://github.com/GStreamer/gstreamer/blob/1.28.9/subprojects/gst-plugins-bad/gst-libs/gst/cuda/gstcudamemory.h), [CUDA memory ownership implementation](https://github.com/GStreamer/gstreamer/blob/1.28.9/subprojects/gst-plugins-bad/gst-libs/gst/cuda/gstcudamemory.cpp), [VA importer](https://github.com/GStreamer/gstreamer/blob/1.28.9/subprojects/gst-plugins-bad/sys/va/gstvabase.c), [public VA surface setup](https://github.com/GStreamer/gstreamer/blob/1.28.9/subprojects/gst-plugins-bad/gst-libs/gst/va/gstvaallocator.h), [NVIDIA public GL interop](https://docs.nvidia.com/cuda/cuda-driver-api/group__CUDA__GL.html), and [NVIDIA public memory transfer API](https://docs.nvidia.com/cuda/cuda-driver-api/group__CUDA__MEM.html).

The internal PosixFileSystem.CreateRecordingFile operation creates the destination with O_CREAT|O_EXCL|O_CLOEXEC and verifies seekability. LinuxVideoRecorder supplies its descriptor to fdsink and retains the SafeFileHandle through pipeline NULL and native release. This remains inside the existing filesystem owner and introduces no public filesystem contract. The opt-in destination fixture checks existing content preservation, duplicate creation rejection and missing-parent failure.


### Linux hardware-video pipeline fixture checkpoint

LinuxVideoRecorder connects appsrc CUDAMemory RGBA input to the selected concrete NVENC encoder, h264parse, mp4mux and fdsink. Typed GObject setters validate property type, writable flags, range and readback, and reject construct-only properties. Input capacity is bounded before GPU copying. Original timestamps are preserved through submission, and native CUDA release errors prevent successful finalization. Product recording remains unavailable until browser audio integration is complete. VA transfer remains unimplemented.

HardwareEncoderFinalizesGpuFramesWithOriginalTimestamps submits 60 colored quadrant frames with a nonzero initial PTS, a timestamp gap and a pattern change at frame 30. It checks native EOS finalization, submitted count and released input texture ownership, retaining hardware-fixture-output/linux-hardware.mp4. Decoded color, orientation and timing acceptance remains pending native output and independent ffmpeg/ffprobe validation. No Linux hardware execution has occurred. The strict Linux x64 hardware-test project cross-build passed with zero warnings/errors; evidence is retained in handoff/linux-completion/validation/encoder-fixture-linux-x64.log.

The five-second gst_element_get_state timeout does not bound the preceding synchronous gst_element_set_state call. Native NULL teardown can block. The recorder retains its pipeline, descriptor and self-root until shutdown succeeds; it never releases active resources on a timer. The isolated hardware test process therefore requires an external process timeout, retaining logs and any partial MP4 on failure. No existing named watchdog was found in the current platform source. This limitation remains for product lifecycle integration.

The current encoder fixture also cross-builds for Linux ARM64 with zero warnings/errors. Managed CUDA, GObject, GstBuffer and DMA-BUF tests passed on the Mac host with Linux platform selection. The focused native library/capability theory passed after adding appsrc, buffer and typed property cases, including unknown-symbol and cross-platform denial. Logs are retained in handoff/linux-completion/validation/encoder-fixture-linux-arm64.log, managed-linux-abi-tests.log and encoder-native-boundary-tests.log. These results do not establish native GPU functionality.
