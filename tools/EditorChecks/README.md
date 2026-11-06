# Browser editor checks

Serve `editor-checks.html` from a temporary development host that also serves `/_content/StickerApp.Shared`. Open it in the built-in local browser; it reports PASS or the first failed assertion. No external browser or automation dependency is needed.

For an isolated check, create `/tmp/stickit-editor-check/wwwroot`, copy this HTML there, build `StickerApp.Web`, then run its DLL with `ASPNETCORE_ENVIRONMENT=Development`, `--contentRoot /tmp/stickit-editor-check`, and a free localhost URL. Open `/editor-checks.html` at that URL. The temporary content root keeps manual UI-test imports separate from the personal library. Do not add the check page to production assets.

The page generates no library writes. It checks actual image/animation modules with a two-frame fixture, including cutout masks on every frame, recoverable opacity, PNG/replayed-frame agreement, animated WebP re-editing, partial transparency, layer apply/undo/redo/export/reopen, and isolated worker/main-thread cancellation. The standard storage checks separately verify saved composition data.
