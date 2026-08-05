# Canonical lexicons

`manifest.json` defines the schema-rich source chain for each translator language. These source artifacts—not the flattened runtime dictionaries—are canonical.

- **Orcish:** the assembled candidate lexicon, including part of speech, grammar class, and tags.
- **Elvish:** the reviewed Eldamo candidate catalog and finalized selection followed by the two reviewed morphology layers and audited complete-coverage layer.
- **Ghukliak:** the campaign dictionary and its audited complete-coverage layer. Ghukliak is Goblin and remains distinct from Orcish.

The desktop embeds the canonical artifacts. Web and PWA dictionaries are deterministic projections that select the first approved translation after applying layers in manifest order. The manifest also records each consumer's normalization contract and effective term count; the Elvish PHP runtime intentionally folds curly apostrophes into ASCII apostrophes.

`verify-lexicon-artifacts.py` reconstructs those projections from the canonical sources and rejects drift, stale coverage provenance, failed generation audits, incorrect runtime precedence, missing desktop resource wiring, or language conflation. Both projection builders resolve their source chain from the manifest.

Regenerate projections with `pwa/build-data.ps1` and `web-translator/export-elven-lexicon.ps1`, then run `python verify-lexicon-artifacts.py`. The required full-regression workflow runs the verifier on every change.
