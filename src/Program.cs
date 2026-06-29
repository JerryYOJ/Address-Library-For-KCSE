using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IDADiffCalculator
{
    class Program
    {
        static void Main(string[] args)
        {
            Migration.IDAListener.initFile();
            // Benchmark/compat switch: set IDADIFF_CONVERTLOC=1 to restore the
            // original (unconditional) loc-run -> function recovery pass.
            if (Environment.GetEnvironmentVariable("IDADIFF_CONVERTLOC") == "1")
                Migration.IDAMigrate.ConvertLocToFunc = true;
            try
            {
                #if DEBUG
                if (args.Length != 3)
                    args = new string[] { "64", @"D:\Games\SkyrimMods\SFDiff\1", @"D:\Games\SkyrimMods\SFDiff\2" };
                #endif
                if (args.Length != 3)
                {
                    Console.WriteLine("Syntax: IDADiffCalculator.exe <32 or 64> <input dir #1> <input dir #2>");
                    Console.WriteLine("Output will be written to output.txt in current directory.");
                }
                else
                {
                    string arch = args[0];
                    string dir1 = args[1];
                    string dir2 = args[2];

#if !DEBUG
                    try
                    {
#endif
                        var dir1d = new System.IO.DirectoryInfo(dir1);
                        var dir2d = new System.IO.DirectoryInfo(dir2);

                        ArchitectureTypes at;
                        long ati = Utility.ParseInt64ExactFast(arch, false);
                        if (ati == 32)
                            at = ArchitectureTypes.x86_32;
                        else if (ati == 64)
                            at = ArchitectureTypes.x86_64;
                        else
                            throw new ArgumentException("Invalid architecture " + arch + "! Expected 32 or 64.");

                        Migration.IDAListener.Write("Calculating ...");
                        var diff = IDADiffCalculator.Migration.IDADiff.Calculate(null, at, dir1d, dir2d);
                        Migration.IDAListener.Write("Writing out results ...");
                        {
                            StringBuilder bld = new StringBuilder(0x10000);
                            diff.WriteReport(bld);
                            bld.AppendLine();
                            foreach (var pair in diff.MatchMap)
                            {
                                bld.Append(pair.Value.Source.ToString());
                                bld.Append('\t');
                                bld.Append(pair.Value.Target.ToString());
                                bld.AppendLine();
                            }
                            using (var sw = new System.IO.StreamWriter("output.txt", false))
                            {
                                sw.Write(bld.ToString());
                            }
                            using (var sw = new System.IO.StreamWriter("output_unmatched_prev.txt", false))
                            {
                                foreach (var x in diff.FailedMatchesInPreviousVersion)
                                    sw.WriteLine((x + diff.Script.Versions[0].BaseAddress).ToString());
                            }
                            using (var sw = new System.IO.StreamWriter("output_unmatched_next.txt", false))
                            {
                                foreach (var x in diff.FailedMatchesInNextVersion)
                                    sw.WriteLine((x + diff.Script.Versions[1].BaseAddress).ToString());
                            }
                            using(var sw = new System.IO.StreamWriter("output_hash_prev.txt", false))
                            {
                                foreach (var pair in diff.FunctionHashesInPreviousVersion)
                                {
                                    sw.Write((pair.Key + diff.Script.Versions[0].BaseAddress).ToString());
                                    sw.Write("\t");
                                    sw.Write(pair.Value.ToString());
                                    sw.WriteLine();
                                }
                            }
                            using (var sw = new System.IO.StreamWriter("output_hash_next.txt", false))
                            {
                                foreach (var pair in diff.FunctionHashesInNextVersion)
                                {
                                    sw.Write((pair.Key + diff.Script.Versions[1].BaseAddress).ToString());
                                    sw.Write("\t");
                                    sw.Write(pair.Value.ToString());
                                    sw.WriteLine();
                                }
                            }
                            using(var sw = new System.IO.StreamWriter("output_failed_inref_large.txt", false))
                            {
                                sw.WriteLine("Previous version:");
                                foreach(var pair in diff.FailedMatchesWithManyInRefsInPreviousVersion)
                                {
                                    sw.Write((pair.Key + diff.Script.Versions[0].BaseAddress).ToString());
                                    sw.Write("\t");
                                    sw.Write(pair.Value.ToString());
                                    sw.WriteLine();
                                }
                                sw.WriteLine();

                                sw.WriteLine("Next version:");
                                foreach (var pair in diff.FailedMatchesWithManyInRefsInNextVersion)
                                {
                                    sw.Write((pair.Key + diff.Script.Versions[1].BaseAddress).ToString());
                                    sw.Write("\t");
                                    sw.Write(pair.Value.ToString());
                                    sw.WriteLine();
                                }
                            }
                        }
                        using (var sw = new System.IO.StreamWriter("outputsorted.txt", false))
                        {
                            using (var sw2 = new System.IO.StreamWriter("outputsortedchanges.txt", false))
                            {
                                string[] columnFmt = new string[] { "{0,-4}", "{0,-16}", "{0,-16}", "{0,-10}", "{0,-8}", "{0,-10}", "{0,-10}", "{0,-10}" };
                                {
                                    var header = new StringBuilder();
                                    header.Append(string.Format(columnFmt[0], ""));
                                    header.Append(string.Format(columnFmt[1], "Source"));
                                    header.Append(string.Format(columnFmt[2], "Target"));
                                    header.Append(string.Format(columnFmt[3], "Offset"));
                                    header.Append(string.Format(columnFmt[4], "Type"));
                                    header.Append(string.Format(columnFmt[5], "Score"));
                                    header.Append(string.Format(columnFmt[6], "Difference"));
                                    header.Append(string.Format(columnFmt[7], "Complexity"));
                                    header.Append("Debug");
                                    sw.WriteLine(header.ToString());
                                    sw.WriteLine(new string('=', header.Length));
                                    sw2.WriteLine(header.ToString());
                                    sw2.WriteLine(new string('=', header.Length));
                                }

                                var alr = new List<temp_result_info>();
                                foreach (var x in diff.MatchMap)
                                {
                                    var r = new temp_result_info();
                                    r.Source = x.Value.Source;
                                    r.Target = x.Value.Target;
                                    r.Type = Migration.IDAObjectTypes.Unknown;

                                    var obj = diff.Script.Versions[0].GetObject(x.Value.Source - diff.Script.Versions[0].BaseAddress);
                                    if (obj == null || obj.Type == Migration.IDAObjectTypes.Unknown)
                                        obj = diff.Script.Versions[1].GetObject(x.Value.Target - diff.Script.Versions[1].BaseAddress);
                                    if (obj != null)
                                        r.Type = obj.Type;

                                    r.Score = x.Value.Score;
                                    r.Diff = x.Value.Difference;
                                    r.Total = x.Value.Total;
                                    alr.Add(r);
                                }
                                if (alr.Count > 1)
                                    alr.Sort((u, v) => u.Source.CompareTo(v.Source));
                                Func<Offset, int, int, int> findIndexInAlr = (addr, type, hint) =>
                                {
                                    for (int i = hint; i < alr.Count; i++)
                                    {
                                        var o = type == 0 ? alr[i].Source : alr[i].Target;
                                        if (o.ToInt64() == 0)
                                            continue;

                                        if (addr < o)
                                            return i;
                                    }

                                    return alr.Count;
                                };
                                int lasti = 0;
                                Offset lasto = 0;
                                var lsx = diff.FailedMatchesInPreviousVersion.ToList();
                                lsx.Sort();
                                foreach (var _x in lsx)
                                {
                                    var r = new temp_result_info();
                                    r.Type = Migration.IDAObjectTypes.Unknown;

                                    var x = diff.Script.Versions[0].BaseAddress + _x;
                                    var obj = diff.Script.Versions[0].GetObject(_x);
                                    if (obj != null)
                                        r.Type = obj.Type;
                                    int complex = -1;
                                    if (obj != null && obj.Comparison != null)
                                        complex = obj.Comparison.GetEntrySize(Migration.IDAObjectComparisonData.IDAObjectComparisonTypes.Asm) + obj.Comparison.GetEntrySize(Migration.IDAObjectComparisonData.IDAObjectComparisonTypes.InRef) + obj.Comparison.GetEntrySize(Migration.IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef) + obj.Comparison.GetEntrySize(Migration.IDAObjectComparisonData.IDAObjectComparisonTypes.CustomString);
                                    r.Total = complex;
                                    r.Source = x;
                                    r.Diff = -1;
                                    r.Score = -1;

                                    int hint = 0;
                                    if (x > lasto)
                                        hint = lasti;
                                    int wh = findIndexInAlr(x, 0, hint);
                                    lasti = wh;
                                    lasto = x;
                                    alr.Insert(wh, r);
                                }
                                lasti = 0;
                                lasto = 0;
                                lsx = diff.FailedMatchesInNextVersion.ToList();
                                lsx.Sort();
                                foreach (var _x in lsx)
                                {
                                    var r = new temp_result_info();
                                    r.Type = Migration.IDAObjectTypes.Unknown;

                                    var x = diff.Script.Versions[1].BaseAddress + _x;
                                    var obj = diff.Script.Versions[1].GetObject(_x);
                                    if (obj != null)
                                        r.Type = obj.Type;
                                    int complex = -1;
                                    if (obj != null && obj.Comparison != null)
                                        complex = obj.Comparison.GetEntrySize(Migration.IDAObjectComparisonData.IDAObjectComparisonTypes.Asm) + obj.Comparison.GetEntrySize(Migration.IDAObjectComparisonData.IDAObjectComparisonTypes.InRef) + obj.Comparison.GetEntrySize(Migration.IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef) + obj.Comparison.GetEntrySize(Migration.IDAObjectComparisonData.IDAObjectComparisonTypes.CustomString);
                                    r.Total = complex;
                                    r.Target = x;
                                    r.Diff = -1;
                                    r.Score = -1;

                                    int hint = 0;
                                    if (x > lasto)
                                        hint = lasti;
                                    int wh = findIndexInAlr(x, 1, hint);
                                    lasti = wh;
                                    lasto = x;
                                    alr.Insert(wh, r);
                                }

                                temp_result_info.TryCompact(diff.Script, alr);

                                foreach (var t in alr)
                                {
                                    string sym = "";
                                    if (t.Compacted)
                                        sym = "*";
                                    else if (t.Score < 0.0)
                                        sym = "!";

                                    bool shouldWriteDiff = sym.Length != 0 || t.Score < 1.0;

                                    sw.Write(string.Format(columnFmt[0], sym));
                                    if(shouldWriteDiff)
                                        sw2.Write(string.Format(columnFmt[0], sym));
                                    long fr = t.Source.ToInt64();
                                    sw.Write(string.Format(columnFmt[1], fr > 0 ? fr.ToString("X") : ""));
                                    if (shouldWriteDiff)
                                        sw2.Write(string.Format(columnFmt[1], fr > 0 ? fr.ToString("X") : ""));
                                    long to = t.Target.ToInt64();
                                    sw.Write(string.Format(columnFmt[2], to > 0 ? to.ToString("X") : ""));
                                    if (shouldWriteDiff)
                                        sw2.Write(string.Format(columnFmt[2], to > 0 ? to.ToString("X") : ""));
                                    string dft = "";
                                    if (to > 0 && fr > 0)
                                    {
                                        long df = to - fr;
                                        if (df >= 0)
                                            dft = df.ToString("X");
                                        else
                                            dft = "-" + Math.Abs(df).ToString("X");
                                    }
                                    sw.Write(string.Format(columnFmt[3], dft));
                                    if(shouldWriteDiff)
                                        sw2.Write(string.Format(columnFmt[3], dft));
                                    string typestr = "";
                                    switch (t.Type)
                                    {
                                        case Migration.IDAObjectTypes.Function: typestr = "func"; break;
                                        case Migration.IDAObjectTypes.Global: typestr = "var"; break;
                                        case Migration.IDAObjectTypes.Loc: typestr = "loc"; break;
                                        case Migration.IDAObjectTypes.Struct: typestr = "struct"; break;
                                        case Migration.IDAObjectTypes.VTable: typestr = "vtable"; break;
                                        case Migration.IDAObjectTypes.Unknown: typestr = ""; break;
                                        default:
                                            throw new ArgumentException();
                                    }
                                    sw.Write(string.Format(columnFmt[4], typestr));
                                    sw.Write(string.Format(columnFmt[5], t.Score >= 0.0 ? t.Score.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : ""));
                                    sw.Write(string.Format(columnFmt[6], t.Diff >= 0 ? t.Diff.ToString() : ""));
                                    sw.Write(string.Format(columnFmt[7], t.Total >= 0 ? t.Total.ToString() : ""));
                                    if (!string.IsNullOrEmpty(t.Debug))
                                        sw.Write(t.Debug);
                                    sw.WriteLine();
                                    if(shouldWriteDiff)
                                    {
                                        sw2.Write(string.Format(columnFmt[4], typestr));
                                        sw2.Write(string.Format(columnFmt[5], t.Score >= 0.0 ? t.Score.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : ""));
                                        sw2.Write(string.Format(columnFmt[6], t.Diff >= 0 ? t.Diff.ToString() : ""));
                                        sw2.Write(string.Format(columnFmt[7], t.Total >= 0 ? t.Total.ToString() : ""));
                                        if (!string.IsNullOrEmpty(t.Debug))
                                            sw2.Write(t.Debug);
                                        sw2.WriteLine();
                                    }
                                }
                            }
                        }
                        Migration.IDAListener.Write("Done! Check output.txt or outputsorted.txt file for results.");
#if !DEBUG
                    }
                    catch (Exception ex)
                    {
                        Migration.IDAListener.Write("Exception (" + ex.GetType().Name + ") " + ex.Message);
                        Migration.IDAListener.Write(ex.StackTrace);
                    }
#endif
                }
            }
            finally
            {
                Migration.IDAListener.closeFile();
            }

            //Console.WriteLine("Press any key to exit ..."); Console.ReadKey();
        }
    }

    class temp_result_info
    {
        internal Migration.IDAObjectTypes Type;
        internal Offset Source;
        internal Offset Target;
        internal double Score;
        internal int Diff;
        internal int Total;
        internal string Debug;
        internal bool Compacted;

        internal static void TryCompact(Migration.IDAMigrate m, List<temp_result_info> all)
        {
            int index = 0;
            while(true)
            {
                if (index >= all.Count)
                    break;

                var cur = all[index];
                if(cur.Score >= 0.0)
                {
                    index++;
                    continue;
                }

                int startIndex = index;
                int endIndex = index + 1;
                for(; endIndex < all.Count; endIndex++)
                {
                    var x = all[endIndex];
                    if (x.Score >= 0.0)
                        break;
                }

                int count = endIndex - startIndex;
                if(count < 2 || (count % 2) != 0)
                {
                    index = endIndex + 1;
                    continue;
                }

                int half = count / 2;
                bool bad = false;
                for(int i = 0; i < half; i++)
                {
                    var first = all[startIndex + i];
                    var second = all[startIndex + i + half];

                    if(first.Source.ToInt64() == 0 || first.Target.ToInt64() != 0)
                    {
                        bad = true;
                        break;
                    }

                    if(second.Source.ToInt64() != 0 || second.Target.ToInt64() == 0)
                    {
                        bad = true;
                        break;
                    }
                }

                if(bad)
                {
                    index = endIndex + 1;
                    continue;
                }

                var add = new List<temp_result_info>();
                for(int i = 0; i < half; i++)
                {
                    var t = new temp_result_info();
                    t.Compacted = true;
                    t.Source = all[startIndex + i].Source;
                    t.Target = all[startIndex + i + half].Target;

                    var aobj = m.Versions[0].GetObject(t.Source - m.Versions[0].BaseAddress);
                    var bobj = m.Versions[1].GetObject(t.Target - m.Versions[1].BaseAddress);

                    if(aobj == null || bobj == null)
                    {
                        add = null;
                        break;
                    }

                    double sc = 1;
                    int wr = 0;
                    int tt = 0;
                    t.Debug = aobj.Comparison.GetComparisonResultScore(bobj.Comparison, ref sc, ref wr, ref tt);
                    t.Score = sc;
                    t.Diff = wr;
                    t.Total = tt;
                    add.Add(t);
                }

                if (add != null && add.Count != 0)
                    all.InsertRange(endIndex, add);

                index = endIndex + (add != null ? add.Count : 0) + 1;
            }
        }
    }
}
