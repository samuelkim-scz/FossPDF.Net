# Contributing to FossPDF

Start with the [feature-parity roadmap](FEATURE_PARITY.md) and its linked GitHub issues. The current target is QuestPDF 2026.9.1; the main consumer target is .NET 9, with netstandard2.1 core compatibility and .NET 8/.NET 9 tests retained.

## Claim and coordinate work

- Find an open `feature-parity` issue and check its native blockers, milestone, acceptance criteria and linked PRs.
- Request assignment in the issue, or assign yourself if your repository permissions allow it. Record one lead assignee and any collaborators.
- Discuss changes to shared public APIs, native contracts, layout state or cache ownership in the affected issues before implementation. Use draft PRs to coordinate parallel work.
- Split large issues into reviewable sub-issues/PRs, keeping the parent acceptance checklist and dependency relationships current.

## Build and test

Install a .NET 9 SDK compatible with `Source/global.json` and the .NET 8 runtime/SDK for the net8.0 tests. From `Source`, run:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
```

Include meaningful tests for changed behavior and the acceptance evidence requested by the issue. Review visual results when changing rendering/layout. Test native resource ownership, caller-owned streams and failure cleanup when those paths change. CI runs on Windows, Linux and macOS.

## Pull requests

Use the PR template and link the work item. `Closes #<number>` is appropriate when the PR completes the entire issue; `Refs #<number>` tracks partial work. Preserve existing FossPDF APIs/namespaces and document supported frameworks and migration paths for additions. Record licenses/notices and runtime assets for new dependencies.

Close a parity issue only when its acceptance checklist, tests, docs/examples and applicable validation reports are complete. GitHub issues own live status and assignments; the versioned roadmap owns the reference target, scope and milestone definitions.

## New gaps and scope changes

Use the parity issue form with a pinned QuestPDF version/source, current FossPDF evidence, observable behavior and proposed acceptance criteria. Classify the capability as missing, partial, already equivalent or validation required. Update the roadmap/baseline manifest when scope changes. Planned upstream features and unverified performance/deployment assumptions require a separate decision before joining the released-feature target.
