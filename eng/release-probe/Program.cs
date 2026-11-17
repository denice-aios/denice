// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Denice.Abstractions.Contracts;
using Denice.Updates;

namespace Denice.ReleaseProbe;

/// <summary>
/// Proves rust/REQUIREMENTS.md E3 on a published release: hands the genuine release file, then altered copies, to
/// Denice Runtime's own installer exactly as <c>denice update</c> does after its download (UpdateService:
/// <c>new VersionInstaller(services, installRoot, ReleaseTrust.Denice).InstallAsync(zip)</c>). The public constructor
/// uses Denice's production trust (the cosign identity of Denice's CI and the pinned code-signing certificates), and
/// <c>SystemServices.Create()</c> gives the real per-OS code-signature check. Every Denice assembly is loaded from the
/// release's own version folder, so the code that judges the release is the code the release ships; this program only
/// prepares the inputs and reports. Exit code 0 when the genuine release is installed and every altered copy is
/// refused with nothing left behind; 1 otherwise; 2 for a usage error.
/// </summary>
internal static class Program
{
    private const string ChecksumsFile = "SHA256SUMS";
    private const string BundleFile = "SHA256SUMS.cosign.bundle";
    private const string AlteredFile = "Denice.Updates.dll";

    private static async Task<int> Main(string[] args)
    {
        if (args.Length is < 3 or > 4)
        {
            await Console.Error.WriteLineAsync(
                "usage: ReleaseProbe <release-version-dir> <work-dir> <release-zip> [<another-platform-release-zip>]").ConfigureAwait(false);
            return 2;
        }

        string shipped = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string candidate = Path.Combine(shipped, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
        byte[] genuine = await File.ReadAllBytesAsync(args[2]).ConfigureAwait(false);
        byte[]? other = args.Length == 4 ? await File.ReadAllBytesAsync(args[3]).ConfigureAwait(false) : null;
        return await RunAsync(shipped, Path.GetFullPath(args[1]), genuine, other).ConfigureAwait(false);
    }

    // Kept out of Main so no Denice type is resolved before the Resolving handler is in place.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> RunAsync(string shipped, string work, byte[] genuine, byte[]? other)
    {
        Assembly installer = typeof(VersionInstaller).Assembly;
        if (!string.Equals(Path.GetDirectoryName(installer.Location), shipped, StringComparison.Ordinal))
        {
            Console.WriteLine($"probe FAIL: Denice.Updates was loaded from {installer.Location}, not from the release");
            return 1;
        }

        Console.WriteLine($"probe: judging with Denice.Updates {installer.GetName().Version} from {installer.Location}");

        // SystemServices is internal to Denice Runtime (InternalsVisibleTo); its public Create() is the one composition
        // point Denice Runtime itself calls, reached here through reflection on the shipped Denice.Engine.
        var services = (ISystemServices)Assembly.Load("Denice.Engine")
            .GetType("Denice.Engine.SystemServices", throwOnError: true)!
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;

        var cases = new List<(string Name, byte[] Zip, bool Accept)>
        {
            ("genuine release", genuine, true),
            ($"one byte changed in {AlteredFile}", Alter(genuine, FlipOneByte), false),
            ($"one byte changed in {AlteredFile} and SHA256SUMS rewritten to match", Alter(genuine, FlipOneByteAndRelist), false),
            ($"a file added that SHA256SUMS does not list", Alter(genuine, AddUnlistedFile), false),
        };
        if (other is not null)
        {
            byte[] otherBundle = Read(other, BundleFile);
            cases.Add(("SHA256SUMS.cosign.bundle replaced by another platform's genuine bundle",
                Alter(genuine, entries => entries[BundleFile] = otherBundle), false));
        }

        bool ok = true;
        int index = 0;
        foreach ((string name, byte[] zip, bool accept) in cases)
        {
            string root = Path.Combine(work, $"case-{++index}");
            Directory.CreateDirectory(root);
            ok &= await JudgeAsync(services, root, name, zip, accept).ConfigureAwait(false);
        }

        Console.WriteLine(ok ? "probe RESULT: PASS" : "probe RESULT: FAIL");
        return ok ? 0 : 1;
    }

    private static async Task<bool> JudgeAsync(ISystemServices services, string root, string name, byte[] zip, bool accept)
    {
        string verdict;
        try
        {
            InstallOutcome outcome = await new VersionInstaller(services, root).InstallAsync(zip, CancellationToken.None)
                .ConfigureAwait(false);
            verdict = $"{outcome.Result} {outcome.Version}";
        }
        catch (InvalidDataException refused)
        {
            verdict = "refused: " + refused.Message;
        }

        string versions = Path.Combine(root, "versions");
        string[] left = Directory.Exists(versions) ? Directory.GetDirectories(versions).Select(d => Path.GetFileName(d)).ToArray() : [];
        bool passed = accept
            ? verdict.StartsWith("Installed ", StringComparison.Ordinal) && left.Length == 1 && left[0] == verdict["Installed ".Length..]
            : verdict.StartsWith("refused: ", StringComparison.Ordinal) && left.Length == 0;
        Console.WriteLine($"probe {(passed ? "PASS" : "FAIL")}: {name}: {verdict}; version folders left: [{string.Join(", ", left)}]");
        return passed;
    }

    private static void FlipOneByte(Dictionary<string, byte[]> entries)
    {
        byte[] bytes = entries[AlteredFile];
        bytes[bytes.Length / 2] ^= 0x01;
    }

    private static void FlipOneByteAndRelist(Dictionary<string, byte[]> entries)
    {
        FlipOneByte(entries);
        string hash = Convert.ToHexStringLower(SHA256.HashData(entries[AlteredFile]));
        string[] lines = Encoding.UTF8.GetString(entries[ChecksumsFile]).Split('\n');
        entries[ChecksumsFile] = Encoding.UTF8.GetBytes(string.Join('\n', lines.Select(line =>
            line.EndsWith("  " + AlteredFile, StringComparison.Ordinal) ? $"{hash}  {AlteredFile}" : line)));
    }

    private static void AddUnlistedFile(Dictionary<string, byte[]> entries) =>
        entries["unlisted.txt"] = Encoding.UTF8.GetBytes("not part of the release");

    private static byte[] Read(byte[] zip, string relative)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        ZipArchiveEntry entry = archive.Entries.Single(e => e.FullName.EndsWith("/" + relative, StringComparison.Ordinal)
            && e.FullName.Count(c => c == '/') == 2);
        using var content = new MemoryStream();
        using (Stream source = entry.Open())
        {
            source.CopyTo(content);
        }

        return content.ToArray();
    }

