#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using EntityId = Kesa.Engine.Runtime.EntityId;

[BurstCompile(CompileSynchronously = true)]
public unsafe struct MoveJob : IJob
{
    [NativeDisableUnsafePtrRestriction] public Fixed* X;
    [NativeDisableUnsafePtrRestriction] public Fixed* V;
    [NativeDisableUnsafePtrRestriction] public Fixed* Out;
    [NativeDisableUnsafePtrRestriction] public int* Managed;
    public int Count;

    public void Execute()
    {
        bool managed = false;
        Discarded(ref managed);
        *Managed = managed ? 1 : 0;
        for (int i = 0; i < Count; i++) Out[i] = Fixed.FromRaw(X[i].Raw + V[i].Raw * 16 / 1000);
    }

    [BurstDiscard] private static void Discarded(ref bool managed) => managed = true;
}

public static unsafe class BulkCheck
{
    public const int N = 10000;
    public static int JobManaged = -1;

    private static void Say(string line) => UnityEngine.Debug.Log("KESA BULK: " + line);

    private static ContentLoadResult Content()
    {
        string root = Path.Combine(Application.temporaryCachePath, "kesa_mods");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(Path.Combine(root, "core/schema"));
        Directory.CreateDirectory(Path.Combine(root, "core/defs"));
        File.WriteAllText(Path.Combine(root, "core/mod.py"), "Mod(id=\"core\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
        File.WriteAllText(Path.Combine(root, "core/schema/types.py"),
            "component(\"Pos\", x=field(fixed, default=0.0), v=field(fixed, default=1.5), n=field(int, default=0))\ncomponent(\"Stun\", left=field(int, default=3))\ndeftype(\"Unit\", components=[\"Pos\"])\n");
        File.WriteAllText(Path.Combine(root, "core/defs/defs.py"),
            "Unit(\"Grunt\")\nUnit(\"Tank\")\n@command(free=True, acts_for=None)\ndef Move():\n    core.bulk.move()\n@command(free=True, acts_for=None)\ndef MoveFail():\n    core.bulk.move()\n    x = 1 // 0\n");
        var host = new HostFunctions();
        host.For("core").Writes("def bulk.move() -> None", "", (HostAccess a) =>
        {
            var x = a.Member("Pos", "x");
            var v = a.Member("Pos", "v");
            foreach (var part in a.Parts("Unit", x, v)) MoveBurst(a, null, x, part.Ids, part.Fixeds(x), part.Fixeds(v));
        });
        var bag = new DiagnosticBag();
        var loaded = ContentLoader.Load(root, ModSorter.Sort(ModDiscovery.Discover(root, bag), bag), bag, host);
        var problems = bag.ToSortedList();
        if (problems.Count > 0) throw new Exception(string.Join("\n", problems));
        return loaded;
    }

    private static void MoveBurst(HostAccess? access, World? world, MemberHandle x, ReadOnlySpan<long> ids, ReadOnlySpan<Fixed> xs, ReadOnlySpan<Fixed> vs)
    {
        using (var output = new NativeArray<Fixed>(ids.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory))
        {
            int managed = -1;
            // 조각은 스택 전용이라 고정한 동안 작업을 끝낸다
            fixed (Fixed* px = xs)
            fixed (Fixed* pv = vs)
                new MoveJob { X = px, V = pv, Out = (Fixed*)output.GetUnsafePtr(), Managed = &managed, Count = ids.Length }.Schedule().Complete();
            JobManaged = managed;
            var written = new ReadOnlySpan<Fixed>(output.GetUnsafeReadOnlyPtr(), ids.Length);
            if (access != null) access.WriteColumn(x, ids, written);
            else world!.WriteColumn(x, ids, written);
        }
    }

    private static World NewWorld(ContentLoadResult loaded)
    {
        var w = new World(loaded);
        var v = w.Member("Pos", "v");
        for (int i = 0; i < N; i++)
        {
            var id = w.Create(i % 2 == 0 ? "core:Grunt" : "core:Tank");
            w.Set(id, v, new FixedValue(Fixed.FromRaw(1000000 + i)));
        }
        return w;
    }

    private static void StepGetSet(World w, MemberHandle x, MemberHandle v, IReadOnlyList<EntityId> ids)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            long nx = ((FixedValue)w.Get(ids[i], x)).Value.Raw, nv = ((FixedValue)w.Get(ids[i], v)).Value.Raw;
            w.Set(ids[i], x, new FixedValue(Fixed.FromRaw(nx + nv * 16 / 1000)));
        }
    }

    private static readonly Fixed[] Buffer = new Fixed[N];

    private static void StepPartsManaged(World w, MemberHandle x, MemberHandle v)
    {
        foreach (var part in w.Parts("Unit", x, v))
        {
            var xs = part.Fixeds(x);
            var vs = part.Fixeds(v);
            for (int i = 0; i < xs.Length; i++) Buffer[i] = Fixed.FromRaw(xs[i].Raw + vs[i].Raw * 16 / 1000);
            w.WriteColumn(x, part.Ids, new ReadOnlySpan<Fixed>(Buffer, 0, xs.Length));
        }
    }

    private static void StepPartsBurst(World w, MemberHandle x, MemberHandle v)
    {
        foreach (var part in w.Parts("Unit", x, v)) MoveBurst(null, w, x, part.Ids, part.Fixeds(x), part.Fixeds(v));
    }

    private static void Measure(string name, Action step)
    {
        var warm = Stopwatch.StartNew();
        while (warm.ElapsedMilliseconds < 2000) step();
        long best = long.MaxValue, alloc = 0;
        for (int i = 0; i < 30; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            step();
            sw.Stop();
            alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            best = Math.Min(best, sw.ElapsedTicks);
        }
        Say(name + ": " + (best * 1e9 / Stopwatch.Frequency / N).ToString("0.0") + " ns/unit, " + (alloc / (double)N).ToString("0.0") + " B/unit");
    }

    private static string Hash(World w) => BitConverter.ToString(SHA256.Create().ComputeHash(w.Save())).Substring(0, 23);

    private static void Require(bool ok, string what)
    {
        if (!ok) throw new Exception("check failed: " + what);
    }

    public static void Run()
    {
        Say("runtime: " + (Application.isEditor ? "editor" : "player") + ", backend " + (Type.GetType("Mono.Runtime") != null ? "mono" : "il2cpp") + ", burst enabled " + BurstCompiler.IsEnabled);
        var loaded = Content();
        var a = NewWorld(loaded);
        var b = NewWorld(loaded);
        var c = NewWorld(loaded);
        var ax = a.Member("Pos", "x");
        var av = a.Member("Pos", "v");
        var ids = a.All("Unit");

        for (int i = 0; i < 50; i++)
        {
            StepGetSet(a, ax, av, ids);
            StepPartsManaged(b, ax, av);
            StepPartsBurst(c, ax, av);
        }
        Say("burst job ran as native: " + (JobManaged == 0));
        Require(Hash(a) == Hash(b), "Get/Set and Parts give the same save");
        Require(Hash(a) == Hash(c), "Get/Set and Burst give the same save");
        Say("save hash same across Get/Set, Parts, Burst: " + Hash(a));

        var before = Hash(c);
        Require(c.Submit("host", "core.MoveFail", new Dictionary<string, Value>()).Outcome == SubmitOutcome.ScriptError, "failing command fails");
        Require(Hash(c) == before, "failed host command rewinds");
        Require(c.Submit("host", "core.Move", new Dictionary<string, Value>()).Succeeded, "host command runs");
        Require(Hash(c) != before, "host command writes");
        Say("host function with Parts+Burst+WriteColumn: command and rollback OK");

        Measure("Get+Set managed", () => StepGetSet(a, ax, av, ids));
        Measure("Parts+WriteColumn managed", () => StepPartsManaged(b, ax, av));
        Measure("Parts+Burst job+WriteColumn", () => StepPartsBurst(c, ax, av));

        c.Add(ids[0], "Stun");
        var left = c.Member("Stun", "left");
        int seen = 0;
        foreach (var part in c.Parts("Unit", left)) seen += part.Count;
        Require(seen == 1, "attached component in parts");
        bool stale = false;
        foreach (var part in c.Parts("Unit", left))
        {
            c.Create("core:Grunt");
            try
            {
                var _ = part.Ids;
            }
            catch (InvalidOperationException)
            {
                stale = true;
            }
            break;
        }
        Require(stale, "stale part refused");
        Say("attached parts and stale check OK");
        Say("OK");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InPlayer()
    {
        if (Application.isEditor) return;
        int code = 0;
        try
        {
            Run();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError("KESA BULK FAILED: " + e);
            code = 1;
        }
        Application.Quit(code);
    }
}
