# English-to-Orcish web translator

This folder is a self-contained PHP 7.4+ web application backed by the fully assembled Orcish lexicon.

## Deploy

Upload these files together to a PHP-enabled directory on the web host:

- `index.php`
- `api.php`
- `OrcishTranslator.php`
- `translator.js`
- `styles.css`
- `orcish-lexicon.json`

Open `index.php` in a browser. The PHP process must be able to read `orcish-lexicon.json`; no database is required, and ordinary PHP session storage must be available.

Translations use a short-lived PHP session value so the result survives the post-to-get redirect exactly once. Reloading the result page clears the English textbox, removes the Orcish result textbox, and prevents accidental form resubmission. When a translation is visible, the footer offers it as a plain-text download beneath the full JSON lexicon download.

If one or more unique English words cannot be translated, the result footer also offers `untranslated-words.txt`. The plain-text file contains only the untranslated words, with one word on each line. The adjacent note invites the user to email that list to `kyrathasoft@gmail.com`; the application does not send email or collect the list automatically.

Set PHP's `memory_limit` to at least `128M`; `192M` or more is recommended for comfortable headroom. Loading and indexing the current 47,564-term lexicon peaked at approximately 102 MB during PHP 7.4 validation.

## Refresh the lexicon

From the repository root in PowerShell:

```powershell
.\web-translator\export-lexicon.ps1
```

The exporter builds the current Release assembly, retrieves the complete in-memory lexicon after all corpus batches and morphology rules have run, and replaces `orcish-lexicon.json`.

## JSON API

Send a POST request to `api.php` as form data or JSON:

```json
{"english":"The orc sees the enemy."}
```

The response contains the original English text, its Orcish translation, the unique untranslated-word list, and the known-English-term count. Browser and API requests are limited to 5,000 words.
