import csv
import io
import json
import random
import re
import sys
import urllib.request
from collections import Counter
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

ROUND = sys.argv[1] if len(sys.argv) > 1 else "round1"
SEED = int(sys.argv[2]) if len(sys.argv) > 2 else 20260720
TARGET = 200
TARGET = int(sys.argv[3]) if len(sys.argv) > 3 else TARGET
WORKERS = int(sys.argv[4]) if len(sys.argv) > 4 else 4
CATALOG_URL = "https://mirror.cs.odu.edu/gutenberg-epub/feeds/pg_catalog.csv"
MIRROR_TEMPLATE = "https://mirror.cs.odu.edu/gutenberg-epub/{id}/pg{id}.txt"
EXISTING_PATH = Path(r"codex-scratch\gutenberg-existing-terms.txt")
AFFIX_PATH = Path(r"C:\Users\Bryan\AppData\Local\Programs\MiKTeX\hunspell\dicts\en_US.aff")
DICTIONARY_PATH = Path(r"C:\Users\Bryan\AppData\Local\Programs\MiKTeX\hunspell\dicts\en_US.dic")
LEDGER_PATH = Path("dont-scrape-gutenberg-again.md")
MANIFEST_PATH = Path(f"codex-scratch/gutenberg-{ROUND}-manifest.json")
STATS_PATH = Path(f"codex-scratch/gutenberg-{ROUND}-word-stats.tsv")
CANDIDATE_PATH = Path(f"codex-scratch/gutenberg-{ROUND}-source-candidates.txt")
PRIOR_MANIFESTS = sorted(Path("codex-scratch").glob("gutenberg-round*-manifest.json"))

RELEVANT = re.compile(
    r"adventure|fantasy|myth|legend|folklore|fairy|war|military|medieval|ancient|"
    r"horror|ghost|supernatural|sword|knight|pirate|exploration|science fiction|"
    r"historical fiction|epic|occult|magic|witch|demon|monster|gothic|sea stor",
    re.I,
)
TOKEN = re.compile(r"(?<![A-Za-z])[A-Za-z]+(?:[-'][A-Za-z]+){0,3}(?![A-Za-z])")
START = re.compile(r"\*\*\*\s*START OF (?:THE|THIS) PROJECT GUTENBERG EBOOK.*?\*\*\*", re.I | re.S)
END = re.compile(r"\*\*\*\s*END OF (?:THE|THIS) PROJECT GUTENBERG EBOOK.*?\*\*\*", re.I | re.S)
STOP = {
    "about", "after", "again", "against", "also", "among", "another", "because", "before", "being",
    "between", "both", "could", "does", "doing", "each", "every", "from", "further", "have", "having",
    "herself", "himself", "into", "itself", "more", "most", "much", "must", "only", "other", "ourselves",
    "should", "some", "such", "than", "that", "their", "theirs", "them", "themselves", "then", "there",
    "these", "they", "this", "those", "through", "under", "until", "very", "what", "when", "where", "which",
    "while", "whom", "whose", "with", "would", "your", "yours", "yourself", "yourselves", "chapter", "volume",
    "project", "gutenberg", "ebook", "ebooks", "license", "copyright", "contents", "illustration", "illustrations",
    "editor", "translator", "transcriber", "proofreading", "printed", "publisher", "edition", "preface", "appendix",
}


def request_bytes(url, limit=None):
    request = urllib.request.Request(url, headers={"User-Agent": "player-assistant-orcish-corpus/1.0"})
    with urllib.request.urlopen(request, timeout=25) as response:
        return response.read() if limit is None else response.read(limit + 1)


def load_valid_words():
    affix_lines = AFFIX_PATH.read_text(encoding="utf-8-sig").splitlines()
    suffixes = {}
    index = 0
    while index < len(affix_lines):
        fields = affix_lines[index].split()
        if len(fields) == 4 and fields[0] == "SFX" and fields[3].isdigit():
            flag, count = fields[1], int(fields[3])
            rules = []
            for offset in range(1, count + 1):
                rule = affix_lines[index + offset].split()
                if len(rule) >= 5:
                    rules.append(("" if rule[2] == "0" else rule[2], "" if rule[3] == "0" else rule[3].split("/")[0], rule[4]))
            suffixes[flag] = rules
            index += count
        index += 1
    words = set()
    for line in DICTIONARY_PATH.read_text(encoding="utf-8-sig").splitlines()[1:]:
        stem_flags = line.split("\t", 1)[0]
        stem, flags = stem_flags.split("/", 1) if "/" in stem_flags else (stem_flags, "")
        stem = stem.lower()
        if not re.fullmatch(r"[a-z][a-z'-]*", stem):
            continue
        words.add(stem)
        for flag in flags:
            for strip, addition, condition in suffixes.get(flag, []):
                try:
                    if not re.search(f"(?:{condition})$", stem) or (strip and not stem.endswith(strip)):
                        continue
                except re.error:
                    continue
                words.add((stem[:-len(strip)] if strip else stem) + addition)
    return words


