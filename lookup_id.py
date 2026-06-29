"""Look up an address-library id <-> address, against kcd_id_registry.csv.

A maintainer who finds a function in IDA on ANY distribution can get its stable id
without needing the Steam 1.9.8 binary -- just search by their version's address.

  python lookup_id.py 0x1226EA8        # by address (any of steam/gog/epic), hex
  python lookup_id.py --id 123         # by id -> shows every version's RVA
  python lookup_id.py 0x44E1A0 --dist gog   # restrict address search to one column
"""
import argparse
import csv
import os

REG = os.path.join(os.path.dirname(__file__), "address_library", "kcd_id_registry.csv")
IMAGEBASE = 0x180000000
COLS = ("steam_rva", "gog_rva", "epic_rva")


def norm(h):
    """accept 0x18012..(abs) or 12.. (rva); return rva hex upper, no 0x."""
    v = int(h, 16)
    if v >= IMAGEBASE:
        v -= IMAGEBASE
    return "%X" % v


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("value", nargs="?", help="address (hex, abs or rva)")
    ap.add_argument("--id", type=int, help="look up by id instead")
    ap.add_argument("--dist", choices=("steam", "gog", "epic"), help="restrict address search")
    args = ap.parse_args()

    with open(REG, encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f))

    hits = []
    if args.id is not None:
        hits = [r for r in rows if int(r["id"]) == args.id]
    elif args.value:
        want = norm(args.value)
        cols = (args.dist + "_rva",) if args.dist else COLS
        hits = [r for r in rows if any(r[c] == want for c in cols)]
    else:
        ap.error("give an address or --id")

    if not hits:
        print("no match")
        return
    for r in hits:
        ty = r.get("type") or "?"
        warn = "  [DATA-verify by code-ref]" if ty in ("var", "?", "") else ""
        print("id %s  type=%s  name=%s  steam=0x%s  gog=%s  epic=%s%s" % (
            r["id"], ty, r["name"] or "-", r["steam_rva"] or "-",
            ("0x" + r["gog_rva"]) if r["gog_rva"] else "(unmatched)",
            ("0x" + r["epic_rva"]) if r["epic_rva"] else "(unmatched)", warn))


if __name__ == "__main__":
    main()
