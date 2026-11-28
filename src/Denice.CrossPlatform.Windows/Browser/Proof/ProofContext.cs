// Copyright (c) Denice.ai. All rights reserved.

using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>What the static import replacements of the Windows proof need: the started mixer and the injected clock.</summary>
/// <param name="Mixer">The mixer core.</param>
/// <param name="Clock">The clock.</param>
internal sealed record ProofContext(MixerCore Mixer, TimeProvider Clock);