    /// <summary>
    /// Copies the release file, letting <paramref name="change"/> edit the files of its version folder (keyed by their
    /// path inside the version folder); every other entry and every entry's attributes are kept as they were.
    /// </summary>
    private static byte[] Alter(byte[] zip, Action<Dictionary<string, byte[]>> change)
    {
        using var source = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        string prefix = source.Entries.Select(e => e.FullName)
            .First(n => n.StartsWith("versions/", StringComparison.Ordinal) && n.Count(c => c == '/') >= 2);
        prefix = prefix[..(prefix.IndexOf('/', "versions/".Length) + 1)];
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var attributes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in source.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)
            && !e.FullName.EndsWith('/')))
        {
            using var content = new MemoryStream();
            using (Stream stream = entry.Open())
            {
                stream.CopyTo(content);
            }

            files[entry.FullName[prefix.Length..]] = content.ToArray();
            attributes[entry.FullName[prefix.Length..]] = entry.ExternalAttributes;
        }

        change(files);
        var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in source.Entries.Where(e => !e.FullName.StartsWith(prefix, StringComparison.Ordinal)
                || e.FullName.EndsWith('/')))
            {
                ZipArchiveEntry copy = target.CreateEntry(entry.FullName);
                copy.ExternalAttributes = entry.ExternalAttributes;
                if (!entry.FullName.EndsWith('/'))
                {
                    using Stream from = entry.Open();
                    using Stream to = copy.Open();
                    from.CopyTo(to);
                }
            }

            foreach ((string relative, byte[] bytes) in files)
            {
                ZipArchiveEntry copy = target.CreateEntry(prefix + relative);
                copy.ExternalAttributes = attributes.GetValueOrDefault(relative, 0x81A4 << 16);
                using Stream to = copy.Open();
                to.Write(bytes);
            }
        }

        return output.ToArray();
    }
}
