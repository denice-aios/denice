// Copyright (c) 4thWAIV. All rights reserved.

namespace Denice.Engine.Setup;

/// <summary>
/// The per-project state record persisted at <c>.denice/state.json</c>: the guard version the project was
/// wired with. A drift from the installed machine version is reported by <c>doctor</c>.
/// </summary>
/// <param name="GuardVersion">The guard version the project is wired with.</param>
internal sealed record ProjectState(string GuardVersion);
