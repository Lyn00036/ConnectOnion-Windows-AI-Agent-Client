<!--
PR titles must follow Conventional Commits:
  <type>(<scope>): <summary>

Examples:
  feat(chat): show live token usage
  fix(storage): preserve messages during migration
  docs(readme): clarify portable installation

Keep only the sections that help reviewers. Mark an optional checkbox N/A when it does not apply;
do not claim a check was run when CI is expected to provide the evidence.
-->

## Summary

<!-- What changed, and what user/developer outcome does it produce? Prefer 2–4 bullets. -->

-

## Related issue

<!-- Use "Closes #123" when merging this PR should close the issue. Otherwise write "Related: #123". -->

Closes #

## Changes

<!-- Name the important implementation decisions, not every edited file. -->

-

## Scope

- [ ] WinUI app, XAML, or window-bound services
- [ ] Core models, runtime, policies, or view models
- [ ] SQLite schema, migrations, or repositories
- [ ] Wire protocol, signing, or WebSocket behavior
- [ ] Notifications, tray, activation, or deployment behavior
- [ ] Build, dependencies, CI, trimming, or release packaging
- [ ] Tests or test infrastructure
- [ ] Documentation only

## Validation

<!--
Check only what was actually run. Add the exact command, result, and any relevant artifact below.
Standalone test projects normally do not need -p:Platform=x64; see AGENTS.md for the output-path trap.
-->

- [ ] Release/x64 solution restore and build
- [ ] Protocol unit tests
- [ ] Core unit and architecture tests
- [ ] SQLite integration tests
- [ ] Protocol conformance gate
- [ ] Published-executable FlaUI smoke tests
- [ ] Trimming audit / trimmed runtime checks
- [ ] Manual affected-flow check
- [ ] Not run locally — reason recorded below

| Command or scenario | Result / evidence |
|---|---|
|  |  |

## Risk and compatibility

<!-- Explain failure modes, migration/rollback behavior, and compatibility impact. Write "None" if appropriate. -->

- User data / schema migration:
- Protocol compatibility:
- Trimming / serialization:
- Packaging / deployment:
- Rollback plan:

## UI and accessibility evidence

<!-- Delete this section when the PR has no visible or interactive change. -->

- [ ] Before/after screenshots or recording attached
- [ ] Narrow-window/responsive behavior checked
- [ ] Light and dark themes checked where relevant
- [ ] English and Simplified Chinese checked where relevant
- [ ] Keyboard navigation, focus, and screen-reader names checked
- [ ] Text scaling and long-content behavior checked

## Author checklist

- [ ] The PR title follows the repository's Conventional Commit format.
- [ ] Commits are focused and do not include unrelated generated or local files.
- [ ] New logic is on the correct side of the App/Core/Protocol boundary.
- [ ] Behavior changes include relevant tests or an explanation of why automation is impractical.
- [ ] User-facing strings use resources, with en-US/zh-CN key parity preserved.
- [ ] New interactive XAML elements expose stable automation IDs and accessible names.
- [ ] Schema changes append a new migration and bump `LatestVersion`; released migrations were not edited.
- [ ] JSON changes use `WireJson` or the appropriate source-generated context and remain trim-safe.
- [ ] Package identity (`Name` and `Publisher`) is unchanged, unless the data-migration impact is explicitly approved.
- [ ] Architecture, protocol, test, trimming, or user documentation was updated where needed.
- [ ] No secrets, recovery phrases, private keys, invite codes, user data, or sensitive logs are included.

## Reviewer notes

<!-- Known limitations, deferred work, areas needing special attention, or useful review order. -->
