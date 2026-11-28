// Copyright (c) Denice.ai. All rights reserved.

using System.Globalization;
using System.Text;

namespace Denice.CrossPlatform.Browser;

/// <summary>
/// The call log of the compile-selected browser-audio redirection proof (DeniceAudioRedirectionProof): ordered events
/// and named counters. It is never written from the mixer's real-time thread. Normal builds do not compile it.
/// </summary>
internal static class AudioProofLog
{
    private const int MaximumLines = 20000;
    private static readonly Lock Gate = new();
    private static readonly List<string> Lines = [];
    private static readonly SortedDictionary<string, long> Counters = new(StringComparer.Ordinal);

    /// <summary>Gets the events in order, one per line.</summary>
    internal static string Events
    {
        get
        {
            lock (Gate)
            {
                return string.Join('\n', Lines);
            }
        }
    }

    /// <summary>Gets every counter as name=value lines.</summary>
    internal static string CounterReport
    {
        get
        {
            lock (Gate)
            {
                var text = new StringBuilder();
                foreach (KeyValuePair<string, long> counter in Counters)
                {
                    text.Append(counter.Key).Append('=').Append(counter.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
                }

                return text.ToString();
            }
        }
    }

    /// <summary>Appends one event.</summary>
    /// <param name="text">The event.</param>
    internal static void Note(string text)
    {
        lock (Gate)
        {
            if (Lines.Count < MaximumLines)
            {
                Lines.Add(text);
            }
        }
    }

    /// <summary>Adds to a named counter.</summary>
    /// <param name="name">The counter.</param>
    /// <param name="amount">The amount.</param>
    internal static void Count(string name, long amount = 1)
    {
        lock (Gate)
        {
            Counters[name] = Counters.GetValueOrDefault(name) + amount;
        }
    }

    /// <summary>Reads a named counter.</summary>
    /// <param name="name">The counter.</param>
    /// <returns>Its value, or zero.</returns>
    internal static long Counter(string name)
    {
        lock (Gate)
        {
            return Counters.GetValueOrDefault(name);
        }
    }
}
