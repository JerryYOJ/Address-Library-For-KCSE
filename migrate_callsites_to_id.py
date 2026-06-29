"""ONE-TIME: rewrite REL::ID(0x<steamRva>) sites to abstract ids from the registry.

  * Offsets_RTTI.h   { 0xRVA }            -> { <id> }   (+ 0xRVA kept in the comment)
  * Offsets_VTABLE.h ::REL::ID(0xRVA)     -> ::REL::ID(<id>)
  * everywhere else  REL::ID(0xRVA)       -> REL::ID(<id>)

Idempotency: only hex-literal forms are touched, so a second run is a no-op.
Reads/writes with newline='' to preserve existing CR/LF.
"""
import csv
import os
import re

HERE = os.path.dirname(__file__)
RE_ROOT = os.path.join(HERE, "..", "RE")
REG = os.path.join(HERE, "address_library", "kcd_id_registry.csv")
DEF = ("Offsets_VTABLE.h", "Offsets_RTTI.h")
SKIP_DIRS = {"CryEngine", "build", ".git", ".vs", "CMakeFiles", "_deps", "vcpkg_installed", "out"}


def iter_sources(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            if fn.endswith((".cpp", ".h")):
                yield os.path.join(dirpath, fn)

rva2id = {}
with open(REG, encoding="utf-8", newline="") as f:
    for r in csv.DictReader(f):
        rva2id[int(r["steam_rva"], 16)] = int(r["id"])


def idof(hexstr):
    return rva2id[int(hexstr, 16)]


def read(p):
    # latin-1 round-trips every byte 1:1 (our edits are ASCII-only), so non-UTF-8
    # source files are preserved exactly.
    with open(p, encoding="latin-1", newline="") as f:
        return f.read()


def write(p, t):
    with open(p, "w", encoding="latin-1", newline="") as f:
        f.write(t)


# 1. Offsets_RTTI.h -- keep the steam RVA in the comment for archive-holders
rtti = os.path.join(RE_ROOT, "include", "Offsets", "Offsets_RTTI.h")
t = read(rtti)
t = re.sub(
    r"(::REL::ID RTTI_\w+\s*)\{\s*(0x[0-9A-Fa-f]+)\s*\};([ \t]*//[ \t]*)",
    lambda mo: "%s{ %d };%s%s " % (mo.group(1), idof(mo.group(2)), mo.group(3), mo.group(2)),
    t)
write(rtti, t)

# 2. Offsets_VTABLE.h
vt = os.path.join(RE_ROOT, "include", "Offsets", "Offsets_VTABLE.h")
t = read(vt)
t = re.sub(r"::REL::ID\((0x[0-9A-Fa-f]+)\)",
           lambda mo: "::REL::ID(%d)" % idof(mo.group(1)), t)
write(vt, t)

# 3. literal call sites everywhere else
lit = re.compile(r"REL::ID\(\s*(0x[0-9A-Fa-f]+)\s*\)")
changed = []
for p in iter_sources(RE_ROOT):
    if os.path.basename(p) in DEF:
        continue
    t = read(p)
    new = lit.sub(lambda mo: "REL::ID(%d)" % rva2id[int(mo.group(1), 16)]
                  if int(mo.group(1), 16) in rva2id else mo.group(0), t)
    if new != t:
        write(p, new)
        changed.append(os.path.relpath(p, RE_ROOT))

print("RTTI + VTABLE tables rewritten")
print("literal call sites rewritten in %d files:" % len(changed))
for c in changed:
    print("  " + c)
