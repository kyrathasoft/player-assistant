import html
import importlib.util
import json
import random
import re
import sys
import urllib.request
from collections import Counter
from concurrent.futures import ThreadPoolExecutor, as_completed
from html.parser import HTMLParser
from pathlib import Path

ROUND = sys.argv[1] if len(sys.argv) > 1 else "round1"
SEED = int(sys.argv[2]) if len(sys.argv) > 2 else 20260724
TARGET = int(sys.argv[3]) if len(sys.argv) > 3 else 100
PREFIX = Path(f"codex-scratch/standard-ebooks-{ROUND}")
EXISTING_PATH = Path("codex-scratch/gutenberg-existing-terms.txt")
GUTENBERG_MANIFESTS = sorted(Path("codex-scratch").glob("gutenberg-round*-manifest.json"))
LEDGER_PATH = Path("dont-scrape-standard-ebooks-again.md")

spec = importlib.util.spec_from_file_location("gutenberg_scraper", "codex-scratch/scrape-gutenberg-200.py")
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)


class MainTextParser(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.main_depth = 0
        self.skip_depth = 0
        self.parts = []

    def handle_starttag(self, tag, attrs):
        if tag == "main":
            self.main_depth += 1
        elif self.main_depth and tag in {"script", "style", "svg", "nav", "header", "footer"}:
            self.skip_depth += 1

    def handle_endtag(self, tag):
        if tag == "main" and self.main_depth:
            self.main_depth -= 1
        elif self.main_depth and tag in {"script", "style", "svg", "nav", "header", "footer"} and self.skip_depth:
            self.skip_depth -= 1

    def handle_data(self, data):
        if self.main_depth and not self.skip_depth:
            self.parts.append(data)


def request_bytes(url, limit=None):
    request = urllib.request.Request(url, headers={"User-Agent": "player-assistant-orcish-corpus/1.0", "Accept": "text/html,application/json"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read() if limit is None else response.read(limit + 1)


def normalized_title(value):
    return re.sub(r"[^a-z0-9]+", "", html.unescape(value).lower())


def repo_to_book(repo):
    description = repo.get("description") or ""
    match = re.search(r"edition of (.+?), by (.+?)(?:\. Translated|\. Illustrated|\. Edited|$)", description)
    if not match or "_" not in repo["name"]:
        return None
    title, authors = match.group(1).strip(), match.group(2).strip().rstrip(".")
    path = repo["name"].replace("_", "/")
    canonical = f"https://standardebooks.org/ebooks/{path}"
    return {"title": title, "authors": authors, "canonicalUrl": canonical, "textUrl": canonical + "/text/single-page", "repoUrl": repo["html_url"]}


def download_book(book):
    try:
        payload = request_bytes(book["textUrl"], 6_000_000)
        if len(payload) > 6_000_000:
            return None
        parser = MainTextParser()
        parser.feed(payload.decode("utf-8", errors="replace"))
        text = " ".join(parser.parts)
        if len(text) < 10_000:
            return None
        return book, len(payload), text
    except Exception:
        return None


def main():
    existing = {line.strip().lower() for line in EXISTING_PATH.read_text(encoding="utf-8-sig").splitlines() if line.strip()}
    dictionary = shared.load_valid_words()
    prior_titles = set()
    for path in GUTENBERG_MANIFESTS:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        prior_titles.update(normalized_title(book["title"]) for book in data.get("books", []))
    used_urls = set(re.findall(r"https://standardebooks\.org/ebooks/[^\s)]+", LEDGER_PATH.read_text(encoding="utf-8-sig"))) if LEDGER_PATH.exists() else set()
    repos = []
    for page in range(1, 8):
        url = f"https://api.github.com/orgs/standardebooks/repos?per_page=100&page={page}&type=public&sort=updated"
        repos.extend(json.loads(request_bytes(url).decode("utf-8")))
    books = []
    seen_titles = set()
    for repo in repos:
        book = repo_to_book(repo)
        if not book:
            continue
        key = normalized_title(book["title"])
        if not key or key in prior_titles or key in seen_titles or book["canonicalUrl"] in used_urls:
            continue
        seen_titles.add(key)
        books.append(book)
    random.Random(SEED).shuffle(books)
    selected = []
    total_frequency = Counter()
    document_frequency = Counter()
    cursor = 0
    while len(selected) < TARGET and cursor < len(books):
        batch = books[cursor:cursor + 24]
        cursor += len(batch)
        with ThreadPoolExecutor(max_workers=4) as executor:
            futures = [executor.submit(download_book, book) for book in batch]
            for future in as_completed(futures):
                result = future.result()
                if result is None or len(selected) >= TARGET:
                    continue
                book, byte_count, text = result
                counts = Counter()
                for match in shared.TOKEN.finditer(text.replace("’", "'")):
                    word = match.group(0).lower().strip("'-")
                    if word not in existing and shared.valid_candidate(word, dictionary):
                        counts[word] += 1
                total_frequency.update(counts)
                document_frequency.update(counts.keys())
                selected.append({**book, "bytes": byte_count, "candidateTokenCount": sum(counts.values()), "uniqueCandidateCount": len(counts)})
                print(f"processed {len(selected)}/{TARGET} books", flush=True)
    if len(selected) != TARGET:
        raise RuntimeError(f"Only {len(selected)} usable title-unique books were downloaded")
    stats = sorted(total_frequency, key=lambda word: (-document_frequency[word], -total_frequency[word], word))
    Path(str(PREFIX) + "-word-stats.tsv").write_text("word\tdocuments\tfrequency\n" + "\n".join(f"{word}\t{document_frequency[word]}\t{total_frequency[word]}" for word in stats) + "\n", encoding="utf-8")
    candidates = sorted(word for word in stats if document_frequency[word] >= 4 and total_frequency[word] >= 8)
    Path(str(PREFIX) + "-source-candidates.txt").write_text("\n".join(candidates) + "\n", encoding="utf-8")
    manifest = {
        "generatedAt": __import__("datetime").datetime.now(__import__("datetime").timezone.utc).isoformat(),
        "randomSeed": SEED, "catalogRepositoryCount": len(repos), "eligibleTitleUniqueBooks": len(books),
        "selectedBookCount": len(selected), "rawDictionaryCandidateCount": len(stats), "sourceCandidateCount": len(candidates),
        "books": sorted(selected, key=lambda book: normalized_title(book["title"])),
    }
    Path(str(PREFIX) + "-manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding="utf-8")
    print(json.dumps({key: manifest[key] for key in ("catalogRepositoryCount", "eligibleTitleUniqueBooks", "selectedBookCount", "rawDictionaryCandidateCount", "sourceCandidateCount")}), flush=True)


if __name__ == "__main__":
    main()
