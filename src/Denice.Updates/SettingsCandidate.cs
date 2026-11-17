// Copyright (c) Denice.ai. All rights reserved.

using System.Collections.Generic;

namespace Denice.Updates;

/// <summary>A settings version that can be restored, with the exact text that restores it and the values it sets.</summary>
/// <param name="Version">The version as <c>denice settings history</c> lists it.</param>
/// <param name="Text">The exact <c>settings.yaml</c> text that restores the version.</param>
/// <param name="Values">The value of each setting the version sets, by name, each already validated.</param>
internal sealed record SettingsCandidate(SettingsVersion Version, string Text, IReadOnlyDictionary<string, string> Values);
