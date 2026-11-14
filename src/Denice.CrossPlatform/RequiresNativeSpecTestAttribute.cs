// Copyright (c) Denice.ai. All rights reserved.

using System;

namespace Denice.CrossPlatform;

/// <summary>
/// Marks a per-OS presence NATIVE-OPS owner interface as one that MUST be covered by its own
/// <c>[Trait("Category","NativeSpec")]</c> pointed-integration spec test (guardrail-1-nativespec-convention-test). This
/// is the single, production-side source of the "which owners require a spec test" list: it lives ON the owner
/// interfaces themselves (macOS <c>IObjCRuntime</c>, Windows <c>ICredentialPromptNativeOps</c>, Linux
/// <c>IPolkitAuthority</c>), so the convention test (<c>NativeSpecCoverageConventionTests</c>) discovers the required
/// set by reflecting over the per-OS production assembly for this attribute, rather than reading a separate test-side
/// list that could silently drift from the interfaces. The required owner IS the interface the attribute is placed on —
/// it carries no argument, so it cannot name a different owner than where it sits and a copy-paste mismatch is
/// structurally impossible. An owner deliberately NOT spec-tested — <c>IWindowsHelloNativeOps</c> (interactive-only) and
/// the <c>ILocalAuthentication</c> / <c>IWindowsUserPresence</c> flow ports (fake-tested) — simply does not carry it.
/// Internal to this contract assembly and applied inside the per-OS libraries over their existing
/// <c>InternalsVisibleTo</c> grants from here. This attribute is NOT (yet) read by the presence analyzers
/// (DN0106/DN0113/DN0114/DN0115/DN0116); making it their single owner source too is tracked as issue #50.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
internal sealed class RequiresNativeSpecTestAttribute : Attribute
{
}
