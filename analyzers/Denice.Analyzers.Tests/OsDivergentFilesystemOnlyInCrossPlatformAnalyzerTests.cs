// Copyright (c) Denice.ai. All rights reserved.

using System.Threading.Tasks;
using Denice.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Denice.Analyzers.Tests;

public class OsDivergentFilesystemOnlyInCrossPlatformAnalyzerTests
{
    private const string SetUnixFileModeSource = """
        using System.IO;

        public class Sample
        {
            public void Chmod(string path) => File.SetUnixFileMode(path, UnixFileMode.UserRead);
        }
        """;

    private const string CreateSymbolicLinkSource = """
        using System.IO;

        public class Sample
        {
            public void Link(string a, string b) => Directory.CreateSymbolicLink(a, b);
        }
        """;

    private const string MarshalSource = """
        using System.Runtime.InteropServices;

        public class Sample
        {
            public int LastError() => Marshal.GetLastPInvokeError();
        }
        """;

    // The single-owner proof: a stub owning interface in Denice.Abstractions, and TWO classes in the SAME
    // assembly — the owner implementing IPlatformFileSystem (its File.SetUnixFileMode is exempt) and a sibling that
    // does not implement it (its Directory.CreateSymbolicLink is RED). This proves the tightening from the
    // CrossPlatform-libraries assembly-set exemption to the owner classes.
    private const string OwnerAndSiblingSource = """
        using System.IO;

        namespace Denice.Abstractions.Contracts
        {
            public interface IPlatformFileSystem { }
        }

        namespace CrossPlatform
        {
            public sealed class PosixFileSystem : Denice.Abstractions.Contracts.IPlatformFileSystem
            {
                public void Chmod(string path) => File.SetUnixFileMode(path, UnixFileMode.UserRead);
            }

            public sealed class Sibling
            {
                public void Link(string a, string b) => Directory.CreateSymbolicLink(a, b);
            }
        }
        """;

    // The shared helper class Denice.CrossPlatform.PlatformFileSystemShared — NOT an owner under
    // ag0101-one-owner-per-os. Its static OS-divergent call (File.SetUnixFileMode) is RED everywhere, including inside a
    // CrossPlatform.* platform library: the shared-helper carve-out is gone, so its divergent logic must move into the
    // one per-OS class. Uses a static member DN0101 still owns after the shrink (the *Info instance members and
    // construction moved to DN0011, fileinfo-abstraction-stays-in-ag0011).
    private const string SharedHelperOnlySource = """
        using System.IO;

        namespace Denice.CrossPlatform
        {
            public static class PlatformFileSystemShared
            {
                public static void Chmod(string path) => File.SetUnixFileMode(path, UnixFileMode.UserRead);
            }
        }
        """;

    // Just the IPlatformFileSystem implementer, no sibling — so the ONE diagnostic (or its absence) is the owner's own
    // SetUnixFileMode. Used to prove the interface owner is exempt only inside a CrossPlatform.* platform library and
    // RED (self-grant blocked) in any other assembly, closing the round-2 hole where implementing IPlatformFileSystem
    // exempted the class in ANY assembly.
    private const string PlatformFileSystemOwnerOnlySource = """
        using System.IO;

        namespace Denice.Abstractions.Contracts
        {
            public interface IPlatformFileSystem { }
        }

        namespace CrossPlatform
        {
            public sealed class PosixFileSystem : Denice.Abstractions.Contracts.IPlatformFileSystem
            {
                public void Chmod(string path) => File.SetUnixFileMode(path, UnixFileMode.UserRead);
            }
        }
        """;

    [Fact]
    public async Task SetUnixFileMode_OutsideCrossPlatform_IsReported()
    {
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(SetUnixFileModeSource, "Denice.Engine"));
        Assert.Equal("DN0101", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public async Task LinkTargetRead_IsNotReported_ItBelongsToDN0011()
    {
        // After the shrink, the *Info instance member LinkTarget is owned wholesale by the wrapper interfaces under
        // DN0011 (fileinfo-abstraction-stays-in-ag0011), not by DN0101 — so DN0101 no longer reports it.
        Assert.Empty(
            await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(
                SharedAnalyzerSources.LinkTargetSource, "Denice.Engine"));
    }

    [Fact]
    public async Task InfoConstruction_IsNotReported_ItBelongsToDN0011()
    {
        // After the shrink, a raw new FileInfo / new DirectoryInfo is owned wholesale by IFileInfo/IDirectoryInfo under
        // DN0011, not DN0101 — so DN0101 no longer reports the construction.
        const string source = """
            using System.IO;

            public class Sample
            {
                public FileInfo File(string path) => new FileInfo(path);
                public DirectoryInfo Dir(string path) => new DirectoryInfo(path);
            }
            """;

        Assert.Empty(
            await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(source, "Denice.Engine"));
    }

    [Fact]
    public async Task CreateSymbolicLink_OutsideCrossPlatform_IsReported()
    {
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(CreateSymbolicLinkSource, "Denice.Engine"));
        Assert.Equal("DN0101", diagnostic.Id);
    }

