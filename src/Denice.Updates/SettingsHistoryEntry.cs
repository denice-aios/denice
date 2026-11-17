// Copyright (c) Denice.ai. All rights reserved.

using System;

namespace Denice.Updates;

/// <summary>One copy in the settings history, as its file name describes it.</summary>
/// <param name="Sequence">The copy's sequence number; a higher number is a newer copy.</param>
/// <param name="Saved">When the copy was saved, in UTC.</param>
/// <param name="Path">The copy's absolute path.</param>
internal sealed record SettingsHistoryEntry(long Sequence, DateTimeOffset Saved, string Path);
