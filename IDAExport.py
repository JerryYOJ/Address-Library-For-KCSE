# IDAExport.py
#
# Port of meh321's original IDAExport.py to IDA 9.x / Python 3.
# Produces idaexport_*.txt files consumed by IDADiffCalculator.exe.
# Output format is byte-compatible with the original IDA6/Py2 exporter.
#
# Usage (recommended, in the IDA GUI):
#   1. (optional) set EXPORT_ROOT / EXPORT_OUTPUT_DIR; default is <script dir>\export\<binary>.
#   2. File -> Script file... -> select this file. Let it finish.
#   3. Move the produced idaexport_*.txt into a per-version directory.
#   4. Repeat for each binary, then run:
#        IDADiffCalculator.exe 64 <dir_A> <dir_B>
#
# Config overrides (define these in the global namespace before exec):
#   EXPORT_OUTPUT_DIR : output directory
#   EXPORT_RANGE      : (lo, hi) address window to restrict the export (for
#                       quick validation). None = whole image.
#   EXPORT_DO_OPHEX   : force operand radix to hex before reading disasm text.
#                       Mutates the IDB's operand display in memory (transient;
#                       do NOT save the IDB afterwards). Needed so symbolic
#                       operands don't break IDADiffCalculator's number parser.

import os
import idaapi
import idautils
import idc
import ida_bytes
import ida_funcs
import ida_segment
import ida_name
import ida_ida
import ida_lines
import ida_nalt

# ---------------------------------------------------------------------------
# configuration
# ---------------------------------------------------------------------------

# Output goes to <export root>\<binary name>\ so each connected binary auto-separates
# (WHGame, WHGame-GOG, WHGame-Epic) when you just run this in each instance.
# Root defaults to <this script's dir>\export; override with EXPORT_ROOT or
# EXPORT_OUTPUT_DIR in the global namespace before exec'ing.
try:
    _SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
except NameError:                       # __file__ absent in some IDA exec contexts
    _SCRIPT_DIR = os.getcwd()
_EXPORT_ROOT = globals().get("EXPORT_ROOT") or os.path.join(_SCRIPT_DIR, "export")
_binbase = os.path.splitext(os.path.basename(ida_nalt.get_input_file_path()))[0]
_DEFAULT_OUTPUT_DIR = os.path.join(_EXPORT_ROOT, _binbase)

def _cfg(name, default):
    g = globals()
    return g[name] if name in g and g[name] is not None else default

OUTPUT_DIR   = _cfg("EXPORT_OUTPUT_DIR", _DEFAULT_OUTPUT_DIR)
EXPORT_RANGE = _cfg("EXPORT_RANGE", None)
DO_OPHEX     = _cfg("EXPORT_DO_OPHEX", True)

file_version = 1

export_base_address = True
export_segments     = True
export_xrefs        = True
export_asm          = True
export_funcs        = True
export_globals      = True
export_names        = True
export_rtti         = False   # kept off (matches original default)
export_vtables      = True
export_string       = True

func_export_short = True       # only export func begin/end (no decompile)
dont_save = False

# demangle flag for short names
try:
    _SHORTDN = ida_ida.inf_get_short_demnames()
except Exception:
    try:
        _SHORTDN = idc.get_inf_attr(idc.INF_SHORT_DEMNAMES)
    except Exception:
        _SHORTDN = 0

if not OUTPUT_DIR.endswith(os.sep):
    OUTPUT_DIR += os.sep
if not os.path.isdir(OUTPUT_DIR):
    os.makedirs(OUTPUT_DIR)

# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------

def GetStr(num):
    return "%X" % num

def IsOkExport(ea):
    if EXPORT_RANGE is not None:
        return EXPORT_RANGE[0] <= ea < EXPORT_RANGE[1]
    return True

def GetPointerSize():
    return 8 if ida_ida.inf_is_64bit() else 4

def GetBaseAddress():
    return idaapi.get_imagebase()

def IsCode(ea):
    if ea == 0 or ea == idaapi.BADADDR:
        return False
    seg = ida_segment.getseg(ea)
    if not seg:
        return False
    if (seg.perm & ida_segment.SEGPERM_EXEC) == 0:
        return False
    return True

def PrepareFile(name):
    fh = open(OUTPUT_DIR + "idaexport_" + name + ".txt", "w",
              encoding="utf-8", errors="replace", newline="")
    fh.write("version\t")
    fh.write(GetStr(file_version))
    fh.write("\n")
    return fh

def FinishFile(fh):
    fh.close()

def WriteIfNot(handle, s, notstr):
    if s != notstr:
        handle.write(s)

