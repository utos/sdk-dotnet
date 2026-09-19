# Utos .NET SDK

Source-bearing .NET packages generated from the [Utos API specification](https://github.com/utos/api).

Unlike a typical generated SDK, this repository **commits the generated C# source**
alongside the `.proto` it came from, so every release is diffable in git and every
package ships with SourceLink + symbols for step-into debugging.

## Packages

| Package | Use it when you… | Contains | Depends on |
|---------|------------------|----------|------------|
| [`Utos.Workflow`](https://www.nuget.org/packages/Utos.Workflow) | define or represent Utos workflows | `Utos.Workflows.V1` message types (`Workflow`, `WorkflowBundle`, activities) | `Google.Protobuf` |
| [`Utos.Daemon.Client`](https://www.nuget.org/packages/Utos.Daemon.Client) | call a Utos daemon | `utos.daemon.v1` messages + gRPC **client** stubs | `Grpc.Core.Api`, `Utos.Workflow` |
| [`Utos.Daemon.Server`](https://www.nuget.org/packages/Utos.Daemon.Server) | implement a Utos daemon | `utos.daemon.v1` messages + gRPC **server** base classes | `Grpc.Core.Api`, `Utos.Workflow` |

```bash
dotnet add package Utos.Daemon.Client   # caller / client
dotnet add package Utos.Daemon.Server   # daemon implementer
```

No custom NuGet source or registry auth required — these install from nuget.org.

> `Utos.Daemon.Client` and `Utos.Daemon.Server` define the same gRPC service types
> and therefore **cannot be referenced together in one assembly** — a process is
> either a caller or a daemon. Pick the one that matches your role. Both packages
> pull in the shared `Utos.Workflow` types. Neither pulls a gRPC transport: choose
> your own (`Grpc.Net.Client` for callers, `Grpc.AspNetCore` for daemons).

## Content digest

`Utos.Workflow` can compute the canonical `sha256:<hex>` content digest of a `WorkflowBundle`
(`bundle.ComputeContentDigest()`) — the cross-SDK content identity carried by
`WorkflowReference.digest`. See [`docs/content-digest.md`](docs/content-digest.md).

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
