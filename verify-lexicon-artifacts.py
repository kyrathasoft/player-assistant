#!/usr/bin/env python3
"""Verify runtime lexicons are exact projections of schema-rich canonical sources."""

from __future__ import annotations

import hashlib
import json
import sys
import unicodedata
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent


class VerificationError(RuntimeError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise VerificationError(message)


def read_json(relative_path: str) -> dict[str, Any]:
    path = ROOT / relative_path
    require(path.is_file(), f"Required lexicon artifact is missing: {relative_path}")
    try:
        document = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise VerificationError(f"Invalid JSON in {relative_path}: {error}") from error
    require(isinstance(document, dict), f"Lexicon artifact must be an object: {relative_path}")
    return document


def add_first(
    terms: dict[str, str],
    normalized_keys: set[str],
    english: Any,
    translation: Any,
    source: str,
) -> None:
    require(isinstance(english, str) and english.strip(), f"{source} has an empty English term.")
    require(isinstance(translation, str) and translation.strip(), f"{source} has no translation for '{english}'.")
    normalized = english.strip().casefold()
    if normalized in normalized_keys:
        return
    normalized_keys.add(normalized)
    terms[english] = translation


def verify_zero_audit(document: dict[str, Any], fields: tuple[str, ...], source: str) -> None:
    validation = document.get("validation")
    require(isinstance(validation, dict), f"Canonical coverage has no validation audit: {source}")
    for field in fields:
        require(validation.get(field) == 0, f"Canonical coverage audit failed {field}: {source}")


def normalize_runtime_key(value: str, normalization: str) -> str:
    if normalization == "trim-lower":
        return value.strip().lower()
    if normalization == "apostrophe-fold-trim-lower":
        return value.replace("’", "'").strip().lower()
    if normalization == "nfkc-trim-lower":
        return unicodedata.normalize("NFKC", value).strip().lower()
    raise VerificationError(f"Unsupported runtime normalization: {normalization}")


def effective_terms(terms: dict[str, str], normalization: str) -> dict[str, str]:
    effective: dict[str, str] = {}
    for english, translation in terms.items():
        effective.setdefault(normalize_runtime_key(english, normalization), translation)
    return effective


def validate_manifest() -> dict[str, Any]:
    manifest = read_json("lexicons/manifest.json")
    require(manifest.get("schemaVersion") == 1, "Unsupported canonical lexicon manifest schema.")
    languages = manifest.get("languages")
    require(isinstance(languages, dict), "Canonical lexicon manifest has no languages object.")
    require(list(languages) == ["orcish", "elvish", "ghukliak"], "Canonical languages must be Orcish, Elvish, and Ghukliak in that order.")

    all_sources: set[str] = set()
    for language, contract in languages.items():
        require(isinstance(contract, dict), f"Manifest contract for {language} must be an object.")
        require(contract.get("sourceTier"), f"Manifest contract for {language} has no source tier.")
        sources = contract.get("canonicalSources")
        require(isinstance(sources, list) and sources, f"Manifest contract for {language} has no canonical sources.")
        for source in sources:
            require(isinstance(source, dict), f"Canonical source for {language} must be an object.")
            path = source.get("path")
            require(isinstance(path, str) and path, f"Canonical source for {language} has no path.")
            require(path not in all_sources, f"Canonical source is assigned more than once: {path}")
            all_sources.add(path)
            document = read_json(path)
            require(document.get("schemaVersion") == source.get("schemaVersion"), f"Schema mismatch for canonical source: {path}")
            require(isinstance(source.get("role"), str) and source["role"], f"Canonical source has no role: {path}")
        for runtime_name in ("pwaProjection", "webRuntime"):
            runtime = contract.get(runtime_name)
            if runtime is None and runtime_name == "webRuntime":
                continue
            require(isinstance(runtime, dict), f"Manifest {runtime_name} for {language} must be an object.")
            require(isinstance(runtime.get("path"), str) and runtime["path"], f"Manifest {runtime_name} for {language} has no path.")
            require(isinstance(runtime.get("normalization"), str), f"Manifest {runtime_name} for {language} has no normalization.")
            require(isinstance(runtime.get("expectedEffectiveTermCount"), int), f"Manifest {runtime_name} for {language} has no effective term count.")

    ghukliak = languages["ghukliak"]
    require(ghukliak.get("distinctFrom") == ["orcish"], "Ghukliak must be explicitly distinct from Orcish.")
    require("Goblin (Ghukliak)" in ghukliak.get("aliases", []), "Ghukliak must retain its Goblin identity.")
    require(not any("goblin" in alias.casefold() for alias in languages["orcish"].get("aliases", [])), "Orcish must not be identified as Goblin.")
    return manifest


def build_orcish(contract: dict[str, Any]) -> dict[str, str]:
    source = contract["canonicalSources"][0]["path"]
    document = read_json(source)
    require(document.get("candidateFields") == ["orcish", "partOfSpeech", "grammarClass", "tags"], "Orcish candidate schema is not canonical.")
    raw_terms = document.get("terms")
    require(isinstance(raw_terms, dict), "Canonical Orcish terms must be an object.")
    require(document.get("uniqueEnglishTerms") == len(raw_terms), "Canonical Orcish term count is inconsistent.")

    terms: dict[str, str] = {}
    normalized: set[str] = set()
    for key, value in raw_terms.items():
        require(isinstance(value, list) and len(value) == 2, f"Malformed canonical Orcish term: {key}")
        _, candidates = value
        require(isinstance(candidates, list) and candidates, f"Canonical Orcish term has no candidates: {key}")
        candidate = candidates[0]
        require(isinstance(candidate, list) and len(candidate) >= 4, f"Malformed canonical Orcish candidate: {key}")
        require(isinstance(candidate[3], list), f"Canonical Orcish tags must be an array: {key}")
        add_first(terms, normalized, key, candidate[0], source)
    return terms


def build_elvish(contract: dict[str, Any], orcish_terms: dict[str, str]) -> dict[str, str]:
    sources = {source["role"]: [] for source in contract["canonicalSources"]}
    for source in contract["canonicalSources"]:
        sources[source["role"]].append(source["path"])

    catalog_path = sources["reviewed-candidate-catalog"][0]
    catalog = read_json(catalog_path)
    catalog_terms = catalog.get("terms")
    require(isinstance(catalog_terms, dict), "Canonical Elvish candidate catalog has no terms.")
    require(catalog.get("uniqueEnglishTerms") == len(catalog_terms), "Canonical Elvish candidate count is inconsistent.")
    for english, candidates in catalog_terms.items():
        require(isinstance(candidates, list) and candidates, f"Canonical Elvish term has no candidates: {english}")
        require(all(isinstance(candidate, list) and len(candidate) >= 7 for candidate in candidates), f"Malformed canonical Elvish candidate: {english}")

    terms: dict[str, str] = {}
    normalized: set[str] = set()
    selected_path = sources["finalized-reviewed-selection"][0]
    selected = read_json(selected_path)
    selected_terms = selected.get("translations")
    require(isinstance(selected_terms, dict), "Canonical Elvish selection has no translations.")
    require(selected.get("translationCount") == len(selected_terms), "Canonical Elvish selection count is inconsistent.")
    for english, candidate in selected_terms.items():
        require(isinstance(candidate, list) and len(candidate) >= 7, f"Malformed finalized Elvish selection: {english}")
        require(english in catalog_terms, f"Finalized Elvish term is absent from the candidate catalog: {english}")
        add_first(terms, normalized, english, candidate[0], selected_path)

    for layer_path in sources["reviewed-morphology-layer"]:
        layer = read_json(layer_path)
        entries = layer.get("entries")
        require(isinstance(entries, list), f"Elvish morphology layer has no entries: {layer_path}")
        require(layer.get("entryCount") == len(entries), f"Elvish morphology count is inconsistent: {layer_path}")
        for entry in entries:
            require(isinstance(entry, dict), f"Malformed Elvish morphology entry: {layer_path}")
            add_first(terms, normalized, entry.get("english"), entry.get("elvish"), layer_path)

    coverage_path = sources["audited-complete-coverage-layer"][0]
    coverage = read_json(coverage_path)
    entries = coverage.get("entries")
    require(isinstance(entries, list), "Elvish complete-coverage layer has no entries.")
    require(coverage.get("candidateFields") == ["english", "elvish", "rootKey", "partOfSpeech", "derivationRule", "flags"], "Elvish complete-coverage schema is not canonical.")
    require(coverage.get("entryCount") == len(entries), "Elvish complete-coverage count is inconsistent.")
    require(coverage.get("sourceEnglishTermCount") == len(orcish_terms), "Elvish coverage is stale relative to canonical Orcish terms.")
    require(coverage.get("priorEnglishTermCount") == len(terms), "Elvish coverage prior-term count is inconsistent.")
    require(coverage.get("validation", {}).get("language") == "Sindarin", "Elvish complete coverage has the wrong language audit.")
    verify_zero_audit(
        coverage,
        ("exactUnreviewedCollisions", "closeFormConflicts", "malformedForms", "repeatedLetterRuns", "fourConsonantRuns"),
        coverage_path,
    )
    for entry in entries:
        require(isinstance(entry, list) and len(entry) >= 6, "Malformed Elvish complete-coverage entry.")
        add_first(terms, normalized, entry[0], entry[1], coverage_path)
    require(coverage.get("expectedFinalEnglishTermCount") == len(terms), "Canonical Elvish final count is inconsistent.")
    return terms


def build_ghukliak(contract: dict[str, Any], orcish_terms: dict[str, str]) -> dict[str, str]:
    source_by_role = {source["role"]: source["path"] for source in contract["canonicalSources"]}
    base_path = source_by_role["campaign-candidate-lexicon"]
    base = read_json(base_path)
    require(base.get("language") == "Ghukliak", "Canonical Ghukliak source has the wrong language.")
    raw_terms = base.get("terms")
    require(isinstance(raw_terms, dict), "Canonical Ghukliak source has no terms.")
    require(base.get("entryCount") == len(raw_terms), "Canonical Ghukliak term count is inconsistent.")

    terms: dict[str, str] = {}
    normalized: set[str] = set()
    for english, candidates in raw_terms.items():
        require(isinstance(candidates, list) and candidates, f"Canonical Ghukliak term has no candidates: {english}")
        candidate = candidates[0]
        require(isinstance(candidate, list) and len(candidate) >= 1, f"Malformed canonical Ghukliak candidate: {english}")
        add_first(terms, normalized, english, candidate[0], base_path)

    coverage_path = source_by_role["audited-complete-coverage-layer"]
    coverage = read_json(coverage_path)
    entries = coverage.get("entries")
    require(isinstance(entries, list), "Ghukliak complete-coverage layer has no entries.")
    require(coverage.get("candidateFields") == ["english", "ghukliak", "rootKey", "partOfSpeech", "derivationRule", "flags"], "Ghukliak complete-coverage schema is not canonical.")
    require(coverage.get("entryCount") == len(entries), "Ghukliak complete-coverage count is inconsistent.")
    normalized_orcish = {" ".join(term.strip().lower().split()) for term in orcish_terms}
    source_hash = hashlib.sha256(("\n".join(sorted(normalized_orcish)) + "\n").encode("utf-8")).hexdigest()
    require(coverage.get("sourceEnglishTermCount") == len(normalized_orcish), "Ghukliak coverage is stale relative to canonical Orcish terms.")
    require(coverage.get("sourceEnglishSetSha256") == source_hash, "Ghukliak coverage source-set hash is stale.")
    require(coverage.get("priorEnglishTermCount") == len(terms), "Ghukliak coverage prior-term count is inconsistent.")
    require(coverage.get("validation", {}).get("language") == "Ghukliak", "Ghukliak complete coverage has the wrong language audit.")
    verify_zero_audit(
        coverage,
        (
            "exactUnreviewedCollisions",
            "closeFormConflicts",
            "malformedForms",
            "unattestedBigrams",
            "repeatedLetterRuns",
            "fourConsonantRuns",
            "partOfSpeechEndingConflicts",
            "passThroughForms",
        ),
        coverage_path,
    )
    for entry in entries:
        require(isinstance(entry, list) and len(entry) >= 6, "Malformed Ghukliak complete-coverage entry.")
        add_first(terms, normalized, entry[0], entry[1], coverage_path)
    require(coverage.get("expectedFinalEnglishTermCount") == len(terms), "Canonical Ghukliak final count is inconsistent.")
    return terms


def verify_runtime_semantics(runtime: dict[str, Any], language: str, expected: dict[str, str], actual: dict[str, str]) -> None:
    normalization = runtime.get("normalization")
    require(isinstance(normalization, str), f"Runtime normalization is missing for {language}.")
    expected_effective = effective_terms(expected, normalization)
    actual_effective = effective_terms(actual, normalization)
    require(actual_effective == expected_effective, f"Runtime-normalized {language} precedence has drifted from canonical sources.")
    require(
        runtime.get("expectedEffectiveTermCount") == len(expected_effective),
        f"Runtime-normalized {language} term count is inconsistent.",
    )


def verify_projection(runtime: dict[str, Any], language: str, expected: dict[str, str]) -> None:
    path = runtime.get("path")
    require(isinstance(path, str), f"Runtime projection path is missing for {language}.")
    document = read_json(path)
    actual = document.get("terms")
    require(isinstance(actual, dict), f"Runtime {language} projection has no terms: {path}")
    require(actual == expected, f"Runtime {language} projection has drifted from canonical sources: {path}")
    verify_runtime_semantics(runtime, language, expected, actual)
    require(document.get("entryCount") == len(expected), f"Runtime {language} entry count is inconsistent: {path}")
    maximum = max((len(term.split()) for term in expected), default=1)
    max_field = "maxEnglishPhraseWords" if path.startswith("web-translator/") else "maxPhraseWords"
    require(document.get(max_field) == maximum, f"Runtime {language} phrase limit is inconsistent: {path}")


def verify_desktop_resources(manifest: dict[str, Any]) -> None:
    project = ET.parse(ROOT / "player-assistant.csproj")
    embedded: dict[str, str | None] = {}
    for element in project.iter():
        if not element.tag.endswith("EmbeddedResource") or "Include" not in element.attrib:
            continue
        logical_name = element.attrib.get("LogicalName") or next(
            (child.text for child in element if child.tag.endswith("LogicalName")),
            None,
        )
        embedded[element.attrib["Include"].replace("\\", "/")] = logical_name
    for language, contract in manifest["languages"].items():
        consumer_path = contract.get("desktopConsumer")
        require(isinstance(consumer_path, str), f"Desktop consumer is missing for {language}.")
        consumer = (ROOT / consumer_path).read_text(encoding="utf-8-sig")
        for resource in contract["desktopResources"]:
            path = resource.get("path")
            logical_name = resource.get("logicalName")
            require(embedded.get(path) == logical_name, f"Desktop resource mapping is wrong for canonical {language} source: {path}")
            require(f'"{logical_name}"' in consumer, f"Desktop {language} consumer does not load canonical resource: {logical_name}")


def verify_projection_builder(manifest: dict[str, Any]) -> None:
    build_script = (ROOT / "pwa" / "build-data.ps1").read_text(encoding="utf-8-sig")
    web_exporter = (ROOT / "web-translator" / "export-elven-lexicon.ps1").read_text(encoding="utf-8-sig")
    require("lexicons\\manifest.json" in build_script, "PWA projection builder does not load the canonical manifest.")
    for contract in manifest["languages"].values():
        for source in contract["canonicalSources"]:
            if source["role"] == "reviewed-candidate-catalog":
                continue
            require(
                f"-Role '{source['role']}'" in build_script,
                f"PWA projection builder does not consume canonical source role: {source['role']}",
            )
    require("lexicons\\manifest.json" in web_exporter, "Elvish web exporter does not load the canonical manifest.")
    for source in manifest["languages"]["elvish"]["canonicalSources"]:
        if source["role"] == "reviewed-candidate-catalog":
            continue
        require(
            f"-Role '{source['role']}'" in web_exporter,
            f"Elvish web exporter does not consume canonical source role: {source['role']}",
        )
    require("webRuntime.path" in web_exporter, "Elvish web exporter does not use the manifest runtime path.")


def main() -> int:
    manifest = validate_manifest()
    contracts = manifest["languages"]
    projections = {
        "orcish": build_orcish(contracts["orcish"]),
    }
    projections["elvish"] = build_elvish(contracts["elvish"], projections["orcish"])
    projections["ghukliak"] = build_ghukliak(contracts["ghukliak"], projections["orcish"])
    for language, terms in projections.items():
        verify_projection(contracts[language]["pwaProjection"], language, terms)

    elvish_runtime = contracts["elvish"]["webRuntime"]
    verify_projection(elvish_runtime, "elvish", projections["elvish"])
    orcish_runtime = contracts["orcish"]["webRuntime"]
    require(orcish_runtime["path"] == contracts["orcish"]["canonicalSources"][0]["path"], "Orcish web runtime must use the canonical candidate lexicon directly.")
    verify_runtime_semantics(orcish_runtime, "orcish", projections["orcish"], projections["orcish"])
    require(contracts["ghukliak"]["webRuntime"] is None, "Ghukliak must not claim a web runtime that does not exist.")
    verify_desktop_resources(manifest)
    verify_projection_builder(manifest)

    counts = ", ".join(f"{language}={len(terms)}" for language, terms in projections.items())
    print(f"Canonical lexicon artifacts verified: {counts}.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except VerificationError as error:
        print(f"Lexicon verification failed: {error}", file=sys.stderr)
        raise SystemExit(1)
