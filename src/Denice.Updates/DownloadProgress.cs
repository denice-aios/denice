// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Updates;

/// <summary>How much of a release file has been downloaded, so a command can show "12 MB of 40 MB".</summary>
/// <param name="Received">The bytes downloaded so far.</param>
/// <param name="Total">The release file's size as GitHub reports it.</param>
public sealed record DownloadProgress(long Received, long Total);
