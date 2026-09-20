# Repository hygiene backlog — 2026-09-20

## Why this exists

A dedicated hygiene pass is required across the FUA repositories. Functional acceptance of a deployed feature is not a substitute for repository hygiene, reproducible tests, CI, secret scanning and maintainable operational source-of-truth.

This document is intentionally a backlog / guardrail. It is not a claim that every item below is already complete.

Repository: `rouhajan/fua-pay`

Observed GitHub visibility on 2026-09-20: **public**

Current CI observation: CI and CodeQL workflows are already present on `main`.

## Cross-repository mandatory hygiene pass

Apply the same review discipline to every actively maintained FUA repository:

- CI must exist and actually execute on relevant pushes/PRs; inspect job steps/logs, not only the badge.
- Add tests appropriate to the repository: .NET build/test, Python compile/unit tests, PowerShell parser gates, Bash syntax gates, packaging/signature smoke tests and fail-closed deployment checks as applicable.
- Enforce security hygiene: no private keys, passwords, tokens, raw credentials, local evidence, spools, captures or dumps in Git.
- Audit dependencies and pinned runtimes/SDKs; use dependency automation where useful.
- Protect `main` and require meaningful checks once workflows are reliable.
- Keep deployed security/configuration source-of-truth in Git; historical scripts that can revert current policy must be removed, deprecated or fail closed.
- Keep repositories clean: no accidental generated output, large binaries, stale dangerous TODOs, or broken executable bits/line endings.
- Periodically verify that a new maintainer could build, test and safely recover the system from repository + documented secret/config process.

## Current FUA repository audit queue

Review these together:

- `rouhajan/fua-pay`
- `rouhajan/fua-print`
- `rouhajan/fua-classroom`

Current observations:

- **fua-pay**: CI + CodeQL are present. Audit actual coverage and required checks for the current payment, identity and document flows. Because this repository is public, do not diagnose future CI failures using a private-repository Actions-minute assumption; inspect the actual runner/workflow failure.
- **fua-print**: CI exists, but recent runs show `runner_id=0` and `steps=[]`. Diagnose runner execution and obtain a real run with populated steps. Do not disturb the accepted 0.1.10.0 classroom-free runtime while doing this hygiene work.
- **fua-classroom**: no GitHub Actions workflow is currently present. Add at least PowerShell parser validation, a repository-hygiene/secret gate, and lightweight structural checks that do not require access to classroom machines.

## Definition of done

This cross-repository hygiene priority is complete only when every active repository has an explicit CI strategy, executable code/scripts have automated gates appropriate to them, checks are observed actually running, secret/local-evidence hygiene is enforced, important deployment/security configuration is tracked, stale dangerous scripts cannot silently revert accepted state, and recovery documentation is sufficient for a new maintainer.

This is a future engineering priority and should survive chat/thread resets.
