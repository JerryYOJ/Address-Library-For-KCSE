"""Audit: every REL::ID(<id>) the code references must exist in EACH distribution's
id-keyed table (steam/gog/epic), or REL::IDDatabase::id2offset fails at runtime on
that distribution. Reports any id used in code but missing from a table.
"""
import os
import re
import struct

HERE = os.path.dirname(__file__)
RE_ROOT = os.path.join(HERE, "..", "RE")
BINDIR = os.path.join(HERE, "address_library")
BUILD = "404-504czj4"
SKIP_DIRS = {"CryEngine", "build", ".git", ".vs", "CMakeFiles", "_deps", "vcpkg_installed", "out"}

# REL::ID(123) / ::REL::ID(123)  and the RTTI table form  RTTI_x { 123 }
ID_RE = re.compile(r"REL::ID\(\s*(\d+)\s*\)")
RTTI_RE = re.compile(r"::REL::ID RTTI_\w+\s*\{\s*(\d+)\s*\}")


def iter_sources(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            if fn.endswith((".cpp", ".h")):
                yield os.path.join(dirpath, fn)


def load_bin(path):
    ids = set()
    with open(path, "rb") as f:
        assert f.read(4) == b"KASL"
        _fmt, _dist, count = struct.unpack("<III", f.read(12))
        data = f.read(count * 8)
    for i in range(count):
        rid, _off = struct.unpack_from("<II", data, i * 8)
        ids.add(rid)
    return ids


def collect_used():
    used = {}  # id -> [files]
    for p in iter_sources(RE_ROOT):
        txt = open(p, encoding="latin-1").read()
        for m in list(ID_RE.finditer(txt)) + list(RTTI_RE.finditer(txt)):
            used.setdefault(int(m.group(1)), []).append(os.path.relpath(p, RE_ROOT))
    return used


def main():
    used = collect_used()
    print("distinct REL::ID(<id>) referenced in code: %d" % len(used))
    for name in ("steam", "gog", "epic"):
        binpath = os.path.join(BINDIR, "kcd_addresslib_%s_%s.bin" % (name, BUILD))
        ids = load_bin(binpath)
        missing = sorted(i for i in used if i not in ids)
        print("\n=== %s (%d ids) : %d/%d referenced ids covered ===" % (
            name, len(ids), len(used) - len(missing), len(used)))
        for i in missing:
            print("  MISSING id %d  (%s)" % (i, ", ".join(sorted(set(used[i])))))


if __name__ == "__main__":
    main()
