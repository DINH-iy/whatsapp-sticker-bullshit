# Browser editor checks

Serve `editor-checks.html` from a temporary development host that also serves `/_content/StickerApp.Shared`. Open it in the built-in local browser; it reports PASS or the first failed assertion. No external browser or automation dependency is needed.

For an isolated check, create `/tmp/stickit-editor-check/wwwroot`, copy this HTML there, build `StickerApp.Web`, then run its DLL with `ASPNETCORE_ENVIRONMENT=Development`, `--contentRoot /tmp/stickit-editor-check`, and a free localhost URL. Open `/editor-checks.html` at that URL. The temporary content root keeps manual UI-test imports separate from the personal library. Do not add the check page to production assets.

The page generates no library writes. It checks actual image/animation modules with a two-frame fixture, including cutout masks on every frame, recoverable opacity, PNG/replayed-frame agreement, animated WebP re-editing, partial transparency, layer apply/undo/redo/export/reopen, and isolated worker/main-thread cancellation. The standard storage checks separately verify saved composition data.

Animation-to-layer handoff checks verify transformed frame pixels and timing, working animations above the final 500 KB budget, and cancellation after main-thread encoding has begun. Final sticker export checks retain their existing size constraints.

`video-timing-checks.mjs` runs with Node and checks MP4 sample timing, variable-rate frame selection, unsupported native timing and conversion preflight budgets. `foreground-checks.html` verifies that the processing menu follows actual jobs, backgrounding terminates a worker, hidden work is rejected and the same session can retry on return. The feature suite also exercises preparation cancellation before editing, queued-image locks, foreground resume and native download cancellation.

Native timing currently reads ordinary MP4/MOV sample tables. Unsupported or fragmented containers require a fixed frame rate; GIF timing uses 10 ms units and this editor requires frames of at least 20 ms. Size preflight checks the editor's pixel/frame budget; the final compressed GIF/WebP size remains content-dependent.
