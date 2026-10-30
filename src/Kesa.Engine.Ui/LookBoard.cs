using System.Collections.Generic;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 한 연결의 판 위 그림을 경계와 맞춘다 (G405·G408·G426): 프레임마다 <see cref="RefreshAsync"/>가 엔티티 물체와 표시의 바뀐 것, 새 효과를 받아
    /// <see cref="LookStage"/>에 넣는다. 도장은 셋이 따로 센다. 재생이 시간에 따라 흐르게 호스트가 <see cref="Advance"/>로 시간을 넘긴다.
    /// <c>staged</c>(G429·G430·G437)면 화면이 세계를 따라잡는 동안 붙잡는다: 효과를 신호마다 한 단계씩 띄우고, 다 띄우면 판을 맞추고,
    /// 무대의 재생(효과·옮기기)이 붙잡을 때를 넘길 때까지 기다린다. 붙잡기는 이 연결의 화면만의 일이다.
    /// </summary>
    public sealed class LookBoard : IPresentationHold
    {
        private readonly IHostSession _session;
        private readonly bool _staged;
        private readonly Queue<List<BridgeFx>> _steps = new Queue<List<BridgeFx>>();
        private long _objects;
        private long _marks;
        private long _fx;
        // 건너뛴 뒤 처음 판을 맞출 때 생긴 재생(옮기기)도 끝 상태로 보낸다
        private bool _skipped;

        /// <param name="presentation"><see cref="BridgeContent.Presentation"/>.</param>
        public LookBoard(IHostSession session, LookStage stage, string presentation = BridgePresentation.Instant)
        {
            _session = session;
            Stage = stage;
            _staged = presentation == BridgePresentation.Staged;
        }

        public LookStage Stage { get; }

        public bool Holding => _staged && (_steps.Count > 0 || Stage.Busy);

        public async ValueTask RefreshAsync()
        {
            if (_staged)
            {
                await HoldAsync();
                return;
            }
            await CatchUp();
            var fx = await _session.WorldFx(_fx);
            Stage.Play(fx);
            _fx = fx.Stamp;
        }

        /// <summary>staged: 새 효과가 있으면 단계로 띄우고, 없으면 판을 세계에 맞춘다. 그 재생이 남아 있으면 참.</summary>
        public async ValueTask<bool> HoldAsync()
        {
            if (Holding) return true;
            if (!_staged) return false;
            var fx = await _session.WorldFx(_fx);
            _fx = fx.Stamp;
            // 놓친 것이 있으면 단계가 맞지 않으므로 남은 것을 한꺼번에 띄운다
            if (fx.Missed) Stage.Play(fx);
            else
                foreach (var batch in fx.Batches)
                {
                    List<BridgeFx>? step = null;
                    foreach (var effect in batch.Effects)
                    {
                        if (step == null || step[0].Signal != effect.Signal)
                        {
                            step = new List<BridgeFx>();
                            _steps.Enqueue(step);
                        }
                        step.Add(effect);
                    }
                }
            NextSteps();
            if (!Holding)
            {
                await CatchUp();
                if (_skipped) Stage.Settle();
                _skipped = false;
            }
            return Holding;
        }

        private async ValueTask CatchUp()
        {
            var objects = await _session.WorldObjects(_objects);
            Stage.Apply(objects);
            _objects = objects.Stamp;
            var marks = await _session.WorldMarks(_marks);
            Stage.Apply(marks);
            _marks = marks.Stamp;
        }

        /// <summary>재생의 시간을 흐르게 하고, 앞 단계가 붙잡을 때를 넘겼으면 다음 단계를 띄운다.</summary>
        public void Advance(long milliseconds)
        {
            Stage.Advance(milliseconds);
            NextSteps();
        }

        public void Skip()
        {
            _steps.Clear();
            Stage.Settle();
            _skipped = true;
        }

        private void NextSteps()
        {
            while (_steps.Count > 0 && !Stage.Busy) Stage.Play(_steps.Dequeue());
        }

        /// <summary>내장 입력: 주 누름(왼쪽 클릭·탭)과 보조 누름 (G416).</summary>
        public const string Primary = "primary";

        public const string Secondary = "secondary";

        /// <summary>
        /// 커서 아래 물체들(위에 그린 것부터)과 자리(<see cref="LookStage.PlaceOnRay"/>)에 들어온 입력을 받을 것에 준다 (G395·G419·G422): 그 입력을
        /// 받는 표시(<c>click=</c>·<c>inputs=</c>)가 먼저, 없으면 엔티티들과 자리를 경계의 <c>WorldInput</c>에 넘긴다. 받을 것이 없으면 null. 연출을 붙잡고 있으면 건너뛰기만 하고 null (G430).
        /// </summary>
        public async ValueTask<BridgeResult?> InputAsync(string input, IEnumerable<string> keysUnderPointer, BridgeWhere? place = null)
        {
            if (Holding)
            {
                Skip();
                return null;
            }
            var entities = new List<long>();
            foreach (var key in keysUnderPointer)
            {
                var action = Stage.ActionOf(key, input);
                if (action != null) return await _session.Act(action.Action, action.Arguments);
                if (key.StartsWith(LookStage.EntityPrefix, System.StringComparison.Ordinal) &&
                    long.TryParse(key.Substring(LookStage.EntityPrefix.Length), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var id))
                    entities.Add(id);
            }
            return entities.Count == 0 && place == null ? null : await _session.WorldInput(input, entities, place);
        }

        /// <summary>주 누름: <c>InputAsync(Primary, …)</c>.</summary>
        public ValueTask<BridgeResult?> PressAsync(IEnumerable<string> keysUnderPointer) => InputAsync(Primary, keysUnderPointer);
    }
}
