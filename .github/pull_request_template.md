## Description
Brief description of what this PR does.

<!-- PR title must follow Conventional Commits: <type>(<scope>): <summary>
     types: feat / fix / docs / test / refactor / style / chore / build / ci
     scopes: chat, agent, sessions, storage, settings, files, ui, protocol, ci, build -->

## Related Issue
Fixes #(issue number)

## Type of Change
- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Refactor / performance (no functional change)
- [ ] Documentation update

## Area(s) Touched
- [ ] `ConnectOnion.WinUIClient` (WinUI 3 app / XAML / view models)
- [ ] `ConnectOnion.Protocol` (wire protocol, signing, WebSocket state machine)
- [ ] Data layer / SQLite schema (`Data/SchemaMigrator.cs`, repositories)
- [ ] Tests (`tests/*`) / Conformance
- [ ] Docs (`docs/*`, `CLAUDE.md`)

## Changes Made
- List the main changes
- Be specific about what was modified
- Note any NuGet packages added or bumped

## Testing
Build:
```powershell
dotnet build ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.sln --configuration Debug -p:Platform=x64
```
Tests run:
- [ ] `tests\ConnectOnion.Protocol.Tests` (wire schema / encoding / event parsing)
- [ ] `tests\ConnectOnion.WinUIClient.UnitTests` (projection, caches, view models)
- [ ] `tests\ConnectOnion.IntegrationTests` (SQLite schema / repositories)
- [ ] Protocol Conformance gate passes (`dotnet run --project ConnectOnion.Protocol.Conformance -c Release`)
- [ ] Manually ran the app and exercised the affected flow

## Schema / Migration (if applicable)
- [ ] Added a new `Migration(n, sql)` and bumped `LatestVersion` (never edited/renumbered a released migration)
- [ ] Verified upgrade from an existing `.db` (not just a fresh one)

## Checklist
- [ ] PR title follows Conventional Commits
- [ ] My code follows the existing patterns (hand-rolled MVVM, `ObservableObject`/`RelayCommand`, no new DI/DTO conventions)
- [ ] I have performed a self-review of my own code
- [ ] I have commented hard-to-understand areas
- [ ] I have made corresponding changes to the documentation (`CLAUDE.md` / `docs/`) where behavior changed
- [ ] My changes generate no new build warnings
- [ ] New and existing tests pass locally
- [ ] Protocol changes are covered by / cross-checked against the Conformance signer

## Screenshots (if UI change)
Add before/after screenshots for any visible UI change (light and dark theme if relevant).

## Additional Notes
Any additional information that reviewers should know.
