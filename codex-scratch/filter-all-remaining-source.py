import json
import re
from pathlib import Path

AFFIX_PATH = Path(r"C:\Users\Bryan\AppData\Local\Programs\MiKTeX\hunspell\dicts\en_US.aff")
DICTIONARY_PATH = Path(r"C:\Users\Bryan\AppData\Local\Programs\MiKTeX\hunspell\dicts\en_US.dic")
INPUT_PATH = Path(r"codex-scratch\batch-all-remaining-exact-remaining.json")
CODE_SCRAPE_PATH = Path(r"codex-scratch\batch-all-remaining-code-scrape.json")
OUTPUT_PATH = Path(r"codex-scratch\batch-all-remaining-source-candidates.txt")
REJECTED_PATH = Path(r"codex-scratch\batch-all-remaining-source-rejected.txt")

EXPLICIT_DROP = {
    "archontos", "radiation", "nuclear", "science",
    "str", "dex", "int", "wis", "cha", "con", "thac", "fgt", "bab", "enc",
    "rtk", "git", "grep", "npm", "pytest", "codex", "json", "yaml", "html", "css",
    "javascript", "powershell", "localhost", "plugin", "plugins", "filepath", "filename",
    "namespace", "nullable", "readonly", "runtime", "serializer", "stylesheet", "tooling",
    "untracked", "frontmatter", "backlinks", "obsidian", "scarlethorizons",
    "st-level", "th-level", "x-in", "youll",
    "app", "application", "assembly", "asset", "assets", "backstory", "backup", "baseline",
    "bidirectional", "bitmap", "browser", "byte", "bytes", "cached", "capability", "chapter", "chapters",
    "cleanup", "code", "coded", "codes", "codified", "columns", "comments", "commits", "computers", "computing",
    "contents", "data", "definitions", "diagrams", "digital", "directories", "directory", "disk", "document",
    "documentation", "documents", "download", "downloaded", "downloads", "edit", "edited", "email", "endpoint",
    "endpoints", "entries", "exception", "exceptions", "exports", "extracted", "extracting", "extracts", "fallback",
    "file", "filed", "files", "filing", "filters", "flags", "folder", "folders", "font", "formats", "framework",
    "generator", "graphics", "grid", "grouped", "grouping", "guidelines", "hacking", "hacks", "headers", "helper",
    "icon", "icons", "importing", "imports", "indexed", "informational", "initializes", "installation", "instance",
    "instructional", "instructions", "integer", "internet", "issue", "issues", "italics", "layout", "layouts",
    "ledger", "lines", "list", "listed", "listing", "lists", "log", "logged", "logger", "loggers", "logging",
    "logs", "lowercase", "mainframe", "marker", "markers", "matrix", "max", "metadata", "method", "methods",
    "min", "mode", "model", "modes", "monitor", "monitored", "monitoring", "network", "networks", "normalizes",
    "numerical", "occurrences", "online", "operations", "optimize", "output", "outline", "outlined", "overview",
    "paper", "parentheses", "parses", "patch", "patched", "patches", "pics", "pixels", "platform", "post", "posted",
    "posts", "preference", "preferences", "print", "prints", "procedural", "procedure", "processed", "processes",
    "processing", "profile", "program", "project", "projected", "projection", "proxies", "proxy", "queries", "quote",
    "randomized", "rating", "references", "refresh", "refreshes", "release", "released", "request", "requested",
    "requirements", "response", "responses", "resume", "resumed", "resumes", "resuming", "revision", "sampled",
    "scan", "scanned", "scanning", "scans", "scrape", "scraped", "screen", "section", "sections", "select",
    "selected", "selects", "sentence", "separator", "sequence", "server", "session", "sessions", "settings",
    "slideshow", "snapshot", "snippets", "socket", "sourced", "sources", "specified", "specify", "stack", "stacked",
    "startup", "state", "states", "static", "storage", "string", "strings", "structured", "summary", "suppress",
    "sync", "synchronized", "syntax", "system", "systems", "tab", "temp", "terminology", "test", "tested", "testing",
    "tests", "text", "timeline", "timeout", "timer", "timers", "token", "tools", "top", "trace", "tracked",
    "tracker", "trackers", "tutorials", "update", "updates", "upgrade", "usages", "user", "users", "utility",
    "utilization", "valid", "validates", "validation", "var", "variable", "variant", "version", "versions", "video",
    "viewers", "visibility", "visualize", "workflow", "worksheet", "word", "words",
    "antibiotic", "charlie", "china", "dollar", "esp", "excl", "faggot", "fer", "govt", "hgt", "japan",
    "jenny", "jun", "lbs", "marijuana", "medication", "meth", "morocco", "movies", "pas", "phones", "poi",
    "pro", "rated", "restaurant", "soda", "store", "stores", "storing", "tops", "val", "yer",
}

