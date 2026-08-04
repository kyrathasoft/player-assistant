from __future__ import annotations

from collections import Counter, defaultdict
from hashlib import sha256
import json
from pathlib import Path
import random
import re
import unicodedata

ROOT = Path(__file__).resolve().parent.parent
ORCISH_PATH = ROOT / "web-translator" / "orcish-lexicon.json"
GHUKLIAK_PATH = ROOT / "web-translator" / "ghukliak-lexicon.json"
COVERAGE_PATH = ROOT / "web-translator" / "ghukliak-complete-coverage.json"
REPORT_PATH = ROOT / "web-translator" / "ghukliak-complete-coverage-report.json"


VOWELS = frozenset("aeiouéò")
SUPPORTED_LETTERS = frozenset("abcdeghiklmnoprstuvwxyzéòčš")
DERIVATIONAL_SUFFIXES = {
    "adverb": "iku",
    "abstract-noun": "uk",
    "agent-noun": "es",
    "noun-adjective": "ung",
    "denominative-verb": "ase",
    "event-result-noun": "m",
}
PART_OF_SPEECH_ENDINGS = {
    "noun": ("m", "s", "d", "n", "hg"),
    "verb": ("i", "e"),
    "adjective": ("g", "r", "k", "ng"),
    "adverb": ("iku",),
}


def normalize(value: str) -> str:
    return " ".join(value.strip().lower().split())


def normalize_form(value: str) -> str:
    return unicodedata.normalize("NFC", value.strip().lower())


def deletions(value: str) -> set[str]:
    return {value[:index] + value[index + 1 :] for index in range(len(value))}


def is_well_formed(value: str, attested_bigrams: set[str]) -> bool:
    if not value or any(character not in SUPPORTED_LETTERS for character in value):
        return False
    if any(value[index] == value[index - 1] == value[index - 2] for index in range(2, len(value))):
        return False

    consonant_run = 0
    for character in value:
        consonant_run = 0 if character in VOWELS else consonant_run + 1
        if consonant_run >= 4:
            return False

    return all(value[index : index + 2] in attested_bigrams for index in range(len(value) - 1))


def matches_part_of_speech_ending(value: str, part_of_speech: str) -> bool:
    endings = PART_OF_SPEECH_ENDINGS.get(part_of_speech)
    return endings is None or value.endswith(endings) or (part_of_speech == "noun" and value.endswith("uk"))


class FormIndex:
    def __init__(self) -> None:
        self.exact_owners: dict[str, set[str]] = defaultdict(set)
        self.deletion_owners: dict[str, set[str]] = defaultdict(set)

    def add(self, form: str, owner: str) -> None:
        normalized = normalize_form(form)
        self.exact_owners[normalized].add(owner)
        for signature in deletions(normalized):
            self.deletion_owners[signature].add(owner)

    def exact_collision(self, form: str) -> bool:
        return normalize_form(form) in self.exact_owners

    def close_conflicting_owners(self, form: str, allowed_owner: str | None = None) -> set[str]:
        normalized = normalize_form(form)
        owners: set[str] = set()
        owners.update(self.deletion_owners.get(normalized, ()))
        for signature in deletions(normalized):
            owners.update(self.exact_owners.get(signature, ()))
            owners.update(self.deletion_owners.get(signature, ()))
        if allowed_owner is not None:
            owners.discard(allowed_owner)
        return owners

    def can_add(self, form: str, allowed_close_owner: str | None = None) -> bool:
        return not self.exact_collision(form) and not self.close_conflicting_owners(form, allowed_close_owner)


def build_phonotactics(base_terms: dict[str, list[list[str]]]) -> tuple[list[str], set[str], dict[str, list[str]], set[str]]:
    starts: Counter[str] = Counter()
    ends: Counter[str] = Counter()
    transitions: dict[str, Counter[str]] = defaultdict(Counter)
    bigrams: set[str] = set()

    for candidates in base_terms.values():
        for candidate in candidates:
            for token in normalize_form(candidate[0]).split():
                if not token or any(character not in SUPPORTED_LETTERS for character in token):
                    continue
                starts[token[0]] += 1
                ends[token[-1]] += 1
                for index in range(len(token) - 1):
                    pair = token[index : index + 2]
                    transitions[token[index]][token[index + 1]] += 1
                    bigrams.add(pair)

    weighted_starts = [character for character, count in sorted(starts.items()) for _ in range(count)]
    weighted_transitions = {
        character: [following for following, count in sorted(counts.items()) for _ in range(count)]
        for character, counts in transitions.items()
    }
    return weighted_starts, set(ends), weighted_transitions, bigrams


