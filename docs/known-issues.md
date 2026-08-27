# Known issues

What is currently wrong, missing, or misleading. **Check a surprising result against this before
investigating it.**

## The size ratio depends on corpus mix more than on the index

An HTML-only slice reports ~58%; a mixed slice of the same corpus reports ~15%. This is not a bug
and not an index-efficiency signal — text-dense formats simply produce more index per source byte.
Never quote a ratio without the `ByExtension` breakdown from the same session.

## `Optimize()` may exhaust memory on a large index

It takes the same deep-copy path as indexing and allocates an `int[vocabulary]` per merge step. At a
238 M-term vocabulary that is a ~953 MB allocation per step. A failure is captured in
`SessionRecord.OptimizeFailureMessage` and the session still completes with its
before-optimize breakdown intact.

## Extracted-text ratio needs text storage

`RatioToExtractedText` is null unless `UseTextStorage` was on, because extracted text volume is only
observable through the text archive. Enabling it grows the index; the analyzer subtracts the archive
from the numerator, but the run is no longer directly size-comparable with one that had it off.

## `.head` heuristic is an inference, not an API

Vocabulary size comes from dividing the largest `.head` file by four. That follows from the segment
format — a flat `int[]` indexed by term id — and is pinned by tests. If a future GroupDocs.Search
release changes the offset table layout, the analyzer will keep reporting a number and the number
will be wrong. Re-verify after any Search version bump.

## Text-archive detection is path-based

`IndexStorageAnalyzer.IsTextArchive` classifies a file as text storage when its relative path
contains "text". If the product changes where the archive lives, those bytes fall into `OtherBytes`
and the extracted-text ratio silently disappears.

## Index bytes are measured per batch by walking the folder

On a large index with many segments this costs a directory enumeration per batch. It is small
against extraction time, but it does grow with segment count.

## The browser helper cannot identify the application

`run.bat` waits for the port to accept a connection, then opens a browser. It has no way to check
*whose* application answered. That is why the port is checked for being free before the host starts
— without that guard, another app already on the port gets opened instead and the real failure (our
Kestrel unable to bind) scrolls past. If you override the port, the same guard applies.

## A killed process leaves a session stuck at "Running"

The session record is flushed periodically, so a host that is killed rather than shut down leaves
its last flush on disk with `Status: Running` and no completion. Nothing later corrects it: the
sessions list will show it as running forever. Treat a "Running" session with no live run behind it
as interrupted, and read its timeline for how far it actually got.

## Static assets and the content root

Two hosting details caused a UI that rendered but did nothing, and both are guarded in `Program.cs`
with the reason attached. Do not remove either guard.

- `UseStaticWebAssets()` is required for `dotnet run -c Release` (Production from source, where
  `blazor.web.js` lives outside wwwroot) but breaks a **published** app, where there is no runtime
  manifest and calling it empties the web root. It is called only when the manifest file exists.
- The content root defaults to the *current working directory*. A published exe launched from
  elsewhere resolves wwwroot to the wrong place and 404s every static file. `Program.cs` pins the
  content root to the executable's folder when it detects publish output.

Symptom for both: the page renders through static SSR and every button is inert, which looks like a
UI defect rather than a hosting one. Check `_framework/blazor.web.js` returns 200 first.

## Kestrel endpoint configuration overrides --urls

`appsettings.json` uses the `Urls` key deliberately. A `Kestrel:Endpoints` block takes precedence
over both `--urls` and `ASPNETCORE_URLS`, which silently defeats `run.bat <port>`.

## A running host blocks the next build

The web host holds `IndexingLab.Core.dll` for as long as it runs, so starting a second one with
`dotnet run` fails on `MSB3027: the file is locked by IndexingLab.Web (<pid>)` after a long silent
MSBuild retry. The symptom reads as "run.bat does not start the web app".

This is ordinary file locking, not a defect: a web host is meant to keep running until you stop it.
`run.bat` checks for a live `IndexingLab.Web` process, not just a busy port, and offers to stop it —
a finished run is already persisted, so stopping is safe. `stop.bat` does the same on its own.

**There is no thread leak.** That was checked rather than assumed: the CLI runner performs a full
run including `Optimize()` and exits with code 0, so nothing created during indexing keeps a
process alive. If a host is resident after its run, it is simply still serving.

## A listener can die without the process noticing

Observed once, after the machine slept for eight hours mid-run: the host kept indexing correctly and
finished, but its HTTP listener was gone and nothing was logged — no Kestrel error, no shutdown
message. Port 5090 read as free while the process was very much alive.

Consequence: a port check alone cannot tell you whether a host is healthy. That is why `run.bat`
checks for the process. If the UI stops responding but the log keeps advancing, the run is fine —
stop the host once it finishes and read the session file, which has everything.

Root cause not established; suspend/resume breaking the listening socket is the leading candidate.

## Only one run at a time## Only one run at a time

Deliberate — two indexers on one machine would contaminate both sets of memory figures. The UI
refuses a second start; there is no queue.

## No cancellation mid-batch

Cancelling waits for the current `Add()` to finish. With a 75 MB batch that can be a minute or more.
