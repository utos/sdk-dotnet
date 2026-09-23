# Utos .NET SDK

Source-bearing .NET packages for the [Utos API specification](https://github.com/utos/api).

Three are generated from the `.proto`; `Utos.Workflow.Source` and `Utos.Workflow.Validation` are
hand-written, and implement rules the spec defines normatively so that every tool enforces the
same ones.

Unlike a typical generated SDK, this repository **commits the generated C# source**
alongside the `.proto` it came from, so every release is diffable in git and every
package ships with SourceLink + symbols for step-into debugging.

## Packages

| Package | Use it when you… | Contains | Depends on |
|---------|------------------|----------|------------|
| [`Utos.Workflow`](https://www.nuget.org/packages/Utos.Workflow) | define or represent Utos workflows | `Utos.Workflows.V1` message types (`Workflow`, `WorkflowBundle`, activities) | `Google.Protobuf` |
| [`Utos.Workflow.Source`](https://www.nuget.org/packages/Utos.Workflow.Source) | read the source format people author | `WorkflowLoader`, the schema short-form compiler, `SourceCodes` | `Utos.Workflow`, `YamlDotNet` |
| [`Utos.Workflow.Validation`](https://www.nuget.org/packages/Utos.Workflow.Validation) | check a bundle against the spec before acting on it | `WorkflowBundleValidator`, `ValidationCodes` | `Utos.Workflow`, `Acornima`, `JsonSchema.Net` |
| [`Utos.Daemon.Client`](https://www.nuget.org/packages/Utos.Daemon.Client) | call a Utos daemon | `utos.daemon.v1` messages + gRPC **client** stubs | `Grpc.Core.Api`, `Utos.Workflow` |
| [`Utos.Daemon.Server`](https://www.nuget.org/packages/Utos.Daemon.Server) | implement a Utos daemon | `utos.daemon.v1` messages + gRPC **server** base classes | `Grpc.Core.Api`, `Utos.Workflow` |

```bash
dotnet add package Utos.Daemon.Client       # caller / client
dotnet add package Utos.Daemon.Server       # daemon implementer
dotnet add package Utos.Workflow.Source     # read authored YAML into a Workflow
dotnet add package Utos.Workflow.Validation # either role, to check a bundle first
```

> **Reading a document and judging a bundle are different jobs**, which is why they are different
> packages. `Utos.Workflow.Source` turns what an author wrote into a `Workflow` and reports what
> only a *document* can get wrong (`UTOS-S###`); `Utos.Workflow.Validation` judges the resulting
> bundle (`UTOS-B/D/M/A/T/C/H###`). A front end — a CLI, a registry's upload path — wants both, and
> references both. Sharing them is what keeps a workflow one tool accepts from being one another
> rejects.

No custom NuGet source or registry auth required — these install from nuget.org.

> `Utos.Daemon.Client` and `Utos.Daemon.Server` define the same gRPC service types
> and therefore **cannot be referenced together in one assembly** — a process is
> either a caller or a daemon. Pick the one that matches your role. Both packages
> pull in the shared `Utos.Workflow` types. Neither pulls a gRPC transport: choose
> your own (`Grpc.Net.Client` for callers, `Grpc.AspNetCore` for daemons).

## Values

A run carries `WorkflowValue`s — the JSON data model plus blobs, with every node's type in its
structure rather than inferred from its content. `Utos.Workflow` ships the two conversions the
spec defines, so a CLI reading `--input` and a daemon checking what was scheduled agree:

```csharp
WorkflowMap input = WorkflowValues.MapFromJson("""{"tenant":"acme","width":640}""");

// Lossless — unless the value holds a blob, which has no plain-JSON form.
if (WorkflowValues.TryToJson(result, out string json)) Console.WriteLine(json);

// UTOS-V101 / V102, every failure rather than the first.
foreach (ValueIssue issue in WorkflowValues.Validate(input)) Console.WriteLine(issue);
```

JSON → value is total and **never produces a blob**: an object holding the key `$blob`, or any
other key, is a map. A blob reaches a run as a handle a client was given by `CreateBlob`.

## Content digest

`Utos.Workflow` can compute the canonical `sha256:<hex>` content digest of a `WorkflowBundle`
(`bundle.ComputeContentDigest()`) — the cross-SDK content identity carried by
`WorkflowReference.digest`. See [`docs/content-digest.md`](docs/content-digest.md).

## Validation

`Utos.Workflow.Validation` implements the rules in the spec's
[`workflow-validation.md`](https://github.com/utos/api/blob/main/docs/workflow-validation.md) and the
static expression rules of
[`template-expressions.md`](https://github.com/utos/api/blob/main/docs/template-expressions.md),
so the CLI, a daemon and your own tooling all enforce one rule set — a workflow one accepts cannot
be rejected by another.

```csharp
var report = WorkflowBundleValidator.Validate(bundle);
foreach (var issue in report.Issues)
    Console.WriteLine($"{issue.Code} at {issue.Path}: {issue.Message}");
```

`ValidationReport` carries `IsValid` and a list of `ValidationIssue`, each with a `Code`, the `Path`
it was found at, and a message. **The code is the contract and the message text is not** — match on
`ValidationCodes` (or the literal `UTOS-A005`), never on wording, which is free to improve between
releases.

The rules are structural and referential: they check shape and that names resolve, and they parse
template expressions against the grammar. Nothing is evaluated, no network is touched, and no claim
is made about what a workflow will do at run time. The conformance fixtures are vendored from
`utos/api` at the spec tag each release is built from, so this package is checked against the same
corpus as every other SDK rather than a local copy free to drift.

## Versioning

The package version mirrors the Utos spec version it was generated from
(`utos/api` tag `vX.Y.Z` → package `X.Y.Z`). SDK-only rebuilds against the same
spec (e.g. a `Grpc.Tools` bump) increment a fourth field (`X.Y.Z.N`). Each release
records the exact `utos/api` tag and commit it was generated from.

## Branches and releases

`dev` is the integration branch; `main` is the release branch. Feature branches and
Dependabot bumps target `dev` and merge there freely — nothing is published from `dev`.
The `dev` → `main` merge **is** the release: it arrives as one push, so a run of bumps
and fixes costs one version rather than one per merge.

Two things then happen on `main` without further help. `release.yml` regenerates from
the current `utos/api` spec tag, drains `## [Unreleased]` in `CHANGELOG.md` into the new
version's section, commits, tags and pushes to nuget.org — then fast-forwards `dev` onto
that release commit, so the two branches are equal between releases. A spec release in
`utos/api` reaches `main` the same way, through a `spec-released` `repository_dispatch`,
independently of whatever is waiting on `dev`.

A push to `main` releases only when it touches `Directory.Packages.props` or `src/`
(excluding `Generated/`): a docs- or test-only merge changes nothing a consumer installs,
so it publishes nothing and its notes wait under `## [Unreleased]` for the next release.

## License

[Apache 2.0](LICENSE)
