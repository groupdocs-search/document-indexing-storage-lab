# Backlog

Wanted capability, ordered by what unblocks the most.

## 1. Compare sessions

The reason the schema carries so much provenance. Pick two sessions, diff their storage breakdowns
and memory figures side by side, and **refuse or loudly warn** when they are not comparable:
different `SearchAssemblyVersion`, different `SelectionHash`, different GC mode, one unlicensed.

The comparison that matters most: same corpus slice, two GroupDocs.Search versions.

## 2. Version sweep

Given a list of package versions, run the same profile against each and produce one table. Needs the
package version to be settable per run rather than pinned in `Directory.Packages.props`, which means
either separate output folders per version or an assembly-load shim.

## 3. Timeline charts

Index bytes, private bytes and segment count over elapsed time. The data is already in the session.

## 4. Export a report

One command that turns a session — or a comparison — into a Markdown summary suitable for pasting
into a forum reply or a ticket. Most sessions exist to be quoted somewhere.

## 5. Per-format ratio breakdown

Index the same byte budget of each format separately and report the ratio per format. This is the
direct answer to "what should I expect my index to cost", and the current single-number ratio is a
weighted average that hides it.

## 6. Vocabulary sampling

Report the longest terms and a sample of the vocabulary, so junk terms — hex, base64, GUIDs — are
visible rather than inferred from a large term count.

## Rejected

- **Search-performance benchmarking.** Different question, different tool. Mixing query timing into
  a storage profiler makes both harder to trust.
- **Automatic ticket filing.** Findings here need human judgement before they become tickets.
- **Running multiple indexers in parallel.** Would contaminate the memory measurements this tool
  exists to produce.
