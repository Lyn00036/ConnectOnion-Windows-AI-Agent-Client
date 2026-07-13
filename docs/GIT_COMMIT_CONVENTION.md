# Git Commit Convention

This project uses Conventional Commits.

## Format

```text
<type>(<scope>): <summary>
```

Examples:

```text
feat(chat): add streaming status display
fix(storage): handle missing SQLite database directory
docs(readme): update desktop setup instructions
test(agent): add connection validation coverage
```

## Types

- `feat`: new user-facing or developer-facing functionality
- `fix`: bug fix
- `docs`: documentation changes
- `test`: tests or testing utilities
- `refactor`: code restructuring without behavior changes
- `style`: formatting-only changes
- `chore`: dependency or maintenance work
- `build`: packaging or build configuration
- `ci`: CI workflow changes

## Recommended Scopes

- `electron`: Electron main process, windows, preload, IPC
- `chat`: chat UI and ConnectOnion SDK interaction
- `agent`: agent configuration and connection management
- `sessions`: local conversation/session history
- `storage`: SQLite schema, repositories, migrations
- `settings`: desktop preferences
- `credentials`: secure credential handling
- `files`: drag-and-drop files and attachments
- `ui`: shared UI components and layout
- `docs`: project documentation
- `ci`: GitHub Actions and validation workflows
- `build`: Windows packaging and release output

## Good Examples

```text
feat(agent): save agent configuration in SQLite
feat(sessions): add local conversation list
feat(files): add attachment drop zone
fix(chat): show failed connection error
refactor(electron): split storage IPC handlers
test(storage): cover migration status query
ci(github-actions): run frontend lint and build
```

## Branch Names

```text
feature/CON-12-agent-config
feature/CON-46-session-history
bugfix/CON-33-connection-error
refactor/sqlite-repositories
```

## Pull Requests

Use the same convention for PR titles:

```text
feat(chat): display agent execution status
```

PR descriptions should include:

- Summary
- Related ticket
- Main changes
- Testing evidence
- Known limitations

## Rules

- Keep commits small and focused.
- Reference Jira tickets when available.
- Do not mix unrelated modules in one commit.
- Run relevant validation before opening a PR.
