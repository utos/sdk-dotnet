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
