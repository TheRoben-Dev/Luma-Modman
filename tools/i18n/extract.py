#!/usr/bin/env python3
"""Lists the player-facing strings in Luma's C# sources as translation keys.

Plain literals stay as they are; interpolated strings ($"...") become patterns with {0}, {1}...
for each hole, the same keys the app's translation lookup matches at run time
(src/Luma.App/Scripts/Ui/I18n.cs). Writes data/translations/template.json (key -> "") and, with
--check LANG, lists keys missing from data/translations/LANG.json.
"""
import json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DIRS = ["src/Luma.App/Scripts", "src/Luma.Host", "src/Luma.Core"]
SKIP_FILES = {"SmokeTest.cs"}
# Calls whose strings never reach the screen.
SKIP_CALLS = re.compile(r"(AppLog\.\w+|Console\.\w+|GetMeta|SetMeta|HasMeta|GetNode|Environment\.\w+|Path\.\w+|Regex\w*|Process\w*|Add(Theme|Constant|Stylebox)\w*|Set(Color|Stylebox|Constant|FontSize|Icon)|Get(Color|Stylebox|Constant|FontSize)|IsActionPressed|ParseInputEvent|Json\w*|Http\w*|EmitSignal|Connect)\s*\($")


def strings(src):
    """Yields (text, is_interpolated, holes, offset) for each regular or interpolated string literal."""
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if src.startswith("//", i):
            i = src.find("\n", i)
            i = n if i < 0 else i
            continue
        if src.startswith("/*", i):
            i = src.find("*/", i + 2)
            i = n if i < 0 else i + 2
            continue
        if c == "'":
            j = i + 1
            while j < n and src[j] != "'":
                j += 2 if src[j] == "\\" else 1
            i = j + 1
            continue
        if src.startswith('"""', i) or src.startswith('$"""', i) or src.startswith('@"', i) or src.startswith('$@"', i) or src.startswith('@$"', i):
            # raw and verbatim strings: skipped (data, queries, paths)
            if '"""' in src[i:i + 4]:
                j = src.find('"""', src.find('"""', i) + 3)
                i = n if j < 0 else j + 3
            else:
                j = src.find('"', i) + 1
                while j < n:
                    if src[j] == '"' and src.startswith('""', j):
                        j += 2
                        continue
                    if src[j] == '"':
                        break
                    j += 1
                i = j + 1
            continue
        if c == '"' or (c == "$" and i + 1 < n and src[i + 1] == '"'):
            interp = c == "$"
            start = i
            j = i + (2 if interp else 1)
            out, holes = [], 0
            while j < n and src[j] != '"':
                if src[j] == "\\":
                    esc = src[j + 1]
                    out.append({"n": "\n", "t": "\t", '"': '"', "\\": "\\", "'": "'", "0": "\0"}.get(esc, esc))
                    j += 2
                    continue
                if interp and src.startswith("{{", j):
                    out.append("{"); j += 2; continue
                if interp and src.startswith("}}", j):
                    out.append("}"); j += 2; continue
                if interp and src[j] == "{":
                    depth, k = 1, j + 1
                    while k < n and depth:
                        if src[k] == '"' or (src[k] == "$" and src[k + 1] == '"'):
                            # nested string inside the hole
                            k += 2 if src[k] == "$" else 1
                            while k < n and src[k] != '"':
                                k += 2 if src[k] == "\\" else 1
                            k += 1
                            continue
                        if src[k] == "{":
                            depth += 1
                        elif src[k] == "}":
                            depth -= 1
                        k += 1
                    out.append("{%d}" % holes)
                    holes += 1
                    j = k
                    continue
                out.append(src[j])
                j += 1
            yield "".join(out), interp, holes, start
            i = j + 1
            continue
        i += 1


def shown(text, src, at):
    """Heuristic: words a person reads, not ids, paths, formats or log lines."""
    t = text.strip()
    letters = re.sub(r"\{\d+\}", "", t)
    if len(re.findall(r"[A-Za-z]", letters)) < 3:
        return False
    if re.fullmatch(r"[a-z0-9_.:/\-+*#?=&%{}\[\] ]+", t):  # ids, keys, paths, lowercase tokens
        return False
    if re.search(r"(^|[^ ])/[a-zA-Z]|\\\\|\.(json|txt|ini|exe|dll|cs|png|svg|zip|esp|esm|log|md)\b|https?:|res://|user://|^\w+:\w|\{\d+\}\{\d+\}$", t) and " " not in t:
        return False
    if " " not in t and not re.match(r"^[A-Z][a-z]+[.!?…]?$", t) and not re.match(r"^[A-Z][a-z]+( [A-Za-z]+)*…?$", t):
        return False
    before = src[max(0, at - 60):at]
    if SKIP_CALLS.search(before.rstrip()) or re.search(r"AppLog\.\w+\(\s*\$?$", before) or re.search(r"(case|=>)\s*$", before) and t.islower():
        return False
    if re.search(r"\[(Obsolete|JsonPropertyName|Description)\($", before.rstrip()):
        return False
    return True


def collect():
    keys = {}
    for d in DIRS:
        for root, _, files in os.walk(os.path.join(ROOT, d)):
            if "/obj" in root or "/bin" in root:
                continue
            for f in files:
                if not f.endswith(".cs") or f in SKIP_FILES:
                    continue
                path = os.path.join(root, f)
                src = open(path, encoding="utf-8").read()
                for text, interp, holes, at in strings(src):
                    if shown(text, src, at):
                        keys.setdefault(text, os.path.relpath(path, ROOT))
    return keys


def main():
    keys = collect()
    out = os.path.join(ROOT, "data", "translations")
    os.makedirs(out, exist_ok=True)
    if len(sys.argv) > 2 and sys.argv[1] == "--check":
        have = json.load(open(os.path.join(out, sys.argv[2] + ".json"), encoding="utf-8"))
        missing = [k for k in keys if k not in have.get("strings", {})]
        print(f"{len(missing)} of {len(keys)} strings missing in {sys.argv[2]}")
        for k in missing[:50]:
            print("  " + json.dumps(k, ensure_ascii=False))
        return
    template = {"language": "", "name": "", "strings": {k: "" for k in sorted(keys)}}
    json.dump(template, open(os.path.join(out, "template.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"{len(keys)} strings")


if __name__ == "__main__":
    main()
