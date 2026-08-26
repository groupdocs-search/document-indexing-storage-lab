# Indexing Storage Lab

Internal profiling tool for **GroupDocs.Search for .NET**. Runs a bounded slice of any document
corpus through the indexer, records everything about the run as a **session**, and breaks the
resulting index down by where its bytes actually went.

Built to answer a customer question — *why is the index 50–94% of the source size?* — but designed
to outlive it: every session records the product version, runtime and GC mode, so the same corpus
can be re-run on a future Search release and the results compared.

## Why it exists

An index folder is opaque. The only way we could diagnose a customer's 38 GB index was because they
pasted a directory listing into a forum thread. This tool does that analysis automatically:

| Component | What it is |
|---|---|
| `index*.body` | Postings — the real inverted index |
| `index*.head` | Term-offset tables: **4 bytes per term, per segment** |
| `index1.info` | Term dictionary |
| `index.info` | Per-document metadata |

The `.head` files are the interesting part. Each is a flat `int[]` indexed by term id, so every
segment carries a slot for *every term in the whole index* — present in that segment or not. That
gives vocabulary size for free (largest `.head` ÷ 4) and makes the recoverable waste measurable.

## First results

A 199 MB stratified sample of `d:\Storage\Aspose` (mixed .xls/.ppt/.doc/.pdf/.txt/.html):

| | before `Optimize()` | after |
|---|---:|---:|
| Index total | 30.4 MB | **25.5 MB** |
| Ratio to source | 15.3% | **12.8%** |
| Segments | 4 | 1 |
| `.head` | 3.22 MB | 1.17 MB |

Two things already worth knowing:

- **`Optimize()` recovered exactly the predicted 2.05 MB.** `RecoverableTermOffsetBytes` is computed
  as `TermOffsetBytes - 4 × vocabulary`, and the measured saving matched it to the byte.
- **The size ratio is dominated by the corpus mix, not by index efficiency.** The same tool on an
  HTML-only slice gave **57.9%**; the mixed slice gave **15.3%**. Text-dense formats produce a lot of
  index per byte of source; PDFs and presentations produce very little. Any "index as a % of source"
  figure is meaningless without knowing the format mix behind it.

## Running it

Set a licence first — an unlicensed run is document-capped and every figure becomes meaningless.
The session records whether one was applied. See [`.env.example`](.env.example).

```bat
setx GROUPDOCS_LIC_PATH "C:\path\to\GroupDocs.Total.lic"
```

### Quick start

```bat
build.bat                        :: build everything in Release
run.bat                          :: launch the UI and open a browser at 127.0.0.1:5080
test.bat                         :: offline test suite
run-profile.bat                  :: list the headless profiles
run-profile.bat smoke            :: 2 GB sanity run
run-profile.bat corpora-30pct    :: the real 90 GB run
```

### Web UI

`run.bat`, or directly:

```bash
dotnet run --project src/IndexingLab.Web -c Release
# http://127.0.0.1:5080
```

Three pages: **Sessions** (history), **New run** (configure and start), **Live** (log + cancel).
The run lives in a hosted service, not in the Blazor circuit — closing the browser does not stop it.

### Headless

For multi-hour runs where you would rather not keep a web host alive:

```bash
dotnet run --project src/IndexingLab.Cli -c Release -- corpora-30pct
```

Profiles live in `src/IndexingLab.Cli/appsettings.json`. Sessions land in the same folder the web UI
reads, so CLI runs appear in the UI list.

| Profile | What it measures |
|---|---|
| `tiny` | 200 MB — pipeline check, finishes in a minute |
| `smoke` | 2 GB — end-to-end sanity |
| `corpora-20pct` / `corpora-30pct` | The real runs: 60 GB / 90 GB baseline |
| `digits-as-separators` | Same slice with `0123456789+/=` as separators — vocabulary reduction |
| `text-storage-ratio` | Text storage on, to measure extracted-text volume |

## Sessions

One JSON file per run, written atomically and flushed every few batches so a killed multi-hour run
still leaves usable data. `SessionRecord` is an append-only schema (`SchemaVersion`) because a future
compare-sessions feature will read these and nothing else.

Each session carries what is needed to judge whether two runs are comparable at all:

- **GroupDocs.Search assembly version**, runtime, OS, architecture, core count
- **Server GC on/off**, build configuration, licence applied
- **Corpus fingerprint** — file count, bytes, per-extension mix, and a SHA-256 over the ordered
  selection, so "these two runs indexed the same files" is checkable rather than assumed
- Full configuration, so a run can be replayed
- Timeline samples, memory peaks, retained heap after a forced collection
- Storage breakdown before and after `Optimize()`

## Configuration notes

**Sampling is stratified by default.** A sorted sequential walk of a corpus organised by format
fills the budget from the alphabetically first folders and never reaches the rest — a 90 GB budget on
`d:\Storage\Aspose` would be all HTML and PDF. Stratified round-robins across the immediate
subfolders so the mix stays representative at any budget. Set `SamplingMode: "Sequential"` if you
specifically want the sorted walk.

**Images and archives are excluded by default.** They yield no text without OCR, so indexing them
inflates the source side of the ratio while contributing nothing to the index. `d:\Storage\Aspose`
holds 38.9 GB of images — including them would distort the headline number badly.

**Runs are bounded by a size budget with a file-count guard.** Size is primary because the question
is index-size-as-a-share-of-source, so a size budget makes the ratio directly interpretable and the
run duration predictable.

**`Optimize()` at the end is a config flag.** It merges segments, which collapses the `.head`
overhead — but it takes the same deep-copy path as indexing and allocates an `int[vocabulary]` per
merge step. At large vocabularies it can fail; the failure is recorded in the session rather than
losing the whole run.

## Layout

```
src/IndexingLab.Core/
  CorpusWalker.cs           bounded, reproducible, stratified corpus selection
  IndexRunner.cs            executes a run, keeps the session updated
  IndexStorageAnalyzer.cs   the .body/.head/dictionary breakdown
  MemorySampler.cs          background peak sampling, retained-after-GC measurement
  SessionRecord.cs          the durable schema
  SessionStore.cs           atomic JSON persistence
src/IndexingLab.Web/        Blazor Server UI + hosted runner
src/IndexingLab.Cli/        headless runner + profiles
tests/IndexingLab.Tests/    offline suite - no corpus, no licence, no network
```

## Documentation

| Document | What it is |
|---|---|
| [AGENTS.md](AGENTS.md) | Invariants, commands, architecture. The single source of truth for coding agents. |
| [docs/known-issues.md](docs/known-issues.md) | What is currently wrong or misleading. Check a surprising result here first. |
| [docs/tech-debt.md](docs/tech-debt.md) | Deliberate compromises and why. |
| [docs/backlog.md](docs/backlog.md) | Wanted capability, and what was rejected. |
| [changelog/](changelog/) | One numbered file per version, with the findings each produced. |

## Not done yet

- **Compare sessions.** The schema is built for it; the feature is not written. See
  [docs/backlog.md](docs/backlog.md).
- **Charts.** The timeline is a table; the JSON is there if you want to plot it.

## Licence

Sample code: [MIT](LICENSE). GroupDocs.Search is a commercial product with
[its own terms](https://purchase.groupdocs.com/policies/).
