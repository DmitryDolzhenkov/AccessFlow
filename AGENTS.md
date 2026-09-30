```markdown
# Mission

Implement AccessFlow incrementally from the approved specification.

The goal is not to maximize code volume.
The goal is to implement the smallest correct solution satisfying
the current issue acceptance criteria.

# Sources of truth

Priority:

1. Current issue
2. docs/prd.md
3. docs/adr/*
4. Existing tests
5. Existing code

If these sources conflict, do not guess.
Report the conflict.

# Working rule

Work on ONE issue at a time.

Do not implement future issues unless required for the current
acceptance criteria.

# Before implementation

1. Read the current issue.
2. Read relevant parts of the PRD.
3. Inspect existing implementation.
4. State assumptions.
5. Propose a short implementation plan.

# Architecture constraints

- Modular monolith.
- PostgreSQL.
- EF Core.
- No message broker.
- No MediatR.
- No AutoMapper.
- No additional production dependency without explicit approval.
- Keep business rules outside HTTP controllers.
- Prefer standard .NET capabilities over new frameworks.

# Scope control

Do not:

- refactor unrelated code;
- redesign working components without a requirement;
- introduce generic frameworks for a single use case;
- implement speculative extensibility;
- add features not mentioned in the current issue.

# Verification

Before considering an issue complete:

1. Run build.
2. Run relevant tests.
3. Run the complete test suite.
4. Compare implementation against every acceptance criterion.
5. Report any criterion without evidence.

# Completion report

Provide:

- summary;
- files changed;
- tests added;
- commands executed;
- acceptance criteria evidence;
- assumptions;
- remaining risks.

# Progress

Update docs/progress.md after completing an issue.

Do not mark an issue complete if tests fail.

# Human decisions

Stop and request human decision when:

- requirements are ambiguous;
- architecture boundaries must change;
- a new dependency is needed;
- a business rule is missing;
- security semantics are unclear;
- the implementation requires changing another issue's scope.
```

## Agent skills

### Issue tracker

Issues live in GitHub Issues for DmitryDolzhenkov/AccessFlow, managed via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.