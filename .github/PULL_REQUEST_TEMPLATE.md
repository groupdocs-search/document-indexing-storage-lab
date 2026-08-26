## What this changes

<!-- One or two sentences. -->

## Does this affect what is measured?

- [ ] No — UI, docs, CI or tooling only.
- [ ] Yes — corpus selection, the storage analyzer, the memory sampler or the run loop changed.

**If yes:** sessions recorded before this change may no longer be comparable with sessions recorded
after it. Say so here, and bump `SessionRecord.SchemaVersion` if the record shape changed.

## Checklist

- [ ] `build.bat` is clean
- [ ] `test.bat` passes
- [ ] New backend behaviour has unit tests (standing constraint, not only for changed behaviour)
- [ ] No session files, index output, `.env` or `.lic` included
- [ ] Invariants in [AGENTS.md](../AGENTS.md) still hold — in particular: runs survive the UI,
      sampling stays stratified, retained memory is measured after a forced collection
