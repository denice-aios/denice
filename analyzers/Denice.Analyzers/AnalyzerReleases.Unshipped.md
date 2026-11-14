; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; RETIRED IDs — assigned, then folded into AG0011 RawPrimitiveOnlyInOwnerAnalyzer before any release.
; They count as used and are never reissued. They cannot be listed under '### Removed Rules' because
; that section requires a prior shipped release, and nothing has shipped yet (RS2007).
;   AG0012  AG0013  AG0014  AG0016  AG0021  AG0028

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AG0001  | Denice.Architecture | Warning | NoClassInAbstractionsAnalyzer
AG0002  | Denice.Design | Warning | ReturnTypesMustNotBeTuplesAnalyzer
AG0003  | Denice.Architecture | Warning | ContractConstructorMustBePrivateAnalyzer
AG0004  | Denice.Architecture | Warning | ContractMustNotExposeConcreteTypeAnalyzer
AG0005  | Denice.Architecture | Warning | ContractPublicSurfaceMustMatchInterfaceAnalyzer
AG0006  | Denice.Architecture | Warning | ContractConcreteTypeMustNotBeReferencedAnalyzer
AG0007  | Denice.Architecture | Warning | ContractConcreteTypeMustNotBeCastToAnalyzer
AG0008  | Denice.Architecture | Warning | InteropOnlyInCrossPlatformLibrariesAnalyzer
AG0009  | Denice.Architecture | Warning | NoOsBranchingOutsideCrossPlatformAnalyzer
AG0010  | Denice.Architecture | Warning | PlatformFactoryMustReturnContainerAnalyzer
AG0011  | Denice.Architecture | Warning | RawPrimitiveOnlyInOwnerAnalyzer (folds the former AG0012/0013/0014/0016/0021/0028 and the *Info half of AG0101)
AG0015  | Denice.Architecture | Warning | TimeMustUseTimeProviderAnalyzer
AG0017  | Denice.Architecture | Warning | GuardedConstructionAnalyzer (container factory)
AG0018  | Denice.Architecture | Warning | TestHelpersOnlyInTestsAnalyzer
AG0019  | Denice.Architecture | Warning | BuilderCompletenessAnalyzer
AG0020  | Denice.Architecture | Warning | PathPurityAnalyzer
AG0022  | Denice.Architecture | Warning | ContainerInterfaceSingleOwnerAnalyzer
AG0023  | Denice.Architecture | Warning | OneDoorIntoCrossPlatformAnalyzer
AG0024  | Denice.Architecture | Warning | NoStaticServiceHolderAnalyzer
AG0025  | Denice.Architecture | Warning | OneOwnerPerInterfaceAnalyzer
AG0026  | Denice.Architecture | Warning | NoOsSkipInTestsAnalyzer
AG0027  | Denice.Architecture | Warning | GuardedConstructionAnalyzer (InMemoryFileSystemStore single construction)
AG0029  | Denice.Architecture | Warning | OneDoorIntoPerOsAnalyzer
AG0030  | Denice.Architecture | Warning | LeafPlatformSingleImplementerAnalyzer
AG0031  | Denice.Architecture | Warning | NoServiceAsParameterAnalyzer
AG0032  | Denice.Architecture | Warning | NoCoverageOptOutAnalyzer
AG0033  | Denice.Architecture | Warning | GuardedConstructionAnalyzer (*Info wrapper construction)
AG0034  | Denice.Architecture | Warning | SystemServicesMemberMustBeServiceAccessorAnalyzer
AG0035  | Denice.Architecture | Warning | NoLiteralFakeRootAnalyzer
AG0036  | Denice.Architecture | Warning | NoLiteralCaseModeSeedAnalyzer
AG0037  | Denice.Architecture | Warning | NoOsBranchingOutsideCrossPlatformAnalyzer (single-OS branch inside a per-OS implementation library)
AG0038  | Denice.Architecture | Warning | TimeoutMustUseTimeProviderAnalyzer (bounded waits/timeouts flow through the injected TimeProvider)
AG0039  | Denice.Architecture | Warning | NoCallerFilePathAnalyzer ([CallerFilePath] banned production and test alike, wherever the custom analyzers are wired in; reach the base directory through IEnvironment.GetBaseDirectory())
AG0040  | Denice.Architecture | Warning | EngineToBoundariesOneDoorAnalyzer (Engine reaches Denice.Boundaries only through the four permitted adapter factories, and only from SystemServices.Create())
AG0041  | Denice.Architecture | Warning | EngineInternalsOneDoorAnalyzer (the CLI and Denice.TestHelpers reach only the SystemServices.Create() invocation among Denice.Engine internals)
AG0101  | Denice.Architecture | Warning | OsDivergentFilesystemOnlyInCrossPlatformAnalyzer (shrunk to static OS-divergent members, Marshal, and P/Invoke)
AG0102  | Denice.Architecture | Warning | PInvokeSignatureMustBeBlittableAnalyzer (no object/dynamic/unconstrained-generic/variadic in a P/Invoke signature)
AG0103  | Denice.Architecture | Warning | MacOsNativeBoolMustBeI1Analyzer (macOS native bool marshalled as UnmanagedType.I1)
AG0104  | Denice.Architecture | Warning | NativeCallbackMustUseFunctionPointerAnalyzer (native callback via static [UnmanagedCallersOnly] &method, not a Marshal delegate pointer)
AG0105  | Denice.Architecture | Warning | NativeCallbackBodyMustBeGuardedAnalyzer (native callback body — [UnmanagedCallersOnly] OR a ComVisible-interface completed-handler — is one try/catch(Exception) returning a native error code)
AG0106  | Denice.Architecture | Warning | NoControlFlowInNativeOpsAnalyzer (no control flow — if/ternary, loop, switch statement/expression, coalesce/coalesce-assignment, conditional-access, short-circuit and/or, pattern and/or combinator — in a native-ops implementer; try/catch exempt)
AG0107  | Denice.Architecture | Warning | NoTimeoutInPresenceImplAnalyzer (no timeout inside a presence impl, its flow port, or its native-ops owner; the gate owns the one 60s bound)
AG0108  | Denice.Architecture | Warning | PresenceCheckOnlyFromApprovalGateAnalyzer (IPresenceCheck.Check invoked only from ApprovalGate in production)
AG0109  | Denice.Architecture | Warning | NoInlinePromptLiteralAtPresenceCallAnalyzer (no inline prompt literal at a production presence call site)
AG0110  | Denice.Architecture | Warning | DbusOnlyInPolkitAuthorityAnalyzer (Tmds.DBus.Protocol confined to the IPolkitAuthority owner; high-level Tmds.DBus banned)
AG0111  | Denice.Architecture | Warning | NoAuthenticationAgentRegistrationAnalyzer (value-scan: never register/spawn a polkit authentication agent)
AG0112  | Denice.Architecture | Warning | NoCachedNativeAuthContextAnalyzer (no cached native auth-context handle field in a native presence port)
AG0113  | Denice.Architecture | Warning | PresenceNativeInteropOwnerAnalyzer (presence-family P/Invoke + Marshal confined to the per-OS native-ops owner — IObjCRuntime / IWindowsHelloNativeOps / ICredentialPromptNativeOps; family-scoped, paired with AG0101)
AG0114  | Denice.Architecture | Warning | NativePresencePortSingleImplementerAnalyzer (each per-OS native port AND native-ops owner has exactly one implementer)
AG0115  | Denice.Architecture | Warning | NoAsyncOrchestrationInNativeOpsAnalyzer (no await, await using, or await foreach; no call returning a Task/Task<T>/ValueTask/ValueTask<T>; no TaskCompletionSource — in a native-ops implementer)
AG0116  | Denice.Architecture | Warning | NoStateInNativeOpsAnalyzer (a native-ops implementer is fully stateless — no field, auto-property, or field-like event; const exempt)
AGS5443 | Denice.Architecture | Warning | TempRootBackDoorAnalyzer (IEnvironment.GetTempDirectory call sites; owned equivalent of Sonar S5443)
