using System;
using System.Collections.Generic;
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
        private readonly List<UiProblem> _problems = new List<UiProblem>();
        private readonly SortedSet<string> _problemTexts = new SortedSet<string>(StringComparer.Ordinal);

        public LookStage(LookLibrary library, ILookRenderer renderer)
        {
            _library = library;
            _renderer = renderer;
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
                Hide("mark:" + key);
            }
        }

        /// <summary>그 열쇠의 표시가 누르면 낼 화면 동작 (G395·G408). 엔티티와 누를 수 없는 표시는 null.</summary>
        public BridgeMarkClick? ClickOf(string key) => _shown.ContainsKey(key) && _clicks.TryGetValue(key, out var click) ? click : null;

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
            foreach (var id in objects.Gone) Hide(EntityPrefix + id);
        }

        /// <summary>그림이나 자리가 없거나 자리를 못 놓으면 숨긴다.</summary>
        public void Show(string key, BridgeLook? look, BridgeWhere? where)
        {
            if (look == null || where == null || !TryPosition(key, where, out var position, out long unit))
            {
                Hide(key);
                return;
            }
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
                var value = LookAttributes.FromValue(binding.Type, raw, out error);
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

        private void Report(string place, string message)
        {
            if (!_problemTexts.Add(place + "\n" + message)) return;
            _problems.Add(new UiProblem(place, message));
        }
    }
}
