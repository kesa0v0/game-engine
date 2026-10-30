using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>staged의 단계 (G430): 신호 하나가 한 단계이고, 단계의 효과가 흐려지기 시작할 때 다음 단계, 마지막 단계에서 놓는다.</summary>
    public class LookBoardStagedTests
    {
        private const string Looks =
            "<Looks>" +
            "<Look name=\"ring\" fade-in=\"0.1\" solid=\"0.2\" fade-out=\"0.5\"><Sprite src=\"r.png\" /></Look>" +
            "<Look name=\"number\" solid=\"0.4\" fade-out=\"0.5\"><Sprite src=\"n.png\" /></Look>" +
            "</Looks>";

        /// <summary><c>WorldFx</c>만 답하는 세션: 처음은 도장만, 다음은 <see cref="Next"/>.</summary>
        public class FxOnlySession : DispatchProxy
        {
            public BridgeWorldFx? Next;

            protected override object? Invoke(MethodInfo? method, object?[]? args)
            {
                if (method!.Name != nameof(IHostSession.WorldFx)) throw new NotSupportedException(method.Name);
                var fx = (long)args![0]! <= 0 || Next == null ? new BridgeWorldFx(1, false, new BridgeFxBatch[0]) : Next;
                Next = null;
                return new ValueTask<BridgeWorldFx>(fx);
            }
        }

        private static BridgeFx Fx(string look, long signal) =>
            new BridgeFx("core.hit", new BridgeLook(look, new KeyValuePair<string, BridgeValue>[0]),
                BridgeWhere.At("square", new BridgeList(new BridgeValue[] { new BridgeInt(0), new BridgeInt(0) })), null, signal);

        private static (LookBoard Board, FxOnlySession Session) Board(string presentation = BridgePresentation.Staged)
        {
            var library = LookLibrary.Load(new[]
            {
                new LookSource("core", "core/fx.look.xml", Looks),
                new LookSource("core", "core/board.space.xml", "<Spaces><Space name=\"square\" kind=\"grid\" /></Spaces>"),
            }, LookKinds.Standard());
            Assert.That(library.Problems, Is.Empty, string.Join("\n", library.Problems));
            var session = DispatchProxy.Create<IHostSession, FxOnlySession>();
            return (new LookBoard(session, new LookStage(library, new MemoryLookRenderer()), presentation), (FxOnlySession)(object)session);
        }

        private static string Shown(LookBoard board) => string.Join(" ", board.Stage.Keys.Where(k => k.StartsWith(LookStage.FxPrefix)));

        [Test]
        public async Task EachSignalIsAStepAndTheNextStartsWhenTheLastFadesOut()
        {
            var (board, session) = Board();
            Assert.That(await board.HoldAsync(), Is.False, "the first call only takes the stamp");
            session.Next = new BridgeWorldFx(5, false, new[]
            {
                new BridgeFxBatch(1, new[] { Fx("ring", 2), Fx("number", 2), Fx("ring", 3) }),
                new BridgeFxBatch(2, new[] { Fx("number", 4) }),
            });
            Assert.That(await board.HoldAsync(), Is.True);
            Assert.That(Shown(board), Is.EqualTo("fx:1 fx:2"), "the ring and the number of signal 2 together");
            board.Advance(399);
            Assert.That(Shown(board), Is.EqualTo("fx:1 fx:2"), "the number fades from 0.4 s");
            board.Advance(1);
            Assert.That(Shown(board), Is.EqualTo("fx:1 fx:2 fx:3"), "signal 3 starts under the tails");
            board.Advance(300);
            Assert.That((Shown(board), board.Holding), Is.EqualTo(("fx:1 fx:2 fx:3 fx:4", true)), "the next batch's signal follows at 0.4 + 0.3 s");
            board.Advance(400);
            Assert.That(board.Holding, Is.False, "let go when the last step fades");
        }

        [Test]
        public async Task MissedEffectsAndInstantDoNotHold()
        {
            var (board, session) = Board();
            await board.HoldAsync();
            session.Next = new BridgeWorldFx(5, true, new[] { new BridgeFxBatch(1, new[] { Fx("ring", 2), Fx("ring", 3) }) });
            Assert.That((await board.HoldAsync(), Shown(board)), Is.EqualTo((false, "fx:1 fx:2")), "missed: all at once");

            var (instant, _) = Board(BridgePresentation.Instant);
            Assert.That(await instant.HoldAsync(), Is.False);
        }
    }
}
