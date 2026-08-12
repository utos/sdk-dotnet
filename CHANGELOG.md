# Changelog

All notable changes to the Utos .NET SDK packages are documented here. This
changelog is **SDK-scoped**: each release links the upstream [`utos/api`](https://github.com/utos/api)
spec tag and commit it was generated from, and adds any SDK-only notes (tooling
bumps, packaging changes).

The format follows [Keep a Changelog](https://keepachangelog.com), and these
packages adhere to [Semantic Versioning](https://semver.org) with the version
mirroring the spec version (a fourth field marks SDK-only rebuilds).

## [Unreleased]

### Added

- Regenerated against the upstream `0.0.12` protos: `CallActivityConfig`/`SpawnActivityConfig`,
  the promise `completion` oneof, `EmitAction`, `ExecutionService.WatchOutput`/`CancelExecution`,
  `EXECUTION_STATUS_CANCELLED`, and `GetExecutionResponse.result`
- Validation for the new shapes: `UTOS-T004` (an `emit` requires a transition — it is the one
  non-terminal action, so a rule that emits and goes nowhere strands the execution) and
  `UTOS-C404` (an `onEmitted` rule must transition; `result` would end the parent mid-stream and
  `emit` would republish a child's value onto the parent's own stream from inside the handler
  consuming it)
- `ActivityDescriptorTests` — asserts the descriptor invariants the source format's dotted `type`
  discriminator relies on: no field name declared at two levels of one resolvable path (which is
  what makes "place each authored key on the message along the path that declares it"
  unambiguous), every path ending at a message with no remaining oneof, and the resolvable set
  matching the documented table. These are properties of the protos rather than of any parser, so
  they are pinned here instead of trusted to review
- **`Utos.Workflow.Validation`** — a new package implementing the bundle validation rules from
  [`docs/workflow-validation.md`](https://github.com/utos/api/blob/main/docs/workflow-validation.md).
  `WorkflowBundleValidator.Validate(bundle)` returns a `ValidationReport` of coded,
  path-addressed `ValidationIssue`s — e.g. `UTOS-C102` at
  `workflows["acme/greet:1.0.0"].spec.activities["send"].http.url` — reported exhaustively rather
  than fail-fast. **No third-party dependencies**: a validation framework forced on every consumer
  of a spec package could not be walked back without a breaking change, and its flattened message
  strings would not survive the trip to another language. Only `code` and `path` are contractual;
  message wording is free to improve.
- **Spec primitives in `Utos.Workflow`** (`Utos.Workflows.V1`), beside the existing digest helper:
  `SemanticVersion` (full semver precedence; build metadata excluded from equality, since the
  version forms part of a dictionary key), `WorkflowIdentity` (the canonical
  `[registry/][namespace/]name:version` bundle key), `WorkflowRef` (a *partial* reference as a user
  types it — optional version, optional `@sha256:` pin), plus the fixed vocabularies
  `WorkflowDocument`, `ReservedKeywords`, `PromiseModes` and `HttpUrlRules`.
- **Conformance test suite** driven by fixtures vendored from `utos/api` into `conformance/`,
  asserting exact `{code, path}` sets. The release workflow vendors that directory alongside
  `proto/`, so the corpus cannot drift from the spec.

- **Content digest for `WorkflowBundle`** (`Utos.Workflow`). `ContentDigest.Compute` /
  `WorkflowBundle.ComputeContentDigest()` produce the canonical `sha256:<hex>` content
  identity carried by `WorkflowReference.digest`, following the spec's
  [canonical serialization](https://github.com/utos/api/blob/v0.0.10/docs/canonical-bundle-digest.md)
  (proto3 JSON → RFC 8785 / JCS → SHA-256). Also `ContentDigest.CanonicalJson` (the pre-hash
  canonical JSON) and `ContentDigest.Verify`. See [`docs/content-digest.md`](docs/content-digest.md).
  Adds a dependency on `jsoncanonicalizer` (and transitively `es6numberserializer`).
  The digest format is **not yet conformance-locked**: golden vectors are deferred until a
  cross-SDK reference set exists, and the SDK does not populate or enforce
  `WorkflowReference.digest` on daemon calls.

### Changed

- **BREAKING**: `UTOS-A007` now also reports an unset nested mode — a `WorkflowActivityConfig`
  with neither `call` nor `spawn`, or a `PromiseActivityConfig` with no `completion` — with the
  `path` naming the level that is unset
- **BREAKING**: `UTOS-C302` is no longer conditional on the mode and its `path` moves to
  `promise.count.requiredCount`, since `requiredCount` now exists only on `PromiseCountConfig`
- **The C# namespace for `utos.workflow.v1` is now `Utos.Workflows.V1`** (plural), following the
  spec's `option csharp_namespace`. The singular form declared a namespace `Utos.Workflow` that
  shadowed the `Workflow` message type for any consumer whose own namespace sits under `Utos.` —
  and C# resolves simple names through enclosing namespaces *before* using-directives, so a
  using-alias could not fix it, only full qualification. **Nothing on the wire changes**: the proto
  package remains `utos.workflow.v1`, message full names are unchanged, and content digests are
  identical. Consumers update their `using` directives. `Utos.Daemon.V1` is unaffected and every
  package id is unchanged.

### Removed

- **BREAKING**: `PromiseModes` (`Utos.Workflows.V1.Spec`) — the string table the promise
  `completion` oneof replaces. An unknown mode is no longer representable, so there is nothing
  left to parse or check
- **BREAKING**: `ValidationCodes.PromiseModeInvalid` (`UTOS-C301`) is **retired**, not renumbered.
  The code stays burned because codes are a stable contract, and recycling one would silently
  change the meaning of a suppression somebody had already written down

## [0.0.12] - 2026-08-12

Generated from [`v0.0.12`](https://github.com/utos/api/releases/tag/v0.0.12) (`a96714b11e23f081d20dab4c53bc78dc1e936e54`). See the [spec changelog](https://github.com/utos/api/blob/v0.0.12/CHANGELOG.md).

## [0.0.11] - 2026-08-11

Generated from [`v0.0.11`](https://github.com/utos/api/releases/tag/v0.0.11) (`f2ff9084810c943c3c6db404b0e57f12858ecfea`). See the [spec changelog](https://github.com/utos/api/blob/v0.0.11/CHANGELOG.md).

## [0.0.10.1] - 2026-07-20

SDK-only rebuild against [`v0.0.10`](https://github.com/utos/api/releases/tag/v0.0.10) (`949b56276cd87e2c2031469e0ecb2f32961a38a3`): codegen/runtime tooling bump, no spec change.

## [0.0.10] - 2026-07-19

Generated from [`v0.0.10`](https://github.com/utos/api/releases/tag/v0.0.10) (`949b56276cd87e2c2031469e0ecb2f32961a38a3`). See the [spec changelog](https://github.com/utos/api/blob/v0.0.10/CHANGELOG.md).

## [0.0.9] - 2026-07-17

Generated from [`v0.0.9`](https://github.com/utos/api/releases/tag/v0.0.9) (`a7a94963b522059803df04516a4b3bec7e7e0b3b`). See the [spec changelog](https://github.com/utos/api/blob/v0.0.9/CHANGELOG.md).

## [0.0.8.3] - 2026-07-16

SDK-only rebuild against [`v0.0.8`](https://github.com/utos/api/releases/tag/v0.0.8) (`e86b8165d6a26b9c66a07d866545d175f942aa3c`): codegen/runtime tooling bump, no spec change.

## [0.0.8.2] - 2026-07-16

SDK-only rebuild against [`v0.0.8`](https://github.com/utos/api/releases/tag/v0.0.8) (`e86b8165d6a26b9c66a07d866545d175f942aa3c`): codegen/runtime tooling bump, no spec change.

## [0.0.8.1] - 2026-06-11

SDK-only rebuild against [`v0.0.8`](https://github.com/utos/api/releases/tag/v0.0.8) (`e86b8165d6a26b9c66a07d866545d175f942aa3c`): codegen/runtime tooling bump, no spec change.

## [0.0.8] - 2026-06-11

Generated from [`v0.0.8`](https://github.com/utos/api/releases/tag/v0.0.8) (`e86b8165d6a26b9c66a07d866545d175f942aa3c`). See the [spec changelog](https://github.com/utos/api/blob/v0.0.8/CHANGELOG.md).
