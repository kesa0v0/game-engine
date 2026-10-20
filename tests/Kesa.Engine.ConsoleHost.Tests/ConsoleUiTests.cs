using System.IO;
using NUnit.Framework;

namespace Kesa.Engine.ConsoleHost.Tests
{
    /// <summary>콘솔의 UI 파일 <c>ui/console.txt</c> (spec-gaps G250): 자리마다 적힌 함수를 불러 그 값을 보인다.</summary>
    public class ConsoleUiTests
    {
        private string _root = null!;

        [SetUp]
        public void NewMods()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-console-ui-" + TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            string mod = Path.Combine(_root, "banking");
            Directory.CreateDirectory(Path.Combine(mod, "schema"));
            Directory.CreateDirectory(Path.Combine(mod, "defs"));
            File.WriteAllText(Path.Combine(mod, "mod.py"), "Mod(id=\"banking\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            File.WriteAllText(Path.Combine(mod, "schema", "types.py"),
                "component(\"Purse\", gold=field(int, default=0))\n" +
                "component(\"Worth\", total=derived(int, lambda c: sum([x.Coin.value for x in c.entity.coins])))\n" +
                "component(\"Coin\", bank=ref(\"Bank\", optional=True, inverse=\"coins\"), value=field(int, default=1))\n" +
                "deftype(\"Bank\", components=[\"Purse\", \"Worth\", \"Seatable\"])\ndeftype(\"Token\", components=[\"Coin\"])\n");
            File.WriteAllText(Path.Combine(mod, "defs", "rules.py"),
                "Bank(\"Vault\")\nToken(\"Penny\")\n" +
                "@command(free=True, acts_for=None)\ndef Open():\n    with preconditions:\n" +
                "        require(len(query(\"Bank\")) == 0, text(\"banking.open\"))\n" +
                "    v = spawn(\"Vault\")\n    spawn(\"Penny\", Coin=Coin(bank=v))\n" +
                "@command(free=True, acts_for=None)\ndef Polish():\n    with preconditions:\n" +
                "        require(len(query(\"Bank\")) > 0, text(\"banking.closed\"))\n" +
                "    for t in query(\"Token\"):\n        t.Coin.value += 1\n" +
                "@command(free=True, acts_for=lambda bank: bank)\ndef Earn(bank: Ref[Bank]):\n    with preconditions:\n" +
                "        choose(bank, where=lambda b: True)\n    bank.Purse.gold += 5\n" +
                "def gold(me):\n    return me.Purse.gold\n" +
                "def coins(me):\n    return [\"gold\", me.Purse.gold]\n" +
                "def broken(me):\n    return me.Purse.gold // 0\n");
        }

        [TearDown]
        public void RemoveMods() => Directory.Delete(_root, true);

        private void Ui(string text)
        {
            Directory.CreateDirectory(Path.Combine(_root, "banking", "ui"));
            File.WriteAllText(Path.Combine(_root, "banking", "ui", "console.txt"), text);
        }

        private (int Code, string Screen, string Errors) Play(string input)
        {
            var screen = new StringWriter();
            var errors = new StringWriter();
            int code = ConsoleHost.Play.Run(new[] { "--lang", "ko", _root, "--seat", "red=banking:Vault" }, new StringReader(input), screen, errors);
            return (code, screen.ToString().Replace("\r\n", "\n"), errors.ToString());
        }

        [Test]
        public void EachSeatShowsTheValuesOfItsFunctions()
        {
            Ui("# 내 금고\nbanking.gold\n\nbanking.coins  # 목록은 한 줄씩\n");
            var (code, screen, errors) = Play("1\n1\n:q\n"); // Open, 그다음 Earn
            Assert.That(code, Is.EqualTo(0), errors);
            Assert.That(screen, Does.Contain("[red] #1 banking:Vault\n  0\n  gold\n  0\n"));
            Assert.That(screen, Does.Contain("[red] #1 banking:Vault\n  5\n  gold\n  5\n"));
            Assert.That(screen, Does.Not.Contain("Purse(gold="));
        }

        [Test]
        public void AWrongFunctionNameStopsAtTheStartWithItsLine()
        {
            Ui("banking.gold\nbanking.gld\nbanking.Earn\n");
            var (code, _, errors) = Play(":q\n");
            Assert.That(code, Is.EqualTo(1));
            Assert.That(errors, Does.Contain("banking/ui/console.txt:2: ").And.Contain("gold"));
            Assert.That(errors, Does.Contain("banking/ui/console.txt:3: ").And.Contain("@command"));
            Assert.That(errors, Does.Not.Contain("console.txt:1:"));
        }

        [Test]
        public void AFailingFunctionShowsAnErrorLineAndTheRestIsDrawn()
        {
            Ui("banking.broken\nbanking.gold\n");
            var (code, screen, errors) = Play("1\n1\n:q\n");
            Assert.That(code, Is.EqualTo(0), errors);
            Assert.That(screen, Does.Contain("[red] #1 banking:Vault\n  banking/ui/console.txt:1: "));
            Assert.That(screen, Does.Contain("\n  5\n"));
        }

        [Test]
        public void WithoutAUiFileTheEntitiesAreListed()
        {
            var (code, screen, errors) = Play("1\n:q\n");
            Assert.That(code, Is.EqualTo(0), errors);
            Assert.That(screen, Does.Contain("#1 banking:Vault  Seatable() Purse(gold=0) Worth(total=1)"));
        }

        [Test]
        public void TheListingRereadsADerivedValueThatReadsOtherEntities()
        {
            // 동전을 닦으면 금고의 도장은 그대로지만 Worth.total은 바뀐다 (G251)
            var (code, screen, errors) = Play("1\n2\n:q\n"); // Open, 그다음 Polish
            Assert.That(code, Is.EqualTo(0), errors);
            Assert.That(screen, Does.Contain("#1 banking:Vault  Seatable() Purse(gold=0) Worth(total=2)"));
        }
    }
}
