# Interface translations

Each UTF-8 JSON file is a flat object mapping the original English UI string to translated text. Files: `en.json`, `nl.json`, `de.json`, `ro.json`, `nb.json`.

Resources are embedded by the shared project and loaded by `Services/Localization/AppLanguage.cs`. Components use `@L["Settings"]`. Lookup tries the selected language, then English, then the source string. Keep keys identical across languages; do not translate JSON property names. Missing translations are safe but visible in English. Catalogs cover navigation, settings/editor controls, the slideshow, help, warnings, validation errors and dynamic statuses.

To translate more text:

1. Add its English key/value to `en.json` and translated values to the other files.
2. Use `@L["The English source"]` for visible system text, or localized parameters for common components. Use `L.Format("Showing {0} of {1} packs", count, total)` for composed UI text; never translate user-generated names, sticker captions, paths or identifiers by accidental string substitution.
3. Keep source exception messages and log calls in English. Use `L.Error(raw)` or `L.Message(raw)` only where system messages are displayed. The presentation helper matches catalog templates, preserves names/counts/reference IDs, and gives unknown provider errors a translated explanation. It never mutates the exception or diagnostic payload. Do not wrap diagnostic log content or exported reports in translation calls.
4. Build the app and test layout in each language, including mobile widths and accessibility labels.

To add a language, add its JSON file, register its code/autonym in `AppLanguage.Supported`, and allow the same code in `wwwroot/js/ui/language.js`. The SDK embeds files from this folder automatically. No translation API, credentials or network requests are used.

Selection is stored as `stickers.language`, and `document.documentElement.lang` follows it. Applying rebuilds the current interface; settings are intended to be changed outside active sticker edits. Factory reset clears this preference. Do not translate the deliberate Dutch font-preview jokes or user artwork.