    [Fact]
    public async Task Marshal_OutsideCrossPlatform_IsReported()
    {
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(MarshalSource, "Denice.Engine"));
        Assert.Equal("DN0101", diagnostic.Id);
    }

    [Fact]
    public async Task OwnerImplementingInterface_IsExempt_SiblingInSameAssembly_IsStillReported()
    {
        // one-owner-class-per-primitive: only a class implementing IPlatformFileSystem is exempt, resolved
        // structurally. Compiled into a per-OS library to prove assembly membership no longer grants the exemption —
        // the owner's File.SetUnixFileMode is clean, but the sibling's Directory.CreateSymbolicLink in the SAME
        // assembly is still RED.
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(
                OwnerAndSiblingSource, "Denice.CrossPlatform.MacOS"));
        Assert.Equal("DN0101", diagnostic.Id);
        Assert.Contains(
            "CreateSymbolicLink", AnalyzerRunner.SpanText(OwnerAndSiblingSource, diagnostic), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformFileSystemOwner_InNonCrossPlatformAssembly_IsReported_SelfGrantBlocked()
    {
        // FIX 1 (DN0101 interface self-grant): implementing IPlatformFileSystem is not enough — the class must also
        // compile into one of the three per-OS implementation libraries (.MacOS/.Linux/.Windows). Declared in
        // Denice.Engine, the owner's own File.SetUnixFileMode is RED, closing the round-2 hole where the interface
        // implementer self-granted in any assembly.
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(
                PlatformFileSystemOwnerOnlySource, "Denice.Engine"));
        Assert.Equal("DN0101", diagnostic.Id);
        Assert.Contains(
            "SetUnixFileMode", AnalyzerRunner.SpanText(PlatformFileSystemOwnerOnlySource, diagnostic), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformFileSystemOwner_InCrossPlatformLibrary_IsExempt()
    {
        // The same IPlatformFileSystem implementer compiled into a real per-OS implementation library (.MacOS) stays
        // exempt: both halves of the conjunction pass. This is the per-OS exemption the ag0101-one-owner-per-os fix
        // keeps green.
        Assert.Empty(
            await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(
                PlatformFileSystemOwnerOnlySource, "Denice.CrossPlatform.MacOS"));
    }

    [Fact]
    public async Task PlatformFileSystemOwner_InCoreCrossPlatformAssembly_IsReported()
    {
        // ag0101-one-owner-per-os: the owner exemption is the THREE per-OS implementation libraries
        // (.MacOS/.Linux/.Windows), NOT the core Denice.CrossPlatform contract assembly — home of
        // PlatformFileSystemShared. A class implementing IPlatformFileSystem compiled into the CORE assembly (name
        // exactly "Denice.CrossPlatform") does NOT self-grant: its raw File.SetUnixFileMode is RED. This closes the
        // latent hole where the earlier four-library assembly gate (IsCrossPlatformLibrary) exempted a core-assembly
        // implementer, reopening the shared-helper escape this work closed.
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(
                PlatformFileSystemOwnerOnlySource, "Denice.CrossPlatform"));
        Assert.Equal("DN0101", diagnostic.Id);
        Assert.Contains(
            "SetUnixFileMode", AnalyzerRunner.SpanText(PlatformFileSystemOwnerOnlySource, diagnostic), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformFileSystemShared_InCrossPlatformLibrary_IsReported_NoSharedHelperCarveOut()
    {
        // ag0101-one-owner-per-os: the shared helper PlatformFileSystemShared is no longer an owner. Even compiled into
        // a real CrossPlatform.* platform library, its File.SetUnixFileMode call is RED — the shared-helper carve-out is
        // gone, so its divergent logic must move into the one per-OS class implementing IPlatformFileSystem. This is the
        // RED that forces PlatformFileSystemShared's divergent calls out.
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(
                SharedHelperOnlySource, "Denice.CrossPlatform.MacOS"));
        Assert.Equal("DN0101", diagnostic.Id);
        Assert.Contains(
            "SetUnixFileMode", AnalyzerRunner.SpanText(SharedHelperOnlySource, diagnostic), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformFileSystemShared_InNonCrossPlatformAssembly_IsReported()
    {
        // The same helper is RED in any non-platform assembly too — it is never exempt.
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(
                SharedHelperOnlySource, "Denice.Engine"));
        Assert.Equal("DN0101", diagnostic.Id);
    }

    [Fact]
    public async Task ReadingUnixFileModeEnumValue_IsNotReported()
    {
        // Reading an enum value as data is a legal edge — the UnixFileMode enum itself is not the File member.
        const string source = """
            using System.IO;

            public class Sample
            {
                public UnixFileMode Bits() => UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            """;

        Assert.Empty(await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(source, "Denice.Engine"));
    }

    [Fact]
    public async Task OsUniformFileCall_IsNotReported_ItBelongsToDN0011()
    {
        const string source = """
            using System.IO;

            public class Sample
            {
                public bool Check(string path) => File.Exists(path);
            }
            """;

        Assert.Empty(await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(source, "Denice.Engine"));
    }

    [Theory]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Recording", "/System/Library/Frameworks/AVFoundation.framework/AVFoundation", "AVMakeRectWithAspectRatioInsideRect", false)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Desktop", "/usr/lib/libSystem.B.dylib", "memmove", false)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Desktop", "/usr/lib/libSystem.B.dylib", "unlink", true)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Browser", "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox", "AudioOutputUnitStart", false)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Browser", "/usr/lib/libSystem.B.dylib", "vm_protect", false)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Browser", "/usr/lib/libSystem.B.dylib", "_dyld_get_image_header", false)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Browser", "/usr/lib/libSystem.B.dylib", "unlink", true)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Desktop", "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox", "AudioOutputUnitStart", true)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Desktop", "/usr/lib/libSystem.B.dylib", "vm_protect", true)]
    [InlineData("Denice.Engine", "Denice.Engine.Browser", "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox", "AudioOutputUnitStart", true)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Desktop", "unknown", "call", true)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Sibling", "/System/Library/Frameworks/AVFoundation.framework/AVFoundation", "call", true)]
    [InlineData("Denice.Engine", "Denice.CrossPlatform.MacOS.Desktop", "/System/Library/Frameworks/Metal.framework/Metal", "call", true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Desktop", "av_libglesv2", "EGL_Initialize", false)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Desktop", "av_libglesv2", "unknown_entry", true)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Desktop", "av_libglesv2", "EGL_Initialize", true)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Desktop", "/System/Library/Frameworks/Metal.framework/Metal", "MTLCreateSystemDefaultDevice", true)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Desktop", "/usr/lib/libobjc.A.dylib", "objc_msgSend", true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Desktop", "/usr/lib/libSystem.B.dylib", "memmove", true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Desktop", "/usr/lib/libobjc.A.dylib", "objc_msgSend", true)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Desktop", "/usr/lib/libobjc.A.dylib", "objc_msgSend", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Desktop", "libEGL.so.1", "eglInitialize", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Browser", "libGLESv2.so.2", "glFinish", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Desktop", "libX11.so.6", "XOpenDisplay", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgstreamer-1.0.so.0", "gst_plugin_get_filename", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgstgl-1.0.so.0", "gst_gl_context_new", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgstgl-1.0.so.0", "gst_gl_context_create", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgstgl-1.0.so.0", "gst_gl_context_can_share", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgstcuda-1.0.so.0", "gst_cuda_allocator_alloc_wrapped", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgstvideo-1.0.so.0", "gst_video_info_new", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgstvideo-1.0.so.0", "gst_video_info_set_format", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgstvideo-1.0.so.0", "unknown_video_symbol", true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Recording", "libgstcuda-1.0.so.0", "gst_cuda_allocator_alloc_wrapped", true)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgobject-2.0.so.0", "g_value_get_uint", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgobject-2.0.so.0", "unknown_gobject_symbol", true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Recording", "libgobject-2.0.so.0", "g_value_get_uint", true)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libcuda.so.1", "cuMemcpy2D_v2", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libcuda.so.1", "unknown_entry", true)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Recording", "libcuda.so.1", "cuMemcpy2D_v2", true)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgstreamer-1.0.so.0", "unknown_entry", true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Recording", "libgstreamer-1.0.so.0", "gst_plugin_get_filename", true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Desktop", "libEGL.so.1", "eglInitialize", true)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Desktop", "libEGL.so.1", "unknown_entry", true)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Filesystem", "libEGL.so.1", "eglInitialize", true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Filesystem", "av_libglesv2", "EGL_Initialize", true)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgstapp-1.0.so.0", "gst_app_src_push_buffer", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgstapp-1.0.so.0", "unknown_app_symbol", true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Recording", "libgstapp-1.0.so.0", "gst_app_src_push_buffer", true)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgstreamer-1.0.so.0", "gst_buffer_new", false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Recording", "libgobject-2.0.so.0", "g_value_set_int", false)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Recording", "libgobject-2.0.so.0", "g_value_set_int", true)]
    public async Task DesktopNativeInterop_EnforcesLibraryAndCapabilityBoundary(string assembly, string area, string library, string entryPoint, bool reported)
    {
        string source = $$"""
            using System.Runtime.InteropServices;
            namespace {{area}}
            {
                public sealed class Capability
                {
                    [DllImport("{{library}}", EntryPoint = "{{entryPoint}}")]
                    private static extern void Native();
                    public void Run() => Native();
                }
            }
            """;
        var diagnostics = await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(source, assembly);
        if (reported)
        {
            Assert.Equal("DN0101", Assert.Single(diagnostics).Id);
        }
        else
        {
            Assert.Empty(diagnostics);
        }
    }

    [Theory]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Browser", "libc.so.6", "dl_iterate_phdr", true, false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Browser", "libc.so.6", "dl_iterate_phdr", false, true)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Browser", "libc.so.6", "mprotect", true, false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Browser", "libc.so.6", "getpagesize", true, false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Browser", "libc.so.6", "setenv", true, false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Browser", "libc.so.6", "unlink", true, true)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Browser", "libpulse.so.0", "pa_stream_new", true, false)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Browser", "libpulse.so.0", "pa_stream_new", false, true)]
    [InlineData("Denice.CrossPlatform.Linux", "Denice.CrossPlatform.Linux.Desktop", "libpulse.so.0", "pa_stream_new", true, true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Browser", "libpulse.so.0", "pa_stream_new", true, true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Browser", "kernel32", "VirtualProtect", true, false)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Browser", "kernel32", "VirtualProtect", false, true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Browser", "kernel32", "GetProcAddress", true, false)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Browser", "kernel32", "SetEvent", true, true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Browser", "ole32", "CoCreateInstance", true, false)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Browser", "ole32", "CoTaskMemAlloc", true, false)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Desktop", "ole32", "CoCreateInstance", true, true)]
    [InlineData("Denice.CrossPlatform.Windows", "Denice.CrossPlatform.Windows.Browser", "kernel32", "DeleteFileW", true, true)]
    [InlineData("Denice.CrossPlatform.MacOS", "Denice.CrossPlatform.MacOS.Browser", "libc.so.6", "mprotect", true, true)]
    public async Task AudioRedirectionProofImports_AreAllowedOnlyInTheProofBuildsBrowserArea(string assembly, string area, string library, string entryPoint, bool proofBuild, bool reported)
    {
        string source = $$"""
            using System.Runtime.InteropServices;
            namespace {{area}}
            {
                public sealed class Capability
                {
                    [DllImport("{{library}}", EntryPoint = "{{entryPoint}}")]
                    private static extern void Native();
                    public void Run() => Native();
                }
            }
            """;
        string[] symbols = proofBuild ? ["DENICE_AUDIO_REDIRECTION_PROOF"] : [];
        var diagnostics = await AnalyzerRunner.RunWithSymbolsAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(source, assembly, symbols);
        if (reported)
        {
            Assert.Equal("DN0101", Assert.Single(diagnostics).Id);
        }
        else
        {
            Assert.Empty(diagnostics);
        }
    }

    [Fact]
    public async Task DesktopCapability_DoesNotAcquireFilesystemOrLastErrorOwnership()
    {
        const string source = """
            using System.IO;
            using System.Runtime.InteropServices;
            namespace Denice.CrossPlatform.MacOS.Desktop
            {
                public sealed class Capability
                {
                    public void Run(string path)
                    {
                        File.SetUnixFileMode(path, UnixFileMode.UserRead);
                        _ = Marshal.GetLastPInvokeError();
                        _ = Marshal.ReadIntPtr(System.IntPtr.Zero);
                    }
                }
            }
            """;
        var diagnostics = await AnalyzerRunner.RunAsync<OsDivergentFilesystemOnlyInCrossPlatformAnalyzer>(source, "Denice.CrossPlatform.MacOS");
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("DN0101", diagnostic.Id));
    }
}
