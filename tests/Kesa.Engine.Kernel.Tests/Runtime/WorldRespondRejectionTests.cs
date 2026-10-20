using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>응답 커맨드로 이미 쓴 질문·없는 질문에 응답하면 거부되고 상태는 그대로다 (C9).</summary>
    public class WorldRespondRejectionTests : LoaderTestBase
    {
        [Test]
        public void RespondToAConsumedOrUnknownRequest()
        {
            Mod("core", schema: "component(\"S\", n=field(int, default=0))\ndeftype(\"Unit\", components=[\"S\"])\n",
                defs: "Unit(\"Scout\")\n" +
                      "@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n" +
                      "    r = ask(u, \"q\", responses=[\"Bump\"])\n    u.S.n += 10\n" +
                      "@command(acts_for=lambda u: u)\ndef Bump(u: Ref[Unit]):\n    u.S.n += 1\n");
            var w = new World(LoadClean());
            var u = w.Create("core:Scout");
            w.Seat(u, "h");
            var args = new Dictionary<string, Value> { ["u"] = new EntityValue(u) };
            var q = w.Submit("h", "core.Go", args).OpenedRequest!;
            var first = w.Respond("h", q.Id, "core.Bump", args);
            Assert.That(first.Succeeded, Is.True, first.Message);
            var save = w.Save();
            var again = w.Respond("h", q.Id, "core.Bump", args);
            var unknown = w.Respond("h", 999, "core.Bump", args);
            Assert.That((again.Outcome, unknown.Outcome), Is.EqualTo((AnswerOutcome.UnknownRequest, AnswerOutcome.UnknownRequest)));
            Assert.That(w.Save(), Is.EqualTo(save));
        }
    }
}
