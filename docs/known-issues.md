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

## Only one run at a time

Deliberate — two indexers on one machine would contaminate both sets of memory figures. The UI
refuses a second start; there is no queue.

## No cancellation mid-batch

Cancelling waits for the current `Add()` to finish. With a 75 MB batch that can be a minute or more.
