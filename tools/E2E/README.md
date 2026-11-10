# End-to-end tests

These tests operate the actual Blazor app, file inputs, editor buttons and library UI. Media is decoded and encoded by the production browser code. Assertions inspect both the visible result and the saved library/media after a page reload; the tests do not replace the store, encoder, workers or JS interop with mocks.

## Run

From the repository root, with the pinned .NET SDK and Node installed:

```sh
npm ci --prefix tools/E2E
npm run install:browsers --prefix tools/E2E
npm test --prefix tools/E2E
```

The runner builds the web host once, then starts a separate localhost host and a fresh library/browser context for **each test**. Your normal `StickerApp.Web/.data` and native libraries are never used. Successful temporary libraries are removed; a failed test retains its library, server log, screenshot and Playwright trace under `tools/E2E/test-results`. Reports and fixtures containing saved test output are ignored by Git. Runtime tests use no external accounts or imports.

The suite runs sequentially at desktop (1280 × 900) and mobile (390 × 844, touch) sizes. This also avoids the app's intentional cancellation of backgrounded GIF work. The mobile project exercises responsive Chromium, not Android WebView or iOS Safari.

To use an already installed Chrome instead of downloading Chromium:

```sh
E2E_CHANNEL=chrome npm test --prefix tools/E2E
```

Select a project or test:

```sh
npm test --prefix tools/E2E -- --project=mobile
npm test --prefix tools/E2E -- --grep 'white meme'
npm run test:headed --prefix tools/E2E
npm run report --prefix tools/E2E
```

## Coverage

| Flow | End-to-end evidence |
| --- | --- |
| Transparent padding / maximum fit | Actual still import, pixel bounds before/after, undo/redo, persisted copy, replace and reload; transparent-only error and cancel |
| Animated maximum fit | Real moving GIF import, fixed bounds across both frames, saved animation frame count, duration and visible span |
| GIF quality / detail | A deterministic noisy GIF forces compression retries; saved animated WebP is ≤500 KiB with all 960 ms and original GIF retained |
| GIF progress / cancellation | Mutation observer records the progress meter rendered on the library card, intermediate percentages and retry count; stop retains source, resume completes |
| Compatibility fallback | The same noisy GIF path runs with OffscreenCanvas unavailable, using production main-thread conversion |
| Meme placement | Above, below and both; separate white/caption layers, undo/redo, edit text, copy, replace and reopen |
| Animated memes | Full-duration animated WebP, retained editable animation source, five layers and successful reopen |
| Device meme templates | Actual template file input, saved editable source, reopen |
| Copy versus replace | Different IDs and owned files for copies; unchanged template hash; replacement keeps the existing sticker identity/count |
| Folders | Create, bulk move, filter, rename, reload, remove, retained media and unfiled membership |
| List / tile views | Library, pack overview and pack contents; separate preferences, reload persistence, controls within the viewport |
| Pack files | `.wassticker`, `.wasticker`, `.wastickers` and `.zip` with `application/octet-stream`, both import entry points, real ZIP validation, reload, invalid-file recovery |

`fixtures/artwork.webp` is a generated red test rectangle encoded with the app's real WebP encoder. All PNG/GIF/ZIP inputs are deterministic fixtures generated locally. The older `tools/EditorChecks/browser` fixtures remain useful for detailed raster/codec checks; they are module integration checks rather than substitutes for this UI suite.

## Android OS file-opening smoke test

Browser automation cannot establish Android resolver registration, URI permissions or activity lifecycle behavior. `android-open-pack.mjs` is an opt-in real-device test for those paths:

```sh
ADB="$PWD/.tools/android-sdk/platform-tools/adb" \
ANDROID_SERIAL=emulator-5554 \
npm run test:android --prefix tools/E2E
```

Prerequisites: an Android 10+ test device/emulator with USB debugging authorized, the current debug app installed, onboarding completed, no pending file imports, and the app language set to English. Choose the serial explicitly (`adb devices`); the runner refuses to guess. `E2E_ANDROID_PACKAGE` can override `com.stickerstudio.app` for a separate test package.

The smoke test:

1. Asserts Android offers the app for all three pack extension spellings with generic binary, ZIP and sticker MIME types.
2. Creates a temporary MediaStore Downloads pack, writes the real ZIP fixture and opens its URI with a read grant.
3. Tests both a cold activity launch and delivery to the existing activity, presses **Import pack**, and verifies the pack title.
4. Restarts the process and verifies the saved pack appears in My Packs.
5. Opens the in-app pack picker, chooses the generic-binary `.wassticker` from Android DocumentsUI (Recents/Downloads), and verifies a distinct imported pack title. This directly detects the greyed-out-file regression.
6. Removes only its temporary MediaStore files and saves screenshots/UI XML in `test-results/android`.

It force-stops the test app during lifecycle checks, so finish any existing edits first. It **does not clear app data or remove existing packs**. Imported packs titled `E2E cold pack …`, its `… picker` copy and `E2E warm pack …` remain available for inspection. This script must actually pass on a device before claiming native opening is validated; a web-suite pass is insufficient. iOS document opening still needs its own device/simulator OS test.

## Performance regression checks

`optimizations.spec.mjs` exercises a 180-sticker library, verifying bounded mounted card/thumbnail counts, persistent poster-cache hits after reload, virtualized scrolling, full-filter selection and 512px editor sources. It instruments real canvas draw calls over defined playback windows to detect regressions to display-rate GIF rendering, checks that pause stops redraws, and still saves/exports editable animated layers. Real Worker construction is counted to verify identical successful conversions are reused while different inputs are encoded independently.

The noisy GIF cases also require representative-frame sampling, skipped full passes and a bounded number of real encoder calls, while retaining all frames/timing and the final 500 KiB budget. Both worker and main-thread fallback paths run. These are deterministic work-regression checks; they do not claim a measured battery-life improvement.
