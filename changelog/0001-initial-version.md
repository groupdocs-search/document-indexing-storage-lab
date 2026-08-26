# 0001 — Initial version

**Version 0.1.0 — 2026-08-26**

First working profiling tool. Built to answer a customer question — *why is the index 50–94% of the
source size?* — and to keep answering it across future GroupDocs.Search releases.

## Added

- **Corpus walker.** Takes any folder, enumerates recursively regardless of layout, and stops at a
  size budget with a file-count guard. Sorted traversal, so an unchanged tree yields the same
  selection on every run.
- **Stratified sampling.** Round-robins across the immediate subfolders so a partial budget still
  reflects the corpus format mix.
- **Index storage analyzer.** Breaks a finished index folder into postings (`index*.body`),
  term-offset tables (`index*.head`), term dictionary (`index1.info`), document metadata
  (`index.info`), text archive and dictionary side-files. Derives vocabulary size from the largest
  `.head` divided by four, and computes how much of the offset overhead a full merge would recover.
- **Session records.** One JSON file per run, written atomically and flushed every five batches.
  Carries the Search assembly version, runtime, OS, core count, Server GC state, licence state,
  build configuration, the full run configuration, a SHA-256 corpus fingerprint, a timeline, memory
  peaks, retained-after-forced-GC figures, and the storage breakdown before and after `Optimize()`.
- **Blazor Server UI** on `http://127.0.0.1:5080/` — sessions list, session detail, new run, live
  log. The run lives in a singleton service, so it survives the browser closing.
- **Headless CLI** with named profiles, writing to the same session folder the UI reads.
- **Offline test suite** — 31 tests over the analyzer, walker and session store. No corpus, no
  licence, no network.
- `run.bat`, `test.bat`, `run-profile.bat`, `build.bat`.

## Measured

A 199 MB stratified sample of `d:\Storage\Aspose` (mixed .xls/.ppt/.doc/.pdf/.txt/.html), GroupDocs.Search
26.8.0, .NET 10, Release:

| | before `Optimize()` | after |
|---|---:|---:|
| Index total | 30.4 MB | 25.5 MB |
| Ratio to source | 15.3% | **12.8%** |
| Segments | 4 | 1 |
| `.head` | 3.22 MB | 1.17 MB |

- **The recoverable-waste prediction is exact.** `RecoverableTermOffsetBytes` computed 2.05 MB
  before the merge; `Optimize()` recovered 2.05 MB.
- **The size ratio is dominated by corpus mix, not index efficiency.** The same settings on an
  HTML-only slice gave **57.9%**; the mixed slice gave **15.3%**. Text-dense formats produce far
  more index per source byte than PDFs and presentations do. Any "index as a share of source"
  figure is uninterpretable without the format mix behind it.
- **12.8% sits inside the 10–33% band** the customer expected. The 94% figure that started the
  thread came from an unoptimised 740-segment index.

## Fixed

- **Web host would not start.** Its `appsettings.json` contained unescaped Windows path separators,
  so the configuration failed to parse. The project built cleanly the whole time, which is why it
  went unnoticed: nothing had actually launched it.
- **Port collision with AsposeNetDocumentProcessor.** Both used 5080. `run.bat` waits for the port
  to answer and cannot tell whose application replied, so it opened a browser at the other tool
  while this one failed to bind. Moved to 5090, and `run.bat` now refuses to start when the port is
  occupied, naming the process that holds it.

## Known limits

- No compare-sessions feature. The schema is designed for it; the UI is not written.
- The timeline is a table, not a chart.
- Extracted-text volume is only measurable when text storage is enabled, so the "index vs extracted
  text" ratio is unavailable on runs without it.
- Only one run at a time, deliberately: two indexers on one machine would contaminate both sets of
  memory figures.
