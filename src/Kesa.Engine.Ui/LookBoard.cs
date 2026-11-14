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
    /// 포인터(<see cref="PointAsync"/>·<see cref="PressedAsync"/>·<see cref="DragAsync"/>·<see cref="ReleaseAsync"/>)는 커서·누름·끌기를 경계로 낸다 (G485~G490).
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

        // ---- 포인터: 커서·누름·끌기 (G485~G490) ----

        private string? _cursorSpace;
        private BridgeValue? _cursorPlace;
        private Pending? _pending;
        private readonly List<Drawing> _drawings = new List<Drawing>();

        /// <summary>포인터 단추를 눌렀을 때의 입력들과 커서 아래 (G486: 어느 입력인지는 누를 때 정한다).</summary>
        private sealed class Pending
        {
            public Pending(List<string> inputs, List<string> keys, BridgeWhere? place)
            {
                Inputs = inputs;
                Keys = keys;
                Place = place;
            }

            public List<string> Inputs { get; }
            public List<string> Keys { get; }
            public BridgeWhere? Place { get; }
        }

        private sealed class Drawing
        {
            public Drawing(string input, string space, GestureStroke stroke)
            {
                Input = input;
                Space = space;
                Stroke = stroke;
            }

            public string Input { get; }
            public string Space { get; }
            public GestureStroke Stroke { get; }
        }

        /// <summary>단추를 누른 채 아직 문턱을 넘지 않았다.</summary>
        public bool Pressing => _pending != null;

        /// <summary>끌기가 진행 중이다.</summary>
        public bool Dragging => _drawings.Count > 0;

        /// <summary>
        /// 커서가 판 위 자리(<see cref="LookStage.PlaceOnRay"/>, null이면 공간 밖)로 왔다. 바뀐 공간만 경계의 <c>Cursor</c>로 알리고(G488·G490),
        /// 끌기 중이면 시작한 공간의 자리만 그 획에 더해 <c>Drag</c>로 알린다(G484·G487). 매 프레임 불러도 된다.
        /// </summary>
        public async ValueTask PointAsync(BridgeWhere? place)
        {
            bool at = place != null && place.Shape == BridgeWhereShape.At && place.Name != null && place.Target != null;
            string? space = at ? place!.Name : null;
            BridgeValue? value = at ? place!.Target : null;
            if (space != _cursorSpace || !Equals(value, _cursorPlace))
            {
                if (_cursorSpace != null && _cursorSpace != space) await _session.Cursor(_cursorSpace, null);
                if (space != null) await _session.Cursor(space, value);
                _cursorSpace = space;
                _cursorPlace = value;
            }
            if (value == null) return;
            foreach (var drawing in _drawings)
                if (drawing.Space == space && drawing.Stroke.Add(value))
                    await _session.Drag(drawing.Input, drawing.Stroke.Value);
        }

        /// <summary>
        /// 이번 프레임에 눌린 입력들(호스트가 수정 키 조합을 가린 것, G423)과 커서 아래. 키보드 입력은 바로 내고, 포인터 단추 입력은 붙잡아 두었다가
        /// <see cref="ReleaseAsync"/>에 낸다(G485). 누를 때의 조합이 끝까지 간다(G486).
        /// </summary>
        public async ValueTask<IReadOnlyList<BridgeResult>> PressedAsync(IReadOnlyList<string> inputs, IReadOnlyList<string> keysUnderPointer,
            BridgeWhere? place = null)
        {
            var results = new List<BridgeResult>();
            var pointer = new List<string>();
            foreach (var input in inputs)
            {
                if (Stage.Inputs?.IsPointer(input) ?? (input == Primary || input == Secondary))
                {
                    pointer.Add(input);
                    continue;
                }
                var result = await InputAsync(input, keysUnderPointer, place);
                if (result != null) results.Add(result);
            }
            if (pointer.Count == 0 || Dragging) return results;
            if (_pending == null) _pending = new Pending(pointer, new List<string>(keysUnderPointer), place);
            else _pending.Inputs.AddRange(pointer);
            return results;
        }

        /// <summary>
        /// 누른 채 문턱을 넘었다(거리는 호스트 설정, G485). 붙잡은 누름은 내지 않고, 그중 모양 종류가 붙은 입력마다 누른 자리의 공간에서 획을 연다.
        /// 누른 곳이 공간 밖이면 아무것도 열지 않는다.
        /// </summary>
        public async ValueTask DragAsync()
        {
            var pending = _pending;
            _pending = null;
            if (pending == null || pending.Place == null || pending.Place.Shape != BridgeWhereShape.At || pending.Place.Name == null ||
                pending.Place.Target == null)
                return;
            string space = pending.Place.Name;
            var layout = Stage.LayoutOf(space);
            if (layout == null) return;
            foreach (var input in pending.Inputs)
            {
                var declared = Stage.Inputs?.Drawn(input);
                if (declared == null) continue;
                var stroke = declared.Shape!.Begin(layout, pending.Place.Target, out string error);
                if (stroke == null)
                {
                    Stage.Report(declared.Place, error);
                    continue;
                }
                if (_cursorSpace == space && _cursorPlace != null) stroke.Add(_cursorPlace);
                _drawings.Add(new Drawing(input, space, stroke));
                await _session.Drag(input, stroke.Value);
            }
        }

        /// <summary>
        /// 단추를 뗐다. 문턱을 넘지 않았으면 붙잡은 누름을 누른 자리로 내고, 끌었으면 끝난 획마다 <c>Drag</c>를 비우고 <c>WorldInput(입력, [], at(공간, 값))</c>을
        /// 낸다(G484·G490). 연출을 붙잡고 있으면 건너뛰기만 한다(G430).
        /// </summary>
        public async ValueTask<IReadOnlyList<BridgeResult>> ReleaseAsync()
        {
            var results = new List<BridgeResult>();
            var pending = _pending;
            _pending = null;
            var drawings = new List<Drawing>(_drawings);
            _drawings.Clear();
            bool skip = (pending != null || drawings.Count > 0) && Holding;
            if (skip) Skip();
            foreach (var drawing in drawings) await _session.Drag(drawing.Input, null);
            if (skip) return results;
            if (pending != null)
                foreach (var input in pending.Inputs)
                {
                    // 모양 종류가 붙은 입력은 끌기로만 낸다: 핸들러는 그 종류의 값을 받는다 (G483)
                    if (Stage.Inputs?.Drawn(input) != null) continue;
                    var result = await InputAsync(input, pending.Keys, pending.Place);
                    if (result != null) results.Add(result);
                }
            foreach (var drawing in drawings)
            {
                var result = await _session.WorldInput(drawing.Input, new long[0], BridgeWhere.At(drawing.Space, drawing.Stroke.Value));
                if (result != null) results.Add(result);
            }
            return results;
        }

        /// <summary>누름이나 끌기를 그만둔다(창이 포커스를 잃음 등). 아무것도 내지 않는다.</summary>
        public async ValueTask CancelPointerAsync()
        {
            _pending = null;
            var drawings = new List<Drawing>(_drawings);
            _drawings.Clear();
            foreach (var drawing in drawings) await _session.Drag(drawing.Input, null);
        }
    }
}
