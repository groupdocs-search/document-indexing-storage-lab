# Tech debt

Deliberate compromises, with the reason attached so they are not "fixed" back into the problem they
avoid.

## Sessions are files, not a database

One JSON file per run in a plain folder. A database would give querying and cheap comparison, but
sessions are artifacts we want to copy between machines, diff in a review, and commit next to a
report. The folder is the feature. Revisit only if session count reaches thousands.

## `SessionStore.List()` deserialises every file

Fine for the hundreds of sessions this will realistically hold. If it becomes slow, add a small
header-only read rather than a database.

## The runner is a singleton with no queue

Two concurrent indexers would contaminate each other's memory measurements, so concurrency is not a
missing feature — it is excluded on purpose. If batch runs are ever wanted, they should be
*sequential* through the same singleton.

## Timeline is a table, not a chart

Charting means a JS dependency and a build step for a tool whose primary consumer reads the JSON.
The data is all there. Add charts when someone actually wants them in the browser.

## The web UI has no authentication

It binds to `127.0.0.1` and drives local file paths. It must not be exposed on a network interface;
if that is ever needed, authentication comes first.

## `RunnerService.Start` returns an id it does not use

The real session id is assigned inside `IndexRunner.Run` once the record is created. The pre-computed
id is vestigial and should be removed when the start path is next touched.

## Index-size sampling walks the folder each batch

Cheap relative to extraction, and it avoids having to track file creation from inside the product.
If segment counts grow into the thousands, cache per-file sizes and re-stat only what changed.