def _seg_bounds(seg):
    begin = seg.start_ea
    end = seg.end_ea
    if EXPORT_RANGE is not None:
        begin = max(begin, EXPORT_RANGE[0])
        end = min(end, EXPORT_RANGE[1])
    return begin, end

def _segments():
    for s_ea in idautils.Segments():
        yield ida_segment.getseg(s_ea)

print("Beginning export")

# ---------------------------------------------------------------------------
# base address
# ---------------------------------------------------------------------------
if export_base_address:
    h = PrepareFile("base")
    h.write("baseaddress\t")
    h.write(GetStr(GetBaseAddress()))
    h.write("\n")
    FinishFile(h)

# ---------------------------------------------------------------------------
# segments
# ---------------------------------------------------------------------------
if export_segments:
    h = PrepareFile("segment")
    for seg in _segments():
        h.write("segment\t")
        h.write(GetStr(seg.start_ea)); h.write("\t")
        h.write(GetStr(seg.end_ea));   h.write("\t")
        h.write(ida_segment.get_segm_name(seg)); h.write("\t")
        h.write(GetStr(seg.perm))
        h.write("\n")
    FinishFile(h)

# ---------------------------------------------------------------------------
# strings
# ---------------------------------------------------------------------------
if export_string:
    h = PrepareFile("string")
    for s in idautils.Strings():
        if not IsOkExport(s.ea):
            continue
        stype = getattr(s, "strtype", None)
        if stype is None:
            stype = getattr(s, "type", 0)
        h.write("string\t")
        h.write(GetStr(s.ea));     h.write("\t")
        h.write(GetStr(s.length)); h.write("\t")
        h.write(GetStr(stype));    h.write("\t")
        txt = str(s).replace("\r", "$BR$").replace("\n", "$NL$").replace("\t", "$TB$")
        h.write(txt)
        h.write("\n")
    FinishFile(h)

# ---------------------------------------------------------------------------
# xrefs (every address; the heavy pass)
# ---------------------------------------------------------------------------
if export_xrefs:
    h = PrepareFile("xrefs")
    for seg in _segments():
        begin, end = _seg_bounds(seg)
        ea = begin
        while ea < end:
            for xref in idautils.XrefsTo(ea, 0):
                if xref.type != 0x15:
                    h.write("xref\t")
                    h.write(GetStr(ea));        h.write("\t")
                    h.write(GetStr(xref.frm));  h.write("\t")
                    h.write(GetStr(xref.type))
                    h.write("\n")
            ea += 1
    FinishFile(h)

# ---------------------------------------------------------------------------
# asm (disassembly + operand fields)
# ---------------------------------------------------------------------------
if export_asm:
    h = PrepareFile("asm")
    for seg in _segments():
        if (seg.perm & ida_segment.SEGPERM_EXEC) == 0:
            continue
        begin, end = _seg_bounds(seg)
        for ea in idautils.Heads(begin, end):
            if not IsOkExport(ea):
                continue
            dont_save = True
            if DO_OPHEX:
                idc.op_hex(ea, -1)
            disasm_line = ida_lines.tag_remove(ida_lines.generate_disasm_line(ea, 0))
            if not disasm_line:
                continue
            h.write("asm\t")
            h.write(GetStr(ea));          h.write("\t")
            h.write(disasm_line);         h.write("\t")
            h.write(idc.print_insn_mnem(ea))
            ins = idautils.DecodeInstruction(ea)
            if ins:
                for i in range(len(ins.ops)):
                    op = ins.ops[i]
                    if op.type == 0:
                        continue
                    h.write("\t")
                    h.write(GetStr(op.n));    h.write(" ")
                    h.write(GetStr(op.type)); h.write(" ")
                    WriteIfNot(h, GetStr(op.dtype),     "0"); h.write(" ")
                    WriteIfNot(h, GetStr(op.reg),       "0"); h.write(" ")
                    WriteIfNot(h, GetStr(op.phrase),    "0"); h.write(" ")
                    WriteIfNot(h, GetStr(op.value),     "0"); h.write(" ")
                    WriteIfNot(h, GetStr(op.addr),      "0"); h.write(" ")
                    WriteIfNot(h, GetStr(op.flags),     "8"); h.write(" ")
                    WriteIfNot(h, GetStr(op.specflag1), "0"); h.write(" ")
                    WriteIfNot(h, GetStr(op.specflag2), "0"); h.write(" ")
                    WriteIfNot(h, GetStr(op.specflag3), "0"); h.write(" ")
                    WriteIfNot(h, GetStr(op.specflag4), "0"); h.write(" ")
                    WriteIfNot(h, GetStr(op.specval),   "0")
            h.write("\n")
    FinishFile(h)

