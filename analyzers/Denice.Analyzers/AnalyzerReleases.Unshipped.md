; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; RETIRED IDs — assigned, then folded into DN0011 RawPrimitiveOnlyInOwnerAnalyzer before any release.
; They count as used and are never reissued. They cannot be listed under '### Removed Rules' because
; that section requires a prior shipped release, and nothing has shipped yet (RS2007).
;   DN0012  DN0013  DN0014  DN0016  DN0021  DN0028

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DN0001  | Denice.Architecture | Warning | NoClassInAbstractionsAnalyzer
DN0002  | Denice.Design | Warning | ReturnTypesMustNotBeTuplesAnalyzer
DN0003  | Denice.Architecture | Warning | ContractConstructorMustBePrivateAnalyzer
DN0004  | Denice.Architecture | Warning | ContractMustNotExposeConcreteTypeAnalyzer
DN0005  | Denice.Architecture | Warning | ContractPublicSurfaceMustMatchInterfaceAnalyzer
DN0006  | Denice.Architecture | Warning | ContractConcreteTypeMustNotBeReferencedAnalyzer
DN0007  | Denice.Architecture | Warning | ContractConcreteTypeMustNotBeCastToAnalyzer
DN0008  | Denice.Architecture | Warning | InteropOnlyInCrossPlatformLibrariesAnalyzer
DN0009  | Denice.Architecture | Warning | NoOsBranchingOutsideCrossPlatformAnalyzer
DN0010  | Denice.Architecture | Warning | PlatformFactoryMustReturnContainerAnalyzer
DN0011  | Denice.Architecture | Warning | RawPrimitiveOnlyInOwnerAnalyzer (folds the former DN0012/0013/0014/0016/0021/0028 and the *Info half of DN0101)
DN0015  | Denice.Architecture | Warning | TimeMustUseTimeProviderAnalyzer
DN0017  | Denice.Architecture | Warning | GuardedConstructionAnalyzer (container factory)
DN0018  | Denice.Architecture | Warning | TestHelpersOnlyInTestsAnalyzer
DN0019  | Denice.Architecture | Warning | BuilderCompletenessAnalyzer
DN0020  | Denice.Architecture | Warning | PathPurityAnalyzer
DN0022  | Denice.Architecture | Warning | ContainerInterfaceSingleOwnerAnalyzer
DN0023  | Denice.Architecture | Warning | OneDoorIntoCrossPlatformAnalyzer
DN0024  | Denice.Architecture | Warning | NoStaticServiceHolderAnalyzer
DN0025  | Denice.Architecture | Warning | OneOwnerPerInterfaceAnalyzer
DN0026  | Denice.Architecture | Warning | NoOsSkipInTestsAnalyzer
DN0027  | Denice.Architecture | Warning | GuardedConstructionAnalyzer (InMemoryFileSystemStore single construction)
DN0029  | Denice.Architecture | Warning | OneDoorIntoPerOsAnalyzer
DN0030  | Denice.Architecture | Warning | LeafPlatformSingleImplementerAnalyzer
DN0031  | Denice.Architecture | Warning | NoServiceAsParameterAnalyzer
DN0032  | Denice.Architecture | Warning | NoCoverageOptOutAnalyzer
DN0033  | Denice.Architecture | Warning | GuardedConstructionAnalyzer (*Info wrapper construction)
DN0034  | Denice.Architecture | Warning | SystemServicesMemberMustBeServiceAccessorAnalyzer
DN0035  | Denice.Architecture | Warning | NoLiteralFakeRootAnalyzer
DN0036  | Denice.Architecture | Warning | NoLiteralCaseModeSeedAnalyzer
DN0037  | Denice.Architecture | Warning | NoOsBranchingOutsideCrossPlatformAnalyzer (single-OS branch inside a per-OS implementation library)
DN0038  | Denice.Architecture | Warning | TimeoutMustUseTimeProviderAnalyzer (bounded waits/timeouts flow through the injected TimeProvider)
DN0039  | Denice.Architecture | Warning | NoCallerFilePathAnalyzer ([CallerFilePath] banned production and test alike, wherever the custom analyzers are wired in; reach the base directory through IEnvironment.GetBaseDirectory())
DN0040  | Denice.Architecture | Warning | EngineToBoundariesOneDoorAnalyzer (Engine reaches Denice.Boundaries only through the four permitted adapter factories, and only from SystemServices.Create())
DN0041  | Denice.Architecture | Warning | EngineInternalsOneDoorAnalyzer (the CLI and Denice.TestHelpers reach only the SystemServices.Create() invocation among Denice.Engine internals)
DN0101  | Denice.Architecture | Warning | OsDivergentFilesystemOnlyInCrossPlatformAnalyzer (shrunk to static OS-divergent members, Marshal, and P/Invoke)
DN0102  | Denice.Architecture | Warning | PInvokeSignatureMustBeBlittableAnalyzer (no object/dynamic/unconstrained-generic/variadic in a P/Invoke signature)
DN0103  | Denice.Architecture | Warning | MacOsNativeBoolMustBeI1Analyzer (macOS native bool marshalled as UnmanagedType.I1)
DN0104  | Denice.Architecture | Warning | NativeCallbackMustUseFunctionPointerAnalyzer (native callback via static [UnmanagedCallersOnly] &method, not a Marshal delegate pointer)
DN0105  | Denice.Architecture | Warning | NativeCallbackBodyMustBeGuardedAnalyzer (native callback body — [UnmanagedCallersOnly] OR a ComVisible-interface completed-handler — is one try/catch(Exception) returning a native error code)
DN0106  | Denice.Architecture | Warning | NoControlFlowInNativeOpsAnalyzer (no control flow — if/ternary, loop, switch statement/expression, coalesce/coalesce-assignment, conditional-access, short-circuit and/or, pattern and/or combinator — in a native-ops implementer; try/catch exempt)
DN0107  | Denice.Architecture | Warning | NoTimeoutInPresenceImplAnalyzer (no timeout inside a presence impl, its flow port, or its native-ops owner; the gate owns the one 60s bound)
DN0108  | Denice.Architecture | Warning | PresenceCheckOnlyFromApprovalGateAnalyzer (IPresenceCheck.Check invoked only from ApprovalGate in production)
DN0109  | Denice.Architecture | Warning | NoInlinePromptLiteralAtPresenceCallAnalyzer (no inline prompt literal at a production presence call site)
DN0110  | Denice.Architecture | Warning | DbusOnlyInPolkitAuthorityAnalyzer (Tmds.DBus.Protocol confined to the IPolkitAuthority owner; high-level Tmds.DBus banned)
DN0111  | Denice.Architecture | Warning | NoAuthenticationAgentRegistrationAnalyzer (value-scan: never register/spawn a polkit authentication agent)
DN0112  | Denice.Architecture | Warning | NoCachedNativeAuthContextAnalyzer (no cached native auth-context handle field in a native presence port)
DN0113  | Denice.Architecture | Warning | PresenceNativeInteropOwnerAnalyzer (presence-family P/Invoke + Marshal confined to the per-OS native-ops owner — IObjCRuntime / IWindowsHelloNativeOps / ICredentialPromptNativeOps; family-scoped, paired with DN0101)
DN0114  | Denice.Architecture | Warning | NativePresencePortSingleImplementerAnalyzer (each per-OS native port AND native-ops owner has exactly one implementer)
DN0115  | Denice.Architecture | Warning | NoAsyncOrchestrationInNativeOpsAnalyzer (no await, await using, or await foreach; no call returning a Task/Task<T>/ValueTask/ValueTask<T>; no TaskCompletionSource — in a native-ops implementer)
DN0116  | Denice.Architecture | Warning | NoStateInNativeOpsAnalyzer (a native-ops implementer is fully stateless — no field, auto-property, or field-like event; const exempt)
AGS5443 | Denice.Architecture | Warning | TempRootBackDoorAnalyzer (IEnvironment.GetTempDirectory call sites; owned equivalent of Sonar S5443)
