# Content digest (`WorkflowBundle`)

`Utos.Workflow` computes the canonical **content digest** of a `WorkflowBundle` — the stable,
cross-SDK identity carried by `WorkflowReference.digest`, independent of the mutable
`name:version` key.

The algorithm and canonical byte form are defined by the Utos spec, **not** by this SDK:

> **Canonical bundle serialization:**
> <https://github.com/utos/api/blob/v0.0.10/docs/canonical-bundle-digest.md>

```
digest = "sha256:" + lowerhex( sha256( JCS( proto3json( WorkflowBundle ) ) ) )
```

i.e. proto3 JSON mapping → RFC 8785 (JSON Canonicalization Scheme) → SHA-256 → lowercase hex with a
`sha256:` prefix.

## API

```csharp
using Utos.Workflow.V1;

WorkflowBundle bundle = ...;

string digest = bundle.ComputeContentDigest();     // "sha256:<64 hex chars>"
//     or:     ContentDigest.Compute(bundle);

bool ok = ContentDigest.Verify(bundle, expected);  // ordinal compare against a known digest

string canonical = ContentDigest.CanonicalJson(bundle);  // the pre-hash canonical JSON (for debugging/conformance)
```

- **Implementation.** proto3 JSON via `Google.Protobuf`'s `JsonFormatter` (default settings —
  lowerCamelCase field names, defaults/unset-optionals/empty maps omitted, `Duration` as `"5s"`),
  then RFC 8785 canonicalization via the `jsoncanonicalizer` package (which pulls
  `es6numberserializer` for spec-exact ECMAScript number formatting — the reason the digest is
  byte-stable across runtimes despite netstandard2.0's non-shortest-round-trip `double` formatting).
- **Non-finite numbers.** `NaN` / `±Infinity` in a `Struct` value are rejected (`ArgumentException`),
  per the spec.

## Status — provisional

The digest format is finalized (locked so every SDK provably agrees) only once a shared reference
implementation **and** committed golden vectors exist; both are deferred (see the spec's
"Conformance" section). Until then:

- The value this SDK computes is **provisional** — a candidate for the future golden vector, not yet
  cross-checked against other implementations. The worked-example digest is pinned in
  `tests/Utos.Workflow.Tests` as a regression guard.
- The SDK **does not** auto-populate or enforce `WorkflowReference.digest` on daemon calls. An
  opt-in client integration can be layered on once the format is locked.
