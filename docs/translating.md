# Translating Luma

Luma's built-in text lives in `data/translations/<code>.json` (built into the app):

```json
{ "language": "sv", "name": "Svenska", "strings": { "Uninstall": "Avinstallera", "Installed {0}.": "Installerade {0}." } }
```

- The keys are the English text exactly as the code has it. Text with values in it uses `{0}`,
  `{1}`… for each value; the translation may use them in any order, or leave one out (some are
  only an English plural "s").
- Every label, button, tooltip and menu item is translated as it's shown, so no code changes are
  needed. Empty values fall back to English.
- `python3 tools/i18n/extract.py` writes `data/translations/template.json` with every key;
  `python3 tools/i18n/extract.py --check sv` lists the keys a language is missing.
- Keep product names (Nexus Mods, Steam, Proton, LOOT…), file names and button names (A, B, LB)
  as they are.

To add a language, copy `template.json` to `<code>.json` (a two-letter code), fill in `language`,
`name` (in that language) and the strings, and open a pull request.
