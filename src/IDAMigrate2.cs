//#define DEBUG_ASM_ERRORS

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace IDADiffCalculator.Migration
{
    internal static class IDAListener
    {
        internal static int Success;
        internal static int Total;
        internal static int Success2;
        internal static int Total2;
        internal static bool Stop;
        private static int Stopped;

        internal static void initFile()
        {
            lock(locker)
            {
                if (file != null)
                    file.Dispose();
                file = new System.IO.StreamWriter("log.txt", false);
            }
        }

        internal static void closeFile()
        {
            lock(locker)
            {
                if (file != null)
                {
                    file.Dispose();
                    file = null;
                }
            }
        }

        private static System.IO.StreamWriter file;

        private static readonly object locker = new object();

        internal static void Write(string message)
        {
            var now = DateTime.Now;
            string time = "[" + string.Format("{0:00}:{1:00}:{2:00}", now.Hour, now.Minute, now.Second) + "] ";
            lock(locker)
            {
                Console.WriteLine(time + message);
                if (file != null)
                {
                    file.WriteLine(time + message);
                    file.Flush();
                }
            }
        }

        internal static void Wait()
        {
            while(System.Threading.Interlocked.CompareExchange(ref Stopped, 0, 0) == 0)
            {
                System.Threading.Thread.Sleep(100);
            }

            System.Threading.Thread.Sleep(100);
        }

        internal static void start()
        {
            var t = new System.Threading.Thread(run);
            t.Start();
        }

        private static void run()
        {
            string last = null;
            while(!Stop)
            {
                System.Threading.Thread.Sleep(5000);
                int has = System.Threading.Interlocked.CompareExchange(ref Success, 0, 0);
                int has2 = System.Threading.Interlocked.CompareExchange(ref Success2, 0, 0);
                string cur = string.Format("{0} / {1} - {2}    {3} / {4} - {5}", has, Total, ((double)has * 100.0 / (double)Total).ToString("0.00") + " %", has2, Total2, ((double)has2 * 100.0 / (double)Total2).ToString("0.00") + " %");
                if (last != cur)
                {
                    Write(cur);
                    last = cur;
                }
            }
            
            System.Threading.Interlocked.Exchange(ref Stopped, 1);
        }
    }

    internal sealed class IDAMigrate
    {
        internal static bool RefDirectionMatters = false;
        internal static bool UseCache1 = true; // dynamic cache
        internal static bool UseCache2 = false; // static cache
        internal static bool SeparateRefPass = true;
        internal static bool IgnoreSomeCompilerGeneratedRdata = true;
        internal static bool TryRemoveEmptyAlignLocs = true;
        internal static bool RecalculateAllPassAtEndAnyway = false;
        internal static bool CompactedString = false;
        // Loc-run -> function recovery (TryToConvertLocToFunc). O(L^2) per loc
        // run; dominates runtime on large binaries. Pointless for byte-identical
        // cross-distribution builds (Steam/GOG/Epic) where the loc/func split is
        // already identical, so default OFF.
        internal static bool ConvertLocToFunc = false;

        internal static long DEBUG_OFFSET1 = 0; // 0x34CB000;
        internal static long DEBUG_OFFSET2 = 0; // 0x116A4E0;

        internal IDAMigrate(ArchitectureTypes architecture)
        {
            this.Architecture = architecture;

            this.IsSameSegment = new IDAHelper_IsSameSegment();
            this.IsSameSegment.Migration = this;

            for (int i = 0; i < this.PassListeners.Length; i++)
                this.PassListeners[i] = new List<IDAPass>();
            
            this.ComparisonParametersSetup = new List<IDAObjectComparisonParameters>();
            int maxBigWrong = 10000;

            if (DEBUG_OFFSET1 != 0 && DEBUG_OFFSET2 != 0)
            {
                var s = new IDAObjectComparisonParameters();
                this.ComparisonParametersSetup.Add(s);

                var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                p.MinRatio = 0.9;
                p.MaxWrong = maxBigWrong;

                p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                p.MinRatio = 0.9;
                p.MaxWrong = maxBigWrong;

                p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                p.MinRatio = 0.9;
                p.MaxWrong = maxBigWrong;

                s.MaxRangeAssignCount = 5000000;
                s.MaxRangeAssignPctSize = 100.0;
                {
                    s.MaxIndexDifferenceForAmbiguous = 100.0;
                    s.MaxIndexDifferenceForCompare = 100.0;
                }
            }
            else
            {
                // 0
                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);
                    s.CalculateIsCachedExact();
                }

                // 1
                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 0.9;
                    p.MaxWrong = maxBigWrong;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 0.9;
                    p.MaxWrong = maxBigWrong;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 0.9;
                    p.MaxWrong = maxBigWrong;
                }

                // 2
                for (int i = 0; i < 3; i++)
                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 0.8;
                    p.MaxWrong = maxBigWrong;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 0.8;
                    p.MaxWrong = maxBigWrong;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 0.7;
                    p.MaxWrong = maxBigWrong;
                }

                // Allow custom string to change
                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.CustomString];
                    p.MinRatio = 0.5;
                    p.MaxWrong = 5;
                }

                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 0.5;
                    p.MaxWrong = 1;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 0.8;
                    p.MaxWrong = maxBigWrong;
                    p.MinSameRefGuid = 1;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 0.7;
                    p.MaxWrong = maxBigWrong;
                }

                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 0.8;
                    p.MaxWrong = maxBigWrong;
                    p.MinSameRefGuid = 1;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 0.5;
                    p.MaxWrong = 1;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 0.7;
                    p.MaxWrong = maxBigWrong;
                }

                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 0.8;
                    p.MaxWrong = maxBigWrong;
                    p.MinSameRefGuid = -1;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 0.8;
                    p.MaxWrong = maxBigWrong;
                    p.MinSameRefGuid = -1;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 0.5;
                    p.MaxWrong = maxBigWrong;
                }

                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 0.8;
                    p.MaxWrong = maxBigWrong;
                    p.MinSameRefGuid = -1;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 0.8;
                    p.MaxWrong = maxBigWrong;
                    p.MinSameRefGuid = -1;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 0.95;
                    p.MaxWrong = maxBigWrong;
                    p.RatioIsComplexityDifferenceInstead = true;
                }

                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    s.AllowRefInPass = false;
                    s.AllowRefOutPass = false;

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 0.1;
                    p.MaxWrong = maxBigWrong;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 1.0;
                    p.MaxWrong = maxBigWrong;
                    p.MinSameRefGuid = 1;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 0.95;
                    p.MaxWrong = maxBigWrong;
                    p.RatioIsComplexityDifferenceInstead = true;
                }

                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    s.AllowRefInPass = false;
                    s.AllowRefOutPass = false;

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 0.1;
                    p.MaxWrong = maxBigWrong;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 1.0;
                    p.MaxWrong = maxBigWrong;
                    p.MinSameRefGuid = 1;
                    p.MinSameRefGuidRatio = 1.0;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 0.2;
                    p.MaxWrong = maxBigWrong;
                    p.RatioIsComplexityDifferenceInstead = true;
                }

                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    s.AllowRefInPass = false;
                    s.AllowRefOutPass = false;

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 0.01;
                    p.MaxWrong = maxBigWrong;
                    p.MinSameRefGuid = 1;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 1.0;
                    p.MaxWrong = maxBigWrong;
                    p.MinSameRefGuid = 1;
                    p.MinSameRefGuidRatio = 1.0;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 0.05;
                    p.MaxWrong = maxBigWrong;
                    p.RatioIsComplexityDifferenceInstead = true;
                }

                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 1.0;
                    p.MaxWrong = 0;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 0.0;
                    p.MaxWrong = 1;
                    p.MinComplex = 1;
                    p.MaxComplex = 1;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 1.0;
                    p.MaxWrong = 0;
                }

                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    s.AllowRefInPass = false;
                    s.AllowRefOutPass = false;

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 1.0;
                    p.MaxWrong = 0;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 1.0;
                    p.MaxWrong = 0;
                    p.MaxComplex = 1;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 1.0;
                    p.MaxWrong = 0;
                }

                // New pass to ignore order of in-refs (could cause issues?)
                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    s.AllowRefInPass = false;
                    s.AllowRefOutPass = false;

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 1.0;
                    p.MaxWrong = 0;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 0.98;
                    p.MaxWrong = maxBigWrong;
                    p.RatioIsComplexityDifferenceInstead = true;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 1.0;
                    p.MaxWrong = 0;
                }

                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    s.AllowRefInPass = false;
                    s.AllowRefOutPass = false;

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 1.0;
                    p.MaxWrong = 0;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 0.5;
                    p.MaxWrong = maxBigWrong;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 1.0;
                    p.MaxWrong = 0;
                }

                // final, another normal pass because we disabled refin refout before
                for (int i = 0; i < 3; i++)
                {
                    var s = new IDAObjectComparisonParameters();
                    this.ComparisonParametersSetup.Add(s);

                    var p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef];
                    p.MinRatio = 0.9;
                    p.MaxWrong = maxBigWrong;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef];
                    p.MinRatio = 0.9;
                    p.MaxWrong = maxBigWrong;

                    p = s.Entries[(int)IDAObjectComparisonData.IDAObjectComparisonTypes.Asm];
                    p.MinRatio = 0.9;
                    p.MaxWrong = maxBigWrong;

                    s.MaxRangeAssignCount = 5000000;
                    s.MaxRangeAssignPctSize = 100.0;
                    //if (i == 0)
                    {
                        s.MaxIndexDifferenceForAmbiguous = 100.0;
                        s.MaxIndexDifferenceForCompare = 100.0;
                        //IDAListener.Write("Slow pass will be " + (this.ComparisonParametersSetup.Count - 1).ToString() + ".");
                    }
                }
            }

            // Preparation passes.
            this.CreatePass(new Passes.IDAPass_RecalculateAdjustedSegmentIndices());

            // Debug
            if (DEBUG_OFFSET1 != 0 && DEBUG_OFFSET2 != 0)
            {

            }
            else
            {
                // First pass of named stuff.
                //this.CreatePass(new Passes.IDAPass_NamedExact());

                // Assign some big things so we don't waste time comparing these later.
                //this.CreatePass(new Passes.IDAPass_BigThings());

                // Normal passes.
                this.CreatePass(new Passes.IDAPass_RefOut());
                this.CreatePass(new Passes.IDAPass_RefIn());
                this.CreatePass(new Passes.IDAPass_CustomStringAssociation());
                this.CreatePass(new Passes.IDAPass_SlowScanExact());
                this.CreatePass(new Passes.IDAPass_AssignRange());
                this.CreatePass(new Passes.IDAPass_AssignRangeSegmentBorder());
                this.CreatePass(new Passes.IDAPass_AggressiveVTableMatch());
            }

            // Last pass.
            this.CreatePass(new Passes.IDAPass_IncrementParameterIndex());
            this.CreatePass(new Passes.IDAPass_RecalculateReferenceData());
            this.CreatePass(new Passes.IDAPass_IncrementParameterIndexFinal());

            foreach (var p in this.Passes)
                p.AssignedMatchesCountByParameterIndex = new int[this.ComparisonParametersSetup.Count];
        }

        internal readonly ArchitectureTypes Architecture;

        internal readonly IDAVersion[] Versions = new IDAVersion[2];

        internal readonly IDAHelper_IsSameSegment IsSameSegment;

        internal readonly List<IDAPass> Passes = new List<IDAPass>();

        internal readonly Utility.DamerauLevensteinMetric DiffCache = new Utility.DamerauLevensteinMetric();

        private readonly IDAPass[] PassLookup = new IDAPass[256];

        internal readonly List<IDAPass>[] PassListeners = new List<IDAPass>[Enum.GetValues(typeof(IDAPass.IDAPassListenerTypes)).Cast<int>().Max() + 1];

        internal readonly List<IDAObjectComparisonParameters> ComparisonParametersSetup;

        internal int ComparisonParametersIndex;

        internal IDAObjectComparisonParameters CurrentComparisonParameters
        {
            get
            {
                int ix = Math.Min(this.ComparisonParametersIndex, this.ComparisonParametersSetup.Count - 1);
                return this.ComparisonParametersSetup[ix];
            }
        }

        internal IDAPass GetPass(byte id)
        {
            return this.PassLookup[id];
        }

        private void CreatePass(IDAPass pass)
        {
            pass.Migration = this;
            pass.Index = this.Passes.Count;
            //pass.AssignedMatchesCountByParameterIndex = new int[32];
            this.Passes.Add(pass);

            byte id = pass.Id;

            if (this.PassLookup[id] != null)
                throw new ArgumentException("id");
            this.PassLookup[id] = pass;
        }

        internal IDAPass CurrentPass
        {
            get;
            private set;
        }

        private int LoopCounter = 0;

#if DEBUG
        private void _do_debug()
        {
            Func<IDAObject, IDAObject> getPrevAssigned = obj =>
            {
                while (obj.Match == null && obj.IndexInSegment > 0)
                    obj = obj.Segment.Objects[obj.IndexInSegment - 1];
                return obj;
            };
            Func<IDAObject, List<IDAObject>> getUnassignedRange = obj =>
            {
                List<IDAObject> ls = new List<IDAObject>();
                int i = obj.Match != null ? (obj.IndexInSegment + 1) : obj.IndexInSegment;
                int c = obj.Segment.Objects.Count;
                while (i < c)
                {
                    var o = obj.Segment.Objects[i++];
                    if (o.Match != null)
                        break;
                    ls.Add(o);
                }
                return ls;
            };

            //var objA = this.Versions[0].GetObject(0x);
            //var objB = this.Versions[1].GetObject(0x);
            var objA = this.Versions[0].GetObject(0x113E200);
            var objB = this.Versions[1].GetObject(0x113E340);
            //var objC = this.Versions[0].GetObject(0xC1CD00);
            //var objD = this.Versions[1].GetObject(0xC1CBB0);
            //var objA = this.Versions[0].GetObject(0x18391B0);
            //var objB = this.Versions[1].GetObject(0x18207A0);

            var rangeA = objA != null ? getUnassignedRange(getPrevAssigned(objA)) : null;
            var rangeB = objB != null ? getUnassignedRange(getPrevAssigned(objB)) : null;

            bool resultCurParam = objA != null && objB != null && objA.Comparison.CompareWithCurrentParameters(objB.Comparison, false);
            bool resultExact = objA != null && objB != null && objA.Comparison.CompareExact(objB.Comparison);

            int z = 5;
        }
