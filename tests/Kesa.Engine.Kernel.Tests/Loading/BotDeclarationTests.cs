using System.Linq;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// <c>@bot</c>과 <c>bot_state</c>의 로드 검사 (spec-gaps G368~G371·G374): 첫 매개변수는 맡은 엔티티, 나머지는 기본값 있는 설정이고,
    /// 본문은 판을 읽고 <c>bot.…</c>·<c>submit</c>·<c>random</c>만 쓴다. <c>bot.…</c>을 건드리는 함수는 봇 안에서만 부른다.
    /// </summary>
    public class BotDeclarationTests : LoaderTestBase
    {
        private const string Units =
            "component(\"Pos\", x=field(int, default=0))\ndeftype(\"Party\", components=[\"Pos\"])\ndeftype(\"Unit\", components=[\"Pos\"])\n" +
            "ui_state(\"Selection\", unit=ref(\"Unit\", optional=True))\n" +
            "bot_state(\"Mind\", target=ref(\"Unit\", optional=True), orders=field(dict[Ref[\"Unit\"], str], default={}), " +
            "plan=field(str, default=\"gather\"))\n";

        private const string Attack =
            "@command(free=True, acts_for=None)\ndef Attack(target: Ref[Unit]):\n    target.Pos.x = 0\n";

        [Test]
        public void ABotReadsRemembersDrawsAndSubmits()
        {
            Mod("core", schema: Units,
                defs: Attack +
                      // 맵도 그 자리에서 고치지 않고 새 값을 쓴다 (G104)
                      "def order(u, what):\n    bot.Mind.orders = {u: what}\n" +
                      "def go(u):\n    submit(\"Attack\", target=u)\n" +
                      "@bot\ndef think(me: Ref[Party], caution: int = 2):\n" +
                      "    for u in query(Unit):\n        if random.int(0, caution) == 0:\n            order(u, \"cover\")\n" +
                      "    bot.Mind.plan = \"attack\"\n    if bot.Mind.target is not None:\n        go(bot.Mind.target)\n");
            var result = LoadClean();
            Assert.That(result.Bots.Keys, Is.EqualTo(new[] { "core.think" }));
            Assert.That(result.Bots["core.think"].Parameters.Select(p => p.Name), Is.EqualTo(new[] { "me", "caution" }));
            Assert.That(result.Schema.BotStates.Single().Name, Is.EqualTo("Mind"));
            Assert.That(result.Schema.TryGetUiState("Mind", out _), Is.False);
        }

        [Test]
        public void TheFirstParameterIsTheEntityItPlays()
        {
            Mod("core", schema: Units, defs: "@bot\ndef think(level: int = 1):\n    pass\n");
            Load(out var diags);
            Assert.That(diags.Single().Message,
                Is.EqualTo("the @bot 'core.think' takes the entity it plays as its first parameter, typed Ref[...] with no default"));
        }

        [Test]
        public void SettingsNeedDefaults()
        {
            Mod("core", schema: Units, defs: "@bot\ndef think(me: Ref[Party], level: int):\n    pass\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdParameterType));
            Assert.That(d.Message, Is.EqualTo("parameter 'level' of the @bot 'core.think' needs a default"));
        }

        [Test]
        public void TheDecoratorTakesNoArguments()
        {
            Mod("core", schema: Units, defs: "@bot(seat=\"Blue\")\ndef think(me: Ref[Party]):\n    pass\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("@bot takes no arguments"));
        }

        [Test]
        public void ABotCannotChangeTheGame()
        {
            Mod("core", schema: Units, defs: "@bot\ndef think(me: Ref[Party]):\n    me.Pos.x = 1\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdUiEffect));
            Assert.That(d.Message, Is.EqualTo("the @bot 'think' changes the game; it can only change bot.… and submit commands"));
        }

        [Test]
        public void ABotCannotWait()
        {
            Mod("core", schema: Units, defs: "@bot\ndef think(me: Ref[Party]):\n    wait(1)\n");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Message), Has.Some.StartsWith("the @bot 'think' changes the game"));
        }

        [Test]
        public void ABotHasNoScreen()
        {
            Mod("core", schema: Units,
                defs: "def chosen():\n    return ui.Selection.unit\n@bot\ndef think(me: Ref[Party]):\n    u = chosen()\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Message, Is.EqualTo("the @bot 'think' uses UI state (ui.…); a bot has no screen"));
            Assert.That(d.Related.Single().Line, Is.EqualTo(5)); // 증거는 부른 곳
        }

        [Test]
        public void GameCodeCannotUseBotState()
        {
            Mod("core", schema: Units,
                defs: "def plan():\n    return bot.Mind.plan\n" +
                      "@command(free=True, acts_for=None)\ndef Go():\n    p = plan()\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdUiEffect));
            Assert.That(d.Message, Is.EqualTo("the @command 'Go' uses bot state (bot.…), so it cannot run as game code"));
        }

        [Test]
        public void AUiActionCannotUseBotState()
        {
            Mod("core", schema: Units, defs: "@ui_action\ndef pick(u: Ref[Unit]):\n    bot.Mind.target = u\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("the @ui_action 'pick' uses bot state (bot.…); only a @bot has a bot's memory"));
        }

        [Test]
        public void ADerivedValueCannotDependOnABot()
        {
            Mod("core", schema: "def plan_len(u):\n    return len(bot.Mind.plan)\n" + Units.Replace(
                    "component(\"Pos\", x=field(int, default=0))", "component(\"Pos\", x=field(int, default=0), n=derived(int, plan_len))"),
                defs: "");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Message), Has.Some.Contains("uses bot state (bot.…); game values cannot depend on a bot"));
        }

        [Test]
        public void BotStateNamesAreChecked()
        {
            Mod("core", schema: Units,
                defs: "@bot\ndef think(me: Ref[Party]):\n    bot.Mind.plans = \"x\"\n    bot.Brain.plan = \"y\"\n    m = bot.Mind\n");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Message), Is.EqualTo(new[]
            {
                "Bot state 'Mind' has no field 'plans'",
                "there is no bot state 'Brain'",
                "write the bot state as bot.State.field; bot and bot.State cannot be used as values",
            }));
        }

        [Test]
        public void ABotStateWriterIsCalledAsAStatement()
        {
            Mod("core", schema: Units,
                defs: "def set_plan(p):\n    bot.Mind.plan = p\n    return p\n" +
                      "@bot\ndef think(me: Ref[Party]):\n    xs = [set_plan(\"a\")]\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("'set_plan' changes bot state, so it can only be called as a statement"));
        }

        [Test]
        public void ABotCannotBeSubmitted()
        {
            Mod("core", schema: Units,
                defs: "@bot\ndef think(me: Ref[Party]):\n    pass\n" +
                      "@ui_action\ndef pick(u: Ref[Unit]):\n    submit(\"think\", me=u)\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("'think' is a @bot, so it cannot be submitted"));
        }

        [Test]
        public void LogicCannotCallABot()
        {
            Mod("core", schema: Units, defs: "@bot\ndef think(me: Ref[Party]):\n    pass\ndef helper(p):\n    think(p)\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("'think' is a @bot, so logic cannot call it or use it as a value"));
        }

        [Test]
        public void BotStateRulesFollowUiState()
        {
            Mod("core", schema: Units + "bot_state(\"Plan\", step=field(int))\n", defs: "");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("'step' needs a default"));
        }

        [Test]
        public void BotStateSharesTheSchemaNamespace()
        {
            Mod("core", schema: Units + "bot_state(\"Selection\", n=field(int, default=0))\n", defs: "");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(new[] { Codes.SchDuplicateName, Codes.SchDuplicateName }));
        }
    }
}
