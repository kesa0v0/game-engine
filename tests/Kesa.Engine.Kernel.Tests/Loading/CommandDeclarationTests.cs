using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary><c>@command</c> 선언과 커맨드 id (spec S6, spec-gaps G109).</summary>
    public class CommandDeclarationTests : LoaderTestBase
    {
        [Test]
        public void CommandIdIsModIdDotFunctionName()
        {
            Mod("core", defs: "@command(acts_for=lambda n: n)\ndef Move(n: int):\n    return n\n" +
                              "@command(free=True, acts_for=None)\ndef EndTurn():\n    return 0\n" +
                              "def helper(x):\n    return x\n");
            var result = LoadClean();
            Assert.That(result.Commands.Keys, Is.EqualTo(new[] { "core.EndTurn", "core.Move" }));
            Assert.That(result.Commands["core.Move"].Free, Is.False);
            Assert.That(result.Commands["core.EndTurn"].Free, Is.True);
            Assert.That(result.Commands["core.Move"].Function.Definition!.Name, Is.EqualTo("Move"));
        }

        [Test]
        public void TwoModsCanDeclareCommandsWithTheSameFunctionName()
        {
            Mod("core", defs: "@command(free=True, acts_for=None)\ndef Move():\n    return 0\n");
            Mod("naval", defs: "@command(free=True, acts_for=None)\ndef Move():\n    return 1\n", extra: ", dependencies=[\"core\"]");
            var result = LoadClean();
            Assert.That(result.Commands.Keys, Is.EqualTo(new[] { "core.Move", "naval.Move" }));
            Assert.That(result.Commands["naval.Move"].Mod, Is.EqualTo("naval"));
        }

        [TestCase("@other\ndef f():\n    return 0\n", "unknown decorator; only @command, @on, @when and @submit_policy are supported")]
        [TestCase("@command(True)\ndef f():\n    return 0\n", "@command takes no positional arguments")]
        [TestCase("@command(cost=1)\ndef f():\n    return 0\n", "@command has no argument 'cost'")]
        [TestCase("@command(free=1)\ndef f():\n    return 0\n", "free= must be True or False")]
        [TestCase("@command(free=True, acts_for=None)\n@command(free=True, acts_for=None)\ndef f():\n    return 0\n", "a function can have only one decorator")]
        [TestCase("@command.free\ndef f():\n    return 0\n", "unknown decorator; only @command, @on, @when and @submit_policy are supported")]
        public void BadDecoratorsAreLoadErrors(string defs, string message)
        {
            Mod("core", defs: defs);
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdBadDecorator));
            Assert.That(d.Message, Is.EqualTo(message));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void ADuplicateCommandNameIsANameErrorNotASecondCommand()
        {
            Mod("core", defs: "@command(free=True, acts_for=None)\ndef Move():\n    return 0\n@command(free=True, acts_for=None)\ndef Move():\n    return 1\n");
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(new[] { Codes.EvlNameTaken }));
            Assert.That(result.Commands, Is.Empty);
        }

        // ---- with preconditions: (G111) ----

        [Test]
        public void ACommandWrittenLikeTheSpecExampleLoads()
        {
            Mod("core", schema: "component(\"Moves\", left=field(int, default=1))\n" +
                                "deftype(\"Unit\", components=[\"Moves\"])\ndeftype(\"City\", components=[\"Moves\"])\n",
                defs: "@command(free=True, acts_for=lambda unit: unit)\ndef Move(unit: Ref[Unit], dest: Ref[City]):\n" +
                      "    with preconditions:\n        require(unit.Moves.left > 0, text(\"msg.no_moves\"))\n    return 0\n");
            var result = LoadClean();
            Assert.That(result.Commands.Keys, Is.EqualTo(new[] { "core.Move" }));
        }

        [Test]
        public void PreconditionLocalsAreVisibleInTheBody()
        {
            Mod("core", defs: "@command(free=True, acts_for=None)\ndef Move(u: int):\n    with preconditions:\n        c = u + 1\n" +
                              "        require(c > 0, text(\"msg.neg\", c=c))\n    return c\n");
            LoadClean();
        }

        [TestCase("@command(free=True, acts_for=None)\ndef f(u: bool):\n    x = 1\n    with preconditions:\n        require(u, text(\"m\"))\n    return x\n",
            "'with' can only be the first statement of a @command function")]
        [TestCase("def f(u: bool):\n    with preconditions:\n        require(u, text(\"m\"))\n    return 0\n",
            "'with' can only be the first statement of a @command function")]
        [TestCase("@command(free=True, acts_for=None)\ndef f(u: bool):\n    if u:\n        with preconditions:\n            require(u, text(\"m\"))\n    return 0\n",
            "'with' can only be the first statement of a @command function")]
        [TestCase("@command(free=True, acts_for=None)\ndef f(u: bool):\n    with other:\n        require(u, text(\"m\"))\n    return 0\n",
            "a command can only open 'with preconditions:'")]
        [TestCase("@command(free=True, acts_for=None)\ndef f(u: bool):\n    with preconditions:\n        x = 1\n        x += 1\n    return 0\n",
            "'with preconditions:' can only hold require(...) calls and 'name = value' assignments")]
        [TestCase("@command(free=True, acts_for=None)\ndef f(u: bool):\n    with preconditions:\n        if u:\n            require(u, text(\"m\"))\n    return 0\n",
            "'with preconditions:' can only hold require(...) calls and 'name = value' assignments")]
        [TestCase("@command(free=True, acts_for=None)\ndef f(u: bool):\n    with preconditions:\n        print(u)\n    return 0\n",
            "'with preconditions:' can only hold require(...) calls and 'name = value' assignments")]
        [TestCase("@command(free=True, acts_for=None)\ndef f(u: bool):\n    require(u, text(\"m\"))\n    return 0\n",
            "require() can only be used as a statement inside 'with preconditions:'")]
        [TestCase("@command(free=True, acts_for=None)\ndef f(u: bool):\n    with preconditions:\n        x = require(u, text(\"m\"))\n    return 0\n",
            "require() can only be used as a statement inside 'with preconditions:'")]
        [TestCase("@command(free=True, acts_for=None)\ndef f(u: bool):\n    with preconditions:\n        require(u)\n    return 0\n",
            "require() takes a condition and a message, got 1 argument")]
        [TestCase("@command(free=True, acts_for=None)\ndef f(u: bool):\n    with preconditions:\n        require(u, text(\"m\"), n=1)\n    return 0\n",
            "require() takes no keyword arguments")]
        [TestCase("@command(free=True, acts_for=None)\ndef f(u: bool):\n    with preconditions:\n        require(u, \"msg.old_form\")\n    return 0\n",
            "the message of require() must be a text(...) value")]
        [TestCase("with preconditions:\n    x = 1\n",
            "'with' can only be the first statement of a @command function")]
        public void PreconditionsPlacementAndContentAreChecked(string defs, string message)
        {
            Mod("core", defs: defs);
            var result = Load(out var diags);
            var d = diags.Single(x => x.Code == Codes.CmdPreconditions);
            Assert.That(d.Message, Is.EqualTo(message), string.Join("\n", diags));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [TestCase("cost")]
        [TestCase("choose")]
        public void CostAndChooseAreNotSupportedYet(string name)
        {
            Mod("core", defs: "@command(free=True, acts_for=None)\ndef f(u: bool):\n    with preconditions:\n        " + name + "(u, 1)\n    return 0\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.EvlUnsupported));
            Assert.That(d.Message, Is.EqualTo("'" + name + "' is not supported yet"));
        }

        // ---- 매개변수 타입 (G114) ----

        private const string TypesSchema =
            "component(\"Moves\", left=field(int, default=1))\n" +
            "deftype(\"Unit\", components=[\"Moves\"])\ndeftype(\"Resource\")\n";

        [Test]
        public void ParameterAnnotationsAreResolvedToTypes()
        {
            Mod("core", schema: TypesSchema,
                defs: "@command(free=True, acts_for=None)\ndef Trade(unit: Ref[Unit], what: DefId[\"Resource\"], n: int, rate: fixed, loud: bool, note: str,\n" +
                      "          path: list[Ref[Unit]], goods: dict[DefId[Resource], int], target: Ref[Unit] | None):\n" +
                      "    return 0\n");
            var result = LoadClean();
            var ps = result.Commands["core.Trade"].Parameters;
            Assert.That(ps.Select(p => p.Name),
                Is.EqualTo(new[] { "unit", "what", "n", "rate", "loud", "note", "path", "goods", "target" }));
            Assert.That(ps.Select(p => p.Type.Display), Is.EqualTo(new[]
            {
                "Ref[\"Unit\"]", "DefId[\"Resource\"]", "int", "fixed", "bool", "str", "list[Ref[\"Unit\"]]",
                "dict[DefId[\"Resource\"], int]", "Ref[\"Unit\"] | None",
            }));
        }

        [TestCase("def Move(unit):", "parameter 'unit' of command 'core.Move' needs a type annotation")]
        [TestCase("def Move(unit: Unit):", "a Def type cannot be a parameter type by itself")]
        [TestCase("def Move(unit: Ref[Unti]):", "there is no Def type 'Unti'")]
        [TestCase("def Move(unit: Ref[Resource]):", "'Resource' has no components, so it has no entities to pass")]
        [TestCase("def Move(unit: Ref[Unit, Unit]):", "Ref[...] takes one type name")]
        [TestCase("def Move(unit: Ref[1]):", "Ref[...] takes a Def type name")]
        [TestCase("def Move(n: float):", "'float' is not a parameter type")]
        [TestCase("def Move(n: set[int]):", "'set[...]' is not a parameter type")]
        [TestCase("def Move(n: int | str):", "only 'T | None' unions are supported in parameter types")]
        [TestCase("def Move(n: int | None | None):", "'| None' is written twice")]
        [TestCase("def Move(n: dict[bool, int]):", "map keys must be str, int, DefId[...] or Ref[...], not bool")]
        [TestCase("def Move(n: dict[int]):", "dict[...] takes a key type and a value type")]
        [TestCase("def Move(n: 3):", "this is not a parameter type")]
        public void BadParameterTypesAreLoadErrors(string header, string message)
        {
            Mod("core", schema: TypesSchema, defs: "@command(free=True, acts_for=None)\n" + header + "\n    return 0\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdParameterType));
            Assert.That(d.Message, Is.EqualTo(message));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void PlainFunctionsNeedNoAnnotations()
        {
            Mod("core", defs: "def helper(x):\n    return x\n");
            LoadClean();
        }

        [Test]
        public void ADefTypeOfAModNotDependedOnCannotBeUsed()
        {
            Mod("core", schema: TypesSchema);
            Mod("naval", defs: "@command(free=True, acts_for=None)\ndef Sail(u: Ref[Unit]):\n    return 0\n");
            Load(out var diags);
            var d = diags.Single(x => x.Code == Codes.CmdParameterType);
            Assert.That(d.Message, Is.EqualTo("Def type 'Unit' belongs to mod 'core', which mod 'naval' does not depend on"));
        }

        [Test]
        public void AShadowedTypeNameIsNotTheBuiltinType()
        {
            // G112: 모드가 int를 묶으면 그 모드에서 int는 엔진 타입이 아니다
            Mod("core", defs: "int = 3\n@command(free=True, acts_for=None)\ndef Move(n: int):\n    return 0\n");
            Load(out var diags);
            var d = diags.Single(x => x.Code == Codes.CmdParameterType);
            Assert.That(d.Message, Is.EqualTo("'int' is this mod's own name, so it cannot be used as a type here"));
        }

        // ---- 기본값 (G116) ----

        [Test]
        public void DefaultsAreLiteralsFittedToTheType()
        {
            Mod("core", schema: TypesSchema,
                defs: "Resource(\"Iron\")\n" +
                      "@command(free=True, acts_for=None)\ndef Trade(n: int = -2, rate: fixed = 3, loud: bool = False, what: DefId[Resource] = \"Iron\",\n" +
                      "          path: list[int] = [1, 2], goods: dict[str, int] = {\"a\": 1}, escort: Ref[Unit] | None = None,\n" +
                      "          unit: Ref[Unit] | None = None):\n" +
                      "    return 0\n");
            var ps = LoadClean().Commands["core.Trade"].Parameters;
            Assert.That(((IntValue)ps[0].Default!).Value, Is.EqualTo(-2));
            Assert.That(ps[1].Default, Is.InstanceOf<FixedValue>()); // G95: fixed 자리의 int는 올린다
            Assert.That(((FixedValue)ps[1].Default!).Value.Raw, Is.EqualTo(3 * Kesa.Engine.Numerics.Fixed.Scale));
            Assert.That(((StrValue)ps[3].Default!).Value, Is.EqualTo("Iron"));
            Assert.That(((ListValue)ps[4].Default!).Items, Has.Count.EqualTo(2));
            Assert.That(ps[6].Default, Is.SameAs(NoneValue.Instance));
        }

        [TestCase("n: int = 1.5", "the default of 'n' must be int, not fixed")]
        [TestCase("n: int = \"1\"", "the default of 'n' must be int, not str")]
        [TestCase("n: int = None", "the default of 'n' is None, but its type int does not allow None")]
        [TestCase("u: Ref[Unit] = None", "the default of 'u' is None, but its type Ref[\"Unit\"] does not allow None")]
        [TestCase("u: Ref[Unit] | None = \"Scout\"", "the default of 'u' cannot be written as a literal: an entity default can only be None")]
        [TestCase("n: int = 1 + 1", "a default parameter value must be a literal")]
        [TestCase("n: int = LIMIT", "a default parameter value must be a literal")]
        [TestCase("p: list[int] = [1, \"a\"]", "the default of 'p' must be int, not str")]
        [TestCase("g: dict[str, int] = {\"a\": 1, \"a\": 2}", "the key \"a\" is written twice")]
        public void BadDefaultsAreLoadErrors(string parameter, string message)
        {
            Mod("core", schema: TypesSchema, defs: "LIMIT = 3\n@command(free=True, acts_for=None)\ndef Move(" + parameter + "):\n    return 0\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdParameterType));
            Assert.That(d.Message, Is.EqualTo(message));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [TestCase("\"Irom\"", Codes.LnkNotFound, "there is no Resource 'Irom'")]
        [TestCase("\"Scout\"", Codes.LnkWrongType, "'Scout' is a Unit, but a Resource is expected here")]
        [TestCase("\"Ore\"", Codes.LnkAbstract, "'Ore' is abstract: a template cannot be referenced")]
        public void DefIdDefaultsAreLinkedLikeDefValues(string id, string code, string message)
        {
            Mod("core", schema: TypesSchema,
                defs: "Resource(\"Iron\")\nResource(\"Ore\", abstract=True)\nUnit(\"Scout\")\n" +
                      "@command(free=True, acts_for=None)\ndef Mine(what: DefId[Resource] = " + id + "):\n    return 0\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(code));
            Assert.That(d.Message, Is.EqualTo(message));
            Assert.That(d.Span.Line, Is.EqualTo(5)); // 리터럴의 위치
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void CommandsOfADisabledModAreNotListed()
        {
            Mod("broken", defs: "@command(free=True, acts_for=None)\ndef Move():\n    return 0\nx = nope\n");
            var result = Load(out _);
            Assert.That(result.Disabled, Is.EqualTo(new[] { "broken" }));
            Assert.That(result.Commands, Is.Empty);
        }
    }
}
