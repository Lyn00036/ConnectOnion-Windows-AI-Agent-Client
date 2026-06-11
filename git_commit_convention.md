# Git Commit Convention

## Purpose

This document defines the Git commit standards used by the project team.

Goals:

- Keep commit history clean and readable
- Improve collaboration efficiency
- Support Agile development workflow
- Enable automated changelog generation
- Facilitate code review and issue tracking

---

# General Principles

## One Commit = One Change

Each commit should represent a single logical change.

✅ Good

feat(auth): add JWT authentication 

❌ Bad

fix everything 

---

## Commit Frequently

Do not accumulate a large number of changes before committing.

Recommended:

- Complete a small feature
- Fix a bug
- Refactor a module
- Add tests

Then commit immediately.

---

## Commit Message Format

We follow the Conventional Commits standard.

text <type>(<scope>): <summary> 

Example:

feat(auth): implement user login endpoint 

---

# Commit Types

## Feature

New functionality.

feat(auth): add password reset API 

---

## Fix

Bug fixes.

fix(chat): resolve websocket reconnect issue 

---

## Refactor

Code restructuring without changing behavior.

refactor(database): simplify repository layer 

---

## Docs

Documentation changes.

docs(readme): update installation guide 

---

## Test

Adding or modifying tests.

test(auth): add login integration tests 

---

## Style

Formatting only.

style(frontend): apply prettier formatting 

---

## Chore

Maintenance work.

chore(deps): update dependencies 

---

## Build

Build-related changes.

build(docker): update production image 

---

## CI

CI/CD configuration changes.

ci(github-actions): add deployment workflow 

---

## Perf

Performance improvements.

perf(api): optimize database query 

---

## Revert

Rollback a previous commit.

revert(auth): revert JWT refresh token changes 

---

# Scope Naming

Recommended scopes:

## Backend

text auth agent chat database api websocket notification user 

Examples:

feat(agent): support agent discovery fix(database): resolve connection leak 

---

## Frontend

text ui chat settings sidebar dashboard login 

Examples:

feat(chat): add message streaming UI fix(sidebar): correct navigation state 

---

## Desktop Client

text electron windows storage tray updater 

Examples:

feat(electron): implement auto update fix(storage): prevent session corruption 

---

# Jira Integration

Include Jira ticket IDs whenever possible.

Format:

feat(auth): add login endpoint [PROJ-123] 

or

PROJ-123 feat(auth): add login endpoint 

Example:

CON-45 feat(agent): implement agent connection workflow 

---

# Commit Examples

## Good Examples

feat(agent): add agent connection service  feat(chat): implement streaming response support  fix(auth): handle expired refresh tokens  refactor(database): separate repository layer  test(api): add user endpoint tests  docs(readme): update setup instructions  ci(github-actions): add backend test pipeline 

---

## Bad Examples

update  fix  123  work  final version  new changes  bug fixed 

---

# Branch Naming Convention

## Feature Branch

text feature/<ticket-id>-<description> 

Examples:

text feature/CON-12-agent-chat feature/CON-45-user-authentication 

---

## Bug Fix Branch

text bugfix/<ticket-id>-<description> 

Examples:

text bugfix/CON-33-login-error 

---

## Hotfix Branch

text hotfix/<description> 

Examples:

text hotfix/database-connection 

---

## Refactor Branch

text refactor/<description> 

Examples:

text refactor/service-layer 

---

# Pull Request Rules

## PR Title

Use the same format as commit messages.

Example:

feat(chat): implement agent message streaming 

---

## PR Description Template

markdown ## Summary  Brief description of the changes.  ## Related Ticket  CON-45  ## Changes  - Added agent connection module - Added websocket support - Added integration tests  ## Testing  - Unit tests passed - Manual testing completed  ## Screenshots  Attach screenshots if applicable. 

---

# Agile Workflow

## Sprint Planning

Create Jira tickets.

Example:

text CON-45 Agent Connection CON-46 Chat History CON-47 User Settings 

---

## Development Flow

text Backlog     ↓ Sprint     ↓ Create Branch     ↓ Development     ↓ Commit     ↓ Push     ↓ Pull Request     ↓ Code Review     ↓ Merge     ↓ Deploy 

---

# Recommended Rules

## Must

✅ Use meaningful commit messages

✅ Keep commits small

✅ Reference Jira tickets

✅ Open Pull Requests for review

✅ Pass all CI checks

---

## Must Not

❌ Commit directly to main

❌ Push broken code

❌ Use vague commit messages

❌ Mix unrelated changes in one commit

❌ Skip code review

---

# Example for ConnectOnion Desktop Client

feat(agent): implement agent address validation  feat(chat): support streaming agent responses  feat(storage): save conversation history locally  fix(electron): resolve window restore issue  refactor(api): extract agent service layer  test(chat): add websocket integration tests  ci(github-actions): add desktop build pipeline 

Following this convention ensures a clean Git history, easier collaboration, traceable Agile development, and efficient code reviews.