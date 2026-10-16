using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 전역 순회 <c>query(...)</c> (spec S3, spec-gaps G192·G193): Def 타입 하나 그리고/또는 컴포넌트들로 엔티티를 id 순으로 꺼내고,
    /// 값으로 거르기는 컴프리헨션이 한다. 읽으면 그 목록을 읽은 것으로 기록해 <c>when</c>과 <c>Explain</c>이 안다.
    /// </summary>
    public class WorldQueryTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Mood\", value=field(int, default=50))\n" +
            "component(\"Out\", n=field(int, default=0), s=field(str, default=\"\"))\n" +
            "component(\"Wounded\", turns=field(int, default=1))\n" +
            "component(\"Census\", towns=derived(int, lambda c: len(query(\"Settlement\"))))\n" +
            "deftype(\"Need\")\n" +
            "deftype(\"Settlement\", components=[\"Mood\"])\n" +
            "deftype(\"Unit\", components=[\"Out\"])\n" +
            "deftype(\"Board\", components=[\"Out\", \"Census\"])\n" +
            "signal(\"Raid\")\n";

        private const string Commands =
            "Settlement(\"Town\")\nUnit(\"Scout\")\nBoard(\"Main\")\nNeed(\"Food\")\n" +
            "def board():\n    return query(\"Board\")[0]\n" +
            "@command(free=True, acts_for=None)\ndef Found(mood: int):\n    s = spawn(\"Town\")\n    s.Mood.value = mood\n" +
            "@command(free=True, acts_for=None)\ndef Raze(s: Ref[Settlement]):\n    destroy(s)\n" +
            "@command(free=True, acts_for=None)\ndef Hurt(e: Ref[Unit]):\n    attach(e, Wounded(turns=2))\n" +
            "@command(free=True, acts_for=None)\ndef Heal(e: Ref[Unit]):\n    detach(e.Wounded)\n";

        private World Make(string defs = "")
        {
            Mod("core", schema: Schema, defs: Commands + defs);
            return new World(LoadClean());
        }

        private static SubmitResult Run(World w, string command, params (string, Value)[] args) =>
            w.Submit("host", "core." + command, args.ToDictionary(a => a.Item1, a => a.Item2));

        private static long[] Ids(IReadOnlyList<EntityId> ids) => ids.Select(i => i.Value).ToArray();

        // ---- 출처 ----

        [Test]
        public void ATypeAComponentOrBothSelectEntitiesInIdOrder()
        {
            var w = Make();
            var a = w.Create("Scout");
            var t = w.Create("Town");
            var b = w.Create("Scout");
            var main = w.Create("Main");
            w.Add(b, "Wounded");
            w.Add(main, "Wounded");
            Assert.That(Ids(w.Query("Unit")), Is.EqualTo(new[] { a.Value, b.Value }));
            // 컴포넌트만이면 타입을 가로지른다. Def에서 온 컴포넌트(Out)도 런타임에 붙인 것(Wounded)도 맞는다
            Assert.That(Ids(w.Query("Wounded")), Is.EqualTo(new[] { b.Value, main.Value }));
            Assert.That(Ids(w.Query("Out")), Is.EqualTo(new[] { a.Value, b.Value, main.Value }));
            Assert.That(Ids(w.Query("Unit", "Wounded")), Is.EqualTo(new[] { b.Value }));
            Assert.That(Ids(w.Query("Settlement")), Is.EqualTo(new[] { t.Value }));
            w.Destroy(a);
            Assert.That(Ids(w.Query("Unit")), Is.EqualTo(new[] { b.Value }), "the dead are not listed");
        }

        [Test]
        public void ScriptsFilterAQueryWithComprehensions()
        {
            var w = Make(
                "@command(free=True, acts_for=None)\ndef Count():\n" +
                "    unhappy = [s for s in query(\"Settlement\") if s.Mood.value < 20]\n" +
                "    b = board()\n" +
                "    b.Out.n = len(unhappy) * 100 + sum(1 for u in query(\"Unit\", \"Wounded\"))\n" +
                "    b.Out.s = \"yes\" if any(s.Mood.value > 90 for s in query(\"Settlement\")) else \"no\"\n");
            var main = w.Create("Main");
            foreach (int mood in new[] { 10, 50, 5, 95 }) w.Set(w.Create("Town"), w.Member("Mood", "value"), new IntValue(mood));
            var u = w.Create("Scout");
            w.Create("Scout");
            w.Add(u, "Wounded");
            Assert.That(Run(w, "Count").Succeeded, Is.True);
            Assert.That(((IntValue)w.Get(main, w.Member("Out", "n"))).Value, Is.EqualTo(201));
            Assert.That(((StrValue)w.Get(main, w.Member("Out", "s"))).Value, Is.EqualTo("yes"));
        }

        [Test]
        public void ANameMadeAtRunTimeIsCheckedWhenItRuns()
        {
            var w = Make("@command(free=True, acts_for=None)\ndef Look(name: str):\n    board().Out.n = len(query(name))\n");
            w.Create("Main");
            var r = Run(w, "Look", ("name", new StrValue("Setlement")));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunUnknownName));
            Assert.That(r.Error.Message, Does.Contain("there is no Def type or component 'Setlement'"));
            Assert.That(r.Error.Hint, Does.Contain("Settlement"));
            Assert.That(Run(w, "Look", ("name", new StrValue("Need"))).Error!.Code, Is.EqualTo(Codes.RunNotEntity));
        }

        // ---- when이 목록 변화를 안다 (G193) ----

        private const string Watch =
            "@when(lambda: any(s.Mood.value < 20 for s in query(\"Settlement\")))\ndef unrest():\n    board().Out.s = board().Out.s + \"u\"\n" +
            "@when(lambda: len(query(\"Unit\", \"Wounded\")) >= 2)\ndef triage():\n    board().Out.s = board().Out.s + \"t\"\n" +
            "@when(lambda: board().Census.towns >= 3)\ndef crowded():\n    board().Out.s = board().Out.s + \"c\"\n";

        private static string Log(World w, EntityId main) => ((StrValue)w.Get(main, w.Member("Out", "s"))).Value;

        [TestCase(false)]
        [TestCase(true)]
        public void AWhenConditionSeesEntitiesComeAndGo(bool fullScan)
        {
            var w = Make(Watch);
            w.WhenFullScan = fullScan;
            var main = w.Create("Main");
            w.Create("Town");
            var u1 = w.Create("Scout");
            var u2 = w.Create("Scout");
            Assert.That(Run(w, "Found", ("mood", new IntValue(60))).Succeeded, Is.True);
            Assert.That(Log(w, main), Is.EqualTo(""));

            // 새 정착지가 기분 10으로 생긴다: 아무도 그 엔티티를 읽은 적이 없어도 목록이 바뀌었으니 다시 본다
            Assert.That(Run(w, "Found", ("mood", new IntValue(10))).Succeeded, Is.True);
            Assert.That(Log(w, main), Is.EqualTo("uc"), "a new unhappy town, and a third town counted through a derived value");

            // 불행한 정착지가 사라지면 거짓이 되고, 다시 생기면 또 발동한다
            var sad = w.Query("Settlement").Last();
            Assert.That(Run(w, "Raze", ("s", new EntityValue(sad))).Succeeded, Is.True);
            Assert.That(Run(w, "Found", ("mood", new IntValue(5))).Succeeded, Is.True);
            Assert.That(Log(w, main), Is.EqualTo("ucuc"), "the town count went 3, 2, 3, so crowded fires again");

            // 컴포넌트를 붙이고 떼면 그 컴포넌트를 출처로 읽은 조건을 다시 본다
            Assert.That(Run(w, "Hurt", ("e", new EntityValue(u1))).Succeeded, Is.True);
            Assert.That(Log(w, main), Is.EqualTo("ucuc"), "one wounded unit is not enough");
            Assert.That(Run(w, "Hurt", ("e", new EntityValue(u2))).Succeeded, Is.True);
            Assert.That(Log(w, main), Is.EqualTo("ucuct"));
            Assert.That(Run(w, "Heal", ("e", new EntityValue(u1))).Succeeded, Is.True);
            Assert.That(Run(w, "Hurt", ("e", new EntityValue(u1))).Succeeded, Is.True);
            Assert.That(Log(w, main), Is.EqualTo("ucuctt"));
        }

        // ---- Explain ----

        [Test]
        public void ExplainShowsTheListAQueryRead()
        {
            var w = Make();
            var main = w.Create("Main");
            var a = w.Create("Town");
            var b = w.Create("Town");
            var node = w.Explain(main, w.Member("Census", "towns"));
            Assert.That(node.Reads.Single().Kind, Is.EqualTo(ExplainKind.Query));
            Assert.That(node.Render(), Is.EqualTo("Census.towns of " + main + " = 2\n  query(\"Settlement\") = [" + a + ", " + b + "]"));
        }

        // ---- 저장 ----

        [Test]
        public void AQueryResultHeldAcrossAnAskIsSaved()
        {
            Mod("core", schema: Schema, defs: Commands +
                "@command(free=True, acts_for=None)\ndef Poll(u: Ref[Unit]):\n" +
                "    towns = query(\"Settlement\")\n" +
                "    r = ask(u, \"Go\", choices=[\"a\", \"b\"])\n" +
                "    spawn(\"Town\")\n" +
                "    u.Out.n = len(towns) * 10 + len(query(\"Settlement\"))\n");
            var content = LoadClean();
            foreach (bool reload in new[] { false, true })
            {
                var world = new World(content);
                var u = world.Create("Scout");
                world.Create("Town");
                world.Create("Town");
                Assert.That(world.Submit("host", "core.Poll", new Dictionary<string, Value> { ["u"] = new EntityValue(u) }).Succeeded, Is.True);
                if (reload)
                {
                    var bytes = world.Save();
                    var loaded = World.Load(content, bytes);
                    Assert.That(loaded.Succeeded, Is.True, string.Join("\n", loaded.Diagnostics));
                    world = loaded.World!;
                }
                var open = world.OpenRequests.Single();
                Assert.That(world.Answer("host", open.Id, open.Who[0], 0).Error, Is.Null);
                // 붙잡아 둔 목록은 그때의 목록(2)이고 새로 읽으면 3
                Assert.That(((IntValue)world.Get(u, world.Member("Out", "n"))).Value, Is.EqualTo(23), "reload " + reload);
            }
        }

        // ---- 로드 검사 (G192) ----

        [TestCase("query(\"Setlement\")", Codes.LnkNotFound, "there is no Def type or component 'Setlement'")]
        [TestCase("query(\"Need\")", Codes.LnkWrongType, "'Need' is a data-only Def type with no components")]
        [TestCase("query(\"Unit\", \"Board\")", Codes.LnkWrongType, "query() was given two Def types ('Unit' and 'Board')")]
        [TestCase("query(\"Raid\")", Codes.LnkNotFound, "'Raid' is a signal, not a Def type or component")]
        [TestCase("query()", Codes.EvlArguments, "query() needs at least one Def type or component name")]
        [TestCase("query(of=\"Unit\")", Codes.EvlArguments, "query() takes no keyword arguments")]
        public void LiteralNamesAreCheckedAtLoad(string call, string code, string message)
        {
            Mod("core", schema: Schema, defs: "def f():\n    return len(" + call + ")\n");
            Load(out var diagnostics);
            var d = diagnostics.Single();
            Assert.That(d.Code, Is.EqualTo(code));
            Assert.That(d.Message, Does.Contain(message));
            if (code == Codes.LnkNotFound && call.Contains("Setlement")) Assert.That(d.Hint, Does.Contain("Settlement"));
        }

        [Test]
        public void AQueriedNameMustBeVisible()
        {
            Mod("core", schema: Schema);
            Mod("other", defs: "def f():\n    return len(query(\"Settlement\"))\n");
            Load(out var diagnostics);
            var d = diagnostics.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.LnkNotVisible));
            Assert.That(d.Message, Does.Contain("'Settlement' belongs to mod 'core', which mod 'other' does not depend on"));
        }

        [Test]
        public void AShadowedQueryIsNotChecked()
        {
            Mod("core", schema: Schema, defs: "def f(query):\n    return query(\"Nothing\")\n");
            LoadClean();
        }
    }
}
