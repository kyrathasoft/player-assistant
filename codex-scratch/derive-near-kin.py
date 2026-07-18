import json
import re
import sys
from pathlib import Path

AFFIX_PATH = Path(r"C:\Users\Bryan\AppData\Local\Programs\MiKTeX\hunspell\dicts\en_US.aff")
DICTIONARY_PATH = Path(r"C:\Users\Bryan\AppData\Local\Programs\MiKTeX\hunspell\dicts\en_US.dic")
SOURCE_PATH = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(r"codex-scratch\batch-next-source-candidates.txt")


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


def plausible_root(source, stem):
    if source == stem or source.startswith(stem):
        return True
    for ending, replacement in (("ies", "y"), ("ied", "y"), ("ying", "ie"), ("ing", "e"), ("ed", "e"), ("er", "e"), ("est", "e")):
        if source.endswith(ending) and source[:-len(ending)] + replacement == stem:
            return True
    return len(stem) > 2 and len(source) > len(stem) and source.startswith(stem + stem[-1])


suffixes = load_suffix_rules()
families_by_form = {}
for line in DICTIONARY_PATH.read_text(encoding="utf-8-sig").splitlines()[1:]:
    stem_and_flags = line.split("\t", 1)[0]
    if "/" in stem_and_flags:
        stem, flags = stem_and_flags.split("/", 1)
    else:
        stem, flags = stem_and_flags, ""
    stem = stem.lower()
    if not re.fullmatch(r"[a-z][a-z'-]*", stem):
        continue
    forms = {stem}
    for flag in flags:
        for suffix_rule in suffixes.get(flag, []):
            form = apply_suffix(stem, suffix_rule)
            if form:
                forms.add(form)
    if len(forms) > 1:
        for form in forms:
            families_by_form.setdefault(form, []).append((stem, forms))

sources = [line.strip() for line in SOURCE_PATH.read_text(encoding="utf-8").splitlines() if line.strip()]
families = {}
for source in sources:
    matches = [(stem, forms) for stem, forms in families_by_form.get(source, []) if plausible_root(source, stem)]
    if matches:
        longest_stem = max(len(stem) for stem, _ in matches)
        forms = set()
        for stem, stem_forms in matches:
            if len(stem) == longest_stem:
                forms.update(stem_forms)
        forms.discard(source)
        forms = {form for form in forms if re.fullmatch(r"[a-z][a-z'-]*", form)}
        if forms:
            families[source] = sorted(forms)

irregular = {
    "awoke": ["awake", "awakes", "awaking", "awoken"],
    "bought": ["buy", "buying", "buys"],
    "borne": ["bear", "bearing", "bears", "bore", "born"],
    "broke": ["break", "breaking", "breaks", "broken"],
    "chosen": ["choose", "chooses", "choosing", "chose"],
    "blew": ["blow", "blowing", "blown", "blows"],
    "dealt": ["deal", "dealing", "deals"],
    "forgot": ["forget", "forgetting", "forgets", "forgotten"],
    "fought": ["fight", "fighting", "fights"],
    "forsworn": ["forswear", "forswearing", "forswears", "forswore"],
    "gave": ["give", "given", "gives", "giving"],
    "gotten": ["get", "gets", "getting", "got"],
    "past": ["pass", "passed", "passes", "passing"],
    "reborne": ["rebear", "rebearing", "rebore", "reborn"],
    "shaken": ["shake", "shakes", "shaking", "shook"],
    "slept": ["sleep", "sleeping", "sleeps"],
    "shine": ["shining", "shone"],
    "shines": ["shining", "shone"],
    "spitting": ["spat", "spit", "spits"],
    "spoke": ["speak", "speaking", "speaks", "spoken"],
    "stuck": ["stick", "sticking", "sticks"],
    "swallows": ["swallowed", "swallowing"],
    "sworn": ["swear", "swearing", "swears", "swore"],
    "taught": ["teach", "teaches", "teaching"],
    "tell": ["telling", "told"],
    "tornados": ["tornado"],
    "wraps": ["wrapped", "wrapping"],
    "writ": ["write", "writes", "writing", "written", "wrote"],
    "written": ["write", "writes", "writing", "wrote"],
    "wakes": ["wake", "waking", "woke", "woken"],
    "withdrawing": ["withdraw", "withdrawn", "withdrew", "withdraws"],
    "win": ["winning", "wins", "won"],
    "women": ["woman"],
    "worse": ["bad", "worst"],
    "wrought": ["work", "worked", "working", "works"],
}
for source, forms in irregular.items():
    if source in sources:
        families[source] = sorted(set(families.get(source, [])) | set(forms))

near = sorted(set().union(*(set(forms) for forms in families.values()))) if families else []
result = json.dumps({"sources": sources, "families": families, "near": near}, separators=(",", ":"))
if len(sys.argv) > 2:
    Path(sys.argv[2]).write_text(result, encoding="utf-8")
else:
    print(result)