MANUAL_KEEP = {
    "adamantine", "all-knowing", "amoeboids", "apothecarial", "archmages", "archomancy", "archwizard",
    "armage", "armourer", "athame", "autarch", "automata", "azata", "badland", "bas-reliefs", "beast-kin",
    "beast-men", "beastman", "benefitted", "berserker", "berserkers", "blood-binder", "bullywug", "caprines",
    "celestials", "chokepoint", "cleric-druids", "concedes", "concession", "confront", "confronted", "conjunction",
    "conserve", "consorted", "contend", "contract-devils", "criticals", "crocodilian", "cross-breeds", "cyclopean",
    "deadfall", "debouchment", "deciphered", "decomposed", "decomposes", "decreased", "defilers", "dehydration",
    "deific", "descents", "desiring", "diabolists", "diseases", "disabuse", "disagreeable", "disbelief", "disengage",
    "disguises", "disinterest", "dislike", "dismissed", "disorganized", "displacer", "disservice", "distends",
    "distill", "distrustful", "dracolich", "dracoliches", "draconic", "dragonborn", "draught", "druidess", "druidic",
    "dwarf-hold", "dwarvish", "dweomered", "elementalist", "empathic", "ever-living", "everflame", "excoriator",
    "excoriators", "fletching", "gaol", "geomancy", "geometers", "giantmen", "goatman", "goblinkind", "goblinoid",
    "golems", "greaves", "guerdon", "half-orc", "hearthfires", "hellhound", "hellhounds", "henge", "heptagrams",
    "hexcraft", "hive-mind", "hominins", "immunities", "inbreeding", "incomprehensibly", "incredible", "incredulity",
    "incredulously", "indifference", "indigestible", "inexperience", "infused", "injustice", "insectoid", "instances",
    "intending", "intolerant", "intoning", "invulnerabilities", "kuo-toa", "laboured", "lich", "lifedrain", "lifeforce",
    "lizard-folk", "lizardfolk", "lizardman", "lockpicking", "longships", "loremaster", "lycanthrope", "lycanthropy",
    "medusan", "mousefolk", "mudras", "multiattack", "multigenerational", "non-combatant", "non-combatants",
    "oathbreakers", "organisation", "owlbears", "paladinic", "palisaded", "paralysation", "paralyse", "pictograms",
    "ploughs", "polearm", "postern", "puissance", "pyromancer", "realigning", "reappears", "rearranged", "reattach",
    "recapture", "receded", "recharge", "recite", "reconsecrated", "recounting", "redeemed", "rediscovers", "reeds",
    "reflexes", "reincarnate", "reintegrate", "reloaded", "reply", "repose", "reposes", "repossess", "reproduction",
    "repulsed", "restrains", "retaken", "reunification", "ribcage", "ribcages", "riverwarden", "rune-carved",
    "rune-staff", "sahuagin", "saurians", "scriptorium", "sellsword", "shape-changers", "shape-changing", "shapechange",
    "share-croppers", "shortswords", "shrieker", "shrooms", "sigils", "skullcrusher", "slime-mold", "slimes", "slinked",
    "smithwork", "sorcerous", "spellbooks", "spirit-binding", "spiritspeaker", "steeders", "steelskin", "stirge",
    "subdual", "subtype", "sumptuary", "svirfneblin", "swanmay", "teleporter", "theatre", "thrikreen", "tortle",
    "trapspringer", "travois", "treant", "truesilver", "tuatara", "tunnellers", "unbiased", "unburdened", "uncertainty",
    "unchanging", "unclaimed", "uncoils", "unconsciousness", "uncover", "undeath", "undeserving", "undid", "undiluted",
    "undisguised", "undone", "unexplored", "unfelt", "unfold", "unforgiving", "unidentified", "uninterested", "unleashes",
    "unmistakable", "unperturbed", "unshaken", "untrapped", "untroubled", "unused", "unusually", "unwilling",
    "unwillingly", "unwritten", "warband", "warhammer", "weaponsmith", "wereboar", "wererats", "weretiger", "wilful",
    "witch-bottle", "wolfsbane", "worksite", "wurms", "wyrdwood", "wyrms", "youngling",
}


