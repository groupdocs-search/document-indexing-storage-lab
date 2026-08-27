@AGENTS.md

Project rules and invariants live in [AGENTS.md](AGENTS.md) — the single source of truth for all
coding agents. Read it first.

| Document | What it is |
|---|---|
| [AGENTS.md](AGENTS.md) | Invariants that must not regress, commands, architecture. |
| [README.md](README.md) | What the tool is and how to run it. |
| [docs/known-issues.md](docs/known-issues.md) | What is currently wrong, missing, or misleading. **Check a surprising result against this before investigating it.** |
| [docs/tech-debt.md](docs/tech-debt.md) | Deliberate compromises, with the reason attached so they are not "fixed" back into the problem they avoid. |
| [docs/backlog.md](docs/backlog.md) | Wanted capability, ordered by what unblocks the most. Includes what was rejected and why. |
| [changelog/](changelog/) | One numbered file per version. Start at [0001-initial-version.md](changelog/0001-initial-version.md). |

## NEVER COMMIT

Do not run `git commit` or `git push` unless the user explicitly asks in that message. Finishing
work is not a request to commit it. Leave changes in the working tree and report what is ready.
Permission given for one commit does not carry to the next. See the box at the top of
[AGENTS.md](AGENTS.md).

Second standing constraint: **every backend capability gets unit tests**, not only changed
behaviour.
