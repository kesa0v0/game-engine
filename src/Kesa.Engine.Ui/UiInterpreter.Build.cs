using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    public sealed partial class UiInterpreter
    {
        // ---- 올리기 ----

        private async ValueTask<UiNode> BuildAsync(UiElement definition, UiNode? parent, ElementId? parentId, int index, UiScope scope, string path)
        {
            if (definition.Include != null) return await BuildIncludeAsync(definition, parent, parentId, index, scope, path);

            var id = _renderer.Create(definition.Kind, parentId, index, definition.Attributes);
            var node = new UiNode(definition, id, parent, scope, path);

            foreach (var b in definition.Bindings)
            {
                var kind = b.Kind == UiBindKind.Property ? RuntimeBindingKind.Property
                    : b.Kind == UiBindKind.ClassToggle ? RuntimeBindingKind.ClassToggle
                    : RuntimeBindingKind.ClassNames;
                string attribute = b.Kind == UiBindKind.Property ? "bind-" + b.Name
                    : b.Kind == UiBindKind.ClassToggle ? "bind-class-" + b.Name
                    : "bind-class";
                await ApplyBindingAsync(Register(node, scope, kind, attribute, b.Name, b.Expression, attribute + "=\"" + b.Source + "\""));
            }

            bool clickable = false;
            UiEvent? close = null;
            foreach (var e in definition.Events)
            {
                var captured = e;
                if (e.Name == "close")
                {
                    close = e;
                    continue;
                }
                _renderer.OnClick(id, () => EventAsync(node, captured));
                clickable = true;
            }
            if (clickable) _eventNodes.Add(node);
            if (definition.Window != null)
            {
                var closeEvent = close;
                Windows.Add(_renderer, id, definition.Window, closeEvent == null ? (Func<ValueTask>?)null : () => EventAsync(node, closeEvent));
                _windowNodes.Add(node);
            }
            if (definition.DragHandle) _renderer.MakeDragHandle(id);

            if (definition.Repeat != null)
            {
                node.Groups = new List<ItemGroup>();
                var source = Register(node, scope, RuntimeBindingKind.RepeatSource, "for", "", definition.Repeat.Source, "for=\"" + definition.Repeat.Text + "\"");
                await ApplyBindingAsync(source);
            }
            else
            {
                for (int i = 0; i < definition.Children.Count; i++)
                    node.Children.Add(await BuildAsync(definition.Children[i], node, id, i, scope, path + "/" + i));
            }
            return node;
        }

        private async ValueTask<UiNode> BuildIncludeAsync(UiElement definition, UiNode? parent, ElementId? parentId, int index, UiScope scope, string path)
        {
            var include = definition.Include!;
            var document = LoadInclude(include.Source, definition.Place, null)!;
            var wrapper = new UiNode(definition, null, parent, scope, path);
            var innerScope = new UiScope(null);
            wrapper.InnerScope = innerScope;
            foreach (var arg in include.Arguments)
                await ApplyBindingAsync(Register(wrapper, scope, RuntimeBindingKind.IncludeArgument, arg.Key, arg.Key, arg.Value,
                    arg.Key + "=\"" + arg.Value + "\""));
            wrapper.Inner = await BuildAsync(document.Root, wrapper, parentId, index, innerScope, path + "/inc");
            foreach (var sheet in document.StyleSheets) _renderer.AddStyleSheet(wrapper.Inner.Handle, sheet);
            return wrapper;
        }

        private RuntimeBinding Register(UiNode owner, UiScope scope, RuntimeBindingKind kind, string attribute, string name, UiExpr expression, string label)
        {
            var binding = new RuntimeBinding(owner.Path + "#" + attribute, kind, owner, scope, name, expression, label);
            owner.Bindings.Add(binding);
            _bindings[binding.Key] = binding;
            return binding;
        }

        // ---- 값 계산과 넣기 ----

        /// <summary>식을 계산해 요소에 넣는다. 실패하면 오류를 담고 직전 값을 그대로 둔다 (G341).</summary>
        private async ValueTask ApplyBindingAsync(RuntimeBinding binding)
        {
            string place = binding.Owner.Definition.Place;
            var value = await EvaluateAsync(binding, place);
            if (value == null) return;

            switch (binding.Kind)
            {
                case RuntimeBindingKind.Property:
                    ApplyProperty(binding, value, place);
                    break;
                case RuntimeBindingKind.ClassToggle:
                    if (UiValues.TryTruthy(value, out bool on, out string whyNot)) _renderer.SetClass(binding.Owner.Handle, binding.Name, on);
                    else Report(place, binding.Label + ": " + whyNot);
                    break;
                case RuntimeBindingKind.ClassNames:
                    ApplyClassNames(binding, value, place);
                    break;
                case RuntimeBindingKind.RepeatSource:
                    if (value is BridgeList list) await ReconcileAsync(binding.Owner, list.Items);
                    else Report(place, binding.Label + ": a list is needed, got a " + value.Kind.ToString().ToLowerInvariant());
                    break;
                case RuntimeBindingKind.IncludeArgument:
                    binding.Owner.InnerScope!.Set(binding.Name, value);
                    break;
            }
        }

        private void ApplyProperty(RuntimeBinding binding, BridgeValue value, string place)
        {
            var handle = binding.Owner.Handle;
            UiProperties.TryGet(binding.Name, out var type);
            switch (type)
            {
                case UiPropertyType.Text:
                    _renderer.SetText(handle, binding.Name, _text(value));
                    break;
                case UiPropertyType.Flag:
                    if (UiValues.TryTruthy(value, out bool flag, out string flagError))
                    {
                        _renderer.SetFlag(handle, binding.Name, flag);
                        if (binding.Name == "display") binding.Owner.Displayed = flag;
                    }
                    else Report(place, binding.Label + ": " + flagError);
                    break;
                case UiPropertyType.Number:
                    if (UiValues.TryNumber(value, out long raw, out string numberError)) _renderer.SetNumber(handle, binding.Name, raw);
                    else Report(place, binding.Label + ": " + numberError);
                    break;
                case UiPropertyType.Image:
                    if (value is BridgeStr path) _renderer.SetImage(handle, path.Value);
                    else Report(place, binding.Label + ": an image is a path (text), got a " + value.Kind.ToString().ToLowerInvariant());
                    break;
            }
        }

        private void ApplyClassNames(RuntimeBinding binding, BridgeValue value, string place)
        {
            var wanted = new SortedSet<string>(StringComparer.Ordinal);
            if (!UiValues.TryClassNames(value, wanted, out string error))
            {
                Report(place, binding.Label + ": " + error);
                return;
            }
            var handle = binding.Owner.Handle;
            var gone = new List<string>();
            foreach (var applied in binding.AppliedClasses)
                if (!wanted.Contains(applied)) gone.Add(applied);
            foreach (var name in gone)
            {
                _renderer.SetClass(handle, name, false);
                binding.AppliedClasses.Remove(name);
            }
            foreach (var name in wanted)
            {
                if (binding.AppliedClasses.Add(name)) _renderer.SetClass(handle, name, true);
            }
        }

        /// <summary>식을 계산한다. 변수는 이름 범위에서, 호출은 세션의 <c>Call</c>(영수증 키 포함)로. 실패하면 오류를 담고 null.</summary>
        private async ValueTask<BridgeValue?> EvaluateAsync(RuntimeBinding binding, string place)
        {
            switch (binding.Expression)
            {
                case VarExpr variable:
                    if (ResolveVariable(variable, binding.Scope, out var resolved, out string error)) return resolved;
                    Report(place, binding.Label + ": " + error);
                    return null;
                case CallExpr call:
                    if (!TryArguments(call, binding.Scope, out var args, out string argError))
                    {
                        Report(place, binding.Label + ": " + argError);
                        return null;
                    }
                    var result = await _session.Call(binding.Key, call.Function, args);
                    if (!result.Succeeded)
                    {
                        Report(place, binding.Label + ": " + result.Error);
                        return null;
                    }
                    return result.Value ?? BridgeNone.Instance;
                default:
                    return null;
            }
        }

        private static bool ResolveVariable(VarExpr variable, UiScope scope, out BridgeValue value, out string error)
        {
            error = "";
            if (!scope.TryGet(variable.Name, out value))
            {
                error = "the variable '" + variable.Name + "' has no value";
                return false;
            }
            string walked = variable.Name;
            foreach (var key in variable.Keys)
            {
                if (!(value is BridgeDict dict))
                {
                    error = "'" + walked + "' is a " + value.Kind.ToString().ToLowerInvariant() + ", not a dict, so '" + key + "' cannot be read";
                    return false;
                }
                if (!dict.TryGet(key, out value))
                {
                    error = "'" + walked + "' has no key '" + key + "'";
                    return false;
                }
                walked += "." + key;
            }
            return true;
        }

        private static bool TryArguments(CallExpr call, UiScope scope, out List<BridgeValue> args, out string error)
        {
            args = new List<BridgeValue>(call.Arguments.Count);
            error = "";
            foreach (var arg in call.Arguments)
            {
                if (arg is LiteralExpr literal)
                {
                    args.Add(literal.Value);
                    continue;
                }
                if (!ResolveVariable((VarExpr)arg, scope, out var value, out error)) return false;
                args.Add(value);
            }
            return true;
        }

        // ---- 반복 (G338·G339) ----

        private async ValueTask ReconcileAsync(UiNode owner, IReadOnlyList<BridgeValue> items)
        {
            var groups = owner.Groups!;
            var repeat = owner.Definition.Repeat!;
            int templateCount = owner.Definition.Children.Count;
            var container = owner.Id!.Value;

            var survivors = new Dictionary<string, ItemGroup>(StringComparer.Ordinal);
            foreach (var g in groups) survivors[g.Key] = g;

            var seen = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var keys = new string[items.Count];
            var kept = new Dictionary<string, ItemGroup>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                keys[i] = UiValues.RepeatKey(items[i], i, seen);
                if (survivors.TryGetValue(keys[i], out var existing)) kept[keys[i]] = existing;
            }

            foreach (var g in groups.ToArray())
            {
                if (kept.ContainsKey(g.Key)) continue;
                groups.Remove(g);
                foreach (var root in g.Roots)
                {
                    var handle = root.Handle;
                    await DestroyAsync(root);
                    _renderer.Remove(handle);
                }
            }

            var changed = new List<ItemGroup>();
            for (int i = 0; i < items.Count; i++)
            {
                if (kept.TryGetValue(keys[i], out var group))
                {
                    int current = groups.IndexOf(group);
                    if (current != i)
                    {
                        groups.RemoveAt(current);
                        groups.Insert(i, group);
                        for (int r = 0; r < group.Roots.Count; r++) _renderer.Move(group.Roots[r].Handle, container, i * templateCount + r);
                    }
                    if (!group.Item.Equals(items[i]))
                    {
                        group.Item = items[i];
                        group.Scope.Set(repeat.Variable, items[i]);
                        changed.Add(group);
                    }
                    continue;
                }

                var created = new ItemGroup(keys[i], items[i], new UiScope(owner.Scope));
                created.Scope.Set(repeat.Variable, items[i]);
                groups.Insert(i, created);
                for (int r = 0; r < templateCount; r++)
                    created.Roots.Add(await BuildAsync(owner.Definition.Children[r], owner, container, i * templateCount + r, created.Scope,
                        owner.Path + "[" + keys[i] + "]/" + r));
            }

            foreach (var group in changed)
                foreach (var root in group.Roots)
                    await ReevaluateAsync(root);
        }

        /// <summary>이름 범위의 값이 바뀐 아래 전체의 바인딩을 다시 계산한다.</summary>
        private async ValueTask ReevaluateAsync(UiNode node)
        {
            if (!node.Alive) return;
            foreach (var binding in node.Bindings.ToArray())
            {
                if (!node.Alive) return;
                await ApplyBindingAsync(binding);
            }
            if (node.Inner != null) await ReevaluateAsync(node.Inner);
            foreach (var child in node.Children.ToArray()) await ReevaluateAsync(child);
            if (node.Groups != null)
                foreach (var group in node.Groups.ToArray())
                    foreach (var root in group.Roots.ToArray())
                        await ReevaluateAsync(root);
            _recheck = true;
        }

        /// <summary>노드와 그 아래를 없앤 것으로 표시하고 영수증을 놓는다. 요소를 렌더러에서 지우는 것은 부르는 쪽이 한다.</summary>
        private async ValueTask DestroyAsync(UiNode node)
        {
            node.Alive = false;
            foreach (var binding in node.Bindings)
            {
                _bindings.Remove(binding.Key);
                await _session.Unbind(binding.Key);
            }
            if (node.Inner != null) await DestroyAsync(node.Inner);
            foreach (var child in node.Children) await DestroyAsync(child);
            if (node.Groups != null)
                foreach (var group in node.Groups)
                    foreach (var root in group.Roots)
                        await DestroyAsync(root);
            _eventNodes.Remove(node);
            if (node.Definition.Window != null && _windowNodes.Remove(node)) Windows.Remove(_renderer, node.Id!.Value);
        }

        /// <summary>창 노드마다 보이는지(자기와 모든 윗 노드의 <c>display</c>)를 창 목록에 넣고 바뀐 것을 그리게 한다 (G354).</summary>
        private void SyncWindows()
        {
            foreach (var node in _windowNodes)
            {
                bool visible = true;
                for (var n = node; n != null && visible; n = n.Parent) visible = n.Displayed;
                Windows.SetVisible(_renderer, node.Id!.Value, visible);
            }
            Windows.Flush();
        }

        // ---- 눌림과 버튼 확인 (G336·G342) ----

        private bool TryEventCall(UiNode node, UiEvent e, out string name, out SortedDictionary<string, BridgeValue> arguments)
        {
            arguments = new SortedDictionary<string, BridgeValue>(StringComparer.Ordinal);
            name = e.Call.Function;
            if (!TryArguments(e.Call, node.Scope, out var values, out string error))
            {
                Report(node.Definition.Place, "on-" + e.Name + "=\"" + e.Source + "\": " + error);
                return false;
            }
            var target = _uiActions.TryGetValue(name, out var action) ? action : _commands[name];
            for (int i = 0; i < values.Count; i++) arguments[target.Parameters[i].Name] = values[i];
            return true;
        }

        private async ValueTask EventAsync(UiNode node, UiEvent e)
        {
            if (!node.Alive) return;
            if (!TryEventCall(node, e, out string name, out var arguments)) return;
            var result = _uiActions.ContainsKey(name) ? await _session.Act(name, arguments) : await _session.Deliver(name, arguments);
            ActionResult?.Invoke(name, result);
            _recheck = true;
            await RefreshAsync();
        }

        private async ValueTask RecheckAsync()
        {
            _recheck = false;
            foreach (var node in _eventNodes.ToArray())
            {
                if (!node.Alive) continue;
                foreach (var e in node.Definition.Events)
                {
                    // 닫힘 요청은 누르는 것이 아니라 창을 회색으로 만들지 않는다
                    if (e.Name != "click") continue;
                    if (!TryEventCall(node, e, out string name, out var arguments))
                    {
                        _renderer.SetFlag(node.Handle, "enabled", false);
                        continue;
                    }
                    var check = await _session.Check(name, arguments);
                    _renderer.SetFlag(node.Handle, "enabled", check.Succeeded);
                }
            }
        }
    }
}