def load_suffix_rules():
    lines = AFFIX_PATH.read_text(encoding="utf-8-sig").splitlines()
    suffixes = {}
    index = 0
    while index < len(lines):
        fields = lines[index].split()
        if len(fields) == 4 and fields[0] == "SFX" and fields[2] in ("Y", "N") and fields[3].isdigit():
            flag = fields[1]
            count = int(fields[3])
            rules = []
            for offset in range(1, count + 1):
                rule = lines[index + offset].split()
                if len(rule) >= 5:
                    rules.append((
                        "" if rule[2] == "0" else rule[2],
                        "" if rule[3] == "0" else rule[3].split("/")[0],
                        rule[4],
                    ))
            suffixes[flag] = rules
            index += count
        index += 1
    return suffixes


def apply_suffix(word, rule):
    strip, addition, condition = rule
    try:
        if not re.search(f"(?:{condition})$", word):
            return None
    except re.error:
        return None
    if strip and not word.endswith(strip):
        return None
    return word[:-len(strip)] + addition if strip else word + addition


suffixes = load_suffix_rules()
dictionary_forms = set()
for line in DICTIONARY_PATH.read_text(encoding="utf-8-sig").splitlines()[1:]:
    stem_and_flags = line.split("\t", 1)[0]
    if "/" in stem_and_flags:
        original_stem, flags = stem_and_flags.split("/", 1)
    else:
        original_stem, flags = stem_and_flags, ""
    if not original_stem or not original_stem[0].islower():
        continue
    stem = original_stem.lower()
    if not re.fullmatch(r"[a-z][a-z'-]*", stem):
        continue
    dictionary_forms.add(stem)
    for flag in flags:
        for rule in suffixes.get(flag, []):
            form = apply_suffix(stem, rule)
            if form and re.fullmatch(r"[a-z][a-z'-]*", form):
                dictionary_forms.add(form)

data = json.loads(INPUT_PATH.read_text(encoding="utf-8"))
all_occurrences = {item["Word"].lower(): int(item["Occurrences"]) for item in data}
code_scrape = json.loads(CODE_SCRAPE_PATH.read_text(encoding="utf-8"))
code_only = {
    item["Word"].lower()
    for item in code_scrape["Candidates"]
    if all_occurrences.get(item["Word"].lower()) == int(item["Occurrences"])
}
kept = []
rejected = []
for item in data:
    word = item["Word"].lower()
    if word in EXPLICIT_DROP or word in code_only:
        rejected.append(item)
    elif word in dictionary_forms or word in MANUAL_KEEP:
        kept.append(word)
    else:
        rejected.append(item)

OUTPUT_PATH.write_text("\n".join(sorted(set(kept))) + "\n", encoding="utf-8")
REJECTED_PATH.write_text(
    "\n".join(f"{item['Word']}|{item['Occurrences']}" for item in rejected) + "\n",
    encoding="utf-8",
)
print(json.dumps({"exact": len(data), "kept": len(set(kept)), "rejected": len(rejected)}))