def create_form(
    key: str,
    index: FormIndex,
    starts: list[str],
    ends: set[str],
    transitions: dict[str, list[str]],
    bigrams: set[str],
    owner: str,
    part_of_speech: str,
) -> tuple[str, int]:
    for attempt in range(10000):
        seed = int.from_bytes(sha256(f"ghukliak-neologism-v2\0{part_of_speech}\0{key}\0{attempt}".encode("utf-8")).digest(), "big")
        rng = random.Random(seed)
        target_length = 9 + rng.randrange(5)
        form = rng.choice(starts)
        while len(form) < target_length or form[-1] not in ends:
            choices = transitions.get(form[-1], ())
            if not choices or len(form) >= 18:
                break
            candidates = [
                character
                for character in choices
                if not (len(form) >= 2 and form[-1] == form[-2] == character)
                and not (
                    character not in VOWELS
                    and len(form) >= 3
                    and all(existing not in VOWELS for existing in form[-3:])
                )
            ]
            if not candidates:
                break
            form += rng.choice(candidates)

        if len(form) < target_length or form[-1] not in ends or not is_well_formed(form, bigrams):
            continue
        endings_for_part_of_speech = list(PART_OF_SPEECH_ENDINGS.get(part_of_speech, ()))
        if endings_for_part_of_speech and not matches_part_of_speech_ending(form, part_of_speech):
            rng.shuffle(endings_for_part_of_speech)
            completed = next(
                (
                    candidate
                    for ending in endings_for_part_of_speech
                    for candidate in attach_suffix(form, ending, bigrams)
                    if len(candidate) <= 18
                ),
                None,
            )
            if completed is None:
                continue
            form = completed
        if not matches_part_of_speech_ending(form, part_of_speech):
            continue
        if not index.can_add(form):
            continue
        index.add(form, owner)
        return form, attempt

    raise RuntimeError(f"Could not create a collision-free Ghukliak form for {key!r}.")


def attach_suffix(root: str, suffix: str, bigrams: set[str]) -> list[str]:
    candidates = [root + suffix]
    for first in sorted(SUPPORTED_LETTERS):
        candidates.append(root + first + suffix)
    for first in sorted(SUPPORTED_LETTERS):
        for second in sorted(SUPPORTED_LETTERS):
            candidates.append(root + first + second + suffix)
    return [candidate for candidate in candidates if is_well_formed(candidate, bigrams)]


def selected_orcish_candidate(term_value: list[object]) -> tuple[str, str, list[str]]:
    candidates = term_value[1]
    if not candidates:
        return "word", "", []
    candidate = candidates[0]
    part_of_speech = normalize_part_of_speech(str(candidate[1] or "word"))
    grammar_class = normalize(str(candidate[2] or ""))
    tags = [normalize(str(tag)) for tag in (candidate[3] or [])]
    return part_of_speech, grammar_class, tags


def normalize_part_of_speech(value: str | None) -> str:
    normalized = normalize(value or "word").rstrip(".")
    return {"adj": "adjective", "adv": "adverb", "v": "verb", "n": "noun"}.get(normalized, normalized)


def get_root_key(english: str, tags: list[str], all_orcish_terms: set[str]) -> str:
    for prefix in ("base-", "family-"):
        for tag in tags:
            if tag.startswith(prefix) and len(tag) > len(prefix):
                candidate = normalize(tag[len(prefix) :])
                if candidate in all_orcish_terms:
                    return candidate
    return english


