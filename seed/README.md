# seed/

`mindattic-site.idealist` — the MindAttic site as content: **53 pages, 86 component-metadata rows,
7 settings and 34 media payloads** in one file. This is the artifact that turns an empty deployment
into the real site ([BIBLE §4.9](../docs/BIBLE.md#M.IDEA-§4.9)).

```pwsh
dotnet run --project src/IdeaCms.Blazor -- --seed core                        # schema + the 53 library .ideas
dotnet run --project src/IdeaCms.Blazor -- --import-idealist seed/mindattic-site.idealist
```

Re-runnable: pages reconcile on uid then slug (so the baseline seed's own pages are **adopted**, not
duplicated) and media is matched by SHA-256, so a second import moves no bytes. Add `--dry-run` to see
what it would do first ([BIBLE §4.9](../docs/BIBLE.md#M.IDEA-§4.9)).

**Why the payloads are in here rather than left to regenerate.** Most of the images are reproducible
— `tools/shoot` recaptures them from the source repos — but not all: the Prose Hub screenshot came
off a clipboard, and a re-capture produces different bytes anyway, so it gets a new uid and every page
referencing the old one would need regenerating. A seed that only half-restores is not a seed. The
file is ~18 MB and that is the price of it actually working on a clone.

It is also what a second, vanilla deployment of Ideas would restore from to get to a known state —
`--import-idealist seed/mindattic-site.idealist --prune` ([M.IDEA-LAW-11](../docs/BIBLE.md#M.IDEA-LAW-11)).

## Refreshing it

```pwsh
dotnet run --project src/IdeaCms.Blazor -- --export-idealist seed/mindattic-site.idealist
```

Verified by restoring into an empty database before committing — 48 pages created, 5 adopted, 34
media uploaded, and every `/_media/{uid}` on the restored pages answering 200. Do that check again if
you refresh it; an unverified seed is worse than none.
