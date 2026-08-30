# Orcish and Elven web translators

This folder is a self-contained PHP 7.4+ web application. `index.php` presents a responsive choice between the Orcish and Elven translators:

- `orcish.php` translates English ↔ Orcish using the assembled Orcish lexicon.
- `elven.php` translates English ↔ Elvish using the complete Sindarin-first Elven lexicon.

Both translators preserve unknown words, support phrases, enforce a 5,000-word limit, offer reverse translation, and provide plain-text downloads for translations and untranslated-word lists.

## Deploy

Upload the complete contents of `web-translator/` to a PHP-enabled directory. The essential runtime files are:

- `index.php`
- `orcish.php`
- `elven.php`
- `api.php`
- `elven-api.php`
- `OrcishTranslator.php`
- `ElvenTranslator.php`
- `TranslatorApiGuard.php`
- `translator.js`
- `styles.css`
- `orcish-lexicon.json`
- `elvish-lexicon.json`

Open `index.php` in a browser. No database is required. The PHP process must be able to read both runtime lexicons, and ordinary PHP session storage must be available.

The interface is mobile-first and adapts from edge-to-edge phone screens to tablet and desktop cards. It respects device safe areas, keeps controls touch-sized, and avoids horizontal scrolling.

Translations use separate short-lived PHP session values so a result survives the post-to-get redirect exactly once. Reloading a result page clears the source and translated text and prevents accidental form resubmission.

Set PHP's `memory_limit` to at least `256M`. Reverse-language indexes are built lazily only when a visitor requests Orcish-to-English or Elvish-to-English translation.

## Refresh the lexicons

From the repository root in PowerShell:

```powershell
.\web-translator\export-lexicon.ps1
.\web-translator\export-elven-lexicon.ps1
```

The Orcish exporter rebuilds the Release assembly and retrieves the complete assembled lexicon. The Elven exporter consolidates the reviewed Eldamo base, first-iteration morphology, second-iteration morphology, and complete-coverage layers into `elvish-lexicon.json`. Earlier sources have priority when duplicate English terms occur.

The Elven policy prefers Sindarin, falls back to Quenya when necessary, and uses validator-reviewed project forms for remaining coverage. Source vocabulary includes Eldamo 0.8.13 under CC BY 4.0.

Validate the deployable files, lexicon metadata, JavaScript, and PHP syntax when a local PHP runtime is available:

```powershell
.\web-translator\verify-web-translators.ps1
```

## JSON APIs

Send a POST request containing an `english` form value or JSON property to `api.php` for Orcish or `elven-api.php` for Elvish:

```json
{"english":"The orc sees the enemy."}
```

Each response contains the original English text, the translated text, the unique untranslated-word list, and the known-English-term count. API requests are limited to 5,000 words.
