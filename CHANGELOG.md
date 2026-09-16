# Changelog

All notable changes to the Utos .NET SDK packages are documented here. This
changelog is **SDK-scoped**: each release links the upstream [`utos/api`](https://github.com/utos/api)
spec tag and commit it was generated from, and adds any SDK-only notes (tooling
bumps, packaging changes).

The format follows [Keep a Changelog](https://keepachangelog.com), and these
packages adhere to [Semantic Versioning](https://semver.org) with the version
mirroring the spec version (a fourth field marks SDK-only rebuilds).

## [Unreleased]

### Changed

- **The validator implements the 0.0.16 rules.** The `0.0.16` package was built from the tag
  before this landed and still enforced 0.0.15's; this is the correction. `UTOS-T005` — an `error`
  action must carry a `code` — on transition rules and `onEmitted` rules alike, with `message`
  checked as a text template and `details` as a struct template. `UTOS-T003` resolves activities
  only: `end` and `error` are no longer keywords, and a document still transitioning to them fails
  there with a message naming the `result` and `error` actions that replaced them. `UTOS-A003` is
  retired, since nothing is reserved. The grammar admits bitwise, shift and `~`, every assignment
  operator (`UTOS-E052` retired), and `new Date`, `new URL`, `new URLSearchParams`
- **A change to hand-written source now releases.** `release.yml` rebuilt only on a
  `Directory.Packages.props` change — which is why `0.0.14.2` existed and why the 0.0.16
  validator fix, merged to `main`, published nothing. A push touching `src/` (`Generated/`
  excluded) rebuilds the current spec under the next 4th-field version, and `workflow_dispatch`
  takes a `rebuild` flag for doing so by hand
- **`ReservedKeywords` is removed from `Utos.Workflow`.** The spec has no reserved names, so a
  type whose only job was to name two of them would be a lie; a daemon or CLI that consulted it
  to recognise a terminal transition must now match the `result` and `error` actions instead

### Added

- **The static expression rules, `UTOS-E0##`** (`api/docs/template-expressions.md`). Every
  `condition` and every string that may carry `{{ }}` — struct leaves, URL, headers, body, branch
  name, `forEach.collection` — is parsed as a strict-mode script and checked against the language's
  grammar, an allow-list of syntax-tree node types. What is refused takes a named code (loops,
  `function`, `class`, `var`, `this`, `async`, `new` beyond `Set`/`Map`, `__proto__` keys,
  bitwise operators…) and `UTOS-E099` covers whatever the list does not mention; a syntax error is
  `UTOS-E060`, `{{` inside a condition `UTOS-E061`, an unclosed or multi-statement interpolation
  `UTOS-E062`, a program with no value `UTOS-E063`. Nothing is evaluated. Each field reports a
  code at most once
- The template splitter finds the closing `}}` by parsing rather than scanning, so `{{ {a: {b: 1}} }}`
  is one program and `{{ '}}' }}` contains its own delimiter
- **Acornima** becomes the package's one third-party dependency: the grammar is defined over
  ECMAScript syntax trees, this is the parser the reference engine parses with (so the tree
  validated is the tree that runs), and it is AOT- and trim-compatible for the NativeAOT CLI

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

## [0.0.17] - 2026-09-16

Generated from [`v0.0.17`](https://github.com/utos/api/releases/tag/v0.0.17) (`a2d3ce91728e7e02681ac584afe84cb1a8ce803e`). See the [spec changelog](https://github.com/utos/api/blob/v0.0.17/CHANGELOG.md).

## [0.0.16.1] - 2026-09-15

SDK-only rebuild against [`v0.0.16`](https://github.com/utos/api/releases/tag/v0.0.16) (`257d55128f6b190bfba17fc8383f9bff192156a7`): codegen/runtime tooling bump, no spec change.

## [0.0.16] - 2026-09-15

Generated from [`v0.0.16`](https://github.com/utos/api/releases/tag/v0.0.16) (`257d55128f6b190bfba17fc8383f9bff192156a7`). See the [spec changelog](https://github.com/utos/api/blob/v0.0.16/CHANGELOG.md).

## [0.0.15] - 2026-09-14

Generated from [`v0.0.15`](https://github.com/utos/api/releases/tag/v0.0.15) (`e757e096be5deeff74b609901f88431ac024578f`). See the [spec changelog](https://github.com/utos/api/blob/v0.0.15/CHANGELOG.md).

## [0.0.14.2] - 2026-09-14

SDK-only rebuild against [`v0.0.14`](https://github.com/utos/api/releases/tag/v0.0.14) (`18ccc5dc9f1d5e9fbd854ccabe0ffa4d89b8fce3`): codegen/runtime tooling bump, no spec change.

## [0.0.14.1] - 2026-09-04

SDK-only rebuild against [`v0.0.14`](https://github.com/utos/api/releases/tag/v0.0.14) (`18ccc5dc9f1d5e9fbd854ccabe0ffa4d89b8fce3`): codegen/runtime tooling bump, no spec change.

## [0.0.14] - 2026-09-03

Generated from [`v0.0.14`](https://github.com/utos/api/releases/tag/v0.0.14) (`18ccc5dc9f1d5e9fbd854ccabe0ffa4d89b8fce3`). See the [spec changelog](https://github.com/utos/api/blob/v0.0.14/CHANGELOG.md).

## [0.0.13] - 2026-08-22

Generated from [`v0.0.13`](https://github.com/utos/api/releases/tag/v0.0.13) (`6d1a903b6aa3a35898542d00382ee47d8ff4c90d`). See the [spec changelog](https://github.com/utos/api/blob/v0.0.13/CHANGELOG.md).

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
