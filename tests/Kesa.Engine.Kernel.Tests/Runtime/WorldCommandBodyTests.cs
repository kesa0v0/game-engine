using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>커맨드 본문의 상태 쓰기 (spec S2·S6, spec-gaps G117·G132).</summary>
    public class WorldCommandBodyTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Moves\", left=field(int, default=2, min=0), doubled=derived(int, lambda m: m.left * 2))\n" +
            "component(\"Bag\", items=const(list[str], default=[]))\n" +
            "component(\"Skill\", key=\"SkillDef\", level=field(int, default=0, min=0, max=20))\n" +
            "component(\"Need\", key=\"NeedDef\", keys=\"listed\", met=field(bool, default=False))\n" +
            "component(\"Owner\", party=ref(\"Party\", optional=True))\n" +
            "deftype(\"SkillDef\")\ndeftype(\"NeedDef\")\n" +
            "deftype(\"Party\", components=[\"Moves\"])\n" +
            "deftype(\"Unit\", components=[\"Moves\", \"Bag\", \"Skill\", \"Need\", \"Owner\"])\n";

        private const string Defs =
            "SkillDef(\"Cooking\")\nNeedDef(\"Food\")\nNeedDef(\"Luxury\")\n" +
            "Party(\"Red\")\nUnit(\"Scout\", Need={\"Food\": Need()})\n" +
            "def spend(u):\n    u.Moves.left = 0\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Partial(unit: Ref[Unit], party: Ref[Party]):\n" +
            "    unit.Moves.left = 1\n" +
            "    unit.Moves.left -= 1\n" +
            "    unit.Owner.party = party\n" +
            "    unit.Skill[\"Cooking\"].level = 5\n" +
            "    unit.Need[\"Food\"].met = True\n" +
            "    spend(unit)\n" +
            "    unit.Moves.left = \"oops\"\n" +
            "def apply(f, u):\n    f(u)\n" +
            "def count(u):\n    u.Moves.left = 1\n    return 1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def ViaPure(unit: Ref[Unit]):\n" +
            "    apply(spend, unit)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def ViaValue(unit: Ref[Unit]):\n" +
            "    f = spend\n" +
            "    f(unit)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def ViaExpression(unit: Ref[Unit]):\n" +
            "    f = count\n" +
            "    n = f(unit) + 1\n" + // 대입의 오른쪽 전체가 아닌 식 안 (G135)
            "@command(free=True, acts_for=None)\n" +
            "def ViaAssignment(unit: Ref[Unit]):\n" +
            "    f = count\n" +
            "    n = f(unit)\n" +
            "    unit.Moves.left += n\n" +
            "def drain(u):\n" +
            "    for i in range(10):\n" +
            "        if u.Moves.left == 0:\n" +
            "            return i\n" +
            "        u.Moves.left -= 1\n" +
            "    return -1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Drain(unit: Ref[Unit]):\n" +
            "    n = drain(unit)\n" +
            "    unit.Moves.left = n + 7\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Step(unit: Ref[Unit]):\n" +
            "    with preconditions:\n" +
            "        left = unit.Moves.left\n" +
            "        require(left > 0, text(\"msg.no_moves\"))\n" +
            "    unit.Moves.left = left - 1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Dash(unit: Ref[Unit]):\n" +
            "    unit.Moves.left -= 1\n" +
            "    unit.Moves.left -= unit.Moves.doubled // 4\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Rest(unit: Ref[Unit]):\n" +
            "    spend(unit)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Train(unit: Ref[Unit], skill: DefId[SkillDef]):\n" +
            "    unit.Skill[skill].level += 3\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Satisfy(unit: Ref[Unit], need: DefId[NeedDef]):\n" +
            "    unit.Need[need].met = True\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Loot(unit: Ref[Unit]):\n" +
            "    unit.Bag.items += [\"Iron\"]\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Join(unit: Ref[Unit], party: Ref[Party]):\n" +
            "    unit.Owner.party = party\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Cheat(unit: Ref[Unit]):\n" +
            "    unit.Moves.doubled = 9\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Typo(unit: Ref[Unit]):\n" +
            // 리스트로 감싸 타입을 모르게 한다: 실행 중 안전망을 본다 (아니면 G228의 로드 오류)
            "    [unit.Moves][0].lefft = 1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Wrong(unit: Ref[Unit]):\n" +
            "    unit.Moves.left = \"many\"\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Overdraw(unit: Ref[Unit]):\n" +
            "    unit.Moves.left -= 5\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Swap(unit: Ref[Unit]):\n" +
            "    unit.Moves = 3\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Early(unit: Ref[Unit]):\n" +
            "    unit.Moves.left = 1\n" +
            "    return\n" +
            "    unit.Moves.left = 0\n";

        private World _world = null!;
        private EntityId _scout, _red;
        private MemberHandle _left = null!;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _scout = _world.Create("Scout");
            _red = _world.Create("Red");
            _left = _world.Member("Moves", "left");
        }

        private SubmitResult Submit(string command, Dictionary<string, Value>? extra = null)
        {
            var args = new Dictionary<string, Value> { ["unit"] = new EntityValue(_scout) };
            if (extra != null)
                foreach (var pair in extra) args[pair.Key] = pair.Value;
            return _world.Submit("host", "banking." + command, args);
        }

        private long Left => ((IntValue)_world.Get(_scout, _left)).Value;

        [Test]
        public void TheBodyWritesAndSeesPreconditionLocals()
        {
            var r = Submit("Step");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
            Assert.That(Left, Is.EqualTo(1));
        }

        [Test]
        public void AFailedPreconditionRunsNoBody()
        {
            _world.Set(_scout, _left, new IntValue(0));
            var r = Submit("Step");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.PreconditionsFailed));
            Assert.That(Left, Is.EqualTo(0));
        }

        [Test]
        public void DerivedReadInTheBodySeesTheWriteBeforeIt()
        {
            // 30 - 1 = 29, 그 뒤의 doubled는 29 * 2 = 58을 보므로 29 - 58 // 4 = 15 (쓰기 전 값을 봤다면 14)
            _world.Set(_scout, _left, new IntValue(30));
            Assert.That(Submit("Dash").Succeeded, Is.True);
            Assert.That(Left, Is.EqualTo(15));
        }

        [Test]
        public void AWriteFunctionValueCalledAsTheWholeRightSideOfAnAssignmentWrites()
        {
            // count는 left를 1로 쓰고 1을 돌려준다: 5 → 1 → 1 + 1 (G135)
            _world.Set(_scout, _left, new IntValue(5));
            Assert.That(Submit("ViaAssignment").Succeeded, Is.True);
            Assert.That(Left, Is.EqualTo(2));
        }

        [Test]
        public void AWriteFunctionReturningFromInsideALoopHandsItsValueToTheNextLine()
        {
            // 2 → 1 → 0, 셋째 바퀴(i=2)에서 돌려준다
            Assert.That(Submit("Drain").Succeeded, Is.True);
            Assert.That(Left, Is.EqualTo(9));
        }

        [Test]
        public void AWriteFunctionCalledAsAStatementWrites()
        {
            Assert.That(Submit("Rest").Succeeded, Is.True);
            Assert.That(Left, Is.EqualTo(0));
        }

        // ---- 롤백 (C8, G133) ----

        [Test]
        public void AScriptErrorUndoesEveryWriteOfTheCommand()
        {
            var party = _world.Member("Owner", "party");
            var r = Submit("Partial", new Dictionary<string, Value> { ["party"] = new EntityValue(_red) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunType));

            Assert.That(Left, Is.EqualTo(2));
            Assert.That(_world.Get(_scout, party), Is.SameAs(NoneValue.Instance));
            Assert.That(_world.Inverse(_red, party), Is.Empty);
            Assert.That(((IntValue)_world.GetRow(_scout, _world.Member("Skill", "level"), "Cooking")).Value, Is.EqualTo(0));
            Assert.That(_world.GetRow(_scout, _world.Member("Need", "met"), "Food"), Is.SameAs(BoolValue.False));
        }

        [Test]
        public void ARefWriteIsUndoneInTheInverseIndexToo()
        {
            // 원래 Red를 가리키던 Ref를 바꾼 뒤 실패: 역방향 색인이 원래대로 돌아와야 한다
            var party = _world.Member("Owner", "party");
            var blue = _world.Create("Red");
            _world.Set(_scout, party, new EntityValue(_red));
            Submit("Partial", new Dictionary<string, Value> { ["party"] = new EntityValue(blue) });
            Assert.That(((EntityValue)_world.Get(_scout, party)).Id, Is.EqualTo(_red));
            Assert.That(_world.Inverse(_red, party), Is.EqualTo(new[] { _scout }));
            Assert.That(_world.Inverse(blue, party), Is.Empty);
        }

        [Test]
        public void TheWorldIsUsableAfterARollback()
        {
            Submit("Partial", new Dictionary<string, Value> { ["party"] = new EntityValue(_red) });
            Assert.That(Submit("Step").Succeeded, Is.True);
            Assert.That(Left, Is.EqualTo(1));
            Assert.That(Submit("Step").Succeeded, Is.True);
            Assert.That(Left, Is.EqualTo(0)); // 성공한 커맨드는 되감기지 않는다
        }

        // ---- 런타임 안전망 (G117): 이름으로 판정할 수 없는 호출 ----

        [Test]
        public void AWriteFunctionHeldInAVariableWritesWhenCalledAsAStatement()
        {
            Assert.That(Submit("ViaValue").Succeeded, Is.True);
            Assert.That(Left, Is.EqualTo(0));
        }

        [TestCase("ViaPure", "'spend' cannot change the world here")]
        [TestCase("ViaExpression", "'count' cannot change the world here")]
        public void AWriteReachedThroughAPureFunctionOrAnExpressionIsRefused(string command, string message)
        {
            // apply는 f(u)만 부르므로 로드 때 순수로 판정된다: 그 안에서 시작한 호출은 순수 모드다
            var r = Submit(command);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunNotWritable));
            Assert.That(r.Error.Message, Does.Contain(message));
            Assert.That(Left, Is.EqualTo(2));
        }

        [Test]
        public void AKeyedRowCanBeWritten()
        {
            Assert.That(Submit("Train", new Dictionary<string, Value> { ["skill"] = new StrValue("Cooking") }).Succeeded, Is.True);
            Assert.That(((IntValue)_world.GetRow(_scout, _world.Member("Skill", "level"), "Cooking")).Value, Is.EqualTo(3));
        }

        [Test]
        public void AListedRowThatExistsCanBeWritten()
        {
            Assert.That(Submit("Satisfy", new Dictionary<string, Value> { ["need"] = new StrValue("Food") }).Succeeded, Is.True);
            Assert.That(_world.GetRow(_scout, _world.Member("Need", "met"), "Food"), Is.SameAs(BoolValue.True));
        }

        [Test]
        public void AMissingRowCannotBeWritten()
        {
            var r = Submit("Satisfy", new Dictionary<string, Value> { ["need"] = new StrValue("Luxury") });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("cannot set 'met' on None"));
            Assert.That(r.Error.Hint, Does.Contain("is not None"));
        }

        [Test]
        public void AListIsAlwaysAConstSoItCannotBeExtended()
        {
            // 상태 칸에는 리스트가 없다 (SCH004): 리스트 칸은 const라 += 도 쓰기 거부다
            var r = Submit("Loot");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunNotWritable));
        }

        [Test]
        public void ARefCanBeWritten()
        {
            Assert.That(Submit("Join", new Dictionary<string, Value> { ["party"] = new EntityValue(_red) }).Succeeded, Is.True);
            Assert.That(((EntityValue)_world.Get(_scout, _world.Member("Owner", "party"))).Id, Is.EqualTo(_red));
            Assert.That(_world.Inverse(_red, _world.Member("Owner", "party")), Is.EqualTo(new[] { _scout }));
        }

        [TestCase("Cheat", Codes.RunNotWritable)]
        [TestCase("Typo", Codes.RunUnknownName)]
        [TestCase("Wrong", Codes.RunType)]
        [TestCase("Overdraw", Codes.RunRange)]
        [TestCase("Swap", Codes.RunScript)]
        public void BadWritesAreScriptErrors(string command, string code)
        {
            var r = Submit(command);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError), r.Error?.ToString());
            Assert.That(r.Error!.Code, Is.EqualTo(code), r.Error.ToString());
            Assert.That(r.Error.Message, Does.StartWith("computing the command banking." + command + ": "));
            Assert.That(Left, Is.EqualTo(2));
        }

        [Test]
        public void ReturnStopsTheBody()
        {
            Assert.That(Submit("Early").Succeeded, Is.True);
            Assert.That(Left, Is.EqualTo(1));
        }

        [Test]
        public void CheckPreconditionsNeverRunsTheBody()
        {
            var report = _world.CheckPreconditions("banking.Step",
                new Dictionary<string, Value> { ["unit"] = new EntityValue(_scout) });
            Assert.That(report.Passed, Is.True);
            Assert.That(Left, Is.EqualTo(2));
        }
    }
}