def get_derivation_rule(
    english: str,
    root_key: str,
    part_of_speech: str,
    root_part_of_speech: str,
    tags: list[str],
) -> str:
    if english == root_key:
        return "invented-root"
    if part_of_speech == "adverb" and root_part_of_speech == "adjective" and ("adverb" in tags or english.endswith("ly")):
        return "adverb"
    if part_of_speech == "noun" and root_part_of_speech == "adjective" and ("abstract-noun" in tags or english.endswith("ness")):
        return "abstract-noun"
    if part_of_speech == "noun" and root_part_of_speech == "verb" and ("agent-noun" in tags or english.endswith(("er", "or"))):
        return "agent-noun"
    if part_of_speech == "adjective" and root_part_of_speech == "noun":
        return "noun-adjective"
    if part_of_speech == "verb" and root_part_of_speech == "noun":
        return "denominative-verb"
    if part_of_speech == "noun" and root_part_of_speech == "verb" and english.endswith(("ing", "tion", "sion", "ment", "ance", "ence", "ure")):
        return "event-result-noun"
    return "independent-neologism"


def main() -> None:
    orcish = json.loads(ORCISH_PATH.read_text(encoding="utf-8"))
    ghukliak = json.loads(GHUKLIAK_PATH.read_text(encoding="utf-8"))
    orcish_terms: dict[str, list[object]] = orcish["terms"]
    base_terms: dict[str, list[list[str]]] = ghukliak["terms"]
    normalized_orcish = {normalize(term): term for term in orcish_terms}
    normalized_base = {normalize(term): term for term in base_terms}
    missing = sorted(set(normalized_orcish) - set(normalized_base))

    starts, ends, transitions, bigrams = build_phonotactics(base_terms)
    form_index = FormIndex()
    base_form_by_english: dict[str, str] = {}
    part_of_speech_by_english: dict[str, str] = {}
    for english, candidates in base_terms.items():
        normalized_english = normalize(english)
        if candidates:
            base_form_by_english[normalized_english] = normalize_form(candidates[0][0])
            part_of_speech_by_english[normalized_english] = normalize_part_of_speech(candidates[0][1])
        for candidate in candidates:
            for token in normalize_form(candidate[0]).split():
                if token and all(character in SUPPORTED_LETTERS for character in token):
                    form_index.add(token, normalized_english)

    for normalized_english, source_english in normalized_orcish.items():
        part_of_speech, _grammar_class, _tags = selected_orcish_candidate(orcish_terms[source_english])
        part_of_speech_by_english.setdefault(normalized_english, part_of_speech)

    metadata: dict[str, tuple[str, str, str, list[str]]] = {}
    root_keys: set[str] = set()
    all_orcish_terms = set(normalized_orcish)
    for normalized_english in missing:
        source_english = normalized_orcish[normalized_english]
        part_of_speech, _grammar_class, tags = selected_orcish_candidate(orcish_terms[source_english])
        root_key = get_root_key(normalized_english, tags, all_orcish_terms)
        derivation_rule = get_derivation_rule(
            normalized_english,
            root_key,
            part_of_speech,
            part_of_speech_by_english.get(root_key, "word"),
            tags,
        )
        metadata[normalized_english] = (root_key, part_of_speech, derivation_rule, tags)
        root_keys.add(root_key)

    root_forms: dict[str, str] = {}
    root_collision_retries = 0
    for root_key in sorted(root_keys):
        if root_key in base_form_by_english:
            root_forms[root_key] = base_form_by_english[root_key]
            continue
        root_form, retries = create_form(
            f"root:{root_key}",
            form_index,
            starts,
            ends,
            transitions,
            bigrams,
            root_key,
            part_of_speech_by_english.get(root_key, "word"),
        )
        root_forms[root_key] = root_form
        root_collision_retries += retries

    entries: list[list[object]] = []
    by_rule: Counter[str] = Counter()
    by_part_of_speech: Counter[str] = Counter()
    derived_fallbacks = 0
    derived_collision_retries = 0
    independent_collision_retries = 0

    for normalized_english in missing:
        source_english = normalized_orcish[normalized_english]
        root_key, part_of_speech, derivation_rule, _tags = metadata[normalized_english]
        flags: list[str] = []

        if normalized_english == root_key and root_key not in normalized_base:
            form = root_forms[root_key]
        elif derivation_rule in DERIVATIONAL_SUFFIXES:
            form = ""
            for candidate in attach_suffix(root_forms[root_key], DERIVATIONAL_SUFFIXES[derivation_rule], bigrams):
                if form_index.can_add(candidate, allowed_close_owner=root_key):
                    form = candidate
                    form_index.add(form, root_key)
                    break
                derived_collision_retries += 1
            if not form:
                form, retries = create_form(
                    f"derived-fallback:{normalized_english}",
                    form_index,
                    starts,
                    ends,
                    transitions,
                    bigrams,
                    normalized_english,
                    part_of_speech,
                )
                independent_collision_retries += retries
                derivation_rule = "independent-neologism"
                flags.append("derived-fallback")
                derived_fallbacks += 1
        else:
            form, retries = create_form(
                f"term:{normalized_english}",
                form_index,
                starts,
                ends,
                transitions,
                bigrams,
                normalized_english,
                part_of_speech,
            )
            independent_collision_retries += retries

        if normalize_form(form) == normalize_form(normalized_english):
            raise RuntimeError(f"Generated pass-through form for {source_english!r}.")
        entries.append([source_english, form, root_key, part_of_speech, derivation_rule, flags])
        by_rule[derivation_rule] += 1
        by_part_of_speech[part_of_speech] += 1

    validation = {
        "language": "Ghukliak",
        "generator": "ghukliak-neologism-v2",
        "exactUnreviewedCollisions": 0,
        "closeFormConflicts": 0,
        "malformedForms": 0,
        "unattestedBigrams": 0,
        "repeatedLetterRuns": 0,
        "fourConsonantRuns": 0,
        "partOfSpeechEndingConflicts": sum(
            not matches_part_of_speech_ending(entry[1], entry[3]) for entry in entries
        ),
        "passThroughForms": 0,
        "rootCollisionRetries": root_collision_retries,
        "derivedFallbacks": derived_fallbacks,
        "derivedCollisionRetries": derived_collision_retries,
        "independentCollisionRetries": independent_collision_retries,
    }
    if validation["partOfSpeechEndingConflicts"] != 0:
        raise RuntimeError(
            f"Generated {validation['partOfSpeechEndingConflicts']} forms with invalid part-of-speech endings."
        )
    expected_final_count = len(set(normalized_base) | set(normalized_orcish))
    source_english_set_sha256 = sha256(
        ("\n".join(sorted(normalized_orcish)) + "\n").encode("utf-8")
    ).hexdigest()
    coverage = {
        "schemaVersion": 1,
        "policy": (
            "Complete Orcish/Ghukliak coverage. Preserve the attached Ghukliak dictionary, reuse reviewed English root families, "
            "apply only derivational endings attested in that dictionary, and otherwise create deterministic collision-free "
            "Ghukliak neologisms from attested letter transitions."
        ),
        "sourceEnglishTermCount": len(normalized_orcish),
        "sourceEnglishSetSha256": source_english_set_sha256,
        "priorEnglishTermCount": len(normalized_base),
        "entryCount": len(entries),
        "expectedFinalEnglishTermCount": expected_final_count,
        "candidateFields": ["english", "ghukliak", "rootKey", "partOfSpeech", "derivationRule", "flags"],
        "validation": validation,
        "byRule": dict(sorted(by_rule.items())),
        "byPartOfSpeech": dict(sorted(by_part_of_speech.items())),
        "entries": entries,
    }
    report = {
        "schemaVersion": 1,
        "previousEnglishTerms": len(normalized_base),
        "sourceEnglishSetSha256": source_english_set_sha256,
        "addedEnglishTerms": len(entries),
        "finalEnglishTerms": expected_final_count,
        "remainingOrcishTermsWithoutGhukliak": 0,
        "generatedRootCount": sum(root_key not in normalized_base for root_key in root_keys),
        "sourceRootReuseCount": sum(root_key in normalized_base for root_key in root_keys),
        "byRule": dict(sorted(by_rule.items())),
        "byPartOfSpeech": dict(sorted(by_part_of_speech.items())),
        "validation": validation,
    }

    COVERAGE_PATH.write_text(json.dumps(coverage, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    REPORT_PATH.write_text(json.dumps(report, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")

    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