#endif

        internal IDADiff Do(Progress prog, System.IO.DirectoryInfo oldDir, System.IO.DirectoryInfo newDir)
        {
            this.LoadVersions(prog, new[] { oldDir, newDir });

            IDAListener.Total = this.Versions[0].Objects.Count;
            IDAListener.Total2 = this.Versions[1].Objects.Count;
            IDAListener.start();
            IDAListener.Write("Beginning diff calculation ...");

            this.Do();

            IDAListener.Stop = true;
            IDAListener.Wait();

#if DEBUG
            //_do_debug();
#endif

            var diff = new IDADiff();
            diff.FailedMatchesInPreviousVersion = this.Versions[0].UnassignedObjects.List.Select(q => q.Value.Begin).ToList();
            diff.FailedMatchesInNextVersion = this.Versions[1].UnassignedObjects.List.Select(q => q.Value.Begin).ToList();
            diff.FunctionHashesInPreviousVersion = this.Versions[0].BuildFunctionHashes();
            diff.FunctionHashesInNextVersion = this.Versions[1].BuildFunctionHashes();
            diff.FailedMatchesWithManyInRefsInPreviousVersion = this.Versions[0].GetFailedMatchesWithManyInRefs();
            diff.FailedMatchesWithManyInRefsInNextVersion = this.Versions[1].GetFailedMatchesWithManyInRefs();
            diff.Passes = this.LoopCounter;
            diff.Script = this;
            Dictionary<Offset, IDADiff.IDADiffResult> matchMap = new Dictionary<Offset, IDADiff.IDADiffResult>();
            List<IDADiff.IDADiffResult> matchLs = new List<IDADiff.IDADiffResult>();
            diff.MatchMap = matchMap;
            diff.MatchList = matchLs;
            foreach (var node in this.Versions[0].AssignedObjects.List)
            {
                var m = node.Value.Match;
                var r = new IDADiff.IDADiffResult()
                {
                    Source = m.First.Begin + m.First.Version.BaseAddress,
                    Target = m.Second.Begin + m.Second.Version.BaseAddress,
                };
                double sc = 1.0;
                int wr = 0;
                int tt = 0;
                r.Debug = m.First.Comparison.GetComparisonResultScore(m.Second.Comparison, ref sc, ref wr, ref tt);
                r.Score = sc;
                r.Difference = wr;
                r.Total = tt;

                matchLs.Add(r);
                matchMap[r.Source] = r;
            }
            return diff;
        }

        private void Do()
        {
            for (int i = 0; i < this.Passes.Count;)
            {
                var p = this.Passes[i];
                if (!p.Initialize())
                    this.Passes.RemoveAt(i);
                else
                    i++;
            }

            var lsondid = this.PassListeners[(int)IDAPass.IDAPassListenerTypes.OnDid];

            int nextPass = 0;
            while (nextPass < this.Passes.Count)
            {
                this.LoopCounter++;

                var pass = this.Passes[nextPass];
                if (this.ComparisonParametersIndex < pass.MinComparisonParameterIndex)
                {
                    nextPass++;
                    this.LoopCounter--;
                    continue;
                }

                this.CurrentPass = pass;
                //if(!(pass is Migration.Passes.IDAPass_IncrementParameterIndex))
                //IDAListener.Write("Running pass " + pass.GetType().Name + " ...");
                bool did = pass.Do();
                this.CurrentPass = null;

                if (did)
                    nextPass = 0;
                else
                    nextPass++;

                pass.OnAfterDo(did);

                if (did)
                {
                    foreach (var p in lsondid)
                        p.OnDid(pass);
                }

                if (this._queuedAssignMatch.Count != 0)
                {
                    for (int i = 0; i < this._queuedAssignMatch.Count; i++)
                    {
                        var t = this._queuedAssignMatch[i];
                        this.CurrentPass = t.Item3;
                        this.AssignMatch(t.Item1, t.Item2);
                        this.CurrentPass = null;
                    }
                    this._queuedAssignMatch.Clear();
                }
            }
        }

        private int _assignMatchDepth = 0;
        private readonly List<Tuple<IDAObject, IDAObject, IDAPass>> _queuedAssignMatch = new List<Tuple<IDAObject, IDAObject, IDAPass>>();

        internal int[] DebugAssignedCount = new int[2];
        internal bool IsAfterRefRecalc = false;

        internal void AssignMatch(IDAObject a, IDAObject b)
        {
#if DEBUG
            if (a.Match != null || b.Match != null || a.Version == b.Version) throw new InvalidOperationException();
#endif
            if (++this._assignMatchDepth >= 2)
                throw new InvalidOperationException("Trying to assign a match while assigning match! This will cause issues.");

            if (a.Version.Index != 0)
                Utility.Swap(ref a, ref b);

            a.Version.UnassignedObjects.Remove(a);
            a.Version.AssignedObjects.Add(a);

            b.Version.UnassignedObjects.Remove(b);
            b.Version.AssignedObjects.Add(b);

            System.Threading.Interlocked.Increment(ref IDAListener.Success);
            System.Threading.Interlocked.Increment(ref IDAListener.Success2);

            var m = new IDAObjMatch();
            m.First = a;
            m.Second = b;

            a.Match = m;
            b.Match = m;

            a.OnAssignedMatch(true);
            b.OnAssignedMatch(false);

            this.DebugAssignedCount[this.IsAfterRefRecalc ? 1 : 0]++;

            var pass = this.CurrentPass;
#if DEBUG
            if (pass == null) throw new InvalidOperationException();
#endif
            pass.AssignedMatchesCount++;
            pass.AssignedMatchesCountByParameterIndex[this.ComparisonParametersIndex]++;
            pass.OnMatchMadeHere(m);
            var ls = this.PassListeners[(int)IDAPass.IDAPassListenerTypes.OnMatchMade];
            foreach (var p in ls)
                p.OnMatchMade(m, pass);

            this._assignMatchDepth--;
        }

        /// <summary>
        /// Queues the assign match.
        /// </summary>
        /// <param name="a">The first object.</param>
        /// <param name="b">The second object.</param>
        /// <param name="fromPass">From which pass is this queued from.</param>
        internal void QueueAssignMatch(IDAObject a, IDAObject b, IDAPass fromPass)
        {
#if DEBUG
            if (fromPass == null) throw new ArgumentNullException("fromPass");
#endif
            _queuedAssignMatch.Add(new Tuple<IDAObject, IDAObject, IDAPass>(a, b, fromPass));
        }

        /// <summary>
        /// Tries to convert loc to function.
        /// </summary>
        private void TryToConvertLocToFunc()
        {
            Dictionary<ulong, List<IReadOnlyList<ulong>>> isFuncMap = new Dictionary<ulong, List<IReadOnlyList<ulong>>>();
            IReadOnlyList<IDAObject>[] objects = new IReadOnlyList<IDAObject>[this.Versions.Length];
            for (int i = 0; i < objects.Length; i++)
            {
                this.Versions[i]._sortObjects();
                objects[i] = this.Versions[i].Objects;
            }

            // Skip the O(L^2) loc-run -> function recovery (the hot spot in
            // TryToConvertLocStringToFunc). The per-version sort above is kept
            // because later phases rely on it; only the recovery is skipped.
            if (!ConvertLocToFunc)
                return;

            for (int i = 0; i < objects.Length; i++)
            {
                foreach (var o in objects[i])
                {
                    if (o.Type != IDAObjectTypes.Function || o.AsmBuilder == null)
                        continue;

                    var asm = o.AsmBuilder.Build(o.Version);
                    if (asm.Data.Length == 0)
                        continue;

                    List<IReadOnlyList<ulong>> ls = null;
                    if (!isFuncMap.TryGetValue(asm.Hash, out ls))
                    {
                        ls = new List<IReadOnlyList<ulong>>(4);
                        isFuncMap[asm.Hash] = ls;
                    }

                    if (!_is_mby_inlist(ls, asm.Data))
                        ls.Add(asm.Data);
                }
            }

            for (int i = 0; i < objects.Length; i++)
            {
                var ls = objects[i];
                int index = 0;
                List<IDAObject> res = new List<IDAObject>();

                while (index < ls.Count)
                {
                    _fill_loc_string(ls, index++, res);

                    if (res.Count == 0)
                        continue;

                    this.TryToConvertLocStringToFunc(res, isFuncMap);
                    res.Clear();
                }
            }
        }

        /// <summary>
        /// Tries to convert loc string to function.
        /// </summary>
        /// <param name="res">The resource.</param>
        /// <param name="isFuncMap">The is function map.</param>
        private void TryToConvertLocStringToFunc(List<IDAObject> res, Dictionary<ulong, List<IReadOnlyList<ulong>>> isFuncMap)
        {
            // If it starts with something bad then it definitely can not be a function. For example "align" or "db (data)"
            if (res[0].AsmBuilder == null || (res[0].AsmBuilder.Lines[0].Flags & (IDAExportAsmBuilder.AssemblyLineFlags.Skip | IDAExportAsmBuilder.AssemblyLineFlags.SkipButIsOkInstructionForExecution)) == IDAExportAsmBuilder.AssemblyLineFlags.Skip)
                return;

            while (res.Count != 0)
            {
                if (res[res.Count - 1].AsmBuilder == null)
                {
                    res.RemoveAt(res.Count - 1);
                    continue;
                }

                var append = new List<ulong>();
                bool cant = false;
                bool stopped = false;
                foreach (var o in res)
                {
                    if (o.AsmBuilder == null)
                        continue;

                    var asm = o.AsmBuilder.Build(o.Version);
                    if (asm.Data.Length == 0)
                        continue;

                    if (stopped)
                    {
                        cant = true;
                        break;
                    }

                    append.AddRange(asm.Data);
                    if (asm.StopAt.HasValue)
                        stopped = true;
                }

                if (append.Count == 0)
                    return;

                if (cant)
                {
                    res.RemoveAt(res.Count - 1);
                    continue;
                }

                ulong hash = Utility.Calculate64BitHashCode(append);
                List<IReadOnlyList<ulong>> lscompare = null;

                bool existFunc = false;
                if (isFuncMap.TryGetValue(hash, out lscompare))
                {
                    foreach (var x in lscompare)
                    {
                        if (x.Count != append.Count)
                            continue;

                        bool yes = true;
                        for (int i = 0; i < x.Count; i++)
                        {
                            if (x[i] != append[i])
                            {
                                yes = false;
                                break;
                            }
                        }

                        if (yes)
                        {
                            existFunc = true;
                            break;
                        }
                    }
                }

                if (!existFunc)
                {
                    res.RemoveAt(res.Count - 1);
                    continue;
                }

                this.ConvertLocStringToFunc(res);
                return;
            }
        }

        /// <summary>
        /// Converts the loc string to function.
        /// </summary>
        /// <param name="res">The resource.</param>
        private void ConvertLocStringToFunc(List<IDAObject> res)
        {
            var primary = res[0];

            primary.Version.ConvertedFromLocsCount += res.Count;
            primary.Version.ConvertedToFuncsCount++;
            primary.Version.ConvertedFromLocs.Add(primary.Begin);

            if (res.Count == 1)
            {
                primary.Type = IDAObjectTypes.Function;
                primary.Flags |= IDAObjectFlags.CreatedFunctionFromLoc;
                return;
            }

            IDAExportAsmBuilder builder = new IDAExportAsmBuilder();
            foreach (var o in res)
            {
                if (o.AsmBuilder != null)
                    builder.Append(o.AsmBuilder);
            }

            primary.AsmBuilder = builder;
            primary.Asm = new IDAObjectAsm();
            primary.Type = IDAObjectTypes.Function;
            primary.Flags |= IDAObjectFlags.CreatedFunctionFromLoc;
            primary.End = res[res.Count - 1].End;

            for (int i = 1; i < res.Count; i++)
            {
                var o = res[i];
                o.Version._deleteObject(o);
            }
        }

        private static void _fill_loc_string(IReadOnlyList<IDAObject> ls, int index, List<IDAObject> res)
        {
            int c = ls.Count;
            while (index < c)
            {
                var o = ls[index++];
                if (o.Type == IDAObjectTypes.Loc)
                {
                    res.Add(o);
                    continue;
                }

                break;
            }
        }

        private static bool _is_mby_inlist(List<IReadOnlyList<ulong>> ls, IReadOnlyList<ulong> e)
        {
            ulong f = e[0];
            int c = e.Count;
            foreach (var x in ls)
            {
                if (x.Count != c || x[0] != f)
                    continue;

                bool same = true;
                for (int i = 1; i < c; i++)
                {
                    if (x[i] != e[i])
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                    return true;
            }

            return false;
        }

        internal uint __high_guid = 0;

        /// <summary>
        /// Sets better segment code if equal segments. If we are sure both versions have completely identical segments then we can set segment code to be index which is
        /// much more accurate in some cases where multiple segments have same name and flags.
        /// </summary>
        private bool SetBetterSegmentCodeIfEqualSegments()
        {
            if (this.Versions.Length != 2)
                throw new NotSupportedException();

            var segs = new[] { this.Versions[0].Segments, this.Versions[1].Segments };
            if (segs[0].Count != segs[1].Count)
                return false;

            for (int i = 0; i < segs[0].Count; i++)
            {
                var a = segs[0][i];
                var b = segs[1][i];

                if (a.Name != b.Name || a.Flags != b.Flags)
                    return false;
            }

            // All segments are same.
            for (int i = 0; i < segs[0].Count; i++)
            {
                var a = segs[0][i];
                var b = segs[1][i];

                a.Code = (byte)i;
                b.Code = (byte)i;
            }

            return true;
        }

        #region Loading

        internal static List<string> _Load_FastSplit(string value, char delim = '\t')
        {
            var ls = _load_fs_cache;
            ls.Clear();

            int li = 0;
            int i = 0;
            for (; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch != delim)
                    continue;

                if (i != li)
                    ls.Add(value.Substring(li, i - li));
                else
                    ls.Add("");

                li = i + 1;
            }

            if (i != 0)
            {
                if (i != li)
                    ls.Add(value.Substring(li, i - li));
                else
                    ls.Add("");
            }

            return ls;
        }
        internal static List<string> _Load_FastSplit2(string value, char delim = '\t')
        {
            var ls = _load_fs_cache2;
            ls.Clear();

            int li = 0;
            int i = 0;
            for (; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch != delim)
                    continue;

                if (i != li)
                    ls.Add(value.Substring(li, i - li));
                else
                    ls.Add("");

                li = i + 1;
            }

            if (i != 0)
            {
                if (i != li)
                    ls.Add(value.Substring(li, i - li));
                else
                    ls.Add("");
            }

            return ls;
        }

        internal static List<string> _Load_FastSplit3(string value, char delim = '\t')
        {
            var ls = _load_fs_cache3;
            ls.Clear();

            int li = 0;
            int i = 0;
            for (; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch != delim)
                    continue;

                if (i != li)
                    ls.Add(value.Substring(li, i - li));
                else
                    ls.Add("");

                li = i + 1;
            }

            if (i != 0)
            {
                if (i != li)
                    ls.Add(value.Substring(li, i - li));
                else
                    ls.Add("");
            }

            return ls;
        }
        private static readonly List<string> _load_fs_cache = new List<string>(32);
        private static readonly List<string> _load_fs_cache2 = new List<string>(32);
        private static readonly List<string> _load_fs_cache3 = new List<string>(32);

        internal static void _Fill_FastSplit(string value, string[] result, char delim = '\t')
        {
            int i = 0;
            int li = 0;
            int ri = 0;
            int len = value.Length;
            while (i < len)
            {
                char ch = value[i];
                if (ch != delim)
                {
                    i++;
                    continue;
                }

                result[ri] = i != li ? value.Substring(li, i - li) : "";
                ri++;
                i++;
                li = i;
            }

            result[ri] = li != len ? value.Substring(li, len - li) : "";
#if DEBUG
            if (++ri != result.Length) throw new InvalidOperationException();
#endif
        }

        internal static IReadOnlyList<long> _Load_FastSplitInt64Hex(string value, long? defaultValue = null, char delim = '\t')
        {
            var ls = _load_fsl_cache;
            ls.Clear();

            int li = 0;
            int i = 0;
            for (; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch != delim)
                    continue;

                if (i != li)
                    ls.Add(Utility.ParseInt64ExactFast(value, li, i - li, true));
                else
                    ls.Add(defaultValue.Value);

                li = i + 1;
            }

            if (i != 0)
            {
                if (i != li)
                    ls.Add(Utility.ParseInt64ExactFast(value, li, i - li, true));
                else
                    ls.Add(defaultValue.Value);
            }

            return ls;
        }
        internal static IReadOnlyList<long> _Load_FastSplitInt64Hex2(string value, long? defaultValue = null, char delim = '\t')
        {
            var ls = _load_fsl_cache2;
            ls.Clear();

            int li = 0;
            int i = 0;
            for (; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch != delim)
                    continue;

                if (i != li)
                    ls.Add(Utility.ParseInt64ExactFast(value, li, i - li, true));
                else
                    ls.Add(defaultValue.Value);

                li = i + 1;
            }

            if (i != 0)
            {
                if (i != li)
                    ls.Add(Utility.ParseInt64ExactFast(value, li, i - li, true));
                else
                    ls.Add(defaultValue.Value);
            }

            return ls;
        }
        private static readonly List<long> _load_fsl_cache = new List<long>(32);
        private static readonly List<long> _load_fsl_cache2 = new List<long>(32);

        private static void _Load_BaseAddress(IDAVersion p, string value)
        {
            p.BaseAddress = Utility.ParseInt64ExactFast(value, true);
        }

        private static void _Load_Segment1(IDAVersion p, string value)
        {
            var spl = _Load_FastSplit(value);

            var seg = new IDASegment(p);
            p.Segments.Add(seg);
            seg.Begin = p.AdjustOffset(Utility.ParseInt64ExactFast(spl[0], true));
            seg.End = p.AdjustOffset(Utility.ParseInt64ExactFast(spl[1], true));
            seg.Name = spl[2];
            seg.Flags = (byte)Utility.ParseInt64ExactFast(spl[3], true);

            // Calculate code for segment.
            {
                byte code = (byte)(seg.Flags & 0xF);
                byte ch = 0;
                if (!string.IsNullOrEmpty(seg.Name))
                {
                    switch (seg.Name)
                    {
                        case ".text": ch = 1; break;
                        case ".data": ch = 2; break;
                        case ".rdata": ch = 3; break;
                        case ".pdata": ch = 4; break;
                        case ".bind": ch = 5; break;
                        case ".tls": ch = 6; break;
                        case ".idata": ch = 7; break;

                        default:
                            {
                                int hs = seg.Name.GetHashCode() & 0x7;
                                ch = (byte)(hs + 8);
                            }
                            break;
                    }
                }
                ch <<= 4;
                code |= ch;

                seg.Code = code;
            }
        }

        private static void _Load_Segment2(IDAVersion p)
        {
            p.Segments.Sort((u, v) => u.Begin.CompareTo(v.Begin));
            for (int i = 0; i < p.Segments.Count; i++)
                p.Segments[i].Index = i;
        }

        private static void _Load_Func(IDAVersion p, string value)
        {
            var spl = _Load_FastSplit(value);
            Offset begin = p.AdjustOffset(Utility.ParseInt64ExactFast(spl[0], true));
            Offset end = p.AdjustOffset(Utility.ParseInt64ExactFast(spl[1], true));

            int sz = (end - begin).ToInt32();
            if (sz < 0)
                throw new ArgumentException();

            if (p.DidDeleteSegment && p.GetSegment(begin) == null)
                return;

            var obj = p.EnsureObject(begin, IDAObjectTypes.Function);
            obj.End = end;
        }

        private static bool _Should_Ignore_Global(IDAVersion p, string vt)
        {
            if (IDAMigrate.IgnoreSomeCompilerGeneratedRdata)
            {
                if (string.IsNullOrEmpty(vt))
                    return false;

                int ix = vt.IndexOf('[');
                if (ix >= 0)
                    vt = vt.Substring(0, ix);

                switch (vt)
                {
                    case "_msEhRef":
                    //case "type_info":
                    case "_msEhDsc1":
                    case "_msEhDsc2":
                    case "UNWIND_CODE":
                    case "UNWIND_INFO":
                    case "RUNTIME_FUNCTION":
                        return true;
                }
            }

            return false;
        }
        
        private static void _Load_Global(IDAVersion p, string value)
        {
            var spl = _Load_FastSplit(value);
            Offset begin = p.AdjustOffset(Utility.ParseInt64ExactFast(spl[0], true));

            if (p.DidDeleteSegment && p.GetSegment(begin) == null)
                return;

            if (_Should_Ignore_Global(p, spl[1]))
            {
                p.IgnoredGlobal.Add(begin);
                return;
            }

            var obj = p.EnsureObject(begin, IDAObjectTypes.Global);
            if (obj == null)
                return;

            string vt = spl[1];
            if (!string.IsNullOrEmpty(vt))
            {
                int? sz = null;
                if (p.Migration.Architecture == ArchitectureTypes.x86_64)
                {
                    string vtx = vt;
                    int arr = 1;
                    if (vtx[vtx.Length - 1] == ']')
                    {
                        int ix1 = vtx.IndexOf('[');
                        if (ix1 >= 0)
                        {
                            string vtarr = vtx.Substring(ix1);
                            vtarr = vtarr.Substring(1, vtarr.Length - 2);

                            try
                            {
                                long arrsz = Utility.ParseInt64ExactFast(vtarr, false);
                                arr = (int)arrsz;
                            }
                            catch
                            {

                            }

                            vtx = vtx.Substring(0, ix1);
                        }
                    }
                    switch (vtx)
                    {
                        case "_msEhRef": sz = 0x20; obj.IgnoreRefsFromThis = true; break;
                        case "type_info": sz = 0x10; break;
                        case "_PMD": sz = 0xC; break;
                        case "_RTTIClassHierarchyDescriptor": sz = 0x10; break;
                        case "_RTTIBaseClassDescriptor": sz = 0x18; break;
                        case "_RTTICompleteObjectLocator": sz = 0x18; break;
                        case "_msEhDsc1": sz = 0x8; obj.IgnoreRefsFromThis = true; break;
                        case "_msEhDsc2": sz = 0x8; obj.IgnoreRefsFromThis = true; break;
                        case "UNWIND_CODE": sz = 0x2; break;
                        case "UNWIND_INFO": sz = 0x4; break;
                        case "RUNTIME_FUNCTION": sz = 0xC; obj.IgnoreRefsFromThis = true; break;
                        case "char": sz = 0x1; break;
                    }

                    if (sz.HasValue)
                        sz = sz.Value * arr;
                }
                else
                {
                    throw new NotImplementedException();
                }

                if (sz.HasValue)
                    obj.Size = sz.Value;
            }
        }

        private static void _Load_RTTIUnreferencedObjects(IDAVersion p, string value)
        {
            var spl = _Load_FastSplit(value);

            // `RTTI Complete Object Locator'
            Offset of = p.AdjustOffset(Utility.ParseInt64ExactFast(spl[0], true));
            var obj = p.GetObject(of);
            if (obj != null)
                return;

            string n = spl[2];
            if (string.IsNullOrEmpty(n))
                return;

            n = PreprocessName(n);

            string vt = null;
            if (n.Contains("`RTTI"))
            {
                if (n.Contains("`RTTI Complete Object Locator'"))
                    vt = "_RTTICompleteObjectLocator";
                else if (n.Contains("`RTTI Class Hierarchy Descriptor'"))
                    vt = "_RTTIClassHierarchyDescriptor";
                else if (n.Contains("`RTTI Base Class Array'"))
                    vt = "";
                else if (n.Contains("`RTTI Base Class Descriptor at"))
                    vt = "_RTTIBaseClassDescriptor";
                else if (n.Contains("`RTTI Type Descriptor'"))
                    vt = "type_info";
            }

            if (vt == null)
                return;

            if (p.DidDeleteSegment && p.GetSegment(of) == null)
                return;

            obj = p.EnsureObject(of, IDAObjectTypes.Global);

            if (!string.IsNullOrEmpty(vt))
            {
                int? sz = null;
                if (p.Migration.Architecture == ArchitectureTypes.x86_64)
                {
                    string vtx = vt;
                    int arr = 1;
                    if (vtx[vtx.Length - 1] == ']')
                    {
                        int ix1 = vtx.IndexOf('[');
                        if (ix1 >= 0)
                        {
                            string vtarr = vtx.Substring(ix1);
                            vtarr = vtarr.Substring(1, vtarr.Length - 2);

                            try
                            {
                                long arrsz = Utility.ParseInt64ExactFast(vtarr, false);
                                arr = (int)arrsz;
                                vtx = vtx.Substring(0, ix1);
                            }
                            catch
                            {

                            }
                        }
                    }
                    switch (vtx)
                    {
                        case "_msEhRef": sz = 0x20; break;
                        case "type_info": sz = 0x10; break;
                        case "_PMD": sz = 0xC; break;
                        case "_RTTIClassHierarchyDescriptor": sz = 0x10; break;
                        case "_RTTIBaseClassDescriptor": sz = 0x18; break;
                        case "_RTTICompleteObjectLocator": sz = 0x18; break;
                        case "_msEhDsc1": sz = 0x8; break;
                        case "_msEhDsc2": sz = 0x8; break;
                        case "UNWIND_CODE": sz = 0x2; break;
                        case "UNWIND_INFO": sz = 0x4; break;
                        case "RUNTIME_FUNCTION": sz = 0xC; break;
                        case "char": sz = 0x1; break;
                    }

                    if (sz.HasValue)
                        sz = sz.Value * arr;
                }
                else
                {
                    throw new NotImplementedException();
                }

                if (sz.HasValue)
                    obj.Size = sz.Value;
            }

            obj.CustomStringAssociation = n;
        }

        private static void _Load_VTable(IDAVersion p, string value)
        {
            var spl = _Load_FastSplitInt64Hex(value);
            long loc = spl[1];
            if (loc < p.BaseAddress || loc > p.BaseAddress + 0x80000000)
                loc = 0;

            Offset begin = p.AdjustOffset(spl[0]);
            if (p.DidDeleteSegment && p.GetSegment(begin) == null)
                return;

            var obj = p.EnsureObject(begin, IDAObjectTypes.VTable);
            obj.Size = (spl.Count - 2) * (p.Migration.Architecture == ArchitectureTypes.x86_64 ? 8 : 4);
            for (int i = 2; i < spl.Count; i++)
            {
                Offset of = p.AdjustOffset(spl[i]);
                if (p.DidDeleteSegment && p.GetSegment(of) == null)
                    continue;
                p.EnsureObject(of, IDAObjectTypes.Unknown); // Don't set func because it may be loc
            }
        }

        private static void _Load_XRef1(IDAVersion p, string value)
        {
            var spl = _Load_FastSplitInt64Hex(value);
            Offset target = p.AdjustOffset(spl[0]);
            Offset source = p.AdjustOffset(spl[1]);

            if (p.IgnoredGlobal.Contains(source) || p.IgnoredGlobal.Contains(target))
                return;

            // This is normal, we may have deleted the segment, so we shouldn't care about this ref.
            if (p.GetSegment(source) == null || p.GetSegment(target) == null)
                return;

            // long type = spl[2];
            p.__loading_refs.Add(new IDAVersion._temp_ref()
            {
                Source = source,
                Target = target,
            });
        }

        private static void _Load_GenerateCodeLocAndUnknownObjects(IDAVersion p)
        {
            var withinCache = new IDAWithinObjectLookupCache();
            withinCache.Refresh(p);

            // Target of references should be some kind of object.
            foreach (var iref in p.__loading_refs)
            {
                var tg = p.GetObject(iref.Target);
                if (tg != null)
                    continue;

                var seg = p.GetSegment(iref.Target);
                if (seg == null) // ? this should not be possible
                    continue;
                
                tg = p.FindWithinObject(iref.Target, withinCache);
                if (tg != null)
                    continue;

                if (seg.CanHaveLoc)
                    tg = p.EnsureObject(iref.Target, IDAObjectTypes.Loc);
                else
                    tg = p.EnsureObject(iref.Target, IDAObjectTypes.Unknown);
            }

            // Source of references should be some kind of object but only in data sections.
            // One reason we need these objects to exist is so that when surrounding objects get assigned the data in between also can get assigned due to range matching.
            foreach (var iref in p.__loading_refs)
            {
                var src = p.GetObject(iref.Source);
                if (src != null)
                    continue;

                var seg = p.GetSegment(iref.Source);
                if (seg == null) // ? probably can't happen
                    continue;
                
                if (seg.IsExecutable)
                    continue;

                src = p.FindWithinObject(iref.Source, withinCache);
                if (src != null)
                    continue;

                src = p.EnsureObject(iref.Source, IDAObjectTypes.Unknown);
            }

            p._sortObjects();
            for (int i = 0; i < p.Objects.Count - 1;)
            {
                var cur = p.Objects[i];
                var next = p.Objects[i + 1];

                bool overlap = next.Begin < cur.End || next.Begin == cur.Begin;
                if (!overlap)
                {
                    i++;
                    continue;
                }

                bool allowed = false;

                if (cur.Type == next.Type)
                {
                    // Current object must be bigger or same size. We can delete next because it is either duplicate or somehow contains a subsection of itself (both possible).
                    if (next.Begin == cur.Begin || next.End == cur.End)
                        allowed = true;
                    else if ((cur.Type == IDAObjectTypes.Global || cur.Type == IDAObjectTypes.Unknown) && cur.Size > 0 && next.Size == 0)
                        allowed = true;
                }
                else if (cur.Type != IDAObjectTypes.Unknown && (next.Type == IDAObjectTypes.Unknown || next.Type == IDAObjectTypes.Global))
                {
                    // Unknown or global are not so important if there is a better or same type of object.
                    allowed = true;
                }

                // There really isn't anything we can do about this.
/*#if DEBUG
                if (!allowed)
                    throw new InvalidOperationException();
#endif*/

                p._deleteObject(i + 1);
            }

            // Calculate dummy sizes for all locs.
            {
                var ls = p.Objects.ToList();
                ls.Sort((u, v) => u.Begin.CompareTo(v.Begin));

                for (int i = 0; i < ls.Count; i++)
                {
                    var o = ls[i];
                    if (o.Type != IDAObjectTypes.Loc)
                        continue;

                    var next = i < (ls.Count - 1) ? ls[i + 1] : null;
                    if (next == null || next.Segment != o.Segment)
                        o.Size = (o.Segment.End - o.Begin).ToInt32();
                    else
                        o.Size = (next.Begin - o.Begin).ToInt32();
                }
            }
        }

        private static void _Load_Asm1(IDAVersion p, string value)
        {
            var spl = _Load_FastSplit(value);
            long begin = Utility.ParseInt64ExactFast(spl[0], true);
            Offset of = p.AdjustOffset(begin);

            var obj = p.FindWithinObject(of, p.WithinObjectLookupCache);
            if (obj == null)
            {
                // This may be normal for some extra bytes or aligns or weird reasons?
                return;
            }

            if (obj.Type != IDAObjectTypes.Function && obj.Type != IDAObjectTypes.Loc)
            {
                // This should definitely not be possible ?
#if DEBUG_ASM_ERRORS
                throw new ArgumentException();
#else
                return;
#endif
            }

            if (obj.AsmBuilder == null)
                obj.AsmBuilder = new IDAExportAsmBuilder();
            obj.AsmBuilder.Append(begin, spl, 1);
        }

        private static void _Load_Asm2(IDAVersion p)
        {
            foreach (var o in p.Objects)
            {
                if (o.AsmBuilder != null)
                {
                    o.Asm = o.AsmBuilder.Build(p);
                    //bool didBadBegin = false;
                    foreach (var ln in o.AsmBuilder.Lines)
                    {
                        if ((ln.Flags & IDAExportAsmBuilder.AssemblyLineFlags.BadRefSource) != IDAExportAsmBuilder.AssemblyLineFlags.None)
                        {
                            var padjusted = p.AdjustOffset(ln.Address);
                            p.__loading_refs.RemoveAll(q => q.Source == padjusted);
                        }
                        /*else if((ln.Flags & IDAExportAsmBuilder.AssemblyLineFlags.Stop) != IDAExportAsmBuilder.AssemblyLineFlags.None && !didBadBegin)
                        {
                            didBadBegin = true;
                            var srcbegin = p.AdjustOffset(ln.Address);
                            var srcend = o.End;
                            p.__loading_refs.RemoveAll(q => q.Source >= srcbegin && q.Source < srcend);
                        }*/
                    }
                    o.AsmBuilder = null;
                }
            }
        }

        private static void _Load_XRef2(IDAVersion p)
        {
            // Actually create references.
            foreach (var tr in p.__loading_refs)
            {
                var r = new IDAReference();
                r.Source = IDAReferencePoint.Create(tr.Source, p);
                r.Target = IDAReferencePoint.Create(tr.Target, p);

                var so = r.Source.Object;
                var to = r.Target.Object;

                // This type of reference is useless to us. This handles cases where both are null or both are same object.
                if (so == to)
                    continue;

                // It is possible in some cases, but we can ignore safely.
                if (so == null || to == null)
                {
                    continue;
                    //throw new InvalidOperationException();
                }

                // Special case, don't allow outgoing references from switch section, this sometimes has invalid references.
                if (so != null && so.Asm.StopAt.HasValue && r.Source.Address >= so.Asm.StopAt.Value)
                    continue;

                // Also don't allow incoming references to switch places? Sometimes the switch table is treated as data and sometimes IDA detects the references correctly.
                if (to != null && to.Asm.StopAt.HasValue && r.Target.Address >= to.Asm.StopAt.Value)
                    continue;

                if (so != null && so.IgnoreRefsFromThis && IDAMigrate.IgnoreSomeCompilerGeneratedRdata)
                    continue;

                ulong data = 0;
                if (so != null)
                    data |= so.Segment.Code;
                else
                {
                    var seg = p.GetSegment(r.Source.Address);
                    if (seg != null)
                        data |= seg.Code;
                    else
                        continue;
                }
                data <<= 8;
                if (to != null)
                    data |= to.Segment.Code;
                else
                {
                    var seg = p.GetSegment(r.Target.Address);
                    if (seg != null)
                        data |= seg.Code;
                    else
                        continue;
                }
                data <<= 8;
                if (RefDirectionMatters && r.Source.Address < r.Target.Address)
                    data |= 1;
                if (r.Source.Offset != 0)
                    data |= 2;
                if (r.Target.Offset != 0)
                    data |= 4;
                data <<= 40;
                r.Data = data;

                if (so != null)
                    so.OutReferences.Add(r);

                if (to != null)
                    to.InReferences.Add(r);
            }

            p.__loading_refs = null;

            foreach (var o in p.Objects)
                o.SortReferences();
        }

        private static void _Load_String(IDAVersion p, string value)
        {
            var spl = _Load_FastSplit(value);

            Offset of = p.AdjustOffset(Utility.ParseInt64ExactFast(spl[0], true));
            var obj = p.GetObject(of);
            if (obj == null)
                return;

            switch (obj.Type)
            {
                case IDAObjectTypes.Global:
                case IDAObjectTypes.Unknown:
                    if (!IsProbablyString(spl[3]))
                        return;
                    _temp_bld.Append(spl[1]);
                    _temp_bld.Append('_');
                    _temp_bld.Append(spl[2]);
                    _temp_bld.Append('_');
                    _temp_bld.Append(spl[3]);
                    obj.CustomStringAssociation = _temp_bld.ToString();
                    _temp_bld.Clear();
                    break;
            }
        }
        private static StringBuilder _temp_bld = new StringBuilder(128);

        private static bool IsProbablyString(string str)
        {
            //int good = 0;
            //int bad = 0;

            return true;
        }
        //private static readonly string _VeryGoodString = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 ,.%'&";
        //private static readonly string _MediumString = ":;#!\"$()[]-+*/\\<>";

        private static string PreprocessName(string input)
        {
            int index;
            while ((index = input.IndexOf("lambda_")) >= 0)
            {
                int end = index + 7;
                while (end < input.Length)
                {
                    char ch = input[end];
                    if ((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F'))
                    {
                        end++;
                        continue;
                    }
                    break;
                }

                input = input.Remove(index, end - index);
                input = input.Insert(index, "LMBDA_REPLACED");
            }

            if (input.Length != 0 && input[0] == 'a')
            {
                bool did = false;
                int end = input.Length - 1;
                while (end >= 0)
                {
                    char ch = input[end];
                    if (ch >= '0' && ch <= '9')
                    {
                        end--;
                        continue;
                    }

                    if (ch == '_')
                    {
                        if (end == input.Length - 1)
                            break;

                        input = input.Substring(0, end);
                        did = true;
                        break;
                    }

                    break;
                }

                if (!did && (input.Length == 15 || input.Length == 14))
                    input = input.Substring(0, 13);
            }

            if (input.StartsWith("nullsub_"))
                return "nullsub";

            return input;
        }

        private static void _Load_Name(IDAVersion p, string value)
        {
            var spl = _Load_FastSplit(value);

            // `RTTI Complete Object Locator'
            Offset of = p.AdjustOffset(Utility.ParseInt64ExactFast(spl[0], true));
            var obj = p.GetObject(of);
            if (obj == null || !string.IsNullOrEmpty(obj.CustomStringAssociation))
                return;

            string n = spl[2];
            if (string.IsNullOrEmpty(n))
                return;

            n = PreprocessName(n);

            bool ok = false;
            if (n.Contains("`RTTI"))
            {
                if (n.Contains("`RTTI Complete Object Locator'") || n.Contains("`RTTI Class Hierarchy Descriptor'") || n.Contains("`RTTI Base Class Array'") || n.Contains("`RTTI Base Class Descriptor at"))
                {
                    ok = true;
                    //if (!string.IsNullOrEmpty(spl[1])) n = spl[1];
                }
            }
            else if (n.Contains("`vftable'"))
            {
                ok = true;
                //if (!string.IsNullOrEmpty(spl[1])) n = spl[1];
            }

            if (!ok)
                return;

            obj.CustomStringAssociation = n;
        }

        private void DeleteUnneededSegments()
        {
            this.DeleteUnneededSegment(".bind", true);
            this.DeleteUnneededSegment(".tls", true);
            this.DeleteUnneededSegment(".gfids", true);
            this.DeleteUnneededSegment("_RDATA", true);
            this.DeleteUnneededSegment(".pdata", true);
        }

        private void DeleteUnneededSegment(string name, bool force)
        {
            var seg0 = this.Versions[0].Segments.Where(q => q.Name == name).ToList();
            var seg1 = this.Versions[1].Segments.Where(q => q.Name == name).ToList();

            if (seg0.Count == seg1.Count && !force)
                return;

            if (seg0.Count != 0)
            {
                foreach (var x in seg0)
                    this.Versions[0].Segments.Remove(x);
                this.Versions[0].DidDeleteSegment = true;
            }

            if(seg1.Count != 0)
            {
                foreach (var x in seg1)
                    this.Versions[1].Segments.Remove(x);
                this.Versions[1].DidDeleteSegment = true;
            }
        }

        /// <summary>
        /// Loads the versions.
        /// </summary>
        /// <param name="prog">The progress bar.</param>
        /// <param name="dirs">The directories.</param>
        /// <exception cref="System.ArgumentException"></exception>
        private void LoadVersions(Progress prog, System.IO.DirectoryInfo[] dirs)
        {
            this.Versions[0] = new IDAVersion(this);
            this.Versions[1] = new IDAVersion(this);

            var first = this.Versions[0];
            var second = this.Versions[1];

            first.Other = second;
            second.Other = first;

            first.Index = 0;
            second.Index = 1;

            if (prog != null)
                prog.Initialize(4);

            IDAListener.Write("Loading base stuff ...");

            for (int i = 0; i < this.Versions.Length; i++)
            {
                var p = this.Versions[i];
                var dir = dirs[i];

                if (this.DoFileLoad(p, dir, "idaexport_base.txt", "baseaddress", _Load_BaseAddress) != 1)
                    throw new ArgumentException();

                this.DoFileLoad(p, dir, "idaexport_segment.txt", "segment", _Load_Segment1);
                _Load_Segment2(p);
            }

            this.DeleteUnneededSegments();

            if (this.SetBetterSegmentCodeIfEqualSegments())
            {
                for (int i = 0; i < this.Versions[0].Segments.Count; i++)
                {
                    var a = this.Versions[0].Segments[i];
                    var b = this.Versions[1].Segments[i];

                    a.Equivalent = b;
                    b.Equivalent = a;
                }
            }
            else
            {
                Dictionary<string, Tuple<List<IDASegment>, List<IDASegment>>> map = new Dictionary<string, Tuple<List<IDASegment>, List<IDASegment>>>();
                for (int i = 0; i < 2; i++)
                {
                    var v = this.Versions[i];
                    foreach (var s in v.Segments)
                    {
                        string n = s.Name + "_" + s.Flags.ToString();
                        Tuple<List<IDASegment>, List<IDASegment>> ls = null;
                        if (!map.TryGetValue(n, out ls))
                        {
                            ls = new Tuple<List<IDASegment>, List<IDASegment>>(new List<IDASegment>(), new List<IDASegment>());
                            map[n] = ls;
                        }

                        if (i == 0)
                            ls.Item1.Add(s);
                        else
                            ls.Item2.Add(s);
                    }
                }

                foreach (var pair in map)
                {
                    if (pair.Value.Item1.Count != pair.Value.Item2.Count)
                    {
                        throw new ArgumentException(); // Unable to assign segment because it's too different!
                        //continue;
                    }

                    for (int i = 0; i < pair.Value.Item1.Count; i++)
                    {
                        var a = pair.Value.Item1[i];
                        var b = pair.Value.Item2[i];

                        a.Equivalent = b;
                        b.Equivalent = a;
                    }
                }
            }

            for (int i = 0; i < this.Versions.Length; i++)
            {
                var p = this.Versions[i];
                var dir = dirs[i];

                IDAListener.Write("Loading " + (i == 0 ? "first" : "second") + " database ...");

                this.DoFileLoad(p, dir, "idaexport_func.txt", "func", _Load_Func);
                this.DoFileLoad(p, dir, "idaexport_global.txt", "global", _Load_Global);
                this.DoFileLoad(p, dir, "idaexport_vtable.txt", "vtable", _Load_VTable);
                this.DoFileLoad(p, dir, "idaexport_name.txt", "name", _Load_RTTIUnreferencedObjects);
                this.DoFileLoad(p, dir, "idaexport_xrefs.txt", "xref", _Load_XRef1);
                _Load_GenerateCodeLocAndUnknownObjects(p);
                p.WithinObjectLookupCache = new IDAWithinObjectLookupCache();
                p.WithinObjectLookupCache.Refresh(p);
                this.DoFileLoad(p, dir, "idaexport_asm.txt", "asm", _Load_Asm1);
                p.WithinObjectLookupCache = null;

                /*#if DEBUG
                                System.Diagnostics.Process.GetCurrentProcess().Kill();
                                return;
#endif*/
                if (prog != null)
                    prog.Advance(1);
            }

            IDAListener.Write("Fixing undetected code locations ...");

            this.TryToConvertLocToFunc();

            IDAListener.Write("Building temporary within object lookup cache ...");

            for (int i = 0; i < this.Versions.Length; i++)
            {
                var p = this.Versions[i];

                p.WithinObjectLookupCache = new IDAWithinObjectLookupCache();
                p.WithinObjectLookupCache.Refresh(p);
            }

            for (int i = 0; i < this.Versions.Length; i++)
            {
                var p = this.Versions[i];
                var dir = dirs[i];

                IDAListener.Write("Loading additional data for " + (i == 0 ? "first" : "second") + " database ...");

                _Load_Asm2(p);
                _Load_XRef2(p);
                this.DoFileLoad(p, dir, "idaexport_string.txt", "string", _Load_String);
                this.DoFileLoad(p, dir, "idaexport_name.txt", "name", _Load_Name);

                if (prog != null)
                    prog.Advance(1);
            }

            if(IDAMigrate.TryRemoveEmptyAlignLocs)
            {
                IDAListener.Write("Trying to remove empty alignment locations ...");
                foreach(var p in this.Versions)
                {
                    int did = 0;
                    for(int i = p.Objects.Count - 1; i >= 0; i--)
                    {
                        var o = p.Objects[i];
                        if(o.Type == IDAObjectTypes.Loc && o.InReferences.Count == 0 && o.OutReferences.Count == 0)
                        {
                            var s = o.Segment;
                            if(s.IsExecutable)
                            {
                                if (o.Asm.Data == null || o.Asm.Data.Length == 0)
                                {
                                    p._deleteObject(i);
                                    did++;
                                }
                            }
                        }
                    }
                    IDAListener.Write("Removed " + did + " empty locs in " + (p.Index == 0 ? "first" : "second") + " database.");
                }
            }

            IDAListener.Write("Initializing lookup cache ...");

            for (int i = 0; i < this.Versions.Length; i++)
            {
                var p = this.Versions[i];

                p._lockObjects();
                p.WithinObjectLookupCache = new IDAWithinObjectLookupCache();
                p.WithinObjectLookupCache.Refresh(p);
            }

            IDAListener.Write("Calculating comparison cache ...");

            for (int i = 0; i < this.Versions.Length; i++)
            {
                foreach (var o in this.Versions[i].Objects)
                    o.Comparison.Refresh();
            }

            IDAListener.Write("Loading complete!");
        }

        /// <summary>
        /// Does the file load.
        /// </summary>
        /// <param name="p">The version.</param>
        /// <param name="dir">The directory.</param>
        /// <param name="fileName">Name of the file.</param>
        /// <param name="key">The key.</param>
        /// <param name="action">The action.</param>
        /// <returns></returns>
        private int DoFileLoad(IDAVersion p, System.IO.DirectoryInfo dir, string fileName, string key, Action<IDAVersion, string> action)
        {
            char ch = key[0];
            System.IO.FileInfo info = new System.IO.FileInfo(System.IO.Path.Combine(dir.FullName, fileName));
            int did = 0;
            using (var f = info.OpenText())
            {
                string l = null;
                while ((l = f.ReadLine()) != null)
                {
                    if (l.Length == 0)
                        continue;

                    if (l[0] != ch || string.Compare(l, 0, key, 0, key.Length) != 0)
                        continue;

                    int ix = l.IndexOf('\t');
                    string a = ix < 0 ? "" : l.Substring(ix + 1);

                    action(p, a);
                    did++;
                }
            }

            return did;
        }

        #endregion
    }

    internal sealed class IDAObjectList
    {
        internal IDAObjectList(int listIndex)
        {
            this.ListIndex = listIndex;
            __high_list_index = Math.Max(__high_list_index, listIndex + 1);
        }

        internal readonly int ListIndex;

        internal static int __high_list_index = 0;

        internal ICollection<IDAInObjectListRef> Collection
        {
            get
            {
                return this.List;
            }
        }

        internal int Count
        {
            get
            {
                return this.Collection.Count;
            }
        }

        internal bool Contains(IDAInObjectListRef reference)
        {
            return reference.List == this;
        }

        internal IDAInObjectListRef Contains(IDAObject obj)
        {
            if (this.ListIndex >= 0)
            {
                var r = obj.__in_indexed_lists[this.ListIndex];
                if (r != null && r.List == this)
                    return r;
                return null;
            }

            foreach (var n in obj.__in_lists)
            {
                if (n.List == this)
                    return n;
            }

            return null;
        }

        internal readonly LinkedList<IDAInObjectListRef> List = new LinkedList<IDAInObjectListRef>();

        internal void Clear()
        {
            var n = this.List.First;
            while (n != null)
            {
                var c = n;
                n = n.Next;

                c.Value.Remove();
            }
        }

        internal IDAInObjectListRef Add(IDAObject obj)
        {
#if DEBUG
            if (this.Contains(obj) != null) throw new InvalidOperationException();
#endif

            var l = new IDAInObjectListRef();
            l.List = this;
            l.Value = obj;
            l.Node = this.List.AddLast(l);
            if (this.ListIndex >= 0)
                obj.__in_indexed_lists[this.ListIndex] = l;
            else
                l.__in_obj = obj.__in_lists.AddLast(l);
            return l;
        }

        internal bool Remove(IDAObject obj)
        {
            var c = this.Contains(obj);
            if (c != null)
                return c.Remove();
            return false;
        }

        internal bool Remove(IDAInObjectListRef reference)
        {
            if (reference.List != this)
                return false;

            var n = reference.Node;
            n.List.Remove(n);
            reference.Node = null;
            reference.List = null;

            if (this.ListIndex >= 0)
                reference.Value.__in_indexed_lists[this.ListIndex] = null;
            else
            {
                n = reference.__in_obj;
                n.List.Remove(n);
                reference.__in_obj = null;
            }
            reference.Value = null;
            return true;
        }

        internal bool Foreach(Func<IDAObject, int> action)
        {
            var n = this.List.First;
            while (n != null)
            {
                var cur = n;
                n = n.Next;

                int r = action(cur.Value.Value);
                if (r == 0)
                    continue;

                if (r < 0)
                {
                    cur.Value.Remove();
                    continue;
                }

                return true;
            }

            return false;
        }
    }

    internal sealed class IDAInObjectListRef
    {
        internal LinkedListNode<IDAInObjectListRef> Node;
        internal IDAObject Value;
        internal IDAObjectList List;
        internal LinkedListNode<IDAInObjectListRef> __in_obj;

        internal bool IsValid
        {
            get
            {
                return this.List != null;
            }
        }

        internal bool Remove()
        {
            var ls = this.List;
            if (ls != null)
                return ls.Remove(this);
            return false;
        }
    }

    internal sealed class IDAWithinObjectLookupCache
    {
        internal IDAWithinObjectLookupCache()
        {

        }

        private sealed class ObjPage
        {
            internal ObjPage(int size)
            {
                this.Data = new IDAObject[size];
            }

            internal readonly IDAObject[] Data;
            internal Offset Begin;
            internal Offset End;
        }

        private readonly List<ObjPage> Pages = new List<ObjPage>();

        private bool Has = false;

        internal IDAObject Get(Offset of)
        {
#if DEBUG
            // Cache is not generated yet!
            if (!this.Has)
                throw new InvalidOperationException();
#endif

            foreach (var p in this.Pages)
            {
                if (of >= p.Begin && of < p.End)
                {
                    int ix = (of - p.Begin).ToInt32();
                    return p.Data[ix];
                }
            }

            return null;
        }

        internal void Refresh(IDAVersion p)
        {
            this.Pages.Clear();

            this.Has = true;

            var ls = p.Objects.ToList();
            if (!p.IsLocked)
                ls.Sort((u, v) => u.Begin.CompareTo(v.Begin));

            List<IDAObject> curls = new List<IDAObject>();
            Offset begin = 0;
            int batch = 0x100000 / 2;

            foreach (var obj in ls)
            {
                if (curls.Count == 0)
                {
                    if (obj.Begin == obj.End)
                        continue;

                    begin = obj.Begin;
                    curls.Add(obj);
                }
                else
                {
                    if (obj.End > obj.Begin)
                        curls.Add(obj);
                }

                var end = obj.End;
                int sz = (end - begin).ToInt32();
                if (sz < batch)
                    continue;

                if (obj.Begin == obj.End)
                {
                    end = curls[curls.Count - 1].End;
                    sz = (end - begin).ToInt32();
                }

                var page = new ObjPage(sz);
                page.Begin = begin;
                page.End = end;
                foreach (var o in curls)
                {
                    int ixb = (o.Begin - begin).ToInt32();
                    int ixe = (o.End - begin).ToInt32();
                    for (int i = ixb; i < ixe; i++)
                        page.Data[i] = o;
                }
                this.Pages.Add(page);
                curls.Clear();
            }

            if (curls.Count != 0)
            {
                var end = curls[curls.Count - 1].End;
                int sz = (end - begin).ToInt32();

                if (sz != 0)
                {
                    var page = new ObjPage(sz);
                    page.Begin = begin;
                    page.End = end;
                    foreach (var o in curls)
                    {
                        int ixb = (o.Begin - begin).ToInt32();
                        int ixe = (o.End - begin).ToInt32();
                        for (int i = ixb; i < ixe; i++)
                            page.Data[i] = o;
                    }
                    this.Pages.Add(page);
                }
            }
        }
    }

    internal sealed class IDAVersion
    {
        internal IDAVersion(IDAMigrate parent)
        {
            this.Migration = parent;
        }

        internal readonly IDAMigrate Migration;

        internal IDAVersion Other;
        internal int Index;
        internal readonly List<IDASegment> Segments = new List<IDASegment>();
        internal Offset BaseAddress;
        internal bool DidDeleteSegment = false;

        internal readonly HashSet<Offset> IgnoredGlobal = new HashSet<Offset>();

        internal IReadOnlyList<IDAObject> Objects
        {
            get
            {
                return this._objects;
            }
        }
        private readonly List<IDAObject> _objects = new List<IDAObject>();
        private readonly Dictionary<Offset, IDAObject> _objectMap = new Dictionary<Offset, IDAObject>();

        internal readonly IDAObjectList AssignedObjects = new IDAObjectList(0);
        internal readonly IDAObjectList UnassignedObjects = new IDAObjectList(1);

        internal struct _temp_ref
        {
            internal Offset Source;
            internal Offset Target;
        }

        internal List<_temp_ref> __loading_refs = new List<_temp_ref>();
        internal HashSet<Offset> __loading_possible_codeloc = new HashSet<Offset>();

        internal IDAWithinObjectLookupCache WithinObjectLookupCache = null;

        internal int ConvertedFromLocsCount = 0;
        internal int ConvertedToFuncsCount = 0;

        internal List<Offset> ConvertedFromLocs = new List<Offset>();

        internal IDAObject EnsureObject(Offset of, IDAObjectTypes type = IDAObjectTypes.Unknown)
        {
            var obj = this.GetObject(of);
            if (obj == null)
            {
                if (this.IsLocked)
                    throw new InvalidOperationException();

                var seg = this.GetSegment(of);
                if (seg == null)
#if DEBUG_ASM_ERRORS
                    throw new ArgumentOutOfRangeException("of");
#else
                    return null;
#endif

                    obj = new IDAObject(this, of, ++this.Migration.__high_guid);
                obj.Type = type;
                obj.Segment = seg;

                this._addNewObject(obj);
                return obj;
            }

            switch (type)
            {
                case IDAObjectTypes.Unknown:
                    break;

                case IDAObjectTypes.Global:
                    switch (obj.Type)
                    {
                        case IDAObjectTypes.Global:
                        case IDAObjectTypes.Struct:
                        case IDAObjectTypes.VTable:
                            break;

                        case IDAObjectTypes.Unknown:
                            obj.Type = IDAObjectTypes.Global;
                            break;

                        default: throw new ArgumentException("Unable to change object type from " + obj.Type + " to " + type + "!");
                    }
                    break;

                case IDAObjectTypes.Struct:
                    switch (obj.Type)
                    {
                        case IDAObjectTypes.Global:
                        case IDAObjectTypes.Unknown:
                            obj.Type = IDAObjectTypes.Struct;
                            break;

                        case IDAObjectTypes.Struct:
                            break;

                        default: throw new ArgumentException("Unable to change object type from " + obj.Type + " to " + type + "!");
                    }
                    break;

                case IDAObjectTypes.VTable:
                    switch (obj.Type)
                    {
                        case IDAObjectTypes.Unknown:
                        case IDAObjectTypes.Global:
                            obj.Type = IDAObjectTypes.VTable;
                            break;

                        case IDAObjectTypes.VTable:
                            break;

                        default: throw new ArgumentException("Unable to change object type from " + obj.Type + " to " + type + "!");
                    }
                    break;

                case IDAObjectTypes.Loc:
                    switch (obj.Type)
                    {
                        case IDAObjectTypes.Unknown:
                            obj.Type = IDAObjectTypes.Loc;
                            break;

                        case IDAObjectTypes.Loc:
                            break;

                        default: throw new ArgumentException("Unable to change object type from " + obj.Type + " to " + type + "!");
                    }
                    break;

                case IDAObjectTypes.Function:
                    switch (obj.Type)
                    {
                        case IDAObjectTypes.Unknown:
                            obj.Type = IDAObjectTypes.Function;
                            break;

                        case IDAObjectTypes.Function:
                            break;

                        // Loc conversion is done elsewhere so don't handle it here.
                        default: throw new ArgumentException("Unable to change object type from " + obj.Type + " to " + type + "!");
                    }
                    break;

                default:
                    throw new NotSupportedException();
            }

            return obj;
        }

        internal IDAObject GetObject(Offset of)
        {
            IDAObject o = null;
            if (this._objectMap.TryGetValue(of, out o))
                return o;

            return null;
        }

        /// <summary>
        /// Finds the offset within object. This will only search objects that have size! It will not return object even if offset exactly matches if object size is 0.
        /// </summary>
        /// <param name="of">The offset.</param>
        /// <param name="cache">The cache.</param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentNullException">cache</exception>
        internal IDAObject FindWithinObject(Offset of, IDAWithinObjectLookupCache cache)
        {
#if DEBUG
            if (cache == null)
                throw new ArgumentNullException("cache");
#endif

            return cache.Get(of);
        }

        internal IDASegment GetSegment(Offset of)
        {
            foreach (var seg in this.Segments)
            {
                if (of >= seg.Begin && of < seg.End)
                    return seg;
            }

            return null;
        }

        internal Offset AdjustOffset(Offset of)
        {
            if (of == 0)
                return 0;

            if (of < this.BaseAddress)
                throw new ArgumentOutOfRangeException("of");

            of -= this.BaseAddress;

            // Sanity check, even reaching 0x1... here would be a huge 100+ mb binary
            if (of >= 0x40000000)
                throw new ArgumentOutOfRangeException("of");

            return of;
        }

        internal Offset UnadjustOffset(Offset of)
        {
            if (of == 0)
                return 0;

            return this.BaseAddress + of;
        }

        internal void _addNewObject(IDAObject obj)
        {
            if (this.IsLocked)
                throw new InvalidOperationException();

            if (obj.Begin == 0)
                throw new ArgumentOutOfRangeException();

            this._objects.Add(obj);
            this._objectMap[obj.Begin] = obj;
            this.UnassignedObjects.Add(obj);
        }

        internal void _deleteObject(int index)
        {
            if (this.IsLocked)
                throw new InvalidOperationException();

            var obj = this._objects[index];
            obj._removeFromAllLists();
            obj._removeAllReferences();
            this._objects.RemoveAt(index);
            this._objectMap.Remove(obj.Begin);
            // Object has not been added to segment list yet.
        }

        internal void _deleteObject(IDAObject obj)
        {
            int ix = this._objects.IndexOf(obj);
            this._deleteObject(ix);
        }

        internal void _lockObjects()
        {
            if (this.IsLocked)
                throw new InvalidOperationException();

            this._sortObjects();

            this.IsLocked = true;

            for (int i = 0; i < this.Objects.Count - 1; i++)
            {
                var cur = this.Objects[i];
                var next = this.Objects[i + 1];

                if (next.Begin < cur.End || cur.End > next.Begin)
                    throw new InvalidOperationException();
            }

            foreach (var o in this.Objects)
            {
                if (o.End < o.Begin)
                    throw new InvalidOperationException();
            }

            for (int i = 0; i < this.Objects.Count; i++)
                this.Objects[i].IndexInVersion = i;

            foreach (var seg in this.Segments)
                seg._lockObjects();
        }

        internal bool IsLocked
        {
            get;
            private set;
        }

        public override string ToString()
        {
            return this.Index == 0 ? "First" : "Second";
        }

        internal void _sortObjects()
        {
            if (this.IsLocked)
                throw new InvalidOperationException();

            this._objects.Sort((u, v) =>
            {
                int c = u.Begin.CompareTo(v.Begin);
                if (c != 0)
                    return c;
                c = u.End.CompareTo(v.End);
                if (c != 0)
                    return -c;
                return 0;
            });
        }

        internal IReadOnlyList<KeyValuePair<Offset, int>> GetFailedMatchesWithManyInRefs()
        {
            var ls = new List<KeyValuePair<Offset, int>>();
            foreach(var o in this.Objects)
            {
                if (o.Match != null)
                    continue;

                int sz = o.Comparison.GetEntrySize(IDAObjectComparisonData.IDAObjectComparisonTypes.InRef);
                if (sz < 5000)
                    continue;

                ls.Add(new KeyValuePair<Offset, int>(o.Begin, sz));
            }

            ls.Sort((u, v) => v.Value.CompareTo(u.Value));
            return ls;
        }

        internal Dictionary<Offset, ulong> BuildFunctionHashes()
        {
            var map = new Dictionary<Offset, ulong>();

            foreach(var o in this.Objects)
            {
                if (o.Asm.Data == null || o.Asm.Data.Length == 0)
                    continue;

                if (o.Asm.Offsets == null || o.Asm.Data.Length != o.Asm.Offsets.Count)
                    throw new InvalidOperationException("ASM data count does not match ASM offset count!");

                // DEBUG! (remove this after)
                /*for(int i = 1; i < o.Asm.Offsets.Count; i++)
                {
                    var p = o.Asm.Offsets[i - 1];
                    var c = o.Asm.Offsets[i];
                    if (p > c)
                        throw new ArgumentException();
                }*/

                // Need to generate new hash, because we care about different things when hooking than the diffing app does.
                Offset begin = o.Asm.Offsets[0];
                List<ulong> ndata = new List<ulong>();
                for(int i = 0; i < o.Asm.Data.Length; i++)
                {
                    long nx = (o.Asm.Offsets[i] - begin).ToInt64();
                    ndata.Add(unchecked((ulong)nx));
                    ndata.Add(o.Asm.Data[i]);
                }

                ulong hash = Utility.Calculate64BitHashCode(ndata);
                map[o.Begin] = hash;
            }

            return map;
        }
    }

    internal sealed class IDASegment
    {
        internal IDASegment(IDAVersion parent)
        {
            this.Version = parent;
        }

        internal readonly IDAVersion Version;

        internal string Name;
        internal Offset Begin;
        internal Offset End;
        internal byte Code;
        internal byte Flags;
        internal int Index;
        internal IDASegment Equivalent;

        internal IReadOnlyList<IDAObject> Objects
        {
            get
            {
                return this._objects;
            }
        }
        private List<IDAObject> _objects = new List<IDAObject>();

        private void _addNewObject(IDAObject obj)
        {
            // This may only be called from _lockObjects !!
            obj.IndexInSegment = this.Objects.Count;
            this._objects.Add(obj);
        }

        internal int FindBeginIndexForIndexPct(double minPct)
        {
            int c = this.Objects.Count;
            if (c == 0)
                return -1;

            int minPossible = 0;
            int maxPossible = c;

            while (minPossible < maxPossible)
            {
                int middle = (maxPossible - minPossible) / 2 + minPossible;
                var o = this._objects[middle];
                double op = o.IndexPctInSegment;

                if (op < minPct)
                {
                    minPossible = middle + 1;
                    continue;
                }

                maxPossible = middle;
            }

            int minCheck = Math.Max(0, minPossible);
            int maxCheck = Math.Min(c - 1, maxPossible + 1);

            for (int i = minCheck; i <= maxCheck; i++)
            {
                var o = this._objects[i];
                if (o.IndexPctInSegment >= minPct)
                    return i;
            }

            return -1;
        }

        internal int FindEndIndexForIndexPct(double maxPct)
        {
            int c = this.Objects.Count;
            if (c == 0)
                return -1;

            int minPossible = 0;
            int maxPossible = c;

            while (minPossible < maxPossible)
            {
                int middle = (maxPossible - minPossible) / 2 + minPossible;
                var o = this._objects[middle];
                double op = o.IndexPctInSegment;

                if (op > maxPct)
                {
                    maxPossible = middle;
                    continue;
                }

                minPossible = middle + 1;
            }

            int minCheck = Math.Max(0, minPossible - 1);
            int maxCheck = Math.Min(c - 1, maxPossible + 1);

            for (int i = maxCheck; i >= minCheck; i--)
            {
                var o = this._objects[i];
                if (o.IndexPctInSegment <= maxPct)
                    return i;
            }

            return -1;
        }

        internal void _lockObjects()
        {
            if (this.IsLocked)
                throw new InvalidOperationException();

            if (!this.Version.IsLocked)
                throw new InvalidOperationException();

            this.IsLocked = true;

            foreach (var o in this.Version.Objects)
            {
                if (o.Segment == this)
                    this._addNewObject(o);
            }
        }

        internal bool IsLocked
        {
            get;
            private set;
        }

        internal bool CanHaveLoc
        {
            get
            {
                // 1 = execute
                // 4 = read
                return (this.Flags & 5) == 5;
            }
        }

        internal bool IsExecutable
        {
            get
            {
                return (this.Flags & 1) != 0;
            }
        }

        internal bool IsReadable
        {
            get
            {
                return (this.Flags & 4) != 0;
            }
        }

        public override string ToString()
        {
            return this.Begin.ToString() + " " + this.Name;
        }
    }

    internal sealed class IDAObject
    {
        internal IDAObject(IDAVersion parent, Offset of, uint guid)
        {
            this.Version = parent;
            this.Begin = of;
            this.End = of;
            this.Guid = guid;
            this.Comparison = new IDAObjectComparisonData(this);
        }

        internal readonly IDAVersion Version;
        internal readonly Offset Begin;
        internal readonly uint Guid;
        internal Offset End;
        internal readonly List<IDAReference> InReferences = new List<IDAReference>();
        internal readonly List<IDAReference> OutReferences = new List<IDAReference>();
        internal IDAObjMatch Match;
        internal IDAObjectTypes Type;
        internal IDAObjectAsm Asm;
        internal IDAExportAsmBuilder AsmBuilder;
        internal readonly LinkedList<IDAInObjectListRef> __in_lists = new LinkedList<IDAInObjectListRef>();
        internal readonly IDAInObjectListRef[] __in_indexed_lists = new IDAInObjectListRef[IDAObjectList.__high_list_index];
        internal int IndexInVersion = -1;
        internal int IndexInSegment = -1;
        //internal int ExpectedSegmentOffset = 0;
        //internal int AdjustedDiffInSegment = 0; // Only used for second version! First does not change it.
        internal double IndexPctInSegment = -1.0;
        internal string Name;
        internal IDASegment Segment;
        internal string CustomStringAssociation;
        internal IDAObjectFlags Flags;
        internal readonly IDAObjectComparisonData Comparison;
        internal int _TriedSectionMatchLength = -1;
        internal IDAObject _TriedSectionMatchBegin = null;
        internal bool IgnoreRefsFromThis;

        internal int ExpectedSegmentOffset
        {
            get
            {
#if DEBUG
                if (this.Version.Index != 1)
                    throw new ArgumentException();
#endif

                int myIndex = this.IndexInSegment;
                int otherIndex = (int)(this.IndexPctInSegment * 0.01 * this.Segment.Equivalent.Objects.Count);
                return otherIndex - myIndex;
            }
        }
        
        internal void FindAllMatches(List<IDAObject> result, bool otherVersion = true, bool forAmbiguous = false, double? overwriteMaxIndexPctDiff = null)
        {
            var segment = otherVersion ? this.Segment.Equivalent : this.Segment;
            double maxDiff;
            var mig = this.Version.Migration;
            var p = mig.ComparisonParametersSetup[mig.ComparisonParametersIndex];
            //bool exact = p.IsCachedExact;
            if (overwriteMaxIndexPctDiff.HasValue)
                maxDiff = overwriteMaxIndexPctDiff.Value;
            else
                maxDiff = forAmbiguous ? p.MaxIndexDifferenceForAmbiguous : p.MaxIndexDifferenceForCompare;

            double min = this.IndexPctInSegment - maxDiff;
            double max = this.IndexPctInSegment + maxDiff;
            int begin = segment.FindBeginIndexForIndexPct(min);
            int end = segment.FindEndIndexForIndexPct(max);

#if DEBUG_ASM_ERRORS
            // Actually allowed if very few objects (less than 100)
            if (begin < 0 || end < 0) throw new InvalidOperationException();
#else
            if (begin < 0 || end < 0)
                return;
#endif

            for (int i = begin; i <= end; i++)
            {
                var o = segment.Objects[i];

                if (o == this)
                    continue;

                if (!this.Comparison.CompareWithCurrentParameters(o.Comparison, forAmbiguous, maxDiff))
                    continue;

                result.Add(o);
            }
        }

        internal void _removeFromAllLists()
        {
            var n = this.__in_lists.First;
            while (n != null)
            {
                var c = n;
                n = n.Next;
                c.Value.Remove();
            }

            for (int i = 0; i < __in_indexed_lists.Length; i++)
            {
                var r = __in_indexed_lists[i];
                if (r != null)
                    r.Remove();
            }
        }

        internal void _removeAllReferences()
        {
            foreach (var r in this.InReferences)
            {
                var other = r.Source.Object;
                if (other != null)
                    other.OutReferences.Remove(r);
            }

            this.InReferences.Clear();

            foreach (var r in this.OutReferences)
            {
                var other = r.Target.Object;
                if (other != null)
                    other.InReferences.Remove(r);
            }

            this.OutReferences.Clear();
        }

        internal void SortReferences()
        {
            this.InReferences.Sort(_Sort_IncomingReferences);
            this.OutReferences.Sort(_Sort_OutgoingReferences);

            for (int i = 0; i < this.InReferences.Count; i++)
                this.InReferences[i].Target.RefIndex = i;

            for (int i = 0; i < this.OutReferences.Count; i++)
                this.OutReferences[i].Source.RefIndex = i;
        }

        private static int _Sort_IncomingReferences(IDAReference a, IDAReference b)
        {
            int c = a.Target.Address.CompareTo(b.Target.Address);
            if (c != 0)
                return c;

            c = a.Source.Address.CompareTo(b.Source.Address);
            if (c != 0)
                return c;

            return 0;
        }

        private static int _Sort_OutgoingReferences(IDAReference a, IDAReference b)
        {
            int c = a.Source.Address.CompareTo(b.Source.Address);
            if (c != 0)
                return c;

            c = a.Target.Address.CompareTo(b.Target.Address);
            if (c != 0)
                return c;

            return 0;
        }

        internal int Size
        {
            get
            {
                return (this.End - this.Begin).ToInt32();
            }
            set
            {
#if DEBUG
                if (value < 0 || this.Version.IsLocked)
                    throw new InvalidOperationException();
#endif
                this.End = this.Begin + value;
            }
        }

        internal void OnAssignedMatch(bool first)
        {
            uint guid = first ? this.Guid : this.Match.First.Guid;
            guid &= 0xFFFFF;

            if (this.InReferences.Count != 0)
            {
                ulong mask = guid;
                mask <<= 20;
                foreach (var t in this.InReferences)
                {
#if DEBUG
                    if ((t.Data & mask) != 0) throw new InvalidOperationException();
#endif
                    t.Data |= mask;

                    if (!IDAMigrate.SeparateRefPass)
                    {
                        var tcomp = t.Source.Object.Comparison;
                        if (tcomp != null)
                            tcomp.Invalidate(IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef, false);
                    }
                }
            }

            if (this.OutReferences.Count != 0)
            {
                ulong mask = guid;
                foreach (var t in this.OutReferences)
                {
#if DEBUG
                    if ((t.Data & mask) != 0) throw new InvalidOperationException();
#endif
                    t.Data |= mask;

                    if (!IDAMigrate.SeparateRefPass)
                    {
                        var tcomp = t.Target.Object.Comparison;
                        if (tcomp != null)
                            tcomp.Invalidate(IDAObjectComparisonData.IDAObjectComparisonTypes.InRef, false);
                    }
                }
            }

            if (!IDAMigrate.SeparateRefPass && this.Comparison != null)
            {
                this.Comparison.Invalidate(IDAObjectComparisonData.IDAObjectComparisonTypes.InRef, true);
                this.Comparison.Invalidate(IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef, true);
                //this.Comparison.Refresh();
            }
        }

        public override string ToString()
        {
            var n = this.Name;
            if (!string.IsNullOrEmpty(n))
                return n;
            return this.Type.ToString() + " " + this.Begin.ToString();
        }
    }

    internal enum IDAObjectTypes : int
    {
        Unknown,
        Function,
        Loc,
        VTable,
        Struct,
        Global,
    }

    [Flags]
    internal enum IDAObjectFlags : uint
    {
        None = 0,

        CreatedFunctionFromLoc = 1,

        TriedSlowScan = 2,

        InRangeTodo = 4,
    }

    internal sealed class IDAReference
    {
        internal IDAReferencePoint Source;
        internal IDAReferencePoint Target;
        internal ulong Data;

        public override string ToString()
        {
            return this.Source.ToString() + " -> " + this.Target.ToString();
        }
    }

    internal struct IDAReferencePoint
    {
        internal IDAObject Object;
        internal Offset Address;
        internal Offset Offset;
        internal int OffsetIndex;
        internal int RefIndex;

        internal static IDAReferencePoint Create(Offset of, IDAVersion p)
        {
            var r = new IDAReferencePoint();
            r.Address = of;
            r.Object = p.GetObject(of) ?? p.FindWithinObject(of, p.WithinObjectLookupCache);

            if (r.Object != null)
            {
                r.Offset = of - r.Object.Begin;
                switch (r.Object.Type)
                {
                    case IDAObjectTypes.Function:
                    case IDAObjectTypes.Loc:
                        {
                            int ix = 0xFFFF;
                            var ls = r.Object.Asm.Offsets;
                            if (ls != null)
                            {
                                var cur = r.Offset;
                                for (int i = 0; i < ls.Count; i++)
                                {
                                    var zof = ls[i];
                                    if (zof == cur)
                                    {
                                        ix = i;
                                        break;
                                    }
                                }
                            }
                            r.OffsetIndex = Math.Min(0xFFFF, ix);
                        }
                        break;

                    case IDAObjectTypes.Global:
                    case IDAObjectTypes.Unknown:
                        r.OffsetIndex = r.Offset.ToInt32();
                        break;

                    case IDAObjectTypes.VTable:
                        r.OffsetIndex = r.Offset.ToInt32() / (p.Migration.Architecture == ArchitectureTypes.x86_64 ? 8 : 4);
                        break;

                    case IDAObjectTypes.Struct:
                        r.OffsetIndex = r.Offset.ToInt32();
                        break;

                    default:
                        throw new NotSupportedException();
                }
            }

            return r;
        }

        public override string ToString()
        {
            var bld = new StringBuilder(64);
            var o = this.Object;
            if (o != null)
            {
                bld.Append(o.ToString());
                /*if (this.Offset != 0)
                {
                    bld.Append('+');
                    bld.Append(this.Offset.ToString());
                }
                else if (this.OffsetIndex >= 0)
                {
                    bld.Append('{');
                    bld.Append(((Offset)this.OffsetIndex).ToString());
                    bld.Append('}');
                }*/
            }
            else
                bld.Append(this.Address.ToString());

            bld.Append('[');
            bld.Append(this.RefIndex.ToString());
            bld.Append(']');

            return bld.ToString();
        }
    }

    internal sealed class IDAObjMatch
    {
        internal IDAObject First;
        internal IDAObject Second;

        public override string ToString()
        {
            return "[" + this.First.ToString() + " = " + this.Second.ToString() + "]";
        }
    }

    internal struct IDAObjectAsm
    {
        internal ulong Hash;
        internal ulong[] Data;
        internal IReadOnlyList<Offset> Offsets;
        internal Offset? StopAt;

        public override string ToString()
        {
            return "[" + this.Data.Length + "] 0x" + this.Hash.ToString("X8");
        }

        public override int GetHashCode()
        {
            return this.Hash.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var o = (IDAObjectAsm)obj;
            int c = this.Data.Length;
            if (c != o.Data.Length)
                return false;

            for (int i = 0; i < c; i++)
            {
                if (this.Data[i] != o.Data[i])
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Assembly code comparison builder.
    /// </summary>
    internal sealed class IDAExportAsmBuilder
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IDAExportAsmBuilder"/> class.
        /// </summary>
        internal IDAExportAsmBuilder()
        {

        }

        /// <summary>
        /// The lines in this builder.
        /// </summary>
        private readonly List<AssemblyCodeLine> lines = new List<AssemblyCodeLine>();

        /// <summary>
        /// Gets or sets the lines of builder.
        /// </summary>
        /// <value>
        /// The lines.
        /// </value>
        internal IReadOnlyList<AssemblyCodeLine> Lines
        {
            get
            {
                return this.lines;
            }
            set
            {
                var ln = value.ToList(); // Value may be self this is why we must copy.
                this.lines.Clear();
                this.lines.AddRange(ln);
                this.Cached = null;
            }
        }

        /// <summary>
        /// The cached value.
        /// </summary>
        private IDAObjectAsm? Cached = null;

        /// <summary>
        /// One line in the assembly builder.
        /// </summary>
        internal sealed class AssemblyCodeLine
        {
            internal Offset Address;
            internal AssemblyLineFlags Flags;
            internal BuildData Data;
            internal ulong? BuiltSimple;
            internal List<ulong> BuiltComplex;

            internal sealed class BuildData
            {
                internal string Full;
                internal string Instruction;
                internal string Comment;
                internal List<AssemblyCodeArg> Arguments;
            }

            internal void Build(IDAExportAsmBuilder builder)
            {
                if (this.BuiltSimple.HasValue || this.BuiltComplex != null)
                    return;

                this.Flags |= AssemblyLineFlags.Built;
                List<ulong> data = new List<ulong>(2);
                List<Offset> addr = new List<Offset>(2);
                builder.BuildLine(this, data, addr);

                this.Data = null;

                if (data.Count == 0)
                {
                    this.BuiltSimple = 0;
                    return;
                }

                if (data.Count == 1)
                {
                    this.BuiltSimple = data[0];
                    return;
                }

                this.BuiltComplex = data;
            }
        }

        /// <summary>
        /// Options.
        /// </summary>
        [Flags]
        internal enum AssemblyLineFlags : uint
        {
            None = 0,

            Skip = 1,

            Stop = 2,

            BadRefSource = 4,

            SkipButIsOkInstructionForExecution = 8,

            Built = 0x10,
        }

        /// <summary>
        /// One argument of an instruction.
        /// </summary>
        internal sealed class AssemblyCodeArg
        {
            internal string Text;
            internal long[] Data;
        }

        /// <summary>
        /// Builds the result.
        /// </summary>
        /// <param name="p">The version.</param>
        /// <returns></returns>
        internal IDAObjectAsm Build(IDAVersion p)
        {
            if (this.Cached.HasValue)
                return this.Cached.Value;

            List<ulong> data = new List<ulong>();
            List<Offset> address = new List<Offset>();
            Offset? stop = null;

            for (int i = 0; i < this.Lines.Count; i++)
            {
                var ln = this.Lines[i];
                if ((ln.Flags & AssemblyLineFlags.Stop) != AssemblyLineFlags.None)
                {
                    stop = ln.Address;
                    break;
                }

                if ((ln.Flags & AssemblyLineFlags.Skip) != AssemblyLineFlags.None)
                    continue;

                if ((ln.Flags & AssemblyLineFlags.Built) == AssemblyLineFlags.None)
                    ln.Build(this);

                if (ln.BuiltSimple.HasValue)
                {
                    data.Add(ln.BuiltSimple.Value);
                    address.Add(ln.Address);
                    continue;
                }

                data.AddRange(ln.BuiltComplex);
                for (int j = 0; j < ln.BuiltComplex.Count; j++)
                    address.Add(ln.Address);
            }

            //data.TrimExcess();
            address.TrimExcess();

            var r = new IDAObjectAsm();
            r.Data = data.ToArray();
            r.Offsets = address;
            r.Hash = Utility.Calculate64BitHashCode(data);
            if (stop.HasValue)
                r.StopAt = p.AdjustOffset(stop.Value);
            this.Cached = r;
            return r;
        }

        /// <summary>
        /// Builds the line.
        /// </summary>
        /// <param name="ln">The line.</param>
        /// <param name="resultData">The result data.</param>
        /// <param name="resultAddress">The result address.</param>
        /// <exception cref="System.ArgumentException">
        /// Ran out of IDs for instructions!
        /// or
        /// </exception>
        private void BuildLine(AssemblyCodeLine ln, List<ulong> resultData, List<Offset> resultAddress)
        {
            ulong p = 0;
            int argc = ln.Data.Arguments != null ? ln.Data.Arguments.Count : 0;

            // Pack instruction.
            {
                string insForPack = ln.Data.Instruction;
                if (insForPack.StartsWith("db ") || insForPack.StartsWith("dw ") || insForPack.StartsWith("dd ") || insForPack.StartsWith("dq "))
                    insForPack = insForPack.Substring(0, 2);
                else if(insForPack.StartsWith("align "))
                    insForPack = insForPack.Substring(0, 5);
                /*else if (insForPack.Contains(" ") && !insForPack.StartsWith("lock ") && !insForPack.StartsWith("rep ") && !insForPack.StartsWith("repne "))
                {
                    IDAListener.Write(insForPack);
                    throw new FormatException(insForPack);
                }*/
                for (int i = 0; i < argc; i++)
                {
                    var t = ln.Data.Arguments[i];
                    long type = t.Data[1];
                    insForPack = insForPack + "_" + type.ToString();
                }
                uint id = GetInstructionId(insForPack);
                if (id > 0xFFFF)
                    throw new ArgumentException("Ran out of IDs for instructions!"); // If this ever happens increase the number here and also increase the shift number below.

                p |= id;
            }

            // Pack arguments.
            {
                int shift = 16;
                for (int i = 0; i < argc; i++)
                {
                    // This is ok and allowed, can't do much about it. Very low chance that this would actually cause false-positives.
                    if (shift >= 64)
                        break;

                    var t = ln.Data.Arguments[i];
                    ulong data = 0;
                    int asz = 0;
                    BuildArg(ln, t, ref data, ref asz);

#if DEBUG
                    ulong mask = 0;
                    if (asz == 64)
                        mask = ulong.MaxValue;
                    else if (asz > 0)
                    {
                        mask = 1;
                        mask <<= asz;
                        mask--;
                    }
                    if ((data & mask) != data)
                        throw new ArgumentException();
#endif

                    data <<= shift;
                    p |= data;
                    shift += asz;
                }
            }

            resultData.Add(p);
            resultAddress.Add(ln.Address);

            string cmt = ln.Data.Comment;
            if (!string.IsNullOrEmpty(cmt))
            {
                if (cmt.Length >= 2 && cmt[0] == '"' && cmt[cmt.Length - 1] == '"')
                {
                    string cmt_str = cmt.Substring(1, cmt.Length - 2);
                    ulong p_cmt = Utility.Calculate64BitHashCode(cmt_str);
                    resultData.Add(p_cmt);
                    resultAddress.Add(ln.Address);
                }
                else if (cmt.Contains("`vftable'"))
                {
                    ulong p_cmt = Utility.Calculate64BitHashCode(cmt);
                    resultData.Add(p_cmt);
                    resultAddress.Add(ln.Address);
                }
            }
        }

        private void BuildArg(AssemblyCodeLine ln, AssemblyCodeArg arg, ref ulong argResult, ref int argSize)
        {
            // [0] op.n
            // [1] op.type
            // [2] op.dtyp
            // [3] op.reg
            // [4] op.phrase
            // [5] op.value
            // [6] op.addr
            // [7] op.flags (8)
            // [8] op.specflag1
            // [9] op.specflag2
            // [10] op.specflag3
            // [11] op.specflag4
            // [12] op.specval

            string str = arg.Text;
            ulong type = unchecked((ulong)arg.Data[1]);
            ulong p = 0;
            int asz = 0;

            switch (type)
            {
                // Register: eax
                case 1:
                    {
                        ulong regId = 0;
                        if (!ParseRegister(str, ref regId))
                            throw new NotSupportedException("Invalid register: " + str);

                        if (regId > 0xFF)
                            throw new ArgumentOutOfRangeException();

                        p = regId;
                        asz = 8;
                    }
                    break;

                // Memory location: [0x1234]
                case 2:
                    {
                        ulong sz = 0;
                        ulong seg = 0;

                        ParseSizeSpecifier(ref str, ref sz);
                        ParseSegmentSpecifier(ref str, ref seg);

                        //long offset = ida[?];

                        if (sz > 0xF || seg > 0xF)
                            throw new ArgumentOutOfRangeException();

                        // Sometimes IDA puts size specifier and sometimes not, for example:
                        // movss xmm8, cs:dword_1234
                        // movss xmm8, dword ptr cs:abc.field
                        // Above are two completely equivalent bytes. For this reason we don't track the size here.
                        sz = 0;

                        seg <<= 4;
                        p = seg | sz;
                        asz = 8;
                    }
                    break;

                // Memory location: [rax(+rdx(*8))]
                case 3:
                    {
                        ulong sz = 0;
                        ulong seg = 0;

                        ParseSizeSpecifier(ref str, ref sz);
                        ParseSegmentSpecifier(ref str, ref seg);

                        if (sz > 0xF || seg > 0xF)
                            throw new ArgumentOutOfRangeException();

                        //if (str.Length != 0)
                        {
                            if (str[0] != '[' || str[str.Length - 1] != ']')
                                throw new FormatException();
                        }
                        str = str.Substring(1, str.Length - 2).Trim();

                        // Possible syntax:
                        // rax
                        // rax+rdx
                        // rax+rdx*8
                        // more?

                        List<ulong> all = new List<ulong>(4);
                        ulong op = 0;
                        ulong lastReg = 0;
                        while (str.Length != 0)
                        {
                            ulong nextop = 0;
                            int ix = str.IndexOfAny(new[] { '+', '*', '-', '/' });
                            if (ix < 0)
                                ix = str.Length;
                            else
                            {
                                switch (str[ix])
                                {
                                    case '+': nextop = 0; break;
                                    case '-': nextop = 1; break;
                                    case '*': nextop = 2; break;
                                    case '/': nextop = 3; break;
                                    default: throw new NotSupportedException();
                                }
                            }

                            string thing = str.Substring(0, ix).Trim();
                            str = ix == str.Length ? "" : str.Substring(ix + 1).Trim();

                            ulong tp = 0;
                            ulong regId = 0;
                            if (ParseRegister(thing, ref regId))
                            {
                                tp = regId;
                                lastReg = tp;
                            }
                            else
                            {
                                ulong val = 0;
                                if (thing[thing.Length - 1] == 'h')
                                {
                                    long dval = Utility.ParseInt64ExactFast(thing.Substring(0, thing.Length - 1), true);
                                    val = unchecked((ulong)dval);
                                }
                                else if (thing.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!Utility.TryParseUInt64(thing, out val))
                                        throw new FormatException();
                                }
                                else
                                {
                                    // If this is exception then check what thing is here.
                                    long dval = Utility.ParseInt64ExactFast(thing, false);
                                    val = unchecked((ulong)dval);
                                }

                                // If number is too big (which is possible) then ignore upper half, we lose some info and it would
                                // cause some false-positive match 0x10004 == 0x4 but this is highly unlikely to cause too many issues.
                                val &= 0xFFFF;

                                // rip+ is location specific
                                if (lastReg >= 81 && lastReg <= 85)
                                    val = 0;

                                tp = val;
                            }
                            op <<= 17;
                            tp |= op;
                            if (regId != 0)
                                tp |= 0x10000;
                            op = nextop;
                            all.Add(tp);
                        }

                        p = sz;
                        int shift = 4;
                        if (seg != 0)
                        {
                            seg <<= shift;
                            p |= seg;
                            shift += 4;
                        }

                        foreach (var u in all)
                        {
                            ulong x = u;
                            if (shift >= 64)
                                break;
                            x <<= shift;
                            p |= x;
                            shift += 16 + 2 + 1; // 16 + 2 bit to say operator + 1 bit to say if it's register or value
                        }

                        asz = Math.Min(64, shift);
                    }
                    break;

                // Memory location: [rax(+rdx(*8))+0x10]
                // fs:[rax+39h]
                case 4:
                    {
                        ulong sz = 0;
                        ulong seg = 0;

                        ParseSizeSpecifier(ref str, ref sz);
                        ParseSegmentSpecifier(ref str, ref seg);

                        if (sz > 0xF || seg > 0xF)
                            throw new ArgumentOutOfRangeException();

                        //if (str.Length != 0)
                        {
                            if (str[0] != '[' || str[str.Length - 1] != ']')
                            {
                                //throw new FormatException();
                                break;
                            }
                        }

                        str = str.Substring(1, str.Length - 2).Trim();

                        // Possible syntax:
                        // rax+0x10
                        // rax+rdx+0x10
                        // rax+rdx*8+0x10
                        // more?

                        List<ulong> all = new List<ulong>(4);
                        ulong op = 0;
                        ulong lastReg = 0;
                        while (str.Length != 0)
                        {
                            ulong nextop = 0;
                            int ix = str.IndexOfAny(new[] { '+', '*', '-', '/' });
                            if (ix < 0)
                                ix = str.Length;
                            else
                            {
                                switch (str[ix])
                                {
                                    case '+': nextop = 0; break;
                                    case '-': nextop = 1; break;
                                    case '*': nextop = 2; break;
                                    case '/': nextop = 3; break;
                                    default: throw new NotSupportedException(arg.Text);
                                }
                            }

                            string thing = str.Substring(0, ix).Trim();
                            str = ix == str.Length ? "" : str.Substring(ix + 1).Trim();

                            ulong tp = 0;
                            ulong regId = 0;
                            if (ParseRegister(thing, ref regId))
                            {
                                tp = regId;
                                lastReg = tp;
                            }
                            else
                            {
                                ulong val = 0;
                                if (thing[thing.Length - 1] == 'h')
                                {
                                    long dval = Utility.ParseInt64ExactFast(thing.Substring(0, thing.Length - 1), true);
                                    val = unchecked((ulong)dval);
                                }
                                else if (thing.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!Utility.TryParseUInt64(thing, out val))
                                        throw new FormatException();
                                }
                                else
                                {
                                    // If this is exception then check what thing is here.
                                    long dval = Utility.ParseInt64ExactFast(thing, false);
                                    val = unchecked((ulong)dval);
                                }

                                // If number is too big (which is possible) then ignore upper half, we lose some info and it would
                                // cause some false-positive match 0x10004 == 0x4 but this is highly unlikely to cause too many issues.
                                val &= 0xFFFF;

                                // rip+ is location specific
                                if (lastReg >= 81 && lastReg <= 85)
                                    val = 0;

                                tp = val;
                            }
                            op <<= 17;
                            tp |= op;
                            if (regId != 0)
                                tp |= 0x10000;
                            op = nextop;
                            all.Add(tp);
                        }

                        p = sz;
                        int shift = 4;
                        if (seg != 0)
                        {
                            seg <<= shift;
                            p |= seg;
                            shift += 4;
                        }

                        foreach (var u in all)
                        {
                            ulong x = u;
                            if (shift >= 64)
                                break;
                            x <<= shift;
                            p |= x;
                            shift += 16 + 2 + 1; // 16 + 2 bit to say operator + 1 bit to say if it's register or value
                        }

                        asz = Math.Min(64, shift);
                    }
                    break;

                // Constant: 0x1234
                case 5:
                    {
                        long val = arg.Data[5];
                        ulong ux = unchecked((ulong)val);
                        int usz = 0;
                        ulong packed = PackValue(ux, ref usz);
                        p = packed;
                        asz = usz;
                    }
                    break;

                // Far address: ?
                case 6:
                    throw new NotSupportedException();

                // Near address: ...
                // This can be anything that fits in 32 bit integer, for example both call sub_123989324 and jz 0xC are both near address.
                case 7:
                    {
                        //long addr = arg.Data[6];
                        // This near address thing can't be used because of "loc" sections that are not translated to functions yet will cause it to say "no" when it should be "yes".
                        int? isNear = null; // nearAddressTranslatorFunc(address, addr);

                        // We could track the near address since jz 0xC and stuff are same within different versions.
                        // BUT!
                        // We do not because "align" again can mess this up without there actually being any changes.
                        /*if(isNear.HasValue)
                        {
                            long uxNear = isNear.Value;
                            ulong ux = unchecked((ulong)uxNear);
                            ux &= 0x7FFF;
                            ux <<= 1;
                            ux |= 1;
                            p = ux;
                            asz = 16;
                        }
                        else
                        {
                            p = 0;
                            asz = 1;
                        }*/

                        p = 0;
                        if (isNear.HasValue)
                            p = 1;
                        asz = 1;
                    }
                    break;

                // Unknown
                case 8:
                    throw new NotSupportedException();

                // FPU:
                // st
                // st(4)
                case 11:
                    {
                        ulong regId = 0;
                        if (!ParseRegister(str, ref regId))
                            throw new FormatException("Invalid register: " + str);

                        p = regId;
                        asz = 8;
                    }
                    break;

                    // 12: register for mm?

                    // register for xmm ?
                case 13:
                    {
                        ulong regId = 0;
                        if (!ParseRegister(str, ref regId))
                            throw new FormatException("Invalid register: " + str);

                        p = regId;
                        asz = 8;
                    }
                    break;

                // register for ymm ?
                case 14:
                    {
                        ulong regId = 0;
                        if (!ParseRegister(str, ref regId))
                            throw new FormatException("Invalid register: " + str);

                        p = regId;
                        asz = 8;
                    }
                    break;

                default:
                    throw new NotSupportedException();
            }

            argResult = p;
            argSize = asz;
        }

        /// <summary>
        /// Appends the specified builder into this builder.
        /// </summary>
        /// <param name="builder">The other builder.</param>
        internal void Append(IDAExportAsmBuilder builder)
        {
            this.lines.AddRange(builder.Lines);
            this.Cached = null;
        }

        /// <summary>
        /// Appends the instruction.
        /// </summary>
        /// <param name="address">The address of instruction (with base address).</param>
        /// <param name="offset">The offset within function.</param>
        /// <param name="spl">The split.</param>
        /// <param name="index">The index in split we should start reading at.</param>
        /// <param name="translator">The translator function for near address.</param>
        internal void Append(Offset address, IReadOnlyList<string> spl, int index)
        {
            string full = spl[index++];
            string clean_instruction = spl[index++];
            string comment = "";

            {
                int ix = full.IndexOf(';');
                if (ix >= 0)
                {
                    comment = full.Substring(ix + 1).Trim();
                    full = full.Substring(0, ix);
                }
                full = full.Trim();
            }

            AssemblyLineFlags flags = AssemblyLineFlags.None;

            if (comment.Length != 0)
            {
                if (comment == "switch jump")
                    flags |= AssemblyLineFlags.BadRefSource;
                else if (comment.EndsWith("for switch statement"))
                    flags |= AssemblyLineFlags.Skip | AssemblyLineFlags.Stop;

                // This can't be good, it has false positives.
                //else if (comment.StartsWith("jumptable") && comment.Contains("case")) flags |= AssemblyLineFlags.Skip | AssemblyLineFlags.Stop;
            }

            // Few special cases.
            bool skipArg = false;
            if (full.Length != 0)
            {
                char fch = full[0];

                // Skip align because these may differ based on where the code ends up. Compiler optimizer may choose to put align here or not and functionality does not change!
                if (fch == 'a')
                {
                    if (clean_instruction == "align" || full.StartsWith("align "))
                    {
                        flags |= AssemblyLineFlags.Skip;
                        skipArg = true;
                    }
                }
                else if (fch == 'd')
                {
                    // Skip data because it is difficult to handle.
                    if (clean_instruction == "db" || full.StartsWith("db ") || clean_instruction == "dw" || full.StartsWith("dw ") || clean_instruction == "dd" || full.StartsWith("dd ") || clean_instruction == "dq" || full.StartsWith("dq "))
                    {
                        flags |= AssemblyLineFlags.Skip;
                        skipArg = true;
                    }
                }
                // Don't skip nop because it is valid to have it.
                //else if (fch == 'n' && clean_instruction == "nop") flags |= AssemblyLineFlags.Skip | okinstructionforexecutionflag;
            }

            string instruction;
            List<Tuple<string, long[]>> args;

            // Extract instruction and arguments.
            {
                args = new List<Tuple<string, long[]>>(8);

                int insend = Math.Min(full.Length, 7);
                bool badin = false;
                while (insend < full.Length && full[insend] != ' ')
                {
                    char ch = full[insend];
                    if (ch == ',' || ch == '[')
                    {
                        badin = true;
                        break;
                    }
                    insend++;
                }

                if (badin)
                {
                    while (insend > 0 && full[insend] != ' ')
                        insend--;
                }

                instruction = full.Substring(0, insend).Trim();
                string fullr = full;
                if (instruction == "lock rep" || instruction == "lock repne")
                {
                    string rm = full.Substring(insend).Trim();
                    int rmix = rm.IndexOf(' ');
                    if (rmix < 0)
                        rmix = rm.Length;

                    instruction = instruction + " " + rm.Substring(0, rmix);
                    insend = rmix;
                    fullr = rm;
                }

                if (!skipArg)
                {
                    string remain = fullr.Substring(insend).Trim();

                    var spla = IDAMigrate._Load_FastSplit2(remain, ',');
                    for (int i = 0; i < spla.Count; i++)
                        spla[i] = spla[i].Trim();
                    int splaIndex = 0;

                    List<long[]> ida = new List<long[]>(8);
                    string[] spl2 = new string[13];
                    while (index < spl.Count)
                    {
                        //var spl2 = IDAMigrate._Load_FastSplit3(spl[index++], ' ');
                        IDAMigrate._Fill_FastSplit(spl[index++], spl2, ' ');
                        long[] ia = new long[spl2.Length];
                        for (int i = 0; i < spl2.Length; i++)
                        {
                            string sp = spl2[i];
                            if (sp.Length == 0)
                            {
                                ia[i] = i == 7 ? 8 : 0;
                                continue;
                            }
                            long x = 0;
                            bool neg = false;
                            if (sp[0] == '-')
                            {
                                neg = true;
                                sp = sp.Substring(1);
                            }
                            x = Utility.ParseInt64ExactFast(sp, true);
                            if (neg)
                                x = -x;
                            ia[i] = x;
                        }
                        ida.Add(ia);
                    }
                    int idaIndex = 0;

                    if (spla.Count != ida.Count)
                    {
                        // arguments = spla
                        // spl = ida
                        if (spla.Count < ida.Count && IsOpcodeWithSkipArgument(instruction))
                        {
                            if (spla.Count > 0 && spla.Count == ida.Count - 1)
                                idaIndex++;
                            else
                                splaIndex = int.MaxValue;
                        }
                        else if (ida.Count >= 1 && spla.Count == ida.Count - 1 && IsOpcodeWithImpliedFirstArgument(instruction))
                        {
                            idaIndex++;
                        }
                        else if (ida.Count >= 1 && spla.Count == ida.Count - 1 && IsOpcodeWithImpliedLastArgument(instruction))
                        {
                            ida.RemoveAt(ida.Count - 1);
                        }
                        else if ((IsLoop(instruction) || instruction.StartsWith("jrcxz")) && spla.Count == 1 && IsMemoryLocation(spla[0]))
                        {
                            ida.RemoveAll(q => q[0] == 1);
                            if (ida.Count != spla.Count)
                                throw new FormatException();
                        }
                        else if (IsLoop(instruction) && spla.Count == 1 && ida.Count == 2)
                        {
                            splaIndex = int.MaxValue;
                        }
                        else if (spla.Count > ida.Count)
                        {
#if DEBUG_ASM_ERRORS
                            // This should not happen?
                            throw new FormatException(full);
#else
                            splaIndex = int.MaxValue;
#endif
                        }
                        else
                        {
#if DEBUG_ASM_ERRORS
                            throw new FormatException(full);
#else
                            splaIndex = int.MaxValue;
#endif
                        }
                    }

                    while (splaIndex < spla.Count)
                    {
                        args.Add(new Tuple<string, long[]>(spla[splaIndex], ida[idaIndex]));
                        splaIndex++;
                        idaIndex++;
                    }
                }
            }

            List<AssemblyCodeArg> argsls = null;
            if (args.Count != 0)
            {
                argsls = new List<AssemblyCodeArg>(args.Count);
                foreach (var x in args)
                {
                    var ca = new AssemblyCodeArg();
                    ca.Text = x.Item1;
                    ca.Data = x.Item2;
                    argsls.Add(ca);
                }
            }

            var ln = new AssemblyCodeLine();
            ln.Address = address;
            ln.Data = new AssemblyCodeLine.BuildData()
            {
                Comment = comment,
                Full = full,
                Instruction = instruction,
                Arguments = argsls,
            };
            ln.Flags = flags;
            this.lines.Add(ln);
            this.Cached = null;

            ln.Build(this);
        }

        /// <summary>
        /// Packs the value. This will try to make the value in a way where we identify differences with as little bits as possible.
        /// </summary>
        /// <param name="x">The value.</param>
        /// <param name="sz">The result bit size.</param>
        /// <returns></returns>
        private static ulong PackValue(ulong x, ref int sz)
        {
            int bitCount = CountBits(x);
            int highestBit = HighestBit(x);

            // Set 16 as size of value.
            {
                sz = 16;
                ulong p = 0;

                // Upper 6 bits are used to show highest bit index, this will help us differentiate between flags constants.
                {
                    ulong highestBitIndex = (ulong)Math.Max(0, highestBit);
                    highestBitIndex <<= (sz - 6);
                    p |= highestBitIndex;
                }

                // Next 3 bits are used to show bit count.
                {
                    if (bitCount <= 2)
                    {

                    }
                    else if (bitCount <= 4)
                        bitCount = 3;
                    else if (bitCount <= 8)
                        bitCount = 4;
                    else if (bitCount <= 16)
                        bitCount = 5;
                    else if (bitCount <= 32)
                        bitCount = 6;
                    else // if (bitCount <= 64)
                        bitCount = 7;

                    ulong bitPow = (ulong)bitCount;
                    bitPow <<= (sz - 9);
                    p |= bitPow;
                }

                // Final bits are showing lower mask of value.
                {
                    ulong lower = x;
                    ulong mask = 0;
                    int left = sz - 9;
                    if (left > 0)
                    {
                        mask = 1;
                        mask <<= left;
                        mask--;
                    }

                    lower &= mask;
                    p |= lower;
                }

                return p;
            }
        }

        /// <summary>
        /// Counts the bits.
        /// </summary>
        /// <param name="x">The value.</param>
        /// <returns></returns>
        private static int CountBits(ulong x)
        {
            int count = 0;
            for (int i = 0; i < 64; i++)
            {
                ulong mask = (ulong)1;
                mask <<= i;
                if ((x & mask) != 0)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Gets the highest bit index that is set.
        /// </summary>
        /// <param name="x">The value.</param>
        /// <returns></returns>
        private static int HighestBit(ulong x)
        {
            int highest = -1;
            for (int i = 0; i < 64; i++)
            {
                ulong mask = (ulong)1;
                mask <<= i;
                if ((x & mask) != 0)
                    highest = i;
            }
            return highest;
        }

        /// <summary>
        /// Parses the register identifier.
        /// </summary>
        /// <param name="str">The string.</param>
        /// <param name="regId">The register identifier.</param>
        /// <returns></returns>
        private static bool ParseRegister(string str, ref ulong regId)
        {
            switch (str)
            {
                case "al": regId = 1; break;
                case "add ah":
                case "ah": regId = 2; break;
                case "ax": regId = 3; break;
                case "eax": regId = 4; break;
                case "rax": regId = 5; break;

                case "bl": regId = 11; break;
                case "add bh":
                case "bh": regId = 12; break;
                case "bx": regId = 13; break;
                case "ebx": regId = 14; break;
                case "rbx": regId = 15; break;

                case "cl": regId = 21; break;
                case "add ch":
                case "ch": regId = 22; break;
                case "cx": regId = 23; break;
                case "ecx": regId = 24; break;
                case "rcx": regId = 25; break;

                case "dl": regId = 31; break;
                case "add dh":
                case "dh": regId = 32; break;
                case "dx": regId = 33; break;
                case "edx": regId = 34; break;
                case "rdx": regId = 35; break;

                case "sil": regId = 41; break;
                case "si": regId = 43; break;
                case "esi": regId = 44; break;
                case "rsi": regId = 45; break;

                case "dil": regId = 51; break;
                case "di": regId = 53; break;
                case "edi": regId = 54; break;
                case "rdi": regId = 55; break;

                case "bpl": regId = 61; break;
                case "bp": regId = 63; break;
                case "ebp": regId = 64; break;
                case "rbp": regId = 65; break;

                case "spl": regId = 71; break;
                case "sp": regId = 73; break;
                case "esp": regId = 74; break;
                case "rsp": regId = 75; break;

                case "ipl": regId = 81; break;
                case "ip": regId = 83; break;
                case "eip": regId = 84; break;
                case "rip": regId = 85; break;

                case "r8": regId = 95; break;
                case "r8d": regId = 94; break;
                case "r8w": regId = 93; break;
                case "r8b": regId = 91; break;

                case "r9": regId = 105; break;
                case "r9d": regId = 104; break;
                case "r9w": regId = 103; break;
                case "r9b": regId = 101; break;

                case "r10": regId = 115; break;
                case "r10d": regId = 114; break;
                case "r10w": regId = 113; break;
                case "r10b": regId = 111; break;

                case "r11": regId = 125; break;
                case "r11d": regId = 124; break;
                case "r11w": regId = 123; break;
                case "r11b": regId = 121; break;

                case "r12": regId = 135; break;
                case "r12d": regId = 134; break;
                case "r12w": regId = 133; break;
                case "r12b": regId = 131; break;

                case "r13": regId = 145; break;
                case "r13d": regId = 144; break;
                case "r13w": regId = 143; break;
                case "r13b": regId = 141; break;

                case "r14": regId = 155; break;
                case "r14d": regId = 154; break;
                case "r14w": regId = 153; break;
                case "r14b": regId = 151; break;

                case "r15": regId = 165; break;
                case "r15d": regId = 164; break;
                case "r15w": regId = 163; break;
                case "r15b": regId = 161; break;

                case "xmm0": regId = 170; break;
                case "xmm1": regId = 171; break;
                case "xmm2": regId = 172; break;
                case "xmm3": regId = 173; break;
                case "xmm4": regId = 174; break;
                case "xmm5": regId = 175; break;
                case "xmm6": regId = 176; break;
                case "xmm7": regId = 177; break;
                case "xmm8": regId = 178; break;
                case "xmm9": regId = 179; break;
                case "xmm10": regId = 180; break;
                case "xmm11": regId = 181; break;
                case "xmm12": regId = 182; break;
                case "xmm13": regId = 183; break;
                case "xmm14": regId = 184; break;
                case "xmm15": regId = 185; break;

                case "ymm0": regId = 190; break;
                case "ymm1": regId = 191; break;
                case "ymm2": regId = 192; break;
                case "ymm3": regId = 193; break;
                case "ymm4": regId = 194; break;
                case "ymm5": regId = 195; break;
                case "ymm6": regId = 196; break;
                case "ymm7": regId = 197; break;
                case "ymm8": regId = 198; break;
                case "ymm9": regId = 199; break;
                case "ymm10": regId = 200; break;
                case "ymm11": regId = 201; break;
                case "ymm12": regId = 202; break;
                case "ymm13": regId = 203; break;
                case "ymm14": regId = 204; break;
                case "ymm15": regId = 205; break;

                case "mm0": regId = 210; break;
                case "mm1": regId = 211; break;
                case "mm2": regId = 212; break;
                case "mm3": regId = 213; break;
                case "mm4": regId = 214; break;
                case "mm5": regId = 215; break;
                case "mm6": regId = 216; break;
                case "mm7": regId = 217; break;
                case "mm8": regId = 218; break;
                case "mm9": regId = 219; break;
                case "mm10": regId = 220; break;
                case "mm11": regId = 221; break;
                case "mm12": regId = 222; break;
                case "mm13": regId = 223; break;
                case "mm14": regId = 224; break;
                case "mm15": regId = 225; break;

                case "st":
                case "st(0)": regId = 230; break;
                case "st(1)": regId = 231; break;
                case "st(2)": regId = 232; break;
                case "st(3)": regId = 233; break;
                case "st(4)": regId = 234; break;
                case "st(5)": regId = 235; break;
                case "st(6)": regId = 236; break;
                case "st(7)": regId = 237; break;

                case "gs": regId = 241; break;
                case "cs": regId = 242; break;
                case "ds": regId = 243; break;
                case "es": regId = 244; break;
                case "fs": regId = 245; break;
                case "ss": regId = 246; break;

                default:
                    {
                        if (str.StartsWith("near ptr"))
                        {
                            regId = 255;
                            break;
                        }

                        regId = 250;
                        break;
                        //return false; // Better just treat it as error code, no point to care about 1 register in all of binary assembly
                    }
            }

            return true;
        }

        /// <summary>
        /// Parses the size specifier.
        /// </summary>
        /// <param name="str">The string.</param>
        /// <param name="sz">The size identifier.</param>
        /// <returns></returns>
        private static bool ParseSizeSpecifier(ref string str, ref ulong sz)
        {
            if (str.Length == 0)
                return false;

            char f = str[0];

            if (str.Length >= 8)
            {
                char p = str[5];
                if (p == 'p')
                {
                    if (f == 'b' && StartsWithAndRemove(ref str, "byte ptr"))
                    {
                        sz = 1;
                        return true;
                    }
                    if (f == 'w' && StartsWithAndRemove(ref str, "word ptr"))
                    {
                        sz = 2;
                        return true;
                    }
                }
                else if (p == ' ')
                {
                    if (str[6] == 'p')
                    {
                        if (f == 'd' && StartsWithAndRemove(ref str, "dword ptr"))
                        {
                            sz = 3;
                            return true;
                        }
                        if (f == 'q' && StartsWithAndRemove(ref str, "qword ptr"))
                        {
                            sz = 4;
                            return true;
                        }
                        if (f == 'f' && StartsWithAndRemove(ref str, "fword ptr"))
                        {
                            sz = 8;
                            return true;
                        }
                        if (f == 't' && StartsWithAndRemove(ref str, "tbyte ptr"))
                        {
                            sz = 9;
                            return true;
                        }
                    }
                }
                else if (p == 'r')
                {
                    if (f == 'x' && StartsWithAndRemove(ref str, "xmmword ptr"))
                    {
                        sz = 5;
                        return true;
                    }
                    if (f == 'y' && StartsWithAndRemove(ref str, "ymmword ptr"))
                    {
                        sz = 6;
                        return true;
                    }
                }
                else if (p == 'd')
                {
                    if (f == 'm' && StartsWithAndRemove(ref str, "mmword ptr"))
                    {
                        sz = 7;
                        return true;
                    }
                }
            }

            if (f == 'l' && StartsWithAndRemove(ref str, "large"))
            {
                sz = 10;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Parses the segment specifier.
        /// </summary>
        /// <param name="str">The string.</param>
        /// <param name="seg">The segment identifier.</param>
        /// <returns></returns>
        private static bool ParseSegmentSpecifier(ref string str, ref ulong seg)
        {
            if (str.Length >= 3 && str[2] == ':')
            {
                if (StartsWithAndRemove(ref str, "cs:"))
                    seg = 1;
                else if (StartsWithAndRemove(ref str, "gs:"))
                    seg = 2;
                else if (StartsWithAndRemove(ref str, "fs:"))
                    seg = 3;
                else if (StartsWithAndRemove(ref str, "ds:"))
                    seg = 4;
                else if (StartsWithAndRemove(ref str, "es:"))
                    seg = 5;
                else
                    return false;
                return true;
            }

            return false;
        }

        /// <summary>
        /// See if string starts with something and if it does remove it (with trim after).
        /// </summary>
        /// <param name="str">The string.</param>
        /// <param name="what">The what.</param>
        /// <returns></returns>
        private static bool StartsWithAndRemove(ref string str, string what)
        {
            if (str.StartsWith(what))
            {
                str = str.Substring(what.Length).Trim();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Preprocesses the instruction.
        /// </summary>
        /// <param name="instruction">The instruction.</param>
        /// <returns></returns>
        private static string PreprocessInstruction(string instruction)
        {
            int ix = -1;
            if (instruction.StartsWith("rep"))
            {
                ix = instruction.IndexOf(' ');
                if (ix >= 0)
                    instruction = instruction.Substring(ix + 1).Trim();
            }

            return instruction;
        }

        /// <summary>
        /// Determines whether the specified instruction's arguments can be skipped.
        /// </summary>
        /// <param name="instruction">The instruction.</param>
        /// <returns></returns>
        private static bool IsOpcodeWithSkipArgument(string instruction)
        {
            instruction = PreprocessInstruction(instruction);
            return OpcodesWithSkipArgument.Contains(instruction);
        }

        private static bool IsOpcodeWithImpliedFirstArgument(string instruction)
        {
            instruction = PreprocessInstruction(instruction);
            return OpcodesWithImpliedFirstArgument.Contains(instruction);
        }

        private static bool IsOpcodeWithImpliedLastArgument(string instruction)
        {
            instruction = PreprocessInstruction(instruction);
            return OpcodesWithImpliedLastArgument.Contains(instruction);
        }

        /// <summary>
        /// Determines whether the specified instruction is loop.
        /// </summary>
        /// <param name="instruction">The instruction.</param>
        /// <returns></returns>
        private static bool IsLoop(string instruction)
        {
            //return instruction == "loop" || instruction == "loope" || instruction == "loopne";
            // There are many variants
            if (instruction.StartsWith("repne"))
                instruction = instruction.Substring(5).Trim();
            return instruction.StartsWith("loop");
        }

        /// <summary>
        /// The opcodes with implied last argument.
        /// </summary>
        private static readonly HashSet<string> OpcodesWithImpliedLastArgument = new HashSet<string>
        {
            "stos",
            "scas",
            "ins",
            "outs",
            "cmps",
            "movs",
        };

        /// <summary>
        /// The opcodes with implied first argument.
        /// </summary>
        private static readonly HashSet<string> OpcodesWithImpliedFirstArgument = new HashSet<string>
        {
            "lods",
            "imul",
            "idiv",
            "mul",
            "div",
            "sahf",
            "fild",
            "fld",
            "fistp",
            "fstp",
            "fist",
            "fst",
            "ficomp",
            "ficom",
            "fcomp",
            "fcom",
            "fadd",
            "faddr",
            "fiadd",
            "fiaddr",
            "fsub",
            "fsubr",
            "fisub",
            "fisubr",
            "fdiv",
            "fdivr",
            "fidiv",
            "fidivr",
            "fmul",
            "fmulr",
            "fimul",
            "fimulr",
            "fsin",
            "fcos",
            "ftan",
            "fbstp",
            "fbst",
        };

        /// <summary>
        /// The opcodes with skip argument.
        /// </summary>
        private static readonly HashSet<string> OpcodesWithSkipArgument = new HashSet<string>
        {
            "cdq",
            "cdqe",
            "cwd",
            "cwde",
            "cqo",
            "cmpsb",
            "cmpsw",
            "cmpsd",
            "cmpsq",
            "stosb",
            "stosw",
            "stosd",
            "stosq",
            "lodsq",
            "lodsd",
            "lodsw",
            "lodsb",
            "movsd",
            "movsb",
            "movsw",
            "movsq",
            "insd",
            "insb",
            "insw",
            "insq",
            "outsb",
            "outsd",
            "outsw",
            "outsq",
            "scasb",
            "scasw",
            "scasd",
            "scasq",
            "lahf",
            "xlat",
        };

        /// <summary>
        /// Determines whether the specified argument is memory location.
        /// </summary>
        /// <param name="arg">The argument.</param>
        /// <returns></returns>
        private static bool IsMemoryLocation(string arg)
        {
            if (arg.StartsWith("near ptr"))
                arg = arg.Substring("near ptr".Length).Trim();
            if (arg.StartsWith("far ptr"))
                arg = arg.Substring("far ptr".Length).Trim();

            return arg.StartsWith("unk_") || arg.StartsWith("loc_") || arg.StartsWith("sub_") || arg.StartsWith("$") || arg.StartsWith("byte_") || arg.StartsWith("word_") || arg.StartsWith("dword_") || arg.StartsWith("qword_") || arg.StartsWith("xmmword_");
        }

        internal static void LoadInstructionId()
        {
            InstructionMap.Clear();
            HighInstructionId = 0;

            var fi = new FileInfo("instruction_id.txt");
            if (!fi.Exists)
                return;

            using(var sw = fi.OpenText())
            {
                string l;
                while((l = sw.ReadLine()) != null)
                {
                    if (l.Length == 0)
                        continue;

                    var spl = l.Split(new[] { '\t' }, StringSplitOptions.None);
                    if (spl.Length != 2)
                        throw new FormatException();

                    uint id;
                    if (!uint.TryParse(spl[1], out id))
                        throw new FormatException();

                    InstructionMap[spl[0]] = id;
                    if (id > HighInstructionId)
                        HighInstructionId = id;
                }
            }
        }

        internal static void SaveInstructionId()
        {
            using(var sw = new StreamWriter("instruction_id.txt", false))
            {
                foreach (var pair in InstructionMap)
                    sw.WriteLine(pair.Key + "\t" + pair.Value);
            }
        }

        /// <summary>
        /// Gets the instruction identifier.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns></returns>
        private static uint GetInstructionId(string key)
        {
            uint u = 0;
            if (InstructionMap.TryGetValue(key, out u))
                return u;

            u = ++HighInstructionId;
            InstructionMap[key] = u;
            return u;
        }

        /// <summary>
        /// The high instruction identifier.
        /// </summary>
        private static uint HighInstructionId = 0;

        /// <summary>
        /// The instruction map.
        /// </summary>
        private static readonly Dictionary<string, uint> InstructionMap = new Dictionary<string, uint>();
    }

    internal abstract class IDAPass
    {
        internal IDAMigrate Migration;
        internal int Index;
        internal uint Listen;
        internal int AssignedMatchesCount;
        internal int[] AssignedMatchesCountByParameterIndex;

        internal enum IDAPassListenerTypes : int
        {
            OnDid,
            OnMatchMade,
            OnComparisonIndexChanged,
        }

        /// <summary>
        /// Registers the specified type of listener.
        /// </summary>
        /// <param name="type">The type.</param>
        protected void Register(IDAPassListenerTypes type)
        {
            uint fl = (uint)1 << (int)type;
            if ((this.Listen & fl) != 0)
                return;

            this.Listen |= fl;
            this.Migration.PassListeners[(int)type].Add(this);
        }

        /// <summary>
        /// Does this instance. Return false to continue onto next pass. Return true to reset back to first pass.
        /// </summary>
        /// <returns></returns>
        internal abstract bool Do();

        /// <summary>
        /// Gets the identifier of pass.
        /// </summary>
        /// <value>
        /// The identifier.
        /// </value>
        internal abstract byte Id
        {
            get;
        }

        /// <summary>
        /// Called when comparison parameter changed.
        /// </summary>
        internal virtual void OnComparisonParameterChanged()
        {

        }

        /// <summary>
        /// Gets the minimum index of the comparison parameter.
        /// </summary>
        /// <value>
        /// The minimum index of the comparison parameter.
        /// </value>
        internal virtual int MinComparisonParameterIndex
        {
            get
            {
                return 0;
            }
        }

        /// <summary>
        /// Initializes this instance. This is called for each pass once before we start to process (but after everything is loaded and ready). If you return false
        /// here then the pass is removed from regular pass list (Do will not be called ever).
        /// </summary>
        internal virtual bool Initialize()
        {
            return true;
        }

        /// <summary>
        /// Called after Do is called. This is only called for the current pass.
        /// </summary>
        /// <param name="did">if set to <c>true</c> then Do returned true.</param>
        internal virtual void OnAfterDo(bool did)
        {

        }

        /// <summary>
        /// Called when any pass returned true. This is called for each pass.
        /// </summary>
        /// <param name="pass">The pass that returned true.</param>
        internal virtual void OnDid(IDAPass pass)
        {

        }

        /// <summary>
        /// Called when match made anywhere. This is called for each pass.
        /// </summary>
        /// <param name="match">The match that was made.</param>
        /// <param name="pass">The pass that made the match (this was the CurrentPass at the time).</param>
        internal virtual void OnMatchMade(IDAObjMatch match, IDAPass pass)
        {

        }

        /// <summary>
        /// Called when match made here in current pass.
        /// </summary>
        /// <param name="match">The match.</param>
        internal virtual void OnMatchMadeHere(IDAObjMatch match)
        {

        }
    }

    internal abstract class IDAHelper
    {
        internal IDAMigrate Migration;
    }

    internal sealed class IDAObjectComparisonParameters
    {
        internal sealed class ComparisonEntryParameters
        {
            internal bool Ignore = false;
            internal bool RatioIsComplexityDifferenceInstead = false;
            internal double MinRatio = 1;
            internal int MaxWrong = 0;
            internal int MinComplex = 0;
            internal int MaxComplex = 0;
            internal int MaxCountDiff = int.MaxValue;
            internal int MinRefGuid = 0;
            internal int MinSameRefGuid = 0;
            internal double MinSameRefGuidRatio = 0.0;
        }

        internal IDAObjectComparisonParameters()
        {
            for (int i = 0; i < this.Entries.Length; i++)
                this.Entries[i] = new ComparisonEntryParameters();
        }

        internal double MaxIndexDifferenceForCompare = 0.5;
        internal double MaxIndexDifferenceForAmbiguous = 1.0;
        internal double MaxRangeAssignPctSize = 0.2;
        internal int MaxRangeAssignCount = 5000000;
        internal int MaxObjectComplexity = -1;
        //internal int RequiredPotentialMatchCount = 1;
        internal bool IsCachedExact;
        internal bool AllowRefInPass = true;
        internal bool AllowRefOutPass = true;

        internal void CalculateIsCachedExact()
        {
            for (int i = 0; i < this.Entries.Length; i++)
            {
                var e = this.Entries[i];
                if (e.Ignore || e.MinRatio != 1 || e.MaxWrong != 0 || e.RatioIsComplexityDifferenceInstead)
                    return;
            }

            this.IsCachedExact = true;
        }

        internal readonly ComparisonEntryParameters[] Entries = new ComparisonEntryParameters[IDAObjectComparisonData.MaxComparisonTypes];
    }

    internal sealed class IDAObjectComparisonData
    {
        internal IDAObjectComparisonData(IDAObject parent)
        {
            this.Object = parent;
        }

        internal readonly IDAObject Object;

        private readonly IDAObjectComparisonEntry?[] entries = new IDAObjectComparisonEntry?[MaxComparisonTypes];

        private readonly int[] invalidateTimes = new int[MaxComparisonTypes];

        private readonly ushort[] cachedCounter = new ushort[MaxComparisonTypes];

        //internal Utility.DistCache cach;

        internal static readonly int MaxComparisonTypes = Enum.GetValues(typeof(IDAObjectComparisonTypes)).Cast<int>().Max() + 1;

        internal Passes.StringDistanceCache cach;

        internal bool IsEmpty
        {
            get;
            set;
        }
        
        internal enum IDAObjectComparisonTypes : int
        {
            Asm = 0,
            InRef = 1,
            OutRef = 2,
            CustomString = 3,
        }

        internal void Invalidate(IDAObjectComparisonTypes type, bool force)
        {
            int i = (int)type;
            if (this.entries[i].HasValue)
            {
                if (!force)
                {
                    int need = Math.Max(1, (this.entries[i].Value.Data != null ? this.entries[i].Value.Data.Length : 0) / 200);
                    if (++this.invalidateTimes[i] < need)
                        return;
                }

                this.invalidateTimes[i] = 0;
                this.entries[i] = null;
                this.NeedRefresh = true;
                if (this.cachedCounter[i] == 0xFFFF)
                    throw new ArgumentException(); // probably maybe can set to 0 but dno
                this.cachedCounter[i]++;
            }
        }

        internal bool NeedRefresh
        {
            get;
            private set;
        } = true;

        internal void Refresh()
        {
            if (!this.NeedRefresh)
                return;

            byte empty = 0;

            int ix = (int)IDAObjectComparisonTypes.Asm;
            if (!this.entries[ix].HasValue)
            {
                var v = this.Object.Asm;
                var e = new IDAObjectComparisonEntry();
                if (v.Data == null)
                {
                    e.Data = EmptyData;
                    e.Hash = 0;
                }
                else
                {
                    e.Data = v.Data;
                    e.Hash = v.Hash;
                }
                this.entries[ix] = e;

                if (e.Data.Length == 0)
                    empty |= 1;
            }

            ix = (int)IDAObjectComparisonTypes.InRef;
            if (!this.entries[ix].HasValue)
            {
                ulong[] ls = new ulong[this.Object.InReferences.Count];
                for (int i = 0; i < ls.Length; i++)
                    ls[i] = this.Object.InReferences[i].Data;
                var e = new IDAObjectComparisonEntry();
                e.Data = ls;
                e.Hash = Utility.Calculate64BitHashCode(ls);
                this.entries[ix] = e;

                if (e.Data.Length == 0)
                    empty |= 2;
            }

            ix = (int)IDAObjectComparisonTypes.OutRef;
            if (!this.entries[ix].HasValue)
            {
                ulong[] ls = new ulong[this.Object.OutReferences.Count];
                for (int i = 0; i < ls.Length; i++)
                    ls[i] = this.Object.OutReferences[i].Data;
                var e = new IDAObjectComparisonEntry();
                e.Data = ls;
                e.Hash = Utility.Calculate64BitHashCode(ls);
                this.entries[ix] = e;

                if (e.Data.Length == 0)
                    empty |= 4;
            }

            ix = (int)IDAObjectComparisonTypes.CustomString;
            if (!this.entries[ix].HasValue)
            {
                string n = this.Object.CustomStringAssociation ?? "";
                var ls = new List<ulong>();
                if (n.Length != 0)
                {
                    if (IDAMigrate.CompactedString)
                    {
                        byte[] enc = Encoding.UTF8.GetBytes(n);
                        int maxCount = enc.Length / 8;
                        for (int i = 0; i < maxCount; i++)
                        {
                            ulong ux = BitConverter.ToUInt64(enc, i * 8);
                            ls.Add(ux);
                        }
                        if ((enc.Length % 8) != 0)
                        {
                            byte[] end = new byte[8];
                            int ib = enc.Length % 8;
                            for (int i = (enc.Length - ib), j = 0; i < enc.Length; i++, j++)
                            {
                                end[j] = enc[i];
                            }
                            ulong ux = BitConverter.ToUInt64(end, 0);
                            ls.Add(ux);
                        }
                    }
                    else
                    {
                        foreach (var x in n)
                            ls.Add(x);
                    }
                }
                var e = new IDAObjectComparisonEntry();
                e.Data = ls.ToArray();
                e.Hash = ls.Count != 0 ? Utility.Calculate64BitHashCode(ls) : 0;
                this.entries[ix] = e;

                if (e.Data.Length == 0)
                    empty |= 8;
            }

            this.IsEmpty = empty == 15;

            this.NeedRefresh = false;
        }

        internal int GetEntrySize(IDAObjectComparisonTypes entry)
        {
            int i = (int)entry;
            if (!this.entries[i].HasValue)
                return 0;
            var ad = this.entries[i].Value.Data;
            if (ad == null)
                return 0;
            return ad.Length;
        }

        internal IDAObjectComparisonEntry GetEntry(IDAObjectComparisonTypes entry)
        {
            return this.entries[(int)entry].Value;
        }

        internal bool CompareEntryExact(IDAObjectComparisonData other, IDAObjectComparisonTypes entry)
        {
            int i = (int)entry;
            var a = this.entries[i].Value;
            var b = other.entries[i].Value;

            if (a.Hash != b.Hash || a.Data.Length != b.Data.Length)
                return false;

            int d = a.Data.Length;
            for (int j = 0; j < d; j++)
            {
                if (a.Data[j] != b.Data[j])
                    return false;
            }

            return true;
        }

        internal string GetComparisonResultScore(IDAObjectComparisonData other, ref double score, ref int wrong, ref int total)
        {
            score = 1.0;
            wrong = 0;
            total = 0;

            var debug = new StringBuilder(32);

            int c = MaxComparisonTypes;
            for (int i = 0; i < c; i++)
            {
                var a = this.entries[i].HasValue ? this.entries[i].Value : new IDAObjectComparisonEntry();
                var b = other.entries[i].HasValue ? other.entries[i].Value : new IDAObjectComparisonEntry();

                if (i != 0)
                    debug.Append(", ");

                if (a.Data == null)
                    a.Data = new ulong[0];
                if (b.Data == null)
                    b.Data = new ulong[0];

                int qtotal = Math.Max(a.Data.Length, b.Data.Length);
                int qother = Math.Min(a.Data.Length, b.Data.Length);
                int qguid1 = 0;
                int qguid2 = 0;

                if (qtotal <= 0)
                {
                    debug.Append("none");
                    continue;
                }

                if (i >= 1 && i <= 2)
                {
                    ulong mask = 0xFFFFF;
                    if (i == 2)
                        mask <<= 20;
                    foreach (var x in a.Data)
                    {
                        if ((x & mask) != 0)
                            qguid1++;
                    }
                    foreach (var x in b.Data)
                    {
                        if ((x & mask) != 0)
                            qguid2++;
                    }
                }

                total += qtotal;

                debug.Append("[ ");
                if (qother == qtotal)
                    debug.Append(qtotal.ToString());
                else
                    debug.Append(a.Data.Length + "->" + b.Data.Length);

                if(qguid1 != 0 || qguid2 != 0)
                {
                    if (qguid1 == qguid2)
                        debug.Append("/" + qguid1);
                    else
                        debug.Append("/" + qguid1 + "->" + qguid2);
                }

                debug.Append(", ");
                
                if (a.Hash == b.Hash)
                {
                    if(a.Data.Length == b.Data.Length)
                    {
                        bool same = true;
                        for(int j = 0; j < a.Data.Length; j++)
                        {
                            if(a.Data[j] != b.Data[j])
                            {
                                same = false;
                                break;
                            }
                        }

                        if (same)
                        {
                            debug.Append("0, 1.0 ]");
                            continue;
                        }
                    }
                }
                
                int dist;
                if (a.Data.Length == 0)
                    dist = b.Data.Length;
                else if (b.Data.Length == 0)
                    dist = a.Data.Length;
                else
                    dist = this.Object.Version.Migration.DiffCache.GetDistance(a.Data, b.Data, int.MaxValue);

                if (dist == 0)
                {
                    debug.Append("NULL_DIST ]");
                    continue;
                }

                double ratio = 1.0 - Math.Min(1.0, (double)dist / (double)qtotal);
                score *= ratio;
                wrong += dist;

                debug.Append(dist.ToString());
                debug.Append(", ");
                debug.Append(ratio.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                debug.Append(" ]");
            }

            return debug.ToString();
        }

        internal bool CompareExact(IDAObjectComparisonData other)
        {
            this.Refresh();
            other.Refresh();

            if (this.IsEmpty && other.IsEmpty)
                return true;
            
            int c = MaxComparisonTypes;
            for (int i = 0; i < c; i++)
            {
                var a = this.entries[i].Value;
                var b = other.entries[i].Value;

                if (a.Hash != b.Hash || a.Data.Length != b.Data.Length)
                    return false;

                //if (a.Data.Length == 0) empty |= (byte)(1 << i);
            }

            for (int i = 0; i < c; i++)
            {
                var a = this.entries[i].Value.Data;
                var b = other.entries[i].Value.Data;

                int d = a.Length;
                for (int j = 0; j < d; j++)
                {
                    if (a[j] != b[j])
                        return false;
                }
            }
            
            //if (this.Object.Version.Migration.ComparisonParametersIndex == 0 || empty != (1 << c) - 1)
            {
                var pq = this.Object.Version.Migration.CurrentComparisonParameters;
                byte hadRefGuid = 0;
                int needSameRef = int.MinValue;
                for (int i = 0; i < c; i++)
                {
                    var p = pq.Entries[i];
                    if (p.Ignore)
                        continue;

                    if (p.MinComplex > 0)
                    {
                        var a = this.entries[i].Value.Data;
                        var b = other.entries[i].Value.Data;

                        if (a.Length < p.MinComplex || b.Length < p.MinComplex)
                            return false;
                    }

                    if(p.MaxComplex > 0)
                    {
                        var a = this.entries[i].Value.Data;
                        var b = other.entries[i].Value.Data;

                        if (a.Length > p.MaxComplex || b.Length > p.MaxComplex)
                            return false;
                    }

                    if (p.MinRefGuid > 0 && i >= 1 && i <= 2)
                    {
                        var a = this.entries[i].Value.Data;
                        var b = other.entries[i].Value.Data;

                        ulong mask = 0xFFFFF;
                        if (i == 2)
                            mask <<= 20;
                        int has = 0;
                        foreach (var x in a)
                        {
                            // low 20 bits is the guid of the object where reference originates
                            // bits 21-40 is the guid of  the object where reference points to
                            // if both are set then match is already made
                            if ((x & mask) != 0)
                                has++;
                        }

                        if (has < p.MinRefGuid)
                            return false;

                        has = 0;
                        foreach (var x in b)
                        {
                            // low 20 bits is the guid of the object where reference originates
                            // bits 21-40 is the guid of  the object where reference points to
                            // if both are set then match is already made
                            if ((x & mask) != 0)
                                has++;
                        }

                        if (has < p.MinRefGuid)
                            return false;
                    }
                    else if (p.MinRefGuid < 0 && i >= 1 && i <= 2)
                    {
                        hadRefGuid |= 1;
                        if ((hadRefGuid & 2) == 0)
                        {
                            int need = -p.MinRefGuid;

                            var a = this.entries[i].Value.Data;
                            var b = other.entries[i].Value.Data;

                            ulong mask = 0xFFFFF;
                            if (i == 2)
                                mask <<= 20;

                            int has = 0;
                            foreach (var x in a)
                            {
                                // low 20 bits is the guid of the object where reference originates
                                // bits 21-40 is the guid of  the object where reference points to
                                // if both are set then match is already made
                                if ((x & mask) != 0)
                                    has++;
                            }

                            if (has >= need)
                            {
                                has = 0;
                                foreach (var x in b)
                                {
                                    // low 20 bits is the guid of the object where reference originates
                                    // bits 21-40 is the guid of  the object where reference points to
                                    // if both are set then match is already made
                                    if ((x & mask) != 0)
                                        has++;
                                }

                                if (has >= need)
                                    hadRefGuid |= 2;
                            }
                        }
                    }

                    if(p.MinSameRefGuid > 0 && (i == 2 || i == 1))
                    {
                        var map = _tempSameRefGuidMap;

                        var a = this.entries[i].Value.Data;
                        var b = other.entries[i].Value.Data;

                        ulong mask = 0xFFFFF;
                        if (i == 2)
                            mask <<= 20;

                        KeyValuePair<int, int> v;
                        foreach(var x in a)
                        {
                            uint guid;
                            if (i == 2)
                                guid = (uint)(((x & mask) >> 20) & 0xFFFFF);
                            else
                                guid = (uint)(x & mask);

                            if (guid == 0)
                                continue;

                            map.TryGetValue(guid, out v);
                            map[guid] = new KeyValuePair<int, int>(v.Key + 1, v.Value);
                        }
                        foreach (var x in b)
                        {
                            uint guid;
                            if (i == 2)
                                guid = (uint)(((x & mask) >> 20) & 0xFFFFF);
                            else
                                guid = (uint)(x & mask);

                            if (guid == 0)
                                continue;

                            map.TryGetValue(guid, out v);
                            map[guid] = new KeyValuePair<int, int>(v.Key, v.Value + 1);
                        }

                        int need = p.MinSameRefGuid;
                        foreach(var pair in map)
                            need -= Math.Min(pair.Value.Key, pair.Value.Value);

                        map.Clear();
                        
                        if (need > 0)
                            return false;
                    }
                    else if(p.MinSameRefGuid < 0 && (i == 1 || i == 2))
                    {
                        if (needSameRef == int.MinValue)
                            needSameRef = -p.MinSameRefGuid;

                        if(needSameRef > 0)
                        {
                            var map = _tempSameRefGuidMap;

                            var a = this.entries[i].Value.Data;
                            var b = other.entries[i].Value.Data;

                            ulong mask = 0xFFFFF;
                            if (i == 2)
                                mask <<= 20;

                            KeyValuePair<int, int> v;
                            foreach (var x in a)
                            {
                                uint guid;
                                if (i == 2)
                                    guid = (uint)(((x & mask) >> 20) & 0xFFFFF);
                                else
                                    guid = (uint)(x & mask);

                                if (guid == 0)
                                    continue;

                                map.TryGetValue(guid, out v);
                                map[guid] = new KeyValuePair<int, int>(v.Key + 1, v.Value);
                            }
                            foreach (var x in b)
                            {
                                uint guid;
                                if (i == 2)
                                    guid = (uint)(((x & mask) >> 20) & 0xFFFFF);
                                else
                                    guid = (uint)(x & mask);

                                if (guid == 0)
                                    continue;

                                map.TryGetValue(guid, out v);
                                map[guid] = new KeyValuePair<int, int>(v.Key, v.Value + 1);
                            }

                            foreach (var pair in map)
                                needSameRef -= Math.Min(pair.Value.Key, pair.Value.Value);

                            map.Clear();
                        }
                    }

                    if (p.MinSameRefGuidRatio > 0.0 && (i == 2 || i == 1))
                    {
                        var map = _tempSameRefGuidRatioMap;

                        var a = this.entries[i].Value.Data;
                        var b = other.entries[i].Value.Data;

                        int total = Math.Max(a.Length, b.Length);
                        int wronga = 0;
                        int wrongb = 0;

                        if (total <= 0)
                            return false;

                        ulong mask = 0xFFFFF;
                        if (i == 2)
                            mask <<= 20;

                        int v;
                        foreach (var x in a)
                        {
                            uint guid;
                            if (i == 2)
                                guid = (uint)(((x & mask) >> 20) & 0xFFFFF);
                            else
                                guid = (uint)(x & mask);

                            if (guid == 0)
                            {
                                wronga++;
                                continue;
                            }

                            map.TryGetValue(guid, out v);
                            map[guid] = v + 1;
                        }
                        foreach (var x in b)
                        {
                            uint guid;
                            if (i == 2)
                                guid = (uint)(((x & mask) >> 20) & 0xFFFFF);
                            else
                                guid = (uint)(x & mask);

                            if (guid == 0)
                            {
                                wrongb++;
                                continue;
                            }

                            map.TryGetValue(guid, out v);
                            map[guid] = v - 1;
                        }

                        foreach (var pair in map)
                        {
                            if (pair.Value > 0)
                                wronga += pair.Value;
                            else
                                wrongb -= pair.Value;
                        }

                        map.Clear();

                        int wrong = Math.Max(wronga, wrongb);

                        double ratio = Math.Max(0.0, 1.0 - (double)wrong / (double)total);
                        if (ratio < p.MinSameRefGuidRatio)
                            return false;
                    }
                }

                if (hadRefGuid == 1)
                    return false;

                if (needSameRef > 0)
                    return false;
            }

            return true;
        }

        private readonly Dictionary<uint, KeyValuePair<int, int>> _tempSameRefGuidMap = new Dictionary<uint, KeyValuePair<int, int>>();
        private readonly Dictionary<uint, int> _tempSameRefGuidRatioMap = new Dictionary<uint, int>();

        /// <summary>
        /// Compares the other entry with current parameters.
        /// </summary>
        /// <param name="other">The other entry.</param>
        /// <param name="forAmbiguous">Is this for ambiguous compare or regular compare.</param>
        /// <param name="overwriteMaxIndexPctDiff">The overwrite maximum index per cent difference.</param>
        /// <param name="compareIndex">Compares index and may return false because of it.</param>
        /// <returns></returns>
        internal bool CompareWithCurrentParameters(IDAObjectComparisonData other, bool forAmbiguous, double? overwriteMaxIndexPctDiff = null)
        {
            this.Refresh();
            other.Refresh();

            var mig = this.Object.Version.Migration;
            var p = mig.CurrentComparisonParameters;
            double maxDiff = overwriteMaxIndexPctDiff.HasValue ? overwriteMaxIndexPctDiff.Value : (forAmbiguous ? p.MaxIndexDifferenceForAmbiguous : p.MaxIndexDifferenceForCompare);

            if (!this.CompareIndex(other, maxDiff))
                return false;

            if (p.IsCachedExact)
            {
                bool r = this.CompareExact(other);
                return r;
            }

            bool result = true;

            int c = MaxComparisonTypes;
            uint um = 0;
            for (int i = 0; i < c; i++)
            {
                uint m = (uint)1 << i;

                var pq = p.Entries[i];
                if (pq.Ignore)
                {
                    um |= m;
                    continue;
                }

                var a = this.entries[i].Value;
                var b = other.entries[i].Value;

                if (a.Hash == b.Hash && a.Data.Length == b.Data.Length)
                {
                    int d = a.Data.Length;
                    bool same = true;
                    for (int j = 0; j < d; j++)
                    {
                        if (a.Data[j] != b.Data[j])
                        {
                            same = false;
                            break;
                        }
                    }

                    if (same)
                    {
                        um |= m;
                        continue;
                    }
                }

                if (pq.MaxWrong == 0 || (pq.MinRatio == 1.0 && !pq.RatioIsComplexityDifferenceInstead))
                    result = false;
                else if (Math.Abs(a.Data.Length - b.Data.Length) > pq.MaxCountDiff)
                    result = false;
            }

            if (result)
            {
                var cache = mig.DiffCache;

                for (int i = 0; i < c; i++)
                {
                    var pq = p.Entries[i];
                    var a = this.entries[i].Value;
                    var b = other.entries[i].Value;

                    if (!pq.Ignore)
                    {
                        if(pq.MinComplex > 0 && (a.Data.Length < pq.MinComplex || b.Data.Length < pq.MinComplex))
                        {
                            result = false;
                            break;
                        }

                        if(pq.MaxComplex > 0 && (a.Data.Length > pq.MaxComplex || b.Data.Length > pq.MaxComplex))
                        {
                            result = false;
                            break;
                        }
                    }

                    uint m = (uint)1 << i;
                    if ((um & m) != 0)
                        continue;

                    int maxWrong = pq.MaxWrong;
                    int maxPossible = Math.Max(a.Data.Length, b.Data.Length); // Max possible things that could go wrong.
                    if (pq.MinRatio != 0.0)
                    {
                        int minPossible = (int)(pq.MinRatio * maxPossible); // Min possible correct count that is allowed.

                        int diffPossible = maxPossible - minPossible; // How many things are allowed to go wrong and be ok.
                        if (diffPossible < maxWrong)
                            maxWrong = diffPossible;

                        if (diffPossible < Math.Abs(a.Data.Length - b.Data.Length))
                        {
                            result = false;
                            break;
                        }
                    }

                    bool shouldCache = IDAMigrate.UseCache2 && !pq.RatioIsComplexityDifferenceInstead && Utility.DistCachePerm.Can((IDAObjectComparisonTypes)i) && Utility.DistCachePerm.Should(a.Data, b.Data);
                    ulong ckey = 0;
                    int diff = -1;
                    if (shouldCache)
                    {
                        ckey = Utility.DistCachePerm.MakeKey(this.Object.IndexInSegment, other.Object.IndexInSegment, (byte)i, this.Object.Segment.Code, this.Object.Version.Index != 0, other.Object.Version.Index != 0);
                        diff = Utility.DistCachePerm.Lookup(ckey);
                    }

                    if (pq.RatioIsComplexityDifferenceInstead)
                        diff = Math.Abs(a.Data.Length - b.Data.Length);

                    if (diff < 0)
                    {
                        Passes.StringDistanceCache ch = null;
                        byte tp = 0;
                        uint ccount = 0;
                        if(IDAMigrate.UseCache1 && Passes.StringDistanceCache.ShouldCache((byte)i, a.Data.Length, b.Data.Length))
                        {
                            tp = (byte)i;
                            if (this.Object.Segment == other.Object.Segment)
                                tp |= 0x10;
                            ccount = this.cachedCounter[i];
                            ccount <<= 16;
                            ccount |= other.cachedCounter[i];

                            ch = this.Object.Comparison.cach;
                            if(ch == null)
                            {
                                ch = new Passes.StringDistanceCache();
                                this.Object.Comparison.cach = ch;
                            }

                            diff = ch.Get(other.Object.IndexInSegment, tp, ccount);
                        }

                        if (diff < 0)
                        {
                            diff = cache.GetDistance(a.Data, b.Data, shouldCache || ch != null ? int.MaxValue : maxWrong);
                            if (shouldCache)
                                Utility.DistCachePerm.Set(ckey, diff);
                            if (ch != null)
                                ch.Set(other.Object.IndexInSegment, tp, ccount, diff);
                        }
                    }

                    if (diff > maxWrong)
                    {
                        result = false;
                        break;
                    }

                    double ratio = 1.0 - Math.Min(1.0, (double)diff / (double)maxPossible);
                    if (ratio < pq.MinRatio) // This can happen due to rounding.
                    {
                        result = false;
                        break;
                    }
                }
            }

            /*if (ch != null)
                ch.SetCached(this.Object.IndexInSegment, result);*/

            return result;
        }

        internal bool CompareIndex(IDAObjectComparisonData other, double maxPctDiff = 1.0)
        {
            double diff = Math.Abs(other.Object.IndexPctInSegment - this.Object.IndexPctInSegment);
            return diff <= maxPctDiff;
        }

        internal struct IDAObjectComparisonEntry
        {
            internal ulong Hash;
            internal ulong[] Data;
        }

        private static readonly ulong[] EmptyData = new ulong[0];
    }

    public struct IDAComparisonResult
    {
        public double Ratio
        {
            get;
            internal set;
        }

        public int Wrong
        {
            get;
            internal set;
        }
    }

    /// <summary>
    /// The result of migration script.
    /// </summary>
    public sealed class IDADiff
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IDADiff"/> class.
        /// </summary>
        internal IDADiff()
        {

        }

        /// <summary>
        /// Calculates the difference.
        /// </summary>
        /// <param name="prog">The progress bar.</param>
        /// <param name="architecture">The architecture.</param>
        /// <param name="prevDir">The previous dir.</param>
        /// <param name="nextDir">The next dir.</param>
        /// <returns></returns>
        public static IDADiff Calculate(Progress prog, ArchitectureTypes architecture, System.IO.DirectoryInfo prevDir, System.IO.DirectoryInfo nextDir)
        {
            IDAExportAsmBuilder.LoadInstructionId();
            var m = new IDAMigrate(architecture);
            var r = m.Do(prog, prevDir, nextDir);
            IDAExportAsmBuilder.SaveInstructionId();
            return r;
        }

        /// <summary>
        /// Gets the script.
        /// </summary>
        /// <value>
        /// The script.
        /// </value>
        internal IDAMigrate Script
        {
            get;
            set;
        }

        /// <summary>
        /// Gets the amount of passes done.
        /// </summary>
        /// <value>
        /// The passes.
        /// </value>
        public int Passes
        {
            get;
            internal set;
        }

        /// <summary>
        /// Gets the match map. The key is old offset of object (including base address).
        /// </summary>
        /// <value>
        /// The match map.
        /// </value>
        public IReadOnlyDictionary<Offset, IDADiffResult> MatchMap
        {
            get;
            internal set;
        }

        /// <summary>
        /// Gets the match list.
        /// </summary>
        /// <value>
        /// The match list.
        /// </value>
        public IReadOnlyList<IDADiffResult> MatchList
        {
            get;
            internal set;
        }

        /// <summary>
        /// Gets the failed matches list in previous version.
        /// </summary>
        /// <value>
        /// The failed matches in previous version.
        /// </value>
        public IReadOnlyList<Offset> FailedMatchesInPreviousVersion
        {
            get;
            internal set;
        }

        /// <summary>
        /// Gets the failed matches list in next version.
        /// </summary>
        /// <value>
        /// The failed matches in next version.
        /// </value>
        public IReadOnlyList<Offset> FailedMatchesInNextVersion
        {
            get;
            internal set;
        }

        /// <summary>
        /// Gets the function asm hashes in previous version.
        /// </summary>
        public IReadOnlyDictionary<Offset, ulong> FunctionHashesInPreviousVersion
        {
            get;
            internal set;
        }

        /// <summary>
        /// Gets the function asm hashes in next version.
        /// </summary>
        public IReadOnlyDictionary<Offset, ulong> FunctionHashesInNextVersion
        {
            get;
            internal set;
        }

        public IReadOnlyList<KeyValuePair<Offset, int>> FailedMatchesWithManyInRefsInPreviousVersion
        {
            get;
            internal set;
        }

        public IReadOnlyList<KeyValuePair<Offset, int>> FailedMatchesWithManyInRefsInNextVersion
        {
            get;
            internal set;
        }

        /// <summary>
        /// One matching result.
        /// </summary>
        public sealed class IDADiffResult
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="IDADiffResult"/> class.
            /// </summary>
            internal IDADiffResult()
            {

            }

            /// <summary>
            /// Gets the source or previous version offset. This includes base address.
            /// </summary>
            /// <value>
            /// The source.
            /// </value>
            public Offset Source
            {
                get;
                internal set;
            }

            /// <summary>
            /// Gets the target or next version offset. This includes base address.
            /// </summary>
            /// <value>
            /// The target.
            /// </value>
            public Offset Target
            {
                get;
                internal set;
            }

            /// <summary>
            /// Gets the warnings.
            /// </summary>
            /// <value>
            /// The warnings.
            /// </value>
            public IReadOnlyList<string> Warnings
            {
                get
                {
                    var ls = this.warnings;
                    if (ls == null)
                        return EmptyWarnings;
                    return ls;
                }
            }
            internal List<string> warnings = null;
            private static readonly string[] EmptyWarnings = new string[0];

            /// <summary>
            /// Gets the score of match. This is a ratio between 0 and 1 where 1 is perfect match.
            /// </summary>
            /// <value>
            /// The score.
            /// </value>
            public double Score
            {
                get;
                internal set;
            }

            /// <summary>
            /// Gets the amount of things that were wrong with this match. If zero then it's perfect match.
            /// </summary>
            /// <value>
            /// The difference.
            /// </value>
            public int Difference
            {
                get;
                internal set;
            }

            /// <summary>
            /// Gets the total amount of things that could go wrong with this match if everything was bad. This can be used to check how complex the objects were, more complex usually
            /// means greater confidence in match because there were many things we could check.
            /// </summary>
            /// <value>
            /// The total.
            /// </value>
            public int Total
            {
                get;
                internal set;
            }

            /// <summary>
            /// Gets a value indicating whether this instance is perfect match.
            /// </summary>
            /// <value>
            /// <c>true</c> if this instance is perfect; otherwise, <c>false</c>.
            /// </value>
            public bool IsPerfect
            {
                get
                {
                    return this.Difference == 0;
                }
            }

            /// <summary>
            /// Gets a value indicating whether this instance is partial match.
            /// </summary>
            /// <value>
            /// <c>true</c> if this instance is partial; otherwise, <c>false</c>.
            /// </value>
            public bool IsPartial
            {
                get
                {
                    return !this.IsPerfect;
                }
            }

            public string Debug
            {
                get;
                set;
            }
        }

        /// <summary>
        /// Writes the report to this string builder.
        /// </summary>
        /// <param name="builder">The builder.</param>
        public void WriteReport(StringBuilder builder)
        {
            var diff = this.Script;

            int totalObjectsInSource = this.MatchList.Count + this.FailedMatchesInPreviousVersion.Count;
            int totalObjectsInTarget = this.MatchList.Count + this.FailedMatchesInNextVersion.Count;
            int yesObjects = this.MatchList.Count;
            int perfectObjects = this.MatchList.Count(q => q.IsPerfect);
            builder.AppendLine("Previous version had " + totalObjectsInSource + " total offsets that needed matching from.");
            builder.AppendLine("Next version had " + totalObjectsInTarget + " total offsets that needed matching to.");
            builder.AppendLine("Matched " + yesObjects + " offsets from one version to another (" + _pct(yesObjects, totalObjectsInSource) + "% / " + _pct(yesObjects, totalObjectsInTarget) + "%) over " + this.Passes + " passes.");
            builder.AppendLine("The amount of matches that were perfect was " + perfectObjects + " (" + _pct(perfectObjects, yesObjects) + "%).");
            builder.AppendLine("Previous version had " + this.FailedMatchesInPreviousVersion.Count + " (" + _pct(this.FailedMatchesInPreviousVersion.Count, totalObjectsInSource) + "%) offsets that could not be matched to new version.");
            builder.AppendLine("Next version had " + this.FailedMatchesInNextVersion.Count + " (" + _pct(this.FailedMatchesInNextVersion.Count, totalObjectsInTarget) + "%) offsets that could not be matched to old version.");
            int cntsame = this.MatchMap.Count(q => q.Value.Source == q.Value.Target);
            builder.AppendLine("The amount of objects that have the exact same address in previous and next version is " + cntsame + " (" + _pct(cntsame, this.MatchMap.Count) + "%).");
            if (diff != null)
            {
                builder.AppendLine("Matches by segment in previous version:");
                foreach (var s in diff.Versions[0].Segments)
                {
                    var objs = s.Objects;
                    int hasm = objs.Count(q => q.Match != null);
                    builder.AppendLine(s.ToString() + ": " + hasm + " (" + _pct(hasm, objs.Count) + "%)");
                }
            }
            builder.AppendLine("Overall success: " + _pct(yesObjects, Math.Max(totalObjectsInSource, totalObjectsInTarget)) + "%");
            builder.AppendLine("Converted " + diff.Versions[0].ConvertedFromLocsCount + " locations to " + diff.Versions[0].ConvertedToFuncsCount + " functions in previous version.");

            bool hadw = false;
            foreach (var obj in this.MatchList)
            {
                var warn = obj.Warnings;
                if (warn.Count != 0)
                {
                    if (!hadw)
                    {
                        builder.AppendLine();
                        builder.AppendLine("Warnings:");
                        hadw = true;
                    }
                    foreach (var x in warn)
                        builder.AppendLine("# " + x);
                }
            }
        }

        private static string _pct(int amt, int total)
        {
            double p;
            if (total == 0)
                p = 100.0;
            else
            {
                p = (double)amt * 100.0;
                p /= (double)total;
            }
            return p.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    internal sealed class IDAHelper_IsSameSegment : IDAHelper
    {
        internal bool Calculate(IDAObject a, IDAObject b)
        {
            var aver = a.Version;
            var bver = b.Version;
            var aseg = a.Segment;
            var bseg = b.Segment;

            if (aver == bver)
                return a.Segment == b.Segment;

            if (aseg.Code != bseg.Code || aseg.Name != bseg.Name || aseg.Flags != bseg.Flags)
                return false;

            return true;
        }
    }
}

namespace IDADiffCalculator.Migration.Passes
{
    internal sealed class IDAPass_RecalculateAdjustedSegmentIndices : IDAPass
    {
        internal override bool Initialize()
        {
            this.Register(IDAPassListenerTypes.OnMatchMade);

            // First segment is just index * 100 / total.
            {
                var v = this.Migration.Versions[0];
                foreach (var s in v.Segments)
                {
                    double total = (double)Math.Max(MinCount, s.Objects.Count) / 100.0;
                    int diff = s.Equivalent.Objects.Count - s.Objects.Count;

                    for (int i = 0; i < s.Objects.Count; i++)
                        s.Objects[i].IndexPctInSegment = (double)i / total;
                }
            }

            // Second segment is adjusted to first.
            {
                var v = this.Migration.Versions[1];
                foreach (var s in v.Segments)
                {
                    if (s.Objects.Count == 0 || s.Equivalent == null)
                        continue;

                    s.Objects[0].IndexPctInSegment = 0;
                    if (s.Objects.Count > 1)
                    {
                        double max = s.Equivalent.Objects[s.Equivalent.Objects.Count - 1].IndexPctInSegment;
                        s.Objects[s.Objects.Count - 1].IndexPctInSegment = max;
                        this.AdjustSegmentRange(s, 0, s.Objects.Count - 1, 0, max);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The minimum count. Needed for segments with very few objects, otherwise we can not make matches at all because if 1 object index is 5% then it is too much "difference".
        /// </summary>
        private static readonly int MinCount = 1000;

        /// <summary>
        /// The maximum readjust range.
        /// </summary>
        private static readonly int MaxReadjustRange = 9999999; // 500;

        /// <summary>
        /// Adjusts the segment range.
        /// </summary>
        /// <param name="segment">The segment.</param>
        /// <param name="beginIndexExcluded">The begin index excluded.</param>
        /// <param name="endIndexExcluded">The end index excluded.</param>
        /// <param name="beginPct">The begin per cent.</param>
        /// <param name="endPct">The end per cent.</param>
        private void AdjustSegmentRange(IDASegment segment, int beginIndexExcluded, int endIndexExcluded, double beginPct, double endPct)
        {
            int range = endIndexExcluded - beginIndexExcluded;
            if (range <= 1)
                return;
            double inc = (endPct - beginPct) / (double)range;
            double cur = beginPct + inc;

            for (int i = beginIndexExcluded + 1; i < endIndexExcluded; i++, cur += inc)
                segment.Objects[i].IndexPctInSegment = cur;
        }

        internal static readonly byte ConstId = 1;

        internal override byte Id
        {
            get
            {
                return ConstId;
            }
        }

        internal override void OnMatchMade(IDAObjMatch match, IDAPass pass)
        {
            var obj = match.Second;
            var seg = obj.Segment;

            double my = match.First.IndexPctInSegment;
            obj.IndexPctInSegment = my;
            IDAObject next = null;
            int lowestIdx = Math.Max(0, obj.IndexInSegment - MaxReadjustRange);
            for (int j = obj.IndexInSegment - 1; j >= lowestIdx; j--)
            {
                var o = seg.Objects[j];
                if (o.Match == null)
                    continue;

                next = o;
                break;
            }
            if (next == null)
                next = seg.Objects[lowestIdx];
            this.AdjustSegmentRange(seg, next.IndexInSegment, obj.IndexInSegment, next.IndexPctInSegment, my);

            next = null;
            int highestIdx = Math.Min(seg.Objects.Count, obj.IndexInSegment + MaxReadjustRange);
            for (int j = obj.IndexInSegment + 1; j < highestIdx; j++)
            {
                var o = seg.Objects[j];
                if (o.Match == null)
                    continue;

                next = o;
                break;
            }
            if (next == null)
                next = seg.Objects[highestIdx - 1];
            this.AdjustSegmentRange(seg, obj.IndexInSegment, next.IndexInSegment, my, next.IndexPctInSegment);
        }

        internal override bool Do()
        {
            throw new InvalidOperationException();
        }
    }

    /*internal sealed class IDAPass_MatchReferences : IDAPass
    {
        internal override bool Initialize()
        {
            this.Register(IDAPassListenerTypes.OnMatchMade);
            return false;
        }

        internal override void OnMatchMade(IDAObjMatch match, IDAPass pass)
        {
            TODO();
        }

        private void TryMatchReferences(IDAObject first, IDAObject second, bool inComing)
        {
            var ls = inComing ? first.InReferences : first.OutReferences;
            for (int i = 0; i < ls.Count; i++)
            {
                var r = ls[i];

                var e = new IDARefData();
                e.Data = r.Data;
                e.Ref = r;
                e.Remove = false;
                this.ar.Add(e);
            }

            ls = inComing ? second.InReferences : second.OutReferences;
            for (int i = 0; i < ls.Count; i++)
            {
                var r = ls[i];

                var e = new IDARefData();
                e.Data = r.Data;
                e.Ref = r;
                e.Remove = false;
                this.br.Add(e);
            }

            IDAObjectComparisonData.IDAObjectComparisonTypes ct = inComing ? IDAObjectComparisonData.IDAObjectComparisonTypes.InRef : IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef;
            if (!first.Comparison.CompareEntryExact(second.Comparison, ct))
            {
                var ard = first.Comparison.GetEntry(ct);
                var brd = second.Comparison.GetEntry(ct);

                if (Utility.GetDifferences(ard.Data, brd.Data, diff) == 0)
                    throw new InvalidOperationException();

                foreach (var d in diff)
                {
                    switch (d.Type)
                    {
                        case Utility.ListDifferenceEntryTypes.Modified:
                            {
#if DEBUG
                                if (d.FirstIndex < 0 || d.SecondIndex < 0) throw new InvalidOperationException();
#endif

                                for (int i = 0; i < d.Length; i++)
                                {
                                    this.ar[d.FirstIndex + i].Remove = true;
                                    this.br[d.SecondIndex + i].Remove = true;
                                }
                            }
                            break;

                        case Utility.ListDifferenceEntryTypes.Added:
                            {
#if DEBUG
                                if (d.SecondIndex < 0) throw new InvalidOperationException();
#endif

                                for (int i = 0; i < d.Length; i++)
                                    this.br[d.SecondIndex + i].Remove = true;
                            }
                            break;

                        case Utility.ListDifferenceEntryTypes.Removed:
                            {
#if DEBUG
                                if (d.FirstIndex < 0) throw new InvalidOperationException();
#endif

                                for (int i = 0; i < d.Length; i++)
                                    this.ar[d.FirstIndex + i].Remove = true;
                            }
                            break;

                        default:
                            throw new NotSupportedException();
                    }
                }

                this.ProcessRemoves();
            }

            // try match reference list in the order it is
            TODO();
        }

        /// <summary>
        /// Processes the removes.
        /// </summary>
        private void ProcessRemoves()
        {
            while(true)
            {
                bool had = false;

                int c = Math.Max(ar.Count, br.Count);
                for(int i = 0; i < c; i++)
                {
                    var a = i < ar.Count ? ar[i] : null;
                    var b = i < br.Count ? br[i] : null;
                    
                    bool wanta = a != null && a.Remove;
                    bool wantb = b != null && b.Remove;
                    
                    // Want to delete both.
                    if(wanta && wantb)
                    {
                        int maxBack = 0;
                        {
                            int wantBack = 0;
                            ulong same = a.Data;
                            for(int j = i - 1; j >= 0; j--)
                            {
                                var back = ar[j];
                                if (back.Data != same)
                                    break;
                                wantBack++;
                            }

                            maxBack = Math.Max(wantBack, maxBack);
                        }
                        {
                            int wantBack = 0;
                            ulong same = b.Data;
                            for (int j = i - 1; j >= 0; j--)
                            {
                                var back = br[j];
                                if (back.Data != same)
                                    break;
                                wantBack++;
                            }

                            maxBack = Math.Max(wantBack, maxBack);
                        }
                        if(maxBack > 0)
                        {
                            ar.RemoveRange(i - maxBack, maxBack);
                            br.RemoveRange(i - maxBack, maxBack);
                            had = true;
                            break;
                        }
                        int remlen = 1;
                        int c2 = Math.Min(ar.Count, br.Count);
                        for(int j = i + 1; j < c2; j++)
                        {
                            if (ar[j].Remove && br[j].Remove)
                                remlen++;
                            else
                                break;
                        }
                        ulong lastdela = ar[i + remlen - 1].Data;
                        ulong lastdelb = br[i + remlen - 1].Data;
                        TODO();
                        int remlena = remlen;
                        int remlenb = remlen;
                        c2 = ar.Count;
                        for(int j = i + remlen; j < c2; j++)
                        {
                            var o = ar[j];
                            if (o.Remove || o.Data != lastdela)
                                break;
                            remlena++;
                        }
                        ar.RemoveRange(i, remlen);
                        br.RemoveRange(i, remlen);
                        had = true;
                        break;
                    }
                    if(wanta)
                    {
                        int wantBack = 0;
                        ulong same = a.Data;
                        for (int j = i - 1; j >= 0; j--)
                        {
                            var back = ar[j];
                            if (back.Data != same)
                                break;
                            wantBack++;
                        }
                        
                        if(wantBack > 0)
                        {
                            int rix = i - wantBack;
                            ar.RemoveRange(rix, wantBack);
                            if (b != null)
                                br.RemoveRange(rix, wantBack);
                            else if (br.Count > rix)
                                br.RemoveRange(rix, br.Count - rix);
                            had = true;
                            break;
                        }

                        int remlen = 1;
                        int c2 = ar.Count;
                        for (int j = i + 1; j < c2; j++)
                        {
                            if (ar[j].Remove)
                                remlen++;
                            else
                                break;
                        }
                        lastdela = ar[i + remlen - 1].Data;
                        ar.RemoveRange(i, remlen);
                        had = true;
                        break;
                    }
                    else if(wantb)
                    {
                        int wantBack = 0;
                        ulong same = b.Data;
                        for (int j = i - 1; j >= 0; j--)
                        {
                            var back = br[j];
                            if (back.Data != same)
                                break;
                            wantBack++;
                        }

                        if (wantBack > 0)
                        {
                            int rix = i - wantBack;
                            br.RemoveRange(rix, wantBack);
                            if (a != null)
                                ar.RemoveRange(rix, wantBack);
                            else if (ar.Count > rix)
                                ar.RemoveRange(rix, ar.Count - rix);
                            had = true;
                            break;
                        }

                        int remlen = 1;
                        int c2 = br.Count;
                        for (int j = i + 1; j < c2; j++)
                        {
                            if (br[j].Remove)
                                remlen++;
                            else
                                break;
                        }
                        lastdelb = br[i + remlen - 1].Data;
                        br.RemoveRange(i, remlen);
                        had = true;
                        break;
                    }
                }
                
                if (had)
                    continue;

                break;
            }
            TODO();
        }

        private void RemoveRangeDueToDifference(int index, int len, bool removeFirst, bool removeSecond)
        {
            // Remove, but if surrounding reference datas are ambiguous then write index to amb / bmb lists for later removal of whole batch
            TODO();

            if(removeFirst && removeSecond)
            {
                if(len == 1)
                {
                    ulong removeda = ard[index];
                    ulong removedb = brd[index];

                    if (index > 0 && ard[index - 1] == removeda)
                        amb.Add(new Tuple<int, ulong, bool>(index - 1, removeda, true));
                    if (index + 1 < ard.Count && ard[index + 1] == removeda)
                        amb.Add(new Tuple<int, ulong, bool>(index, removeda, true));

                    if (index > 0 && brd[index - 1] == removedb)
                        amb.Add(new Tuple<int, ulong, bool>(index - 1, removedb, false));
                    if (index + 1 < brd.Count && brd[index + 1] == removedb)
                        amb.Add(new Tuple<int, ulong, bool>(index, removedb, false));
                }
                else
                {
                    ulong removeda0 = ard[index];
                    ulong removeda1 = ard[index + len - 1];
                    ulong removedb0 = brd[index];
                    ulong removedb1 = brd[index + len - 1];

                    if (index > 0 && ard[index - 1] == removeda0)
                        amb.Add(new Tuple<int, ulong, bool>(index - 1, removeda0, true));
                    if (index + len < ard.Count && ard[index + len] == removeda1)
                        amb.Add(new Tuple<int, ulong, bool>(index, removeda1, true));

                    if (index > 0 && brd[index - 1] == removedb0)
                        amb.Add(new Tuple<int, ulong, bool>(index - 1, removedb0, false));
                    if (index + len < brd.Count && brd[index + len] == removedb1)
                        amb.Add(new Tuple<int, ulong, bool>(index, removedb1, false));
                }

                ard.RemoveRange(index, len);
                ar.RemoveRange(index, len);
                brd.RemoveRange(index, len);
                br.RemoveRange(index, len);
                return;
            }

            if(removeFirst)
            {
                if (len == 1)
                {
                    ulong removeda = ard[index];

                    if (index > 0 && ard[index - 1] == removeda)
                        amb.Add(new Tuple<int, ulong, bool>(index - 1, removeda, true));
                    if (index + 1 < ard.Count && ard[index + 1] == removeda)
                        amb.Add(new Tuple<int, ulong, bool>(index, removeda, true));
                }
                else
                {
                    ulong removeda0 = ard[index];
                    ulong removeda1 = ard[index + len - 1];

                    if (index > 0 && ard[index - 1] == removeda0)
                        amb.Add(new Tuple<int, ulong, bool>(index - 1, removeda0, true));
                    if (index + len < ard.Count && ard[index + len] == removeda1)
                        amb.Add(new Tuple<int, ulong, bool>(index, removeda1, true));
                }

                ard.RemoveRange(index, len);
                ar.RemoveRange(index, len);
                return;
            }

            if(removeSecond)
            {
                if (len == 1)
                {
                    ulong removedb = brd[index];

                    if (index > 0 && brd[index - 1] == removedb)
                        amb.Add(new Tuple<int, ulong, bool>(index - 1, removedb, false));
                    if (index + 1 < brd.Count && brd[index + 1] == removedb)
                        amb.Add(new Tuple<int, ulong, bool>(index, removedb, false));
                }
                else
                {
                    ulong removedb0 = brd[index];
                    ulong removedb1 = brd[index + len - 1];

                    if (index > 0 && brd[index - 1] == removedb0)
                        amb.Add(new Tuple<int, ulong, bool>(index - 1, removedb0, false));
                    if (index + len < brd.Count && brd[index + len] == removedb1)
                        amb.Add(new Tuple<int, ulong, bool>(index, removedb1, false));
                }

                brd.RemoveRange(index, len);
                br.RemoveRange(index, len);
                return;
            }
        }

        private sealed class IDARefData
        {
            internal ulong Data;
            internal IDAReference Ref;
            internal bool Remove;
        }

        private readonly List<IDARefData> ar = new List<IDARefData>();
        private readonly List<IDARefData> br = new List<IDARefData>();
        private readonly List<Core.Utility.ListDifferenceEntry> diff = new List<Utility.ListDifferenceEntry>();
    }*/

    internal sealed class IDAPass_RefOut : IDAPass
    {
        internal override byte Id
        {
            get
            {
                return ConstId;
            }
        }

        internal static readonly byte ConstId = 3;

        internal override bool Initialize()
        {
            this.Register(IDAPassListenerTypes.OnMatchMade);
            this.Register(IDAPassListenerTypes.OnComparisonIndexChanged);
            return true;
        }

        internal override void OnMatchMade(IDAObjMatch match, IDAPass pass)
        {
            if(this.IsAllowed)
                this.Todo.Add(match);
        }

        private List<IDAObjMatch> Todo = new List<IDAObjMatch>();
        private List<IDAObjMatch> Alt = new List<IDAObjMatch>();
        private bool IsAllowed = true;

        internal override void OnComparisonParameterChanged()
        {
            this.Todo.Clear();

            this.IsAllowed = this.Migration.CurrentComparisonParameters.AllowRefOutPass;
            if (!this.IsAllowed)
                return;

            foreach (var o in this.Migration.Versions[0].AssignedObjects.List)
                this.Todo.Add(o.Value.Match);
        }

        internal override bool Do()
        {
            if (!this.IsAllowed)
                return false;

            bool did = false;
            while (true)
            {
                var ls = this.Todo;
                if (ls.Count == 0)
                    return did;

                // Swap out queue.
                {
                    this.Todo = this.Alt;
                    this.Alt = ls;
                }

                foreach (var m in ls)
                {
                    if (this.ProcessMatch(m))
                        did = true;
                }

                ls.Clear();
            }
        }

        private bool ProcessMatch(IDAObjMatch m)
        {
            var a = m.First;
            var b = m.Second;

            bool did = false;
            int ai = 0;
            int bi = 0;
            bool cachedYes = false;

            while (true)
            {
                bool c = cachedYes;
                cachedYes = false;
                var res = this.ProcessReference(a.OutReferences, b.OutReferences, ref ai, ref bi, c);
                if ((res & ProcessResult.DidAssign) != ProcessResult.None)
                    did = true;
                if ((res & ProcessResult.Stop) != ProcessResult.None)
                    break;
                if ((res & ProcessResult.NextRefAmbIsCachedYes) != ProcessResult.None)
                    cachedYes = true;
            }

            return did;
        }

        [Flags]
        private enum ProcessResult : byte
        {
            None = 0,

            DidAssign = 1,

            Stop = 2,

            NextRefAmbIsCachedYes = 4,
        }

        private ProcessResult ProcessReference(List<IDAReference> als, List<IDAReference> bls, ref int ai, ref int bi, bool cachedYes)
        {
            if (ai >= als.Count || bi >= bls.Count)
                return ProcessResult.Stop;

            var a = als[ai];
            var b = bls[bi];

            if (this.IsBadReference(a))
            {
                if (this.IsBadReference(b))
                {
                    ai++;
                    bi++;
                    return ProcessResult.None;
                }

                ai++;
                return ProcessResult.None;
            }
            else if (this.IsBadReference(b))
            {
                bi++;
                return ProcessResult.None;
            }

            if (!cachedYes)
            {
                bool r00 = this.CheckReference(a, b, true);
                if (!r00)
                {
                    int aleft = als.Count - ai;
                    int bleft = bls.Count - bi;
                    var anext = aleft >= 2 ? als[ai + 1] : null;
                    var bnext = bleft >= 2 ? bls[bi + 1] : null;

                    bool r10 = anext != null && this.CheckReference(anext, b, true);
                    bool r01 = bnext != null && this.CheckReference(a, bnext, true);
                    bool r11 = anext != null && bnext != null && this.CheckReference(anext, bnext, true);

                    if (r11)
                    {
                        // This is too ambiguous if either of these is set.
                        if (r10 || r01)
                            return ProcessResult.Stop;

                        ai++;
                        bi++;
                        return ProcessResult.NextRefAmbIsCachedYes;
                    }

                    if (r01)
                    {
                        // Too ambiguous.
                        if (r10 || aleft >= bleft)
                            return ProcessResult.Stop;
                        bi++;
                        return ProcessResult.NextRefAmbIsCachedYes;
                    }

                    if (r10)
                    {
                        // Too ambiguous.
                        if (r01 || bleft >= aleft)
                            return ProcessResult.Stop;
                        ai++;
                        return ProcessResult.NextRefAmbIsCachedYes;
                    }

                    // Too ambiguous to do anything about this.
                    return ProcessResult.Stop;
                }
            }

            var ao = a.Target.Object;
            var bo = b.Target.Object;
            if (ao != null && bo != null && ao.Match == null && bo.Match == null && ao.Comparison.CompareWithCurrentParameters(bo.Comparison, false))
            {
                this.Migration.AssignMatch(ao, bo);
                ai++;
                bi++;
                return ProcessResult.DidAssign;
            }

            ai++;
            bi++;
            return ProcessResult.None;
        }

        private bool IsBadReference(IDAReference a)
        {
            return a.Target.Object == null;
        }

        private bool CheckReference(IDAReference a, IDAReference b, bool forAmbiguous)
        {
            if (a.Data != b.Data)
                return false;

            var ao = a.Target.Object;
            var bo = b.Target.Object;

            if (ao == null)
                return bo == null;
            if (bo == null)
                return false;

            var am = ao.Match;
            var bm = bo.Match;
            if (am != null)
                return am == bm;
            if (bm != null)
                return false;

            return ao.Comparison.CompareWithCurrentParameters(bo.Comparison, forAmbiguous);
        }
    }

    internal sealed class IDAPass_RefIn : IDAPass
    {
        internal override byte Id
        {
            get
            {
                return ConstId;
            }
        }

        internal static readonly byte ConstId = 5;

        internal override bool Initialize()
        {
            this.Register(IDAPassListenerTypes.OnMatchMade);
            this.Register(IDAPassListenerTypes.OnComparisonIndexChanged);
            return true;
        }

        internal override void OnMatchMade(IDAObjMatch match, IDAPass pass)
        {
            if(this.IsAllowed)
                this.Todo.Add(match);
        }

        internal override void OnComparisonParameterChanged()
        {
            this.Todo.Clear();

            this.IsAllowed = this.Migration.CurrentComparisonParameters.AllowRefInPass;
            if (!this.IsAllowed)
                return;

            foreach (var o in this.Migration.Versions[0].AssignedObjects.List)
                this.Todo.Add(o.Value.Match);
        }

        private readonly List<IDAObjMatch> Todo = new List<IDAObjMatch>();
        private bool IsAllowed = true;

        internal override bool Do()
        {
            if (!this.IsAllowed)
                return false;

            var ls = this.Todo;
            if (ls.Count == 0)
                return false;

            for (int i = 0; i < ls.Count; i++)
            {
                var m = ls[i];
                if (this.ProcessMatch(m))
                {
                    ls.RemoveRange(0, i + 1);
                    return true;
                }
            }

            ls.Clear();
            return false;
        }

        private bool ProcessMatch(IDAObjMatch m)
        {
            this._amb0.Clear();
            this._amb1.Clear();

            bool did = false;
            for (int i = 0; i < m.First.InReferences.Count; i++)
            {
                var a = m.First.InReferences[i];
                if (this.IsIgnoredReference(a))
                    continue;

                if (this.IsAmbiguous(0, i, a, m.First))
                    continue;

                ulong data = a.Data;
                IDAObject possible = null;
                for (int j = 0; j < m.Second.InReferences.Count; j++)
                {
                    var b = m.Second.InReferences[j];
                    if (b.Data != data)
                        continue;

                    if (this.IsIgnoredReference(b))
                        continue;

                    if (this.IsAmbiguous(1, j, b, m.Second))
                        continue;

                    if (a.Source.Object.Comparison.CompareWithCurrentParameters(b.Source.Object.Comparison, true))
                    {
                        if (possible != null)
                        {
                            possible = null;
                            break;
                        }

                        possible = b.Source.Object;
                    }
                }

                if (possible == null)
                    continue;

                if (a.Source.Object.Comparison.CompareWithCurrentParameters(possible.Comparison, false))
                {
                    this.Migration.AssignMatch(a.Source.Object, possible);
                    did = true;
                }
            }

            return did;
        }

        private bool IsIgnoredReference(IDAReference r)
        {
            var ro = r.Source.Object;
            return ro == null || ro.Match != null;
        }

        private readonly List<ulong> _amb0 = new List<ulong>();
        private readonly List<ulong> _amb1 = new List<ulong>();

        private bool IsAmbiguous(int objIndex, int refIndex, IDAReference r, IDAObject owner)
        {
            int v = this._getAmbiguous(objIndex, refIndex);
            if (v != 0)
                return v > 0;

            var ro = r.Source.Object;
            for (int i = 0; i < owner.InReferences.Count; i++)
            {
                if (i == refIndex)
                    continue;

                var r2 = owner.InReferences[i];
                if (r2.Data != r.Data)
                    continue;

                if (this.IsIgnoredReference(r2))
                    continue;

                var ro2 = r2.Source.Object;
                if (ro.Comparison.CompareWithCurrentParameters(ro2.Comparison, true))
                {
                    this._setAmbiguous(objIndex, refIndex, true);
                    this._setAmbiguous(objIndex, i, true);
                    return true;
                }
            }

            this._setAmbiguous(objIndex, refIndex, false);
            return false;
        }

        private int _getAmbiguous(int objIndex, int refIndex)
        {
            var ls = objIndex == 0 ? this._amb0 : this._amb1;

            int listIndex = refIndex / 32;
            if (listIndex >= ls.Count)
                return 0;

            refIndex = (refIndex % 32) * 2;
            ulong ux = (ls[listIndex] >> refIndex) & 3;
            if (ux == 0)
                return 0;
            if (ux == 1)
                return 1;
            return -1;
        }

        private void _setAmbiguous(int objIndex, int refIndex, bool value)
        {
            var ls = objIndex == 0 ? this._amb0 : this._amb1;
            int listIndex = refIndex / 32;
            while (listIndex >= ls.Count)
                ls.Add(0);

            refIndex = (refIndex % 32) * 2;
            ulong mask = value ? (ulong)1 : 2;
            mask <<= refIndex;
            ls[listIndex] |= mask;
        }
    }

    internal sealed class IDAPass_CustomStringAssociation : IDAPass
    {
        internal override byte Id
        {
            get
            {
                return ConstId;
            }
        }

        internal static readonly byte ConstId = 6;

        internal override void OnComparisonParameterChanged()
        {
            if (this.Migration.ComparisonParametersIndex == 0)
                this._lastTriedIndex = -1;
        }

        internal override bool Initialize()
        {
            this.Register(IDAPassListenerTypes.OnComparisonIndexChanged);
            return true;
        }

        private int _lastTriedIndex = -1;

        internal override bool Do()
        {
            if (this._lastTriedIndex >= this.Migration.ComparisonParametersIndex)
                return false;

            this._lastTriedIndex = this.Migration.ComparisonParametersIndex;

            bool did = false;
            Dictionary<string, List<IDAObject>> mapPrev = new Dictionary<string, List<IDAObject>>();
            Dictionary<string, List<IDAObject>> mapNext = new Dictionary<string, List<IDAObject>>();

            for (int i = 0; i < this.Migration.Versions.Length; i++)
            {
                var v = this.Migration.Versions[i];
                foreach (var o in v.Objects)
                {
                    if (string.IsNullOrEmpty(o.CustomStringAssociation))
                        continue;

                    if (o.Match != null)
                        continue;

                    List<IDAObject> ls = null;
                    var map = i == 0 ? mapPrev : mapNext;
#if DEBUG
                    var e = o.Comparison.GetEntry(IDAObjectComparisonData.IDAObjectComparisonTypes.CustomString);
                    if (e.Data.Length == 0) throw new InvalidOperationException();
#endif
                    if (!map.TryGetValue(o.CustomStringAssociation, out ls))
                    {
                        ls = new List<IDAObject>(4);
                        map[o.CustomStringAssociation] = ls;
                    }
                    ls.Add(o);
                }
            }

            var mapPrevLs = mapPrev.ToList();
            mapPrevLs.Sort((u, v) => u.Key.CompareTo(v.Key)); // Reason we do this is because when adjusting in order of offset then readjusting segment
            // indices takes very long time due to always having to search until end of segment for next obj
            foreach (var pair in mapPrevLs)
            {
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    var a = pair.Value[i];

                    bool ambiguous = false;
                    for (int j = 0; j < pair.Value.Count; j++)
                    {
                        if (i == j)
                            continue;

                        var b = pair.Value[j];
                        if (a.Comparison.CompareWithCurrentParameters(b.Comparison, true))
                        {
                            ambiguous = true;
                            break;
                        }
                    }

                    if (ambiguous)
                        continue;

                    List<IDAObject> ls = null;
                    if (!mapNext.TryGetValue(pair.Key, out ls))
                        continue;

                    IDAObject possible = null;
                    for (int j = 0; j < ls.Count; j++)
                    {
                        var b = ls[j];

                        // Possible if we matched just now.
                        if (b.Match != null)
                            continue;

                        if (a.Comparison.CompareWithCurrentParameters(b.Comparison, true))
                        {
                            if (possible != null)
                            {
                                possible = null;
                                break;
                            }

                            possible = b;
                        }
                    }

                    if (possible != null && a.Comparison.CompareWithCurrentParameters(possible.Comparison, false))
                    {
                        this.Migration.AssignMatch(a, possible);
                        did = true;
                    }
                }
            }

            return did;
        }
    }

    // This pass checks unambiguously same name and somewhat same size of data only (Not checking actual data content)
    internal sealed class IDAPass_NamedExact : IDAPass
    {
        internal override byte Id
        {
            get
            {
                return ConstId;
            }
        }

        internal static readonly byte ConstId = 12;

        internal override bool Initialize()
        {
            return true;
        }

        private bool _once = false;

        internal override bool Do()
        {
            if (_once)
                return false;
            _once = true;

            bool did = false;
            List<KeyValuePair<IDAObject, IDAObject>> lsall = new List<KeyValuePair<IDAObject, IDAObject>>();
            {
                Dictionary<string, List<IDAObject>> mapPrev = new Dictionary<string, List<IDAObject>>();
                Dictionary<string, List<IDAObject>> mapNext = new Dictionary<string, List<IDAObject>>();
                List<KeyValuePair<IDAObject, IDAObject>> lsb = new List<KeyValuePair<IDAObject, IDAObject>>();

                for (int i = 0; i < this.Migration.Versions.Length; i++)
                {
                    var v = this.Migration.Versions[i];
                    foreach (var o in v.Objects)
                    {
                        if (string.IsNullOrEmpty(o.CustomStringAssociation))
                            continue;

                        if (o.Match != null)
                            continue;

                        List<IDAObject> ls = null;
                        var map = i == 0 ? mapPrev : mapNext;
                        if (!map.TryGetValue(o.CustomStringAssociation, out ls))
                        {
                            ls = new List<IDAObject>(4);
                            map[o.CustomStringAssociation] = ls;
                        }
                        ls.Add(o);
                    }
                }

                foreach(var prevPair in mapPrev)
                {
                    if (prevPair.Value.Count != 1)
                        continue;

                    List<IDAObject> nextLs;
                    if (!mapNext.TryGetValue(prevPair.Key, out nextLs) || nextLs.Count != 1)
                        continue;

                    var a = prevPair.Value[0];
                    var b = nextLs[0];

                    lsb.Add(new KeyValuePair<IDAObject, IDAObject>(a, b));
                }

                var rnd = new Random(); // speed up the range assignment later
                while(lsb.Count != 0)
                {
                    int choseni = rnd.Next(0, lsb.Count);
                    var p = lsb[choseni];
                    lsall.Add(p);
                    lsb.RemoveAt(choseni);
                }
            }

            foreach(var pair in lsall)
            {
                var a = pair.Key;
                var b = pair.Value;

                if (a.Match != null || b.Match != null)
                    continue;

                a.Comparison.Refresh();
                b.Comparison.Refresh();

                bool ok = true;
                for (int i = 0; i < 3; i++)
                {
                    int az = a.Comparison.GetEntrySize((IDAObjectComparisonData.IDAObjectComparisonTypes)i);
                    int bz = b.Comparison.GetEntrySize((IDAObjectComparisonData.IDAObjectComparisonTypes)i);

                    int mcount = Math.Min(az, bz);
                    int msize = Math.Max(az, bz);

                    if (msize <= 0)
                        continue;

                    double ratio = (double)mcount / (double)msize;
                    if(ratio < 0.9)
                    {
                        ok = false;
                        break;
                    }
                }

                if (!ok)
                    continue;

                this.Migration.AssignMatch(a, b);
                did = true;
            }
            
            return did;
        }
    }

    internal sealed class IDAPass_BigThings : IDAPass
    {
        internal override byte Id
        {
            get
            {
                return ConstId;
            }
        }

        internal static readonly byte ConstId = 13;

        private bool _once = false;

        internal override bool Do()
        {
            if (this.Migration.ComparisonParametersIndex != 1)
                return false;

            if (_once)
                return false;
            _once = true;

            bool did = false;
            List<Tuple<IDAObject, IDAObject, int>> lsall = new List<Tuple<IDAObject, IDAObject, int>>();
            {
                {
                    var v = this.Migration.Versions[0];
                    var v2 = this.Migration.Versions[1];
                    foreach (var o in v.Objects)
                    {
                        if (o.Match != null)
                            continue;

                        o.Comparison.Refresh();

                        int bigIndex = 0;
                        int bigAmt = 0;
                        for(int j = 0; j < 3; j++)
                        {
                            int a = o.Comparison.GetEntrySize((IDAObjectComparisonData.IDAObjectComparisonTypes)j);
                            if(a > bigAmt)
                            {
                                bigIndex = j;
                                bigAmt = a;
                            }
                        }

                        if (bigAmt < 10000)
                            continue;

                        IDAObject of1 = null;
                        foreach(var o2 in v2.Objects)
                        {
                            if (Math.Abs(o.IndexPctInSegment - o2.IndexPctInSegment) >= 1.0)
                                continue;

                            o2.Comparison.Refresh();

                            int amt2 = o2.Comparison.GetEntrySize((IDAObjectComparisonData.IDAObjectComparisonTypes)bigIndex);
                            int q0 = Math.Min(amt2, bigAmt);
                            int q1 = Math.Max(amt2, bigAmt);

                            double r0 = (double)q0 / (double)q1;
                            if(r0 >= 0.9 && o.Comparison.CompareWithCurrentParameters(o2.Comparison, true))
                            {
                                if(of1 != null)
                                {
                                    of1 = null;
                                    break;
                                }

                                of1 = o2;
                            }
                        }

                        if (of1 != null)
                            lsall.Add(new Tuple<IDAObject, IDAObject, int>(o, of1, bigAmt));
                    }
                }
            }

            lsall.Sort((u, v) => v.Item3.CompareTo(u.Item3));

            foreach (var pair in lsall)
            {
                var a = pair.Item1;
                var b = pair.Item2;

                if (a.Match != null || b.Match != null)
                    continue;

                if (!a.Comparison.CompareWithCurrentParameters(b.Comparison, false))
                    continue;

                this.Migration.AssignMatch(a, b);
                did = true;
            }

            return did;
        }
    }

    internal sealed class IDAPass_SlowScanExact : IDAPass
    {
        internal override byte Id
        {
            get
            {
                return ConstId;
            }
        }

        internal static readonly byte ConstId = 4;

        internal override bool Initialize()
        {
            this.Register(IDAPassListenerTypes.OnComparisonIndexChanged);

            return true;
        }

        internal override void OnComparisonParameterChanged()
        {
            if (this.Migration.ComparisonParametersIndex == 0)
            {
                foreach (var o in this.Migration.Versions[0].UnassignedObjects.List)
                    o.Value.Flags &= ~IDAObjectFlags.TriedSlowScan;

                this.ResetThis();
            }
        }

        internal override bool Do()
        {
            if (this.Migration.ComparisonParametersIndex != 0)
                return false;

            while (this.Pass < this.MaxPass)
            {
                double maxNormalDiff = this.Migration.ComparisonParametersSetup[this.Migration.ComparisonParametersIndex].MaxIndexDifferenceForCompare;

                foreach (var node in this.Migration.Versions[0].UnassignedObjects.List)
                {
                    var obj = node.Value;

                    if ((obj.Flags & IDAObjectFlags.TriedSlowScan) != IDAObjectFlags.None)
                        continue;

                    var asm = obj.Asm.Data;
                    if (asm == null || asm.Length < this.MinAsmCount || asm.Length > this.MaxAsmCount)
                        continue;

                    obj.Flags |= IDAObjectFlags.TriedSlowScan;

                    obj.FindAllMatches(this.Matches, false, true);
                    if (this.Matches.Count != 0)
                    {
                        this.Matches.Clear();
                        continue;
                    }

                    obj.FindAllMatches(this.Matches, true, true);
                    if (this.Matches.Count == 0)
                        continue;

                    if (this.Matches.Count > 1)
                    {
                        this.Matches.Clear();
                        continue;
                    }

                    var other = this.Matches[0];
                    this.Matches.Clear();

                    // This is still too far away to be proper match right now.
                    if (!obj.Comparison.CompareIndex(other.Comparison, maxNormalDiff))
                        continue;

                    this.Migration.AssignMatch(obj, other);
                    return true;
                }

                this.IncPass();
            }

            return false;
        }

        private int Pass = 0;
        private int MaxPass = 30;
        private int MinAsmCount = 100;
        private int MaxAsmCount = int.MaxValue;

        private readonly List<IDAObject> Matches = new List<IDAObject>();

        private void ResetThis()
        {
            this.Pass = 0;
            this.MinAsmCount = 100;
            this.MaxAsmCount = int.MaxValue;
        }

        private void IncPass()
        {
            this.Pass++;
            this.MaxAsmCount = this.MinAsmCount - 1;
            this.MinAsmCount -= 2;
        }
    }

    internal sealed class IDAPass_AssignRange : IDAPass
    {
        internal override byte Id
        {
            get
            {
                return ConstId;
            }
        }

        internal static readonly byte ConstId = 7;

        internal override bool Initialize()
        {
            this.Register(IDAPassListenerTypes.OnComparisonIndexChanged);
            this.Register(IDAPassListenerTypes.OnMatchMade);
            return true;
        }

        private readonly List<IDAObject> Todo1 = new List<IDAObject>();
        private readonly List<IDAObject> Todo2 = new List<IDAObject>();

        private void AddObject(IDAObject obj)
        {
            var sego = obj.Segment.Objects;
            int oxi = obj.IndexInSegment;
            if (oxi == sego.Count - 1)
                return;

            if (sego[oxi + 1].Match != null)
                return;

#if DEBUG
            if ((obj.Flags & IDAObjectFlags.InRangeTodo) != IDAObjectFlags.None) throw new InvalidOperationException();
#endif
            obj.Flags |= IDAObjectFlags.InRangeTodo;
            if (obj.Segment.IsExecutable)
                this.Todo1.Add(obj);
            else
                this.Todo2.Add(obj);
        }

        internal override void OnMatchMade(IDAObjMatch match, IDAPass pass)
        {
            if (this.IsAssigning)
                return;

            var o = match.First;

            var b = o._TriedSectionMatchBegin;
            if (b != null && b._TriedSectionMatchLength != -1)
            {
                b._TriedSectionMatchLength = -1;
                this.AddObject(b);
            }
            o._TriedSectionMatchLength = -1;
            this.AddObject(o);
        }

        internal override void OnComparisonParameterChanged()
        {
            this.Todo1.Clear();
            this.Todo2.Clear();

            var ls = this.Migration.Versions[0].Objects;
            for(int i = 0; i < ls.Count; i++)
            {
                var o = ls[i];
                o._TriedSectionMatchBegin = null;
                o._TriedSectionMatchLength = -1;
                if (o.Match != null)
                {
                    o.Flags &= ~IDAObjectFlags.InRangeTodo;
                    this.AddObject(o);
                }
            }
        }

        internal override bool Do()
        {
            var ls = this.Todo1;
            for (int i = ls.Count - 1; i >= 0; i--)
            {
                var o = ls[i];
                ls.RemoveAt(i);
                o.Flags &= ~IDAObjectFlags.InRangeTodo;

                if (this.TryObject(o))
                    return true;
            }

            ls = this.Todo2;
            for (int i = ls.Count - 1; i >= 0; i--)
            {
                var o = ls[i];
                ls.RemoveAt(i);
                o.Flags &= ~IDAObjectFlags.InRangeTodo;

                if (this.TryObject(o))
                    return true;
            }

            return false;
        }

        private static bool _debugModeRange = false;

        private bool TryObject(IDAObject o)
        {
            /*if(o.Begin == 0x85FA0)
            {
                _debugModeRange = true;
            }*/

            int fc;
            if ((fc = this.FillRange(o, true)) == 0)
                return false;

            int sc = this.FillRange(o.Match.Second, false);
            if (fc != sc)
            {
                this.FirstRange.Clear();
                this.SecondRange.Clear();
                return false;
            }

            double diff1 = Math.Abs(this.FirstRange[this.FirstRange.Count - 1].IndexPctInSegment - this.FirstRange[0].IndexPctInSegment);
            double diff2 = Math.Abs(this.SecondRange[this.SecondRange.Count - 1].IndexPctInSegment - this.SecondRange[0].IndexPctInSegment);
            if (diff1 > this.Migration.CurrentComparisonParameters.MaxRangeAssignPctSize || diff2 > this.Migration.CurrentComparisonParameters.MaxRangeAssignPctSize || sc > this.Migration.CurrentComparisonParameters.MaxRangeAssignCount)
            {
                this.FirstRange.Clear();
                this.SecondRange.Clear();
                return false;
            }

            bool ok = true;
            for (int i = 0; i < fc; i++)
            {
                var a = this.FirstRange[i];
                var b = this.SecondRange[i];

                if (!a.Comparison.CompareWithCurrentParameters(b.Comparison, false))
                {
                    ok = false;
                    break;
                }
            }

            if (!ok)
            {
                this.FirstRange.Clear();
                this.SecondRange.Clear();
                return false;
            }

            this.IsAssigning = true;
            for (int i = 0; i < fc; i++)
                this.Migration.AssignMatch(this.FirstRange[i], this.SecondRange[i]);
            this.IsAssigning = false;

            this.FirstRange.Clear();
            this.SecondRange.Clear();
            return true;
        }

        private bool IsAssigning = false;

        private int FillRange(IDAObject begin, bool first)
        {
            var ls = first ? this.FirstRange : this.SecondRange;
            var s = begin.Segment;
            int c = s.Objects.Count;
            int i = begin.IndexInSegment + 1;

            while (i < c)
            {
                var o = s.Objects[i++];
                if (o.Match != null)
                    break;

                ls.Add(o);
            }

            int len = ls.Count;
            if (first)
            {
                begin._TriedSectionMatchBegin = begin;
                begin._TriedSectionMatchLength = len;
                foreach (var o in ls)
                {
                    o._TriedSectionMatchBegin = begin;
                    o._TriedSectionMatchLength = len;
                }
            }
            return len;
        }

        private readonly List<IDAObject> FirstRange = new List<IDAObject>();
        private readonly List<IDAObject> SecondRange = new List<IDAObject>();
    }

    internal sealed class IDAPass_AssignRangeSegmentBorder : IDAPass
    {
        internal override byte Id
        {
            get
            {
                return ConstId;
            }
        }

        internal static readonly byte ConstId = 9;

        internal override bool Do()
        {
            foreach (var s in this.Migration.Versions[0].Segments)
            {
                if (this.TryAssign(s))
                    return true;
            }

            return false;
        }

        private readonly List<IDAObject> FirstRange = new List<IDAObject>();
        private readonly List<IDAObject> SecondRange = new List<IDAObject>();
        private IDAObject CurrentEnd = null;

        private bool TryAssign(IDASegment s)
        {
            int r = this.TryBeginAssign(s);
            if ((r & 6) == 0)
                r |= this.TryEndAssign(s);
            return (r & 2) != 0;
        }

        // ret:
        // 1 - did the thing
        // 2 - actually assigned something
        // 4 - did all the things

        private int TryBeginAssign(IDASegment s)
        {
            var ls = s.Objects;
            int c = ls.Count;

            if (c == 0)
                return 0;

            var first = ls[0];
            if (first.Match != null)
                return 0;

            int i;
            for (i = 0; i < c; i++)
            {
                var o = ls[i];
                if (o.Match != null)
                {
                    this.CurrentEnd = o;
                    break;
                }

                this.FirstRange.Add(o);
            }

            int ret = 1;
            if (i == c)
                ret |= 4;

            var os = s.Equivalent;
            if (os == null)
            {
                this.FirstRange.Clear();
                ret |= 4;
                return ret;
            }

            var ols = os.Objects;
            int oc = ols.Count;
            for (i = 0; i < oc; i++)
            {
                var o = ols[i];
                if (o.Match != null)
                {
                    if (o.Match.First != this.CurrentEnd)
                    {
                        this.FirstRange.Clear();
                        this.SecondRange.Clear();
                        return ret;
                    }

                    break;
                }

                this.SecondRange.Add(o);
            }

            if (this.FirstRange.Count == 0 || this.FirstRange.Count != this.SecondRange.Count)
            {
                this.FirstRange.Clear();
                this.SecondRange.Clear();
                return ret;
            }

            bool ok = true;
            for (i = 0; i < this.FirstRange.Count; i++)
            {
                var a = this.FirstRange[i];
                var b = this.SecondRange[i];

                if (!a.Comparison.CompareWithCurrentParameters(b.Comparison, false))
                {
                    ok = false;
                    break;
                }
            }

            if (!ok)
            {
                this.FirstRange.Clear();
                this.SecondRange.Clear();
                return ret;
            }

            for (i = 0; i < this.FirstRange.Count; i++)
            {
                var a = this.FirstRange[i];
                var b = this.SecondRange[i];

                this.Migration.AssignMatch(a, b);
            }
            ret |= 2;
            this.FirstRange.Clear();
            this.SecondRange.Clear();
            return ret;
        }

        private int TryEndAssign(IDASegment s)
        {
            var ls = s.Objects;
            int c = ls.Count;

            if (c == 0)
                return 0;

            var first = ls[ls.Count - 1];
            if (first.Match != null)
                return 0;

            int i;
            for (i = c - 1; i >= 0; i--)
            {
                var o = ls[i];
                if (o.Match != null)
                {
                    this.CurrentEnd = o;
                    break;
                }

                this.FirstRange.Add(o);
            }

            int ret = 1;
#if DEBUG
            if (i == -1) // This should not happen at this point
                //ret |= 4;
                throw new InvalidOperationException();
#endif

            var os = s.Equivalent;
            if (os == null)
            {
                this.FirstRange.Clear();
                return ret;
            }

            var ols = os.Objects;
            int oc = ols.Count;
            for (i = oc - 1; i >= 0; i--)
            {
                var o = ols[i];
                if (o.Match != null)
                {
                    if (o.Match.First != this.CurrentEnd)
                    {
                        this.FirstRange.Clear();
                        this.SecondRange.Clear();
                        return ret;
                    }

                    break;
                }

                this.SecondRange.Add(o);
            }

            if (this.FirstRange.Count == 0 || this.FirstRange.Count != this.SecondRange.Count)
            {
                this.FirstRange.Clear();
                this.SecondRange.Clear();
                return ret;
            }

            bool ok = true;
            for (i = 0; i < this.FirstRange.Count; i++)
            {
                var a = this.FirstRange[i];
                var b = this.SecondRange[i];

                if (!a.Comparison.CompareWithCurrentParameters(b.Comparison, false))
                {
                    ok = false;
                    break;
                }
            }

            if (!ok)
            {
                this.FirstRange.Clear();
                this.SecondRange.Clear();
                return ret;
            }

            for (i = 0; i < this.FirstRange.Count; i++)
            {
                var a = this.FirstRange[i];
                var b = this.SecondRange[i];

                this.Migration.AssignMatch(a, b);
            }
            ret |= 2;
            this.FirstRange.Clear();
            this.SecondRange.Clear();
            return ret;
        }
    }

    internal sealed class IDAPass_AggressiveVTableMatch : IDAPass
    {
        internal override byte Id
        {
            get
            {
                return ConstId;
            }
        }

        internal static readonly byte ConstId = 11;

        internal override int MinComparisonParameterIndex
        {
            get
            {
                return 2;
            }
        }

        private bool Did = false;

        internal override bool Do()
        {
            if (this.Did)
                return false;

            this.Did = true;
            Dictionary<string, Tuple<List<IDAObject>, List<IDAObject>>> map = new Dictionary<string, Tuple<List<IDAObject>, List<IDAObject>>>();
            foreach (var node in this.Migration.Versions[0].UnassignedObjects.List)
            {
                var o = node.Value;
                string custom = o.CustomStringAssociation;
                if (!string.IsNullOrEmpty(custom) && custom.Contains("`vftable'"))
                {
                    Tuple<List<IDAObject>, List<IDAObject>> t = null;
                    if (!map.TryGetValue(custom, out t))
                    {
                        t = new Tuple<List<IDAObject>, List<IDAObject>>(new List<IDAObject>(), new List<IDAObject>());
                        map[custom] = t;
                    }

                    t.Item1.Add(o);
                }
            }
            foreach (var node in this.Migration.Versions[1].UnassignedObjects.List)
            {
                var o = node.Value;
                string custom = o.CustomStringAssociation;
                if (!string.IsNullOrEmpty(custom) && custom.Contains("`vftable'"))
                {
                    Tuple<List<IDAObject>, List<IDAObject>> t = null;
                    if (!map.TryGetValue(custom, out t))
                    {
                        t = new Tuple<List<IDAObject>, List<IDAObject>>(new List<IDAObject>(), new List<IDAObject>());
                        map[custom] = t;
                    }

                    t.Item2.Add(o);
                }
            }

            bool did = false;
            foreach (var pair in map)
            {
                var t = pair.Value;

                // Ambiguous or missing.
                if (t.Item1.Count != 1 || t.Item2.Count != 1)
                    continue;

                var a = t.Item1[0];
                var b = t.Item2[0];
#if DEBUG
                // This check is not really necessary it's allowed to be unknown or global, just here for debugging something.
                //if (a.Type != IDAObjectTypes.VTable || b.Type != IDAObjectTypes.VTable) throw new InvalidOperationException();
#endif
                this.Migration.AssignMatch(a, b);
                did = true;
            }

            return did;
        }
    }

    internal sealed class IDAPass_IncrementParameterIndex : IDAPass
    {
        internal static readonly byte ConstId = 2;

        internal override byte Id
        {
            get
            {
                return ConstId;
            }
        }

        private int TriedSamePassTimes = 0;

        internal override bool Do()
        {
            int maxIndex = this.Migration.ComparisonParametersSetup.Count - 2;
            if (!IDAMigrate.SeparateRefPass)
                maxIndex++;

            if(IDAMigrate.DEBUG_OFFSET1 != 0 && IDAMigrate.DEBUG_OFFSET2 != 0)
            {
                var a = this.Migration.Versions[0].GetObject(IDAMigrate.DEBUG_OFFSET1);
                var b = this.Migration.Versions[1].GetObject(IDAMigrate.DEBUG_OFFSET2);

                if (a != null && b != null)
                {
                    bool amb = a.Comparison.CompareWithCurrentParameters(b.Comparison, true);
                    bool comp = a.Comparison.CompareWithCurrentParameters(b.Comparison, false);

                    IDAListener.Write("Compared debug offsets: ambiguous=" + amb + ", comparison=" + comp);
                }
                else
                    IDAListener.Write("Failed to compare debug offsets, because a=" + (a != null) + ", b=" + (b != null));
            }

            if (this.Migration.ComparisonParametersIndex < maxIndex)
            {
                /*if (this.Migration.ComparisonParametersIndex == 1 && this.TriedSamePassTimes < 1)
                {
                    this.TriedSamePassTimes++;
                    IDAListener.Write("Redoing (" + this.TriedSamePassTimes + ") comparison parameter index " + this.Migration.ComparisonParametersIndex + " ...");
                }
                else*/
                {
                    this.TriedSamePassTimes = 0;
                    this.Migration.ComparisonParametersIndex++;
                    IDAListener.Write("Incrementing comparison parameter index to " + this.Migration.ComparisonParametersIndex + " ...");
                }
                foreach (var p in this.Migration.PassListeners[(int)IDAPassListenerTypes.OnComparisonIndexChanged])
                    p.OnComparisonParameterChanged();
                return true;
            }

            return false;
        }
    }

    internal sealed class IDAPass_IncrementParameterIndexFinal : IDAPass
    {
        internal static readonly byte ConstId = 10;

        internal override byte Id
        {
            get
            {
                return ConstId;
            }
        }

        private bool Did = false;

        internal override bool Do()
        {
            if (this.Did)
                return false;
            this.Did = true;

            int maxIndex = this.Migration.ComparisonParametersSetup.Count - 1;
            if (this.Migration.ComparisonParametersIndex < maxIndex)
            {
                this.Migration.ComparisonParametersIndex++;
                IDAListener.Write("Incrementing comparison parameter index to final " + this.Migration.ComparisonParametersIndex + " ...");
                foreach (var p in this.Migration.PassListeners[(int)IDAPassListenerTypes.OnComparisonIndexChanged])
                    p.OnComparisonParameterChanged();
                return true;
            }

            return false;
        }
    }

    internal sealed class IDAPass_RecalculateReferenceData : IDAPass
    {
        internal override byte Id
        {
            get
            {
                return ConstId;
            }
        }

        internal static readonly byte ConstId = 8;

        private bool Did = false;

        internal override bool Do()
        {
            // Still want to recalculate references also just before finishing so we can later determine if all objects were matched properly when writing result.
            bool goback = !this.Did && (IDAMigrate.SeparateRefPass || IDAMigrate.RecalculateAllPassAtEndAnyway);
            this.Did = true;
            this.Migration.IsAfterRefRecalc = true;

            if (IDAMigrate.SeparateRefPass)
            {
                for (int i = 0; i < this.Migration.Versions.Length; i++)
                {
                    foreach (var o in this.Migration.Versions[i].Objects)
                    {
                        o.Comparison.Invalidate(IDAObjectComparisonData.IDAObjectComparisonTypes.InRef, true);
                        o.Comparison.Invalidate(IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef, true);
                        o.Comparison.Refresh();
                    }
                }

                Utility.DistCachePerm.ResetCache((byte)IDAObjectComparisonData.IDAObjectComparisonTypes.InRef);
                Utility.DistCachePerm.ResetCache((byte)IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef);
            }

            if (!goback)
                return false;

            this.Migration.ComparisonParametersIndex = 0;

            IDAListener.Write("Resetting comparison index for reference recalculate ...");

            foreach (var p in this.Migration.PassListeners[(int)IDAPassListenerTypes.OnComparisonIndexChanged])
                p.OnComparisonParameterChanged();

            return true;
        }
    }

    internal sealed class StringDistanceCache
    {
        private readonly Dictionary<uint, ulong> Map = new Dictionary<uint, ulong>();

        private static uint MakeKey(int index, byte type)
        {
#if DEBUG
            if (index < 0 || index > 0x00FFFFFF)
                throw new ArgumentOutOfRangeException();
#endif

            uint key = type;
            key <<= 24;
            key |= (uint)index;
            return key;
        }

        internal static bool ShouldCache(byte type, int mySize, int otherSize)
        {
            return mySize > 10 || otherSize > 10;
        }

        internal int Get(int index, byte type, uint cacheCounter)
        {
            ulong v;
            if (this.Map.TryGetValue(MakeKey(index, type), out v))
            {
                uint ux = (uint)(v & 0xFFFFFFFF);
                if(cacheCounter == ux)
                {
                    v >>= 32;
                    ux = (uint)v;
                    return unchecked((int)ux);
                }
            }

            return -1;
        }

        internal void Set(int index, byte type, uint cacheCounter, int value)
        {
            ulong v = unchecked((uint)value);
            v <<= 32;
            v |= cacheCounter;
            this.Map[MakeKey(index, type)] = v;
        }
    }
}
