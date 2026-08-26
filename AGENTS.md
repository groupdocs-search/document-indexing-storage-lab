# IndexingStorageLab

Standalone .NET 10 / Blazor Server profiling tool for **GroupDocs.Search index storage**. Runs a
bounded slice of a document corpus through the indexer, records the run as a **session**, and breaks
the resulting index folder down by where its bytes actually went.

It exists because an index folder is opaque: the only way we could diagnose a customer's 38 GB index
was because they pasted a directory listing into a forum thread. This tool does that analysis
automatically, and stores enough provenance that the same corpus can be re-run on a future Search
release and compared.

It is **not** a search demo and not a benchmark of query speed. It indexes, measures, and stops.

## Commands

```
build.bat                             # dotnet build IndexingStorageLab.sln -c Release
run.bat                               # build + launch the UI on http://127.0.0.1:5090/
test.bat                              # offline test suite
run-profile.bat                       # list headless profiles
run-profile.bat smoke                 # 2 GB sanity run
run-profile.bat corpora-30pct         # the real 90 GB run

dotnet test tests/IndexingLab.Tests                        # same as test.bat
dotnet run --project src/IndexingLab.Web -c Release
dotnet run --project src/IndexingLab.Cli -c Release -- smoke
```

The test suite needs no corpus, no licence and no network. It builds synthetic index folders in the
temp directory and asserts against them, and it never loads a GroupDocs assembly at run time.

## Architecture

- `src/IndexingLab.Core` — everything that does work, with no UI dependency.
  - `CorpusWalker` — bounded, reproducible, **stratified** corpus selection.
  - `IndexRunner` — executes one run and keeps its `SessionRecord` current.
  - `IndexStorageAnalyzer` — the `.body` / `.head` / dictionary / metadata breakdown.
  - `MemorySampler` — background peak sampling and retained-after-forced-GC measurement.
  - `SessionRecord` — the durable schema.
  - `SessionStore` — atomic JSON persistence, one file per session.
- `src/IndexingLab.Web` — Blazor Server host, four screens, plus `RunnerService`, the singleton
  that owns the in-flight run.
- `src/IndexingLab.Cli` — the same core, headless, driven by named profiles in its `appsettings.json`.
- `tests/IndexingLab.Tests` — offline suite over the analyzer, the walker and the session store.

## Critical invariants — do not regress

1. **A run must survive the UI.** `RunnerService` is a singleton; nothing about a run's lifetime may
   depend on a Blazor circuit. Runs take hours — closing the browser, reloading the page, or losing
   the websocket must not stop or corrupt one. The UI only ever *reads* runner state and the session
   store.

2. **`SessionRecord` is append-only.** A future compare-sessions feature will read these files and
   nothing else. Add fields; never repurpose or remove one; bump `SchemaVersion` when the shape
   changes. Anything needed to judge whether two runs are comparable belongs in the record —
   product version, runtime, GC mode, licence state, corpus fingerprint, full configuration.

3. **Sampling is stratified by default.** A sorted sequential walk of a corpus organised by format
   fills the budget from the alphabetically first folder and never reaches the rest. On
   `d:\Storage\Aspose` that produced a **57.9%** size ratio from an HTML-only slice where the
   stratified mix gave **15.3%**. A ratio taken from an unrepresentative slice is worse than no
   ratio, because it looks like an answer. `CorpusWalkerTests` pins both modes.

4. **The vocabulary derivation is `.head` bytes ÷ 4.** Each `.head` is a flat `int[]` indexed by
   term id — `SegmentInFile.GetOffset` reads at `termIndex * sizeof(int)` — so every segment carries
   a slot for *every term in the whole index*. That is where vocabulary size and the recoverable
   waste figure both come from. If the product's segment format changes, this breaks silently and
   the analyzer will report confident nonsense.

5. **An unlicensed run measures nothing.** Trial mode caps the document count while the tool still
   produces a full, convincing session. `EnvironmentInfo.IsLicensed` is recorded and the UI marks
   such sessions. Never present figures from one.

6. **Retained memory is measured after a forced blocking collection.** An unforced
   `GC.GetTotalMemory` reading counts uncollected garbage, not residency. Getting this wrong once
   produced a "sharding cuts resident memory 7.7×" conclusion that was pure artifact.

7. **Peak private bytes is sampled on a timer, not at batch boundaries.** Batch sizes differ between
   configurations, so a peak recorded only at commit points measures the batching cadence and
   flatters whichever run commits least often.

8. **Images and archives are excluded by default.** They yield no text without OCR, so indexing them
   inflates the source side of the ratio while contributing nothing to the index.
   `d:\Storage\Aspose` holds 38.9 GB of images.

9. **`InvariantGlobalization` stays off.** Under invariant mode legacy `.xls` extraction raises
   `CultureNotFoundException` for every such file, which shows up as extraction errors and a
   silently smaller index — indistinguishable from a product regression. See `Directory.Build.props`.

10. **`Optimize()` failure is a result, not a crash.** It takes the same deep-copy path as indexing
    and allocates an `int[vocabulary]` per merge step, so at scale it can exhaust memory. The
    failure is recorded in `SessionRecord.OptimizeFailureMessage` and the session still completes.

## Environment

Licence is read from `GROUPDOCS_LIC_PATH` (full path to a `.lic`), falling back to `LIC_PATH`
(a licences *folder*, the internal convention). See `.env.example`.

Paths this machine uses, configured in the CLI profiles and the web defaults:

| What | Where |
|---|---|
| Corpus | `d:\Storage\Aspose` — 894,327 files, 415.2 GB, by format |
| Sessions | `d:\Storage\Output\indexing-lab-sessions` |
| Index output | `d:\Storage\Output\indexing-lab-index\<profile>` |
| UI port | **5090**, not 5080 |

**Port 5090 is deliberate.** `AsposeNetDocumentProcessor` owns 5080 on this machine. When both used
5080, `run.bat` opened a browser at the *other* application while this one failed to bind, and the
bind error scrolled past unnoticed. `run.bat` now refuses to start if the port is taken and names
the process holding it; pass a port to override (`run.bat 5091`).

`d:\Storage\govdocs\zipfiles` holds the raw Digital Corpora set (1000 archives, 310.6 GB) if a
non-extracted corpus is ever needed. The walker takes any folder; nothing assumes a layout.

## Conventions

- Explicit types over `var`, matching the existing code.
- The GroupDocs.Search version is pinned once in `Directory.Packages.props`. Central Package
  Management is on — do not put `Version=` on a `PackageReference`.
- `using Index = GroupDocs.Search.Index;` is required in any file using `Index`; implicit usings
  pull in `System.Index` and the reference is otherwise ambiguous.
- Sessions and index output live outside the repo. Never commit either.
