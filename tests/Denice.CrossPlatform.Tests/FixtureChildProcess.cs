// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Denice.Abstractions.Contracts;
using Xunit.Abstractions;

namespace Denice.CrossPlatform.Tests;

/// <summary>Runs one named native fixture scenario in a bounded child of this test assembly's own executable, so the
/// scenario executes on that child's OS main thread. The child's output is kept as <c>execution.log</c>; a child that
/// outlives its bound is handed to the platform's hang capture and then killed.</summary>
internal static class FixtureChildProcess
{
    /// <summary>The bound for stopping a child and draining its output.</summary>
    internal static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan FixtureTimeout = TimeSpan.FromSeconds(90);

    // Set once at the start of the fixture child's Main, so its stage markers carry elapsed time.
    private static long fixtureStart;

    /// <summary>Starts the scenario in a child of this assembly's executable and waits for it within the fixture bound.</summary>
    /// <param name="services">The system services.</param>
    /// <param name="output">The test log that also receives the child's output.</param>
    /// <param name="scenario">The scenario name the child's Main dispatches.</param>
    /// <param name="directory">The artifact directory, created here and passed to the child.</param>
    /// <param name="removedEnvironment">The inherited environment variables the child must not see.</param>
    /// <param name="captureHang">Records a timed-out child's state from its process id and the artifact directory; best effort, it must not throw.</param>
    /// <returns>The child's exit code.</returns>
    [SuppressMessage("Denice.Architecture", "DN0011", Justification = "This native integration fixture owns a bounded child process so the scenario executes on its OS main thread; no product process interface exists. All other environment, file and clock operations use Systemservices.")]
    internal static async Task<int> RunAsync(ISystemServices services, ITestOutputHelper output, string scenario, string directory, IReadOnlyList<string> removedEnvironment, Func<int, string, Task> captureHang)
    {
        string executable = Path.Combine(services.Environment.GetBaseDirectory(), "Denice.CrossPlatform.Tests");
        services.FileSystem.GetDirectoryWriter().CreateDirectory(directory);
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(scenario);
        start.ArgumentList.Add(directory);
        foreach (string name in removedEnvironment)
        {
            _ = start.Environment.Remove(name);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the native desktop fixture.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(FixtureTimeout, services.Clock).ConfigureAwait(false);
        }
        catch (TimeoutException timeout)
        {
            Exception? cleanupFailure = await CaptureAndStopAsync(services, process, directory, captureHang).ConfigureAwait(false);
            if (cleanupFailure is not null)
            {
                throw new AggregateException("The native fixture timed out and could not be stopped.", timeout, cleanupFailure);
            }

            throw;
        }
        finally
        {
            string log;
            try
            {
                log = await stdout.WaitAsync(CleanupTimeout, services.Clock).ConfigureAwait(false) + "\n" + await stderr.WaitAsync(CleanupTimeout, services.Clock).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                log = "The native fixture's output did not close after it was stopped.";
            }

            await services.FileSystem.GetFileWriter().WriteAllBytesAsync(Path.Combine(directory, "execution.log"), Encoding.UTF8.GetBytes(log), CancellationToken.None).ConfigureAwait(false);
            output.WriteLine(log);
        }

        return process.ExitCode;
    }

    /// <summary>Marks the start of the fixture child, the origin of its stage markers' elapsed time.</summary>
    /// <param name="services">The system services.</param>
    internal static void Begin(ISystemServices services) => fixtureStart = services.Clock.GetTimestamp();

    /// <summary>Writes a fixture progress marker so a timed-out child shows the last stage it reached.</summary>
    /// <param name="services">The system services.</param>
    /// <param name="stage">The stage reached.</param>
    internal static void Stage(ISystemServices services, string stage) =>
        services.Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Fixture stage at {services.Clock.GetElapsedTime(fixtureStart).TotalSeconds:F2} s: {stage}"));

    /// <summary>Captures a timed-out fixture's state, then always stops it, whatever happened to the best-effort capture.</summary>
    /// <returns>The failure to stop the fixture, or null when it exited.</returns>
    [SuppressMessage("Denice.Architecture", "DN0011", Justification = "Stops this bounded native fixture's own child process; no product process interface exists.")]
    private static async Task<Exception?> CaptureAndStopAsync(ISystemServices services, Process process, string directory, Func<int, string, Task> captureHang)
    {
        Exception? stopFailure = null;
        try
        {
            await captureHang(process.Id, directory).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(CleanupTimeout, services.Clock).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                stopFailure = exception;
            }
        }

        return stopFailure;
    }
}
