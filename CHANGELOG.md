# Changelog

All notable changes to the Utos .NET SDK packages are documented here. This
changelog is **SDK-scoped**: each release links the upstream [`utos/api`](https://github.com/utos/api)
spec tag and commit it was generated from, and adds any SDK-only notes (tooling
bumps, packaging changes).

The format follows [Keep a Changelog](https://keepachangelog.com), and these
packages adhere to [Semantic Versioning](https://semver.org) with the version
mirroring the spec version (a fourth field marks SDK-only rebuilds).

## [Unreleased]

### Fixed

- **A release runs the corpus it just vendored, and will not publish a package that fails it.**
  `release.yml` built, packed and pushed to nuget.org without ever testing — CI gates PRs against
  the corpus the repo *already had*, while this job replaces that corpus and then packs, so nothing
  ever ran the new fixtures. That is how `0.0.18` shipped green while failing 14 of its own
- **Regenerating and publishing are now separate outcomes**, because blocking both on the corpus
  would deadlock. The fixtures arrive with the spec and the implementation follows them, so a spec
  release that adds rules *must* be able to land its protos before those rules exist — otherwise the
  implementation that would satisfy them has nothing to compile against, and the only way out is
  vendoring `utos/api` by hand, which is the manual step this workflow exists to remove. A failing
  corpus therefore commits the regenerated source and fast-forwards `dev` onto it, tags nothing,
  publishes nothing, and ends the job red. Leaving the tag off is also what makes the retry
  correct: an absent `v{version}` is already how the job decides a spec is unpublished, so the next
  push — the one carrying the implementation — resolves to that same version and releases it, with
  no bookkeeping to undo

## [0.0.18.1] - 2026-09-19

SDK-only rebuild against [`v0.0.18`](https://github.com/utos/api/releases/tag/v0.0.18) (`f0f91bd68133716f6256c6f5155874938a8b585f`): codegen/runtime tooling bump, no spec change.

### Added

- **`Utos.Workflow.Validation` enforces the schema rules of spec 0.0.18** — `UTOS-H001` through
  `UTOS-H014`, over the four slots a workflow may declare: an activity's `schema.input`, and
  `spec.output`, `spec.emits` and `spec.env`. Every slot is optional and an absent one is the empty
  schema, so a bundle built before schemas existed reports nothing new. The 14 fixtures the spec
  shipped with the corpus now pass; they had been failing since `0.0.18` was vendored in, because
  the corpus arrives with the spec and the implementation follows it
- **`UTOS-H013` and `UTOS-H014` compare property sets, never values.** A `transition.input`, or the
  `input` of anything that starts a document, must supply every property the target activity's
  declared input requires and none it does not declare. An input transform's keys are always
  literal — only its leaf values may be templates — so this is knowable with certainty at load,
  which is the test every rule in `workflow-validation.md` has to pass. Whether a template will
  produce a string is not, and stays the daemon's business
- **`JsonSchema.Net` is a new dependency of `Utos.Workflow.Validation`, and the second exception to
  that package depending on nothing.** Exactly one rule needs it: `UTOS-H008`, a `default`
  validating against the schema that declares it, where the schema is not known until a bundle is
  read — so nothing generate-ahead can serve it. Every other `UTOS-H0##` rule is a structural walk
  over the `Struct` and uses no evaluator. It clears the same bar Acornima did: `dotnet publish
  -p:PublishAot=true` against this assembly reports zero IL2026/IL3050, which the NativeAOT `utos`
  binary needs. That holds only if schemas are parsed with `JsonSchema.FromText` — the
  `JsonSerializer.Deserialize<JsonSchema>` overload is reflective and is not to be used here

### Changed

- **The README documents `Utos.Workflow.Validation`.** The package table listed three packages and
  there are four — the validator has shipped since `0.0.17` and appeared nowhere, so the one package
  a tool author most needs to find was the one the README omitted. Adds it to the table and a
  section covering `WorkflowBundleValidator.Validate`, the `ValidationReport` shape, and that the
  code is the contract while the message text is not
- **`dev` becomes the integration branch.** Every merge to `main` that touched
  `Directory.Packages.props` or `src/` released, so four Dependabot bumps were four versions and
  each release commit pushed back to `main` left the rest needing a rebase. Feature branches and
  Dependabot now target `dev`, and the `dev` -> `main` merge is the release — one push, one
  version, however much accumulated. `release.yml` fast-forwards `dev` onto the release commit it
  writes, so the branches are equal between releases rather than drifting by the regenerated
  source and the changelog entry
- **A release drains `## [Unreleased]`.** The changelog step only ever inserted a generated
  provenance line, so hand-written notes stayed under `Unreleased` after the version they
  described had shipped — everything down to `0.0.16` was sitting there. The notes now move into
  the section for the version that carried them
- **Dependabot groups its bumps.** The config had no `groups`, which is why this repo gets a PR
  per package where `utos/daemon` and `utos/cli` get one. `Grpc.*` and `Google.Protobuf` share a
  PR whatever the update type — the generated source is compiled against one and produced by the
  other, so they cannot land a merge apart — and everything else is grouped for minor and patch,
  leaving a major to arrive on its own
- **`UTOS-T005` admits the re-raise** (spec `0.0.17`). An `error` action with no `code`, `message` or
  `details`, in an `onFailure` rule, re-raises the failure being handled and is valid. Empty on
  `onSuccess` or on an `onEmitted` rule, where no failure is in scope, it is still `UTOS-T005`; so is
  a partly written one on `onFailure`, such as a `message` with no `code`
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

## [0.0.18] - 2026-09-19

Generated from [`v0.0.18`](https://github.com/utos/api/releases/tag/v0.0.18) (`f0f91bd68133716f6256c6f5155874938a8b585f`). See the [spec changelog](https://github.com/utos/api/blob/v0.0.18/CHANGELOG.md).

## [0.0.17.1] - 2026-09-16

SDK-only rebuild against [`v0.0.17`](https://github.com/utos/api/releases/tag/v0.0.17) (`a2d3ce91728e7e02681ac584afe84cb1a8ce803e`): codegen/runtime tooling bump, no spec change.

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
