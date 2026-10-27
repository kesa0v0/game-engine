using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>올려 둔 패널 하나. <see cref="UiInterpreter.UnmountAsync"/>로 내린다.</summary>
    public sealed class UiPanel
    {
        internal UiPanel(UiNode root)
        {
            Root = root;
        }

        internal UiNode Root { get; }

        /// <summary>렌더러가 만든 맨 위 요소.</summary>
        public ElementId Element => Root.Handle;
    }

    /// <summary>패널을 올린 결과. 문제가 있으면 <see cref="Panel"/>이 null이다.</summary>
    public sealed class UiMountResult
    {
        public UiMountResult(UiPanel? panel, IReadOnlyList<UiProblem> problems)
        {
            Panel = panel;
            Problems = problems;
        }

        public UiPanel? Panel { get; }

        public IReadOnlyList<UiProblem> Problems { get; }
    }

    /// <summary>
    /// UXML 해석기 (spec-gaps G330~G345): 읽은 문서를 한 연결의 세션에 맞춰 올리고, 갱신 때 영수증이 바뀐 연결만 다시 부르고, 눌림을
    /// 커맨드나 화면 동작으로 낸다. 화면은 <see cref="IUiRenderer"/>가 그린다. 세션을 부르는 쪽은 한 스레드에서만 쓴다.
    /// </summary>
    public sealed partial class UiInterpreter
    {
        private readonly IHostSession _session;
        private readonly IUiRenderer _renderer;
        private readonly UiTextFormatter _text;
        private readonly Func<string, string?>? _files;

        private readonly SortedDictionary<string, BridgeCommand> _commands = new SortedDictionary<string, BridgeCommand>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, BridgeCommand> _uiActions = new SortedDictionary<string, BridgeCommand>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, RuntimeBinding> _bindings = new SortedDictionary<string, RuntimeBinding>(StringComparer.Ordinal);
        private readonly List<UiNode> _eventNodes = new List<UiNode>();
        private readonly List<UiProblem> _errors = new List<UiProblem>();
        private readonly SortedSet<string> _errorTexts = new SortedSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, UiDocument?> _includes = new Dictionary<string, UiDocument?>(StringComparer.Ordinal);

        private bool _loaded;
        private bool _refreshing;
        private bool _recheck = true;
        private long _version = -1;
        private int _panels;

        /// <param name="text">값을 글자로 바꾸는 것(번역기를 꽂는다, G345).</param>
        /// <param name="files"><c>Include</c>의 <c>src</c>로 UXML 글을 찾는다. 못 찾으면 null. 없으면 <c>Include</c>를 쓸 수 없다.</param>
        public UiInterpreter(IHostSession session, IUiRenderer renderer, UiTextFormatter text, Func<string, string?>? files = null)
        {
            _session = session;
            _renderer = renderer;
            _text = text;
            _files = files;
        }

        /// <summary>바인딩을 계산하다 난 오류. 같은 오류는 한 번만 담는다. 그 바인딩은 직전 값을 유지한다 (G341).</summary>
        public IReadOnlyList<UiProblem> Errors => _errors;

        /// <summary>눌러서 낸 커맨드나 화면 동작의 결과(이름, 결과). 거절 이유를 보이는 것은 호스트 몫이다.</summary>
        public event Action<string, BridgeResult>? ActionResult;

        /// <summary>
        /// 문서를 올린다. 문서가 부르는 함수와 커맨드·화면 동작 이름을 먼저 모두 확인하고, 문제가 하나라도 있으면 아무것도 만들지 않는다.
        /// <paramref name="arguments"/>는 문서가 <c>params</c>로 선언한 이름의 값이다(<see cref="UiDocument.SeatParameter"/>에는 호스트가 앉은 엔티티를 넣는다).
        /// </summary>
        public async ValueTask<UiMountResult> MountAsync(UiDocument document, IReadOnlyDictionary<string, BridgeValue> arguments)
        {
            await EnsureLoadedAsync();
            var problems = new List<UiProblem>();
            CheckArguments(document, arguments, document.Root.Place, problems);
            await ValidateAsync(document, problems, new List<string> { document.Name });
            if (problems.Count > 0) return new UiMountResult(null, problems);

            var scope = new UiScope(null);
            foreach (var name in document.Parameters) scope.Set(name, arguments[name]);
            string panelId = "p" + (++_panels);
            var root = await BuildAsync(document.Root, null, null, 0, scope, panelId);
            await RecheckAsync();
            return new UiMountResult(new UiPanel(root), problems);
        }

        /// <summary>패널을 내린다: 요소를 없애고 영수증을 놓는다.</summary>
        public async ValueTask UnmountAsync(UiPanel panel)
        {
            var handle = panel.Root.Handle;
            await DestroyAsync(panel.Root);
            _renderer.Remove(handle);
        }

        /// <summary>
        /// 세계가 바뀐 뒤 화면을 맞춘다: 영수증이 바뀌었거나 실패했던 연결만 다시 부르고, 세계 도장이 움직였거나 낸 것이 있으면 버튼을
        /// 다시 확인한다 (G342·G345).
        /// </summary>
        public async ValueTask RefreshAsync()
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                foreach (var key in await _session.Changed())
                {
                    if (!_bindings.TryGetValue(key, out var binding) || !binding.Owner.Alive) continue;
                    await ApplyBindingAsync(binding);
                    if (binding.Kind == RuntimeBindingKind.IncludeArgument && binding.Owner.Inner != null)
                        await ReevaluateAsync(binding.Owner.Inner);
                }
                long version = await _session.Version();
                if (version != _version || _recheck)
                {
                    _version = version;
                    await RecheckAsync();
                }
            }
            finally
            {
                _refreshing = false;
            }
        }

        // ---- 올리기 전에 확인 ----

        private async ValueTask EnsureLoadedAsync()
        {
            if (_loaded) return;
            foreach (var c in await _session.Commands()) _commands[c.Name] = c;
            foreach (var a in await _session.UiActions()) _uiActions[a.Name] = a;
            _loaded = true;
        }

        private static void CheckArguments(UiDocument document, IReadOnlyDictionary<string, BridgeValue> arguments, string place, List<UiProblem> problems)
        {
            foreach (var name in document.Parameters)
                if (!arguments.ContainsKey(name))
                    problems.Add(new UiProblem(place, document.Name + " needs the value '" + name + "' (params)"));
            foreach (var pair in arguments)
            {
                if (Array.IndexOf(ToArray(document.Parameters), pair.Key) < 0)
                    problems.Add(new UiProblem(place, document.Name + " does not take '" + pair.Key + "'" + UiSuggest.Hint(pair.Key, document.Parameters)));
            }
        }

        private static string[] ToArray(IReadOnlyList<string> list)
        {
            var array = new string[list.Count];
            for (int i = 0; i < array.Length; i++) array[i] = list[i];
            return array;
        }

        private async ValueTask ValidateAsync(UiDocument document, List<UiProblem> problems, List<string> stack)
        {
            await ValidateElementAsync(document.Root, problems, stack);
        }

        private readonly SortedDictionary<string, string?> _callChecks = new SortedDictionary<string, string?>(StringComparer.Ordinal);

        private async ValueTask CheckCallAsync(string place, string attribute, UiExpr expression, List<UiProblem> problems)
        {
            if (!(expression is CallExpr call)) return;
            string key = call.Function + "/" + call.Arguments.Count;
            if (!_callChecks.TryGetValue(key, out var why))
            {
                why = await _session.CheckCall(call.Function, call.Arguments.Count);
                _callChecks[key] = why;
            }
            if (why != null) problems.Add(new UiProblem(place, attribute + ": " + why));
        }

        private async ValueTask ValidateElementAsync(UiElement element, List<UiProblem> problems, List<string> stack)
        {
            foreach (var b in element.Bindings)
                await CheckCallAsync(element.Place, b.Kind == UiBindKind.Property ? "bind-" + b.Name : "bind-class", b.Expression, problems);
            if (element.Repeat != null) await CheckCallAsync(element.Place, "for", element.Repeat.Source, problems);
            foreach (var e in element.Events) ValidateEvent(element.Place, e, problems);

            if (element.Include != null) await ValidateIncludeAsync(element, problems, stack);
            foreach (var child in element.Children) await ValidateElementAsync(child, problems, stack);
        }

        private void ValidateEvent(string place, UiEvent e, List<UiProblem> problems)
        {
            string name = e.Call.Function;
            if (!_commands.TryGetValue(name, out var target) && !_uiActions.TryGetValue(name, out target))
            {
                var known = new List<string>();
                foreach (var k in _commands.Keys) known.Add(k);
                foreach (var k in _uiActions.Keys) known.Add(k);
                known.Sort(StringComparer.Ordinal);
                problems.Add(new UiProblem(place, "on-" + e.Name + ": '" + name + "' is neither a command nor a UI action" + UiSuggest.Hint(name, known)));
                return;
            }
            if (e.Call.Arguments.Count > target.Parameters.Count)
                problems.Add(new UiProblem(place, "on-" + e.Name + ": '" + name + "' takes " + target.Parameters.Count + " argument(s), got " + e.Call.Arguments.Count));
        }

        private async ValueTask ValidateIncludeAsync(UiElement element, List<UiProblem> problems, List<string> stack)
        {
            var include = element.Include!;
            var included = LoadInclude(include.Source, element.Place, problems);
            if (included == null) return;
            if (stack.Contains(include.Source))
            {
                problems.Add(new UiProblem(element.Place, "Include '" + include.Source + "' includes itself (" + string.Join(" -> ", stack) + ")"));
                return;
            }
            var given = new List<string>();
            foreach (var arg in include.Arguments)
            {
                given.Add(arg.Key);
                await CheckCallAsync(element.Place, arg.Key, arg.Value, problems);
                if (Array.IndexOf(ToArray(included.Parameters), arg.Key) < 0)
                    problems.Add(new UiProblem(element.Place, include.Source + " does not take '" + arg.Key + "'" + UiSuggest.Hint(arg.Key, included.Parameters)));
            }
            foreach (var name in included.Parameters)
                if (!given.Contains(name)) problems.Add(new UiProblem(element.Place, "Include '" + include.Source + "' needs the value '" + name + "'"));

            stack.Add(include.Source);
            await ValidateElementAsync(included.Root, problems, stack);
            stack.RemoveAt(stack.Count - 1);
        }

        private UiDocument? LoadInclude(string source, string place, List<UiProblem>? problems)
        {
            if (_includes.TryGetValue(source, out var cached))
            {
                if (cached == null) problems?.Add(new UiProblem(place, "Include '" + source + "' could not be read"));
                return cached;
            }
            string? text = _files?.Invoke(source);
            if (text == null)
            {
                problems?.Add(new UiProblem(place, "Include '" + source + "' was not found" + (_files == null ? " (no way to find files was given)" : "")));
                return null;
            }
            var read = UxmlReader.Read(source, text);
            if (!read.Succeeded)
            {
                problems?.AddRange(read.Problems);
                _includes[source] = null;
                return null;
            }
            _includes[source] = read.Document;
            return read.Document;
        }

        private void Report(string place, string message)
        {
            string text = place + ": " + message;
            if (_errorTexts.Add(text)) _errors.Add(new UiProblem(place, message));
        }
    }
}
