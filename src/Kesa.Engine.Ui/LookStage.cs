using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 판 위 그림 (G390·G395·G408·G412): 경계가 준 표시(<c>WorldMarks</c>)와 엔티티 물체(<c>WorldObjects</c>)를 그림 정의로 펼쳐 렌더러에
    /// 만들고 고치고 지운다. 같은 열쇠의 그림 값이 바뀌면 이름이 같을 때는 묶인 값만 다시 넣는다. 한 스레드에서만 쓴다.
    /// </summary>
    public sealed class LookStage
    {
        private readonly LookLibrary _library;
        private readonly ILookRenderer _renderer;
        private readonly SortedDictionary<string, ShownObject> _shown = new SortedDictionary<string, ShownObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, BridgeMarkClick> _clicks = new Dictionary<string, BridgeMarkClick>(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyDictionary<string, BridgeMarkClick>> _inputs =
            new Dictionary<string, IReadOnlyDictionary<string, BridgeMarkClick>>(StringComparer.Ordinal);
        private readonly List<UiProblem> _problems = new List<UiProblem>();
        private readonly SortedSet<string> _problemTexts = new SortedSet<string>(StringComparer.Ordinal);

        private readonly InputLibrary? _declared;

        private readonly UiTextFormatter? _format;

        /// <param name="inputs">입력 선언. 주면 표시의 <c>inputs=</c>에 선언에 없는 이름이 오면 알린다 (G424).</param>
        /// <param name="format">묶인 글자 속성(<c>Text</c>의 <c>text</c>)의 값을 화면 글자로 (G427, 화면 UXML과 같은 번역기). 없으면 수와 글을 그대로.</param>
        public LookStage(LookLibrary library, ILookRenderer renderer, InputLibrary? inputs = null, UiTextFormatter? format = null)
        {
            _library = library;
            _renderer = renderer;
            _declared = inputs;
            _format = format;
        }

        /// <summary>그리다 난 문제(같은 것은 한 번). 판은 막지 않는다 (G364·G392).</summary>
        public IReadOnlyList<UiProblem> Problems => _problems;

        /// <summary>보이는 물체의 열쇠. 표시는 <c>mark:</c>, 엔티티는 <c>entity:</c>가 앞에 붙는다.</summary>
        public IEnumerable<string> Keys => _shown.Keys;

        public void Apply(BridgeWorldMarks marks)
        {
            foreach (var mark in marks.Changed)
            {
                if (mark.Click != null) _clicks["mark:" + mark.Key] = mark.Click;
                else _clicks.Remove("mark:" + mark.Key);
                if (mark.Inputs.Count > 0) _inputs["mark:" + mark.Key] = mark.Inputs;
                else _inputs.Remove("mark:" + mark.Key);
                if (_declared != null)
                    foreach (var input in mark.Inputs.Keys.OrderBy(k => k, StringComparer.Ordinal))
                        if (!_declared.Knows(input)) Report(FunctionOf(mark.Key), _declared.Unknown(input) + " (inputs= of a mark)");
                if (mark.Click != null && mark.Inputs.ContainsKey(LookBoard.Primary))
                    Report(mark.Key, "click= and inputs[\"primary\"] both answer the primary press; click= is used (spec-gaps G418)");
                if (mark.Error != null)
                {
                    Report(mark.Key, mark.Error);
                    Hide("mark:" + mark.Key);
                    continue;
                }
                Show("mark:" + mark.Key, mark.Look, mark.Where);
            }
            foreach (var key in marks.Gone)
            {
                _clicks.Remove("mark:" + key);
                _inputs.Remove("mark:" + key);
                Leave("mark:" + key);
            }
        }

        /// <summary>그 열쇠의 표시가 누르면 낼 화면 동작 (G395·G408). 엔티티와 누를 수 없는 표시는 null.</summary>
        public BridgeMarkClick? ClickOf(string key) => _shown.ContainsKey(key) && _clicks.TryGetValue(key, out var click) ? click : null;

        /// <summary>그 열쇠의 표시가 그 입력에 낼 화면 동작 (G418): 주 누름은 <c>click=</c>이 먼저, 그다음 <c>inputs=</c>. 받지 않으면 null.</summary>
        public BridgeMarkClick? ActionOf(string key, string input)
        {
            if (!_shown.ContainsKey(key)) return null;
            if (input == LookBoard.Primary && _clicks.TryGetValue(key, out var click)) return click;
            return _inputs.TryGetValue(key, out var inputs) && inputs.TryGetValue(input, out var action) ? action : null;
        }

        /// <summary>
        /// 반직선이 닿는 자리 (G422): 선언된 공간을 이름 순으로 보아 처음 되돌린 <c>at("공간", 값)</c>. 위치는 그림 물체와 같은 좌표계(판의 바탕 기준)다.
        /// </summary>
        public BridgeWhere? PlaceOnRay(LookPosition from, LookPosition direction)
        {
            foreach (var space in _library.Spaces.Values)
                if (space.Layout.TryPlaceOnRay(from, direction, out var place) && place != null)
                    return BridgeWhere.At(space.Name, place);
            return null;
        }

        /// <summary>엔티티 물체의 열쇠 앞머리. 뒤는 엔티티 id.</summary>
        public const string EntityPrefix = "entity:";

        public void Apply(BridgeWorldObjects objects)
        {
            foreach (var o in objects.Changed) Show(EntityPrefix + o.Entity.Id, o.Look, o.Where);
            foreach (var id in objects.Gone) Leave(EntityPrefix + id);
        }

        // 판에서 사라짐: 그림 정의의 exit=을 마지막 자리에 효과로 띄우고 지운다 (G431)
        private void Leave(string key)
        {
            if (_shown.TryGetValue(key, out var shown) && shown.Definition?.Exit != null && shown.Position != null)
            {
                string fx = NextFxKey();
                ShowAt(fx, new BridgeLook(shown.Definition.Exit, shown.Look.Keys), shown.Position.Value, shown.Unit);
                if (_shown.TryGetValue(fx, out var exit)) StartFx(fx, exit);
            }
            Hide(key);
        }

        /// <summary>그림이나 자리가 없거나 자리를 못 놓으면 숨긴다.</summary>
        public void Show(string key, BridgeLook? look, BridgeWhere? where)
        {
            if (look == null || where == null || !TryPosition(key, where, out var position, out long unit))
            {
                Hide(key);
                return;
            }
            ShowAt(key, look, position, unit);
        }

        private void ShowAt(string key, BridgeLook look, LookPosition position, long unit)
        {
            _library.Looks.TryGetValue(look.Name, out var definition);
            if (_shown.TryGetValue(key, out var shown) && shown.Look.Name == look.Name)
            {
                if (!shown.Look.Equals(look))
                {
                    shown.Look = look;
                    SetKeys(shown.Scope, look);
                    foreach (var root in shown.Roots) Reevaluate(root, look.Name);
                }
            }
            else
            {
                Hide(key);
                shown = Create(key, look, definition);
            }
            if (!shown.Position.Equals(position) || shown.Unit != unit)
            {
                shown.Position = position;
                shown.Unit = unit;
                _renderer.PlaceObject(shown.Handle, position, unit);
            }
        }

        public void Hide(string key)
        {
            if (!_shown.TryGetValue(key, out var shown)) return;
            _shown.Remove(key);
            _renderer.Remove(shown.Handle);
        }

        private ShownObject Create(string key, BridgeLook look, LookDefinition? definition)
        {
            var handle = _renderer.CreateObject(key);
            var scope = new UiScope(null);
            SetKeys(scope, look);
            var shown = new ShownObject(handle, look, definition, scope);
            _shown[key] = shown;
            if (definition == null)
            {
                Report(key, "no look is named '" + look.Name + "'" + UiSuggest.Hint(look.Name, _library.Looks.Keys));
                _renderer.ShowMissing(handle);
                return shown;
            }
            for (int i = 0; i < definition.Elements.Count; i++)
                shown.Roots.Add(Build(definition.Elements[i], handle, i, scope, definition.Name));
            return shown;
        }

        private static void SetKeys(UiScope scope, BridgeLook look)
        {
            scope.Clear();
            foreach (var pair in look.Keys) scope.Set(pair.Key, pair.Value);
        }

        // ---- 효과 (G398·G425·G426) ----

        /// <summary>효과 물체의 열쇠 앞머리. 뒤는 띄운 순번.</summary>
        public const string FxPrefix = "fx:";

        private sealed class PlayingFx
        {
            public PlayingFx(string key, ShownObject shown, long fadeIn, long solid, long fadeOut, long driftX, long driftY, long grow)
            {
                Key = key;
                Shown = shown;
                FadeIn = fadeIn;
                Solid = solid;
                FadeOut = fadeOut;
                DriftX = driftX;
                DriftY = driftY;
                Grow = grow;
            }

            public string Key { get; }
            public ShownObject Shown { get; }

            /// <summary>밀리초.</summary>
            public long FadeIn { get; }
            public long Solid { get; }
            public long FadeOut { get; }
            public long Life => FadeIn + Solid + FadeOut;

            /// <summary>초당 공간 단위(고정소수점). <c>rise</c>는 y만 있는 <c>drift</c>다.</summary>
            public long DriftX { get; }
            public long DriftY { get; }

            /// <summary>초당 크기 배율 변화(고정소수점).</summary>
            public long Grow { get; }

            public long Elapsed;
        }

        private readonly List<PlayingFx> _playing = new List<PlayingFx>();
        private long _fxNext;

        /// <summary>떠 있는 효과의 수.</summary>
        public int PlayingCount => _playing.Count;

        /// <summary>
        /// 새 효과를 띄운다. 얼마 동안 어떻게 보이는지는 그림 정의의 시간 칸이고 그림 값이 덮는다(G425). 시간은 <see cref="Advance"/>가 흐르게 한다.
        /// 효과 함수의 실패는 문제로 남긴다.
        /// </summary>
        public void Play(BridgeWorldFx fx)
        {
            foreach (var batch in fx.Batches) Play(batch.Effects);
        }

        /// <summary>효과들을 함께 띄우고, 그중 가장 늦게 흐려지기 시작하는 때(밀리초, <c>fade-in</c>+<c>solid</c>)를 돌려준다 (G430의 한 단계).</summary>
        public long Play(IEnumerable<BridgeFx> effects)
        {
            long lead = 0;
            foreach (var effect in effects)
            {
                if (effect.Error != null)
                {
                    Report(effect.Function, effect.Error);
                    continue;
                }
                string key = NextFxKey();
                Show(key, effect.Look, effect.Where);
                if (_shown.TryGetValue(key, out var shown)) lead = Math.Max(lead, StartFx(key, shown));
            }
            return lead;
        }

        private string NextFxKey() => FxPrefix + (++_fxNext).ToString(System.Globalization.CultureInfo.InvariantCulture);

        // 띄운 물체를 효과로 돌리기 시작하고 흐려지기 시작하는 때(밀리초)를 돌려준다
        private long StartFx(string key, ShownObject shown)
        {
            var drift = Field(shown, "drift");
            var playing = new PlayingFx(key, shown, Millis(Number(shown, "fade-in")), Millis(Number(shown, "solid")),
                Millis(Number(shown, "fade-out")), drift?.Numbers[0] ?? 0, (drift?.Numbers[1] ?? 0) + Number(shown, "rise"), Number(shown, "grow"));
            if (playing.Life == 0 && shown.Definition != null)
                Report(shown.Definition.Place, "look '" + shown.Definition.Name + "' is played as an effect but has no fade-in, solid or fade-out, " +
                                               "so it ends at once (spec-gaps G425)");
            _playing.Add(playing);
            Animate(playing);
            return playing.FadeIn + playing.Solid;
        }

        /// <summary>떠 있는 효과를 모두 지운다 (재생 건너뛰기).</summary>
        public void StopEffects()
        {
            foreach (var playing in _playing) Hide(playing.Key);
            _playing.Clear();
        }

        /// <summary>떠 있는 효과의 시간을 <paramref name="milliseconds"/>만큼 흐르게 하고, 수명이 끝난 것을 지운다.</summary>
        public void Advance(long milliseconds)
        {
            for (int i = _playing.Count - 1; i >= 0; i--)
            {
                var playing = _playing[i];
                playing.Elapsed += milliseconds;
                if (playing.Elapsed >= playing.Life)
                {
                    _playing.RemoveAt(i);
                    Hide(playing.Key);
                    continue;
                }
                Animate(playing);
            }
        }

        private void Animate(PlayingFx p)
        {
            long t = p.Elapsed;
            long opacity = t < p.FadeIn ? t * BridgeFixed.Scale / p.FadeIn
                : t < p.FadeIn + p.Solid ? BridgeFixed.Scale
                : p.FadeOut == 0 ? 0
                : BridgeFixed.Scale - (t - p.FadeIn - p.Solid) * BridgeFixed.Scale / p.FadeOut;
            _renderer.SetOpacity(p.Shown.Handle, opacity);
            if (p.DriftX == 0 && p.DriftY == 0 && p.Grow == 0) return;
            long unit = p.Shown.Unit;
            var at = p.Shown.Position ?? default;
            var moved = new LookPosition(at.X + p.DriftX * unit / BridgeFixed.Scale * t / 1000, at.Y + p.DriftY * unit / BridgeFixed.Scale * t / 1000, at.Z);
            // 크기는 물체의 단위에 곱한다: 1 + grow × 초, 0 아래로는 가지 않음
            long scale = Math.Max(0, BridgeFixed.Scale + p.Grow * t / 1000);
            _renderer.PlaceObject(p.Shown.Handle, moved, unit * scale / BridgeFixed.Scale);
        }

        private long Number(ShownObject shown, string name) => Field(shown, name)?.Numbers[0] ?? 0;

        // 그림 값이 덮으면 그 값, None이거나 못 읽으면 적힌 값, 둘 다 없으면 null (G425)
        private LookAttributeValue? Field(ShownObject shown, string name)
        {
            var definition = shown.Definition;
            if (definition == null) return null;
            definition.Timing.TryGetValue(name, out var written);
            foreach (var binding in definition.TimingBindings)
            {
                if (binding.Attribute != name) continue;
                if (!TryRead(binding.Expression, shown.Scope, out var raw, out string error))
                {
                    Report(definition.Place, "look '" + definition.Name + "', bind-" + name + "=\"" + binding.Source + "\": " + error);
                    continue;
                }
                if (raw is BridgeNone) continue;
                var value = LookAttributes.FromValue(binding.Type, raw, out error);
                if (value == null || (LookDefinition.IsDuration(name) && value.Numbers[0] < 0))
                {
                    Report(definition.Place, "look '" + definition.Name + "', bind-" + name + "=\"" + binding.Source + "\": " +
                                             (value == null ? error : "a time is not negative"));
                    continue;
                }
                return value;
            }
            return written;
        }

        private static long Millis(long seconds) => seconds * 1000 / BridgeFixed.Scale;

        // ---- 자리 (G394·G414) ----

        private bool TryPosition(string key, BridgeWhere where, out LookPosition position, out long unit)
        {
            position = default;
            unit = 0;
            if (where.Shape != BridgeWhereShape.At)
            {
                Report(key, (where.Shape == BridgeWhereShape.Onto ? "onto(…)" : "between(…)") + " is not drawn yet");
                return false;
            }
            if (where.Name == null)
            {
                Report(key, "at(…) without a space name needs a default space, which cannot be declared yet; write at(\"space\", …)");
                return false;
            }
            if (!_library.Spaces.TryGetValue(where.Name, out var space))
            {
                Report(key, "no space is named '" + where.Name + "'" + UiSuggest.Hint(where.Name, _library.Spaces.Keys));
                return false;
            }
            unit = space.Layout.Unit;
            if (space.Layout.TryPlace(where.Target!, out position, out string error)) return true;
            Report(key, "space '" + where.Name + "': " + error);
            return false;
        }

        // ---- 펼치기 (G390·G413) ----

        private LookNode Build(LookElement definition, ElementId parent, int index, UiScope scope, string look)
        {
            var node = new LookNode(definition, _renderer.Create(definition.Kind, parent, index), scope);
            foreach (var attribute in definition.Attributes) _renderer.Set(node.Handle, attribute.Key, attribute.Value);
            ApplyBindings(node, look);
            if (definition.Repeat != null)
            {
                node.Groups = new List<LookGroup>();
                Reconcile(node, look);
            }
            else
            {
                for (int i = 0; i < definition.Children.Count; i++)
                    node.Children.Add(Build(definition.Children[i], node.Handle, i, scope, look));
            }
            return node;
        }

        private void Reevaluate(LookNode node, string look)
        {
            ApplyBindings(node, look);
            if (node.Groups != null)
            {
                Reconcile(node, look);
                return;
            }
            foreach (var child in node.Children) Reevaluate(child, look);
        }

        /// <summary>묶인 값을 계산해 바뀐 것만 넣는다. 실패하면 문제를 담고 직전 값을 그대로 둔다 (G341과 같은 길).</summary>
        private void ApplyBindings(LookNode node, string look)
        {
            foreach (var binding in node.Definition.Bindings)
            {
                string label = "look '" + look + "', bind-" + binding.Attribute + "=\"" + binding.Source + "\"";
                if (!TryRead(binding.Expression, node.Scope, out var raw, out string error))
                {
                    Report(node.Definition.Place, label + ": " + error);
                    continue;
                }
                var value = binding.Type == LookAttributeType.Text && _format != null && !(raw is BridgeNone)
                    ? LookAttributeValue.OfText(_format(raw))
                    : LookAttributes.FromValue(binding.Type, raw, out error);
                if (value == null)
                {
                    Report(node.Definition.Place, label + ": " + error);
                    continue;
                }
                if (node.Applied.TryGetValue(binding, out var before) && before.Equals(value)) continue;
                node.Applied[binding] = value;
                if (binding.Attribute == "display") _renderer.SetVisible(node.Handle, value.Flag);
                else _renderer.Set(node.Handle, binding.Attribute, value);
            }
        }

        private static bool TryRead(VarExpr expression, UiScope scope, out BridgeValue value, out string error)
        {
            // 그림 값에 없는 키는 None (G415): 켜진 상태만 넘기면 된다
            if (!scope.TryGet(expression.Name, out _))
            {
                value = BridgeNone.Instance;
                error = "";
                return true;
            }
            return UiInterpreter.ResolveVariable(expression, scope, out value, out error);
        }

        // ---- 반복 (G338·G339와 같은 짝짓기) ----

        private void Reconcile(LookNode owner, string look)
        {
            var repeat = owner.Definition.Repeat!;
            var groups = owner.Groups!;
            if (!TryRead(repeat.Source, owner.Scope, out var value, out string error) || !(value is BridgeList list))
            {
                if (error.Length == 0) error = "a list is needed, got a " + value.Kind.ToString().ToLowerInvariant();
                Report(owner.Definition.Place, "look '" + look + "', for=\"" + repeat.Text + "\": " + error);
                return;
            }
            var items = list.Items;
            int templateCount = owner.Definition.Children.Count;

            var survivors = new Dictionary<string, LookGroup>(StringComparer.Ordinal);
            foreach (var g in groups) survivors[g.Key] = g;
            var seen = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var keys = new string[items.Count];
            var kept = new Dictionary<string, LookGroup>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                keys[i] = UiValues.RepeatKey(items[i], i, seen);
                if (survivors.TryGetValue(keys[i], out var existing)) kept[keys[i]] = existing;
            }

            foreach (var g in groups.ToArray())
            {
                if (kept.ContainsKey(g.Key)) continue;
                groups.Remove(g);
                foreach (var root in g.Roots) _renderer.Remove(root.Handle);
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (kept.TryGetValue(keys[i], out var group))
                {
                    int current = groups.IndexOf(group);
                    if (current != i)
                    {
                        groups.RemoveAt(current);
                        groups.Insert(i, group);
                        for (int r = 0; r < group.Roots.Count; r++) _renderer.Move(group.Roots[r].Handle, owner.Handle, i * templateCount + r);
                    }
                    group.Item = items[i];
                    group.Scope.Set(repeat.Variable, items[i]);
                    foreach (var root in group.Roots) Reevaluate(root, look);
                    continue;
                }
                var created = new LookGroup(keys[i], items[i], new UiScope(owner.Scope));
                created.Scope.Set(repeat.Variable, items[i]);
                groups.Insert(i, created);
                for (int r = 0; r < templateCount; r++)
                    created.Roots.Add(Build(owner.Definition.Children[r], owner.Handle, i * templateCount + r, created.Scope, look));
            }
        }

        // 같은 함수의 표시마다 같은 경고를 내지 않게
        private static string FunctionOf(string markKey)
        {
            int slash = markKey.IndexOf('/');
            return slash < 0 ? markKey : markKey.Substring(0, slash);
        }

        private void Report(string place, string message)
        {
            if (!_problemTexts.Add(place + "\n" + message)) return;
            _problems.Add(new UiProblem(place, message));
        }
    }
}
