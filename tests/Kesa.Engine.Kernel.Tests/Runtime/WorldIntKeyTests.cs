using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary><c>key=int</c> 컴포넌트와 <c>replace_rows</c>·<c>append_row</c> (spec S4, spec-gaps G129).</summary>
    public class WorldIntKeyTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Die\", key=int, value=field(int, default=1), " +
            "bonus=derived(int, lambda d: d.value + len(contribs(d, \"bonus\"))))\n" +
            "component(\"Skill\", key=\"SkillDef\", keys=\"listed\", level=field(int, default=0))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"Node\", components=[\"Die\", \"Skill\"])\n";

        private const string Defs =
            "SkillDef(\"Cooking\")\nNode(\"Roll\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Throw(node: Ref[Node], a: int, b: int, c: int):\n" +
            "    replace_rows(node.Die, [Die(value=a), Die(value=b), Die(value=c)])\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Clear(node: Ref[Node]):\n" +
            "    replace_rows(node.Die, [])\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Push(node: Ref[Node], v: int):\n" +
            "    append_row(node.Die, Die(value=v))\n" +
            "@command(free=True, acts_for=None)\n" +
            "def PushDefault(node: Ref[Node]):\n" +
            "    append_row(node.Die)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Bump(node: Ref[Node], i: int):\n" +
            "    node.Die[i].value += 1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Total(node: Ref[Node]):\n" +
            "    total = 0\n" +
            "    keys = 0\n" +
            "    for i in node.Die:\n" +
            "        total += node.Die[i].value\n" +
            "        keys = keys * 10 + node.Die[i].key\n" +
            "    append_row(node.Die, Die(value=total * 1000 + keys))\n" +
            "@command(free=True, acts_for=None)\n" +
            "def CountMissing(node: Ref[Node]):\n" +
            "    if node.Die[9] is None and 9 not in node.Die:\n" +
            "        append_row(node.Die, Die(value=len(node.Die)))\n" +
            "@command(free=True, acts_for=None)\n" +
            "def ThrowThenFail(node: Ref[Node]):\n" +
            "    replace_rows(node.Die, [Die(value=6)])\n" +
            "    node.Die[0].value = \"oops\"\n" +
            "@command(free=True, acts_for=None)\n" +
            "def ReplaceSkill(node: Ref[Node]):\n" +
            "    replace_rows(node.Skill, [])\n" +
            "@command(free=True, acts_for=None)\n" +
            "def ThrowWrong(node: Ref[Node]):\n" +
            "    replace_rows(node.Die, [Die(value=1), Skill(level=2)])\n";

        private World _world = null!;
        private EntityId _node;
        private MemberHandle _value = null!, _bonus = null!;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _node = _world.Create("banking:Roll");
            _value = _world.Member("Die", "value");
            _bonus = _world.Member("Die", "bonus");
        }

        private SubmitResult Submit(string command, params (string Name, long Value)[] ints)
        {
            var args = new Dictionary<string, Value> { ["node"] = new EntityValue(_node) };
            foreach (var (name, value) in ints) args[name] = new IntValue(value);
            return _world.Submit("host", "banking." + command, args);
        }

        private long[] Values() => _world.RowInts(_node, "Die").Select(k => ((IntValue)_world.GetRow(_node, _value, k)).Value).ToArray();

        [Test]
        public void ReplaceRowsNumbersFromZeroAndAppendRowTakesTheNextKey()
        {
            Assert.That(Submit("Throw", ("a", 5), ("b", 2), ("c", 6)).Succeeded, Is.True);
            Assert.That(_world.RowInts(_node, "Die"), Is.EqualTo(new long[] { 0, 1, 2 }));
            Assert.That(Values(), Is.EqualTo(new long[] { 5, 2, 6 }));
            Assert.That(Submit("Push", ("v", 4)).Succeeded, Is.True);
            Assert.That(_world.RowInts(_node, "Die"), Is.EqualTo(new long[] { 0, 1, 2, 3 }));
            Assert.That(Submit("Throw", ("a", 1), ("b", 1), ("c", 1)).Succeeded, Is.True);
            Assert.That(Values(), Is.EqualTo(new long[] { 1, 1, 1 })); // 이전 줄은 모두 지워졌다
        }

        [Test]
        public void AppendRowOnAnEmptyListStartsAtZeroAndUsesDefaults()
        {
            Assert.That(Submit("PushDefault").Succeeded, Is.True);
            Assert.That(_world.RowInts(_node, "Die"), Is.EqualTo(new long[] { 0 }));
            Assert.That(Values(), Is.EqualTo(new long[] { 1 }));
            Assert.That(Submit("Clear").Succeeded, Is.True);
            Assert.That(_world.RowInts(_node, "Die"), Is.Empty);
        }

        [Test]
        public void AppendRowGoesAfterTheLargestKeyEvenWithGaps()
        {
            _world.AddRow(_node, "Die", 7);
            Assert.That(Submit("Push", ("v", 3)).Succeeded, Is.True);
            Assert.That(_world.RowInts(_node, "Die"), Is.EqualTo(new long[] { 7, 8 }));
        }

        [Test]
        public void ACellIsWrittenByIndexAndReadInKeyOrder()
        {
            Submit("Throw", ("a", 5), ("b", 2), ("c", 6));
            Assert.That(Submit("Bump", ("i", 1)).Succeeded, Is.True);
            Assert.That(Values(), Is.EqualTo(new long[] { 5, 3, 6 }));
            // 합 14, 키 0·1·2 순
            Assert.That(Submit("Total").Succeeded, Is.True);
            Assert.That(Values()[3], Is.EqualTo(14 * 1000 + 12));
        }

        [Test]
        public void AMissingIndexIsNone()
        {
            Submit("Throw", ("a", 5), ("b", 2), ("c", 6));
            Assert.That(Submit("CountMissing").Succeeded, Is.True);
            Assert.That(Values()[3], Is.EqualTo(3));
            var r = Submit("Bump", ("i", 9));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("None"));
        }

        [Test]
        public void RenumberingDropsContributionsOnTheOldRows()
        {
            Submit("Throw", ("a", 5), ("b", 2), ("c", 6));
            _world.AddContribution(_node, _bonus, 1, "bonus", new IntValue(1));
            Assert.That(((IntValue)_world.GetRow(_node, _bonus, 1)).Value, Is.EqualTo(3));
            Submit("Throw", ("a", 5), ("b", 2), ("c", 6));
            Assert.That(((IntValue)_world.GetRow(_node, _bonus, 1)).Value, Is.EqualTo(2));
        }

        [Test]
        public void AFailedReplaceIsUndoneWithTheContributions()
        {
            Submit("Throw", ("a", 5), ("b", 2), ("c", 6));
            _world.AddContribution(_node, _bonus, 1, "bonus", new IntValue(1));
            var r = Submit("ThrowThenFail");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(Values(), Is.EqualTo(new long[] { 5, 2, 6 }));
            Assert.That(((IntValue)_world.GetRow(_node, _bonus, 1)).Value, Is.EqualTo(3));
        }

        [Test]
        public void ReplaceRowsChecksEveryItemBeforeChangingAnything()
        {
            Submit("Throw", ("a", 5), ("b", 2), ("c", 6));
            var r = Submit("ThrowWrong");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("Die(...) values, not Skill(...)"));
            Assert.That(Values(), Is.EqualTo(new long[] { 5, 2, 6 }));
        }

        [Test]
        public void ListHelpersOnlyWorkOnIntKeys()
        {
            var r = Submit("ReplaceSkill");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("keyed by SkillDef"));
        }

        [Test]
        public void TheHostReadsIntRowsAndRejectsOtherKeyKinds()
        {
            _world.AddRow(_node, "Die", 0, new Init(_value, new IntValue(4)));
            Assert.That(((IntValue)_world.GetRow(_node, _value, 0)).Value, Is.EqualTo(4));
            Assert.That(_world.HasRow(_node, "Die", 1), Is.False);
            var e = Assert.Throws<WorldException>(() => _world.GetRow(_node, _value, "banking:Cooking"));
            Assert.That(e!.Message, Does.Contain("keyed by int, not Def ids"));
        }

        // ---- 로드 시점 ----

        private IReadOnlyList<Diagnostic> LoadWith(string schema, string defs = "")
        {
            Mod("banking", schema: schema, defs: defs);
            Load(out var diagnostics);
            return diagnostics;
        }

        [Test]
        public void AnIntKeyIsAlwaysListed()
        {
            var d = LoadWith("component(\"Die\", key=int, keys=\"all\", value=field(int, default=1))\n")
                .Single(x => x.Code == Codes.SchBadDeclaration);
            Assert.That(d.Message, Is.EqualTo("a component keyed by int is always keys=\"listed\""));
        }

        [Test]
        public void IntRowsCannotBeWrittenInADef()
        {
            var diags = LoadWith("component(\"Die\", key=int, value=field(int, default=1))\ndeftype(\"Node\", components=[\"Die\"])\n",
                "Node(\"Roll\", Die={\"0\": Die(value=3)})\n");
            Assert.That(diags.Any(x => x.Code == Codes.DefBadValue && x.Message.Contains("keyed by int")), Is.True,
                string.Join("\n", diags));
        }

        [Test]
        public void IntRowsCannotBePatched()
        {
            var diags = LoadWith("component(\"Die\", key=int, value=field(int, default=1))\ndeftype(\"Node\", components=[\"Die\"])\n",
                "Node(\"Roll\")\npatch.set(\"Roll\", \"Die[0].value\", 3)\n");
            Assert.That(diags.Any(x => x.Message.Contains("keyed by int and exist only at runtime")), Is.True,
                string.Join("\n", diags));
        }
    }
}
