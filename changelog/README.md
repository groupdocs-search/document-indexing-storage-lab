# Changelog

One file per version, numbered and never renumbered: `NNNN-short-slug.md`.

The number is the ordering; the slug is for humans scanning the directory. A file is appended to
while its version is unreleased and frozen once the version ships — corrections after that go in the
next file, not by rewriting an old one.

| File | Version | Date | Summary |
|---|---|---|---|
| [0001-initial-version.md](0001-initial-version.md) | 0.1.0 | 2026-08-26 | First working lab: corpus walker, storage analyzer, session store, Blazor UI, headless CLI, offline test suite. |

## Sections

Each entry uses the [Keep a Changelog](https://keepachangelog.com/) headings, in this order, and
omits any that are empty:

- **Added** — new capability that was not there before.
- **Changed** — behaviour that already existed and now behaves differently.
- **Fixed** — a defect corrected.
- **Removed** — capability taken out.
- **Measured** — findings this version produced. This tool's output *is* its product, so results
  belong in the changelog alongside the code that produced them.
- **Known limits** — what this version knowingly does not do. Detail lives in
  [`docs/known-issues.md`](../docs/known-issues.md); the entry states only what changed.

## Rules

- Write for the operator, not the compiler. "A partial budget now draws from every subfolder, because
  a sorted walk was reporting an HTML-only ratio as if it described the corpus" beats "changed
  `SamplingMode` default".
- State what a measurement means, not just its value.