# ---------------------------------------------------------------------------
# functions (short: begin + end only)
# ---------------------------------------------------------------------------
if export_funcs:
    if not func_export_short:
        raise NotImplementedError("long func export (decompile) is not ported")
    h = PrepareFile("func")
    if EXPORT_RANGE is not None:
        fiter = idautils.Functions(EXPORT_RANGE[0], EXPORT_RANGE[1])
    else:
        fiter = idautils.Functions()
    for func_ea in fiter:
        if not IsOkExport(func_ea):
            continue
        f = ida_funcs.get_func(func_ea)
        if not f:
            continue
        dont_save = True
        h.write("func\t")
        h.write(GetStr(f.start_ea)); h.write("\t")
        h.write(GetStr(f.end_ea))
        h.write("\n")
    FinishFile(h)

# ---------------------------------------------------------------------------
# globals (data objects that have code/data xrefs)
# ---------------------------------------------------------------------------
def write_global(handle, ea):
    if ida_funcs.get_func_name(ea):      # skip addresses inside functions
        return
    has_xrefs = False
    for xref in idautils.XrefsTo(ea, 0):
        if xref.type != 0x15:
            has_xrefs = True
            break
    if not has_xrefs:
        return
    handle.write("global\t")
    handle.write(GetStr(ea))
    handle.write("\t")
    gti = idc.get_type(ea)
    if not gti:
        tif = idaapi.tinfo_t()
        if idaapi.get_tinfo(tif, ea):
            gti = tif.dstr()
    if not gti:
        gti = idc.guess_type(ea)
    if gti:
        handle.write(gti)
    handle.write("\n")

if export_globals:
    h = PrepareFile("global")
    for seg in _segments():
        nm = ida_segment.get_segm_name(seg)
        if (seg.perm & ida_segment.SEGPERM_EXEC) != 0 and nm == ".text":
            continue
        begin, end = _seg_bounds(seg)
        ea = begin
        while ea < end:
            if IsOkExport(ea):
                write_global(h, ea)
            ea += 1
    FinishFile(h)

# ---------------------------------------------------------------------------
# names
# ---------------------------------------------------------------------------
if export_names:
    h = PrepareFile("name")
    for ea, nm in idautils.Names():
        if not IsOkExport(ea):
            continue
        h.write("name\t")
        h.write(GetStr(ea)); h.write("\t")
        h.write(nm);         h.write("\t")
        dem = ida_name.demangle_name(nm, _SHORTDN)
        if dem:
            h.write(dem)
        h.write("\n")
    FinishFile(h)

# ---------------------------------------------------------------------------
# vtables (.rdata runs of code pointers)
# ---------------------------------------------------------------------------
def write_vtable(handle, ea):
    ps = GetPointerSize()
    first_fn = ida_bytes.get_qword(ea) if ps == 8 else ida_bytes.get_dword(ea)
    if not IsCode(first_fn):
        return
    has_xref = False
    for xref in idautils.XrefsTo(ea, 0):
        if xref.type == 0x15:
            continue
        if not IsCode(xref.frm):
            continue
        has_xref = True
        break
    if not has_xref:
        return
    handle.write("vtable\t")
    handle.write(GetStr(ea)); handle.write("\t")
    loc_ptr = ida_bytes.get_qword(ea - ps) if ps == 8 else ida_bytes.get_dword(ea - ps)
    handle.write(GetStr(loc_ptr))
    cur = ea
    is_first = True
    while True:
        fn = ida_bytes.get_qword(cur) if ps == 8 else ida_bytes.get_dword(cur)
        if not IsCode(fn):
            break
        if not is_first:
            stop = False
            for xref in idautils.XrefsTo(cur, 0):
                if xref.type == 0x15:
                    continue
                if not IsCode(xref.frm):
                    continue
                stop = True
                break
            if stop:
                break
        handle.write("\t")
        handle.write(GetStr(fn))
        cur += ps
        is_first = False
    handle.write("\n")

if export_rtti or export_vtables:
    h_vt = PrepareFile("vtable") if export_vtables else None
    ps = GetPointerSize()
    for seg in _segments():
        if ida_segment.get_segm_name(seg) != ".rdata":
            continue
        begin, end = _seg_bounds(seg)
        ea = begin
        while ea < end:
            if export_vtables and IsOkExport(ea):
                write_vtable(h_vt, ea)
            ea += ps
    if h_vt:
        FinishFile(h_vt)

print("Done with export")
if dont_save and DO_OPHEX:
    print("op_hex modified operand radix in memory -- close the IDB WITHOUT saving!")