def valid_candidate(word, dictionary):
    if not (3 <= len(word) <= 28) or word in STOP or any(ch.isdigit() for ch in word):
        return False
    if "'" in word:
        if word.endswith("'s"):
            word = word[:-2]
        else:
            return False
    parts = word.split("-")
    return 1 <= len(parts) <= 4 and all(part in dictionary and len(part) >= 2 for part in parts)


def download_book(row):
    book_id = row["Text#"]
    url = MIRROR_TEMPLATE.format(id=book_id)
    try:
        payload = request_bytes(url, 5_000_000)
        if len(payload) > 5_000_000:
            return None
        text = payload.decode("utf-8-sig", errors="replace")
        start = START.search(text)
        if start:
            text = text[start.end():]
        end = END.search(text)
        if end:
            text = text[:end.start()]
        if len(text) < 10_000:
            return None
        return row, url, len(payload), text
    except Exception:
        return None


def main():
    existing = {line.strip().lower() for line in EXISTING_PATH.read_text(encoding="utf-8-sig").splitlines() if line.strip()}
    used_ids = set(re.findall(r"/ebooks/(\d+)", LEDGER_PATH.read_text(encoding="utf-8-sig"))) if LEDGER_PATH.exists() else set()
    for path in PRIOR_MANIFESTS:
        try:
            manifest = json.loads(path.read_text(encoding="utf-8-sig"))
        except (json.JSONDecodeError, OSError):
            continue
        used_ids.update(str(book.get("id")) for book in manifest.get("books", []) if book.get("id") is not None)
    dictionary = load_valid_words()
    catalog_text = request_bytes(CATALOG_URL).decode("utf-8-sig", errors="replace")
    rows = [row for row in csv.DictReader(io.StringIO(catalog_text)) if row["Text#"] not in used_ids and row["Type"] == "Text" and row["Language"] == "en" and RELEVANT.search((row["Subjects"] or "") + ";" + (row["Bookshelves"] or ""))]
    random.Random(SEED).shuffle(rows)
    selected = []
    total_frequency = Counter()
    document_frequency = Counter()
    cursor = 0
    while len(selected) < TARGET and cursor < len(rows):
        batch = rows[cursor:cursor + max(40, WORKERS * 4)]
        cursor += len(batch)
        with ThreadPoolExecutor(max_workers=WORKERS) as executor:
            futures = [executor.submit(download_book, row) for row in batch]
            for future in as_completed(futures):
                result = future.result()
                if result is None or len(selected) >= TARGET:
                    continue
                row, url, byte_count, text = result
                counts = Counter()
                for match in TOKEN.finditer(text.replace("’", "'")):
                    word = match.group(0).lower().strip("'-")
                    if word not in existing and valid_candidate(word, dictionary):
                        counts[word] += 1
                total_frequency.update(counts)
                document_frequency.update(counts.keys())
                selected.append({
                    "id": int(row["Text#"]), "title": row["Title"], "authors": row["Authors"],
                    "canonicalUrl": f"https://www.gutenberg.org/ebooks/{row['Text#']}", "textUrl": url,
                    "bytes": byte_count, "candidateTokenCount": sum(counts.values()), "uniqueCandidateCount": len(counts),
                })
                print(f"processed {len(selected)}/{TARGET} books", flush=True)
    if len(selected) != TARGET:
        raise RuntimeError(f"Only {len(selected)} usable books were downloaded")
    stats = sorted(total_frequency, key=lambda word: (-document_frequency[word], -total_frequency[word], word))
    STATS_PATH.write_text("word\tdocuments\tfrequency\n" + "\n".join(f"{word}\t{document_frequency[word]}\t{total_frequency[word]}" for word in stats) + "\n", encoding="utf-8")
    candidates = sorted(word for word in stats if document_frequency[word] >= 4 and total_frequency[word] >= 8)
    CANDIDATE_PATH.write_text("\n".join(candidates) + "\n", encoding="utf-8")
    manifest = {
        "generatedAt": __import__("datetime").datetime.now(__import__("datetime").timezone.utc).isoformat(),
        "randomSeed": SEED, "excludedLedgerBookCount": len(used_ids), "eligibleCatalogRows": len(rows), "selectedBookCount": len(selected),
        "rawDictionaryCandidateCount": len(stats), "sourceCandidateCount": len(candidates),
        "minimumDocumentFrequency": 4, "minimumTotalFrequency": 8, "books": sorted(selected, key=lambda book: book["id"]),
    }
    MANIFEST_PATH.write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding="utf-8")
    print(json.dumps({key: manifest[key] for key in ("eligibleCatalogRows", "selectedBookCount", "rawDictionaryCandidateCount", "sourceCandidateCount")}), flush=True)


if __name__ == "__main__":
    main()
