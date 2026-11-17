// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Updates;

/// <summary>
/// One setting as <c>denice settings</c> lists it: its name, its current value, and its default.
/// </summary>
/// <param name="Name">The setting's name.</param>
/// <param name="Value">The setting's current value: the value <c>settings.yaml</c> sets, or else its default.</param>
/// <param name="DefaultValue">The value the setting has when nothing sets it.</param>
public sealed record SettingState(string Name, string Value, string DefaultValue);
