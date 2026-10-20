using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// DefId 값을 <c>:</c> 없는 문자열 리터럴과 비교하거나 그것으로 찾으면 경고 (spec-gaps G261): 실행 중의 값은 늘 전체 이름이라
    /// 조용히 맞지 않는다.
    /// </summary>
    public class BareDefIdWarningTests : LoaderTestBase
    {
        private const string Schema =
            "deftype(\"Cargo\")\n" +
            "component(\"Mark\", kind=field(DefId[\"Ship\"], default=\"Sloop\"), name=field(str, default=\"\"))\n" +
            "deftype(\"Ship\", components=[\"Mark\"], cost=const(dict[DefId[\"Cargo\"], int]), builds=const(list[DefId[\"Ship\"]]))\n";

        private const string Defs = "Cargo(\"Salt\")\nShip(\"Sloop\")\nShip(\"Brig\")\n";

        private static string Diag(Diagnostic d) =>
            d.Code + " " + d.Severity + " " + d.Line + " " + d.Message + (d.Hint == null ? "" : " | " + d.Hint);

        private IReadOnlyList<Diagnostic> Check(string code, out ContentLoadResult result)
        {
            Mod("banking", schema: Schema, defs: Defs + code);
            result = Load(out var diags);
            return diags;
        }

        private string[] Lines(string code) => Check(code, out _).Select(d => d.Code + " " + d.Line).ToArray();

        [Test]
        public void ComparingAFieldWithABareLiteralIsAWarningThatKeepsTheModOn()
        {
            var diags = Check("def f(s: Ref[Ship]) -> bool:\n    return s.Mark.kind == \"Sloop\"\n", out var result);
            Assert.That(diags.Select(Diag), Is.EqualTo(new[]
            {
                "LNK005 Warning 5 a Def id value is always a full id like 'banking:Sloop', so it never equals \"Sloop\"" +
                " | write defid(\"Sloop\") or \"banking:Sloop\"",
            }));
            Assert.That(result.Disabled, Is.Empty);
        }

        [Test]
        public void FullIdsDefidAndPlainStringsAreFine()
        {
            Assert.That(Lines("def f(s: Ref[Ship]) -> bool:\n" +
                              "    return s.Mark.kind == \"banking:Sloop\" or s.Mark.kind == defid(\"Brig\") or s.Mark.name == \"Sloop\"\n"),
                Is.Empty);
        }

        [Test]
        public void EveryPlaceADefIdMeetsABareLiteralIsFound()
        {
            Assert.That(Lines(
                "def f(s: Ref[Ship], k: DefId[Ship]) -> int:\n" +              // 4
                "    n = 1 if \"Brig\" != k else 0\n" +                         // 5: 매개변수, 왼쪽에 리터럴
                "    n += s.Ship.cost[\"Salt\"]\n" +                            // 6: DefId 키 맵에서 찾기
                "    n += 1 if \"Salt\" in s.Ship.cost else 0\n" +              // 7: 맵의 in
                "    n += 1 if \"Brig\" in s.Ship.builds else 0\n" +            // 8: DefId 리스트의 in
                "    n += 1 if k in [\"Sloop\", \"banking:Brig\"] else 0\n" +   // 9: 리터럴 목록 (맨 것만)
                "    for b in s.Ship.builds:\n" +
                "        n += 1 if b == \"Sloop\" else 0\n" +                   // 11: 리스트의 원소
                "    for c in s.Ship.cost:\n" +
                "        n += 1 if c == \"Salt\" else 0\n" +                    // 13: 맵의 키
                "    return n\n"),
                Is.EqualTo(new[] { "LNK005 5", "LNK005 6", "LNK005 7", "LNK005 8", "LNK005 9", "LNK005 11", "LNK005 13" }));
        }

        [Test]
        public void AChoiceTypoForADefIdParameterIsALoadError()
        {
            var diags = Check("@command(free=True, acts_for=None)\ndef Pick(k: DefId[Ship]):\n" +
                              "    with preconditions:\n        choose(k, from_=[\"Sloop\", \"Slop\"])\n", out var result);
            Assert.That(diags.Select(Diag), Is.EqualTo(new[]
            {
                "LNK001 Error 7 there is no Ship 'banking:Slop' | did you mean 'banking:Sloop'?",
            }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "banking" }));
        }
    }
}
