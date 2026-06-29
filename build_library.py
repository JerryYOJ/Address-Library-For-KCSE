"""Join the Steam->GOG and Steam->Epic diffs into a unified cross-version address
library, then (by default) compile it to the deployable id-keyed .bin tables.

Pipeline:
  1. parse <diffdir>/steam_gog/outputsorted.txt + steam_epic/outputsorted.txt
     -> address_library/kcd_address_library.csv  (+ _all3_perfect.csv, summary.txt)
  2. ../analysis/bootstrap_id_registry.py --full   (append-only id registry, full corpus)
  3. ../analysis/build_addresslib_bin.py --build X  (per-distribution id-keyed .bin)

Note: step 2/3 preserve any code-anchored corrections already in the registry
(bootstrap is append-only). Re-run codeanchor_full.py --apply afterwards to
correct NEW entries from a fresh diff, then re-run with --bins-only.

Usage:
  python build_library.py <diffdir> [--build 404-504czj4] [--no-bins]
  python build_library.py --bins-only [--build 404-504czj4]
"""
import argparse
import csv
import os
import subprocess
import sys
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "address_library")
ANALYSIS = os.path.normpath(os.path.join(HERE, "..", "analysis"))  # registry/bin build scripts live here
IMAGEBASE = 0x180000000
HDR = ["id_steam_rva", "steam_abs", "gog_abs", "epic_abs", "type", "gog_score", "epic_score"]


def parse_sorted(path):
    """steam_abs -> (target_abs, type, score_str) for matched rows only."""
    m = {}
    with open(path, encoding="utf-8", errors="replace") as f:
        for i, line in enumerate(f):
            if i < 2:            # header + '====' separator
                continue
            src = line[4:20].strip()
            tgt = line[20:36].strip()
            typ = line[46:54].strip()
            sc = line[54:64].strip()
            if not src or not tgt:
                continue          # unmatched row (only one side present)
            try:
                s = int(src, 16); t = int(tgt, 16)
            except ValueError:
                continue
            m[s] = (t, typ, sc)
    return m


def build_csv(diffdir):
    gog = parse_sorted(os.path.join(diffdir, "steam_gog", "outputsorted.txt"))
    epic = parse_sorted(os.path.join(diffdir, "steam_epic", "outputsorted.txt"))
    print("parsed gog matches=%d  epic matches=%d" % (len(gog), len(epic)))

    rows = []
    for s in sorted(set(gog) | set(epic)):
        g = gog.get(s); e = epic.get(s)
        rows.append({
            "id":         "%X" % (s - IMAGEBASE),
            "steam":      "%X" % s,
            "gog":        ("%X" % g[0]) if g else "",
            "epic":       ("%X" % e[0]) if e else "",
            "type":       (g or e or (0, "", ""))[1] or "",
            "gog_score":  g[2] if g else "",
            "epic_score": e[2] if e else "",
        })

    def emit(name, subset):
        with open(os.path.join(OUT, name), "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f); w.writerow(HDR)
            for r in subset:
                w.writerow([r["id"], r["steam"], r["gog"], r["epic"], r["type"], r["gog_score"], r["epic_score"]])

    emit("kcd_address_library.csv", rows)
    all3p = [r for r in rows if r["gog"] and r["epic"] and r["gog_score"] == "1" and r["epic_score"] == "1"]
    emit("kcd_address_library_all3_perfect.csv", all3p)

    lines = ["KCD cross-version address library", "=================================",
             "canonical ID = Steam RVA (abs - 0x180000000)", "",
             "Steam objects with >=1 cross-version match : %d" % len(rows),
             "  matched in GOG                           : %d" % len(gog),
             "  matched in Epic                          : %d" % len(epic),
             "  present in all three AND perfect both    : %d" % len(all3p), "",
             "By type (all matched):"]
    for t, c in Counter(r["type"] for r in rows).most_common():
        lines.append("  %-8s %d" % (t or "(blank)", c))
    summary = "\n".join(lines)
    with open(os.path.join(OUT, "summary.txt"), "w", encoding="utf-8") as f:
        f.write(summary + "\n")
    print(summary)
    print("\nwrote CSV ->", OUT)


def compile_bins(build):
    """Update the id registry (append-only) and compile the per-distribution bins."""
    for script, extra in (("bootstrap_id_registry.py", ["--full"]),
                          ("build_addresslib_bin.py", ["--build", build])):
        print("\n>>> %s %s" % (script, " ".join(extra)))
        subprocess.run([sys.executable, os.path.join(ANALYSIS, script), *extra], check=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("diffdir", nargs="?", help="dir with steam_gog/ and steam_epic/ outputsorted.txt")
    ap.add_argument("--build", default="404-504czj4", help="build code for the .bin filenames")
    ap.add_argument("--no-bins", action="store_true", help="only write the CSV, skip registry+bin compile")
    ap.add_argument("--bins-only", action="store_true", help="skip diff parsing; just (re)compile bins from the existing registry")
    args = ap.parse_args()

    os.makedirs(OUT, exist_ok=True)
    if not args.bins_only:
        if not args.diffdir:
            ap.error("diffdir is required (or use --bins-only)")
        build_csv(args.diffdir)
    if not args.no_bins:
        compile_bins(args.build)


if __name__ == "__main__":
    main()
