using System;
using System.Collections.Generic;
using System.Linq;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 그림 요소 종류와 공간 종류를 등록하는 자리 (G390·G394·G413). <see cref="Standard"/>가 기본 종류(<c>Sprite</c>·<c>Text</c>·<c>Model</c>·<c>Group</c>·<c>grid</c>)를
    /// 넣은 것을 주고 게임은 여기에 제 종류를 더한다. 해석기는 기본 종류를 따로 알지 않는다.
    /// </summary>
    public sealed class LookKinds
    {
        private readonly SortedDictionary<string, LookElementKind> _elements = new SortedDictionary<string, LookElementKind>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, SpaceKind> _spaces = new SortedDictionary<string, SpaceKind>(StringComparer.Ordinal);
        private readonly SortedSet<string> _modelFormats = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>모델 로더가 맡는 확장자(소문자, 점 포함) (G391). 호스트가 로더를 등록하며 더한다.</summary>
        public IEnumerable<string> ModelFormats => _modelFormats;

        /// <summary><c>Model</c>의 <c>src</c>가 이 확장자면 로드 때 받는다.</summary>
        public void AddModelFormat(string extension)
        {
            if (!extension.StartsWith(".", StringComparison.Ordinal) || extension.Length < 2)
                throw new ArgumentException("a model format is an extension like '.glb', got '" + extension + "'");
            _modelFormats.Add(extension.ToLowerInvariant());
        }

        /// <summary>그 경로를 맡을 모델 로더가 없으면 이유.</summary>
        public string? CheckModel(string path)
        {
            int dot = path.LastIndexOf('.');
            string extension = dot < 0 || dot < path.LastIndexOf('/') ? "" : path.Substring(dot).ToLowerInvariant();
            if (_modelFormats.Contains(extension)) return null;
            return "no model loader takes " + (extension.Length == 0 ? "a file without an extension" : "'" + extension + "'") +
                   (_modelFormats.Count == 0 ? " (this host registers none)" : " (can be: " + string.Join(", ", _modelFormats) + ")") + " (spec-gaps G391)";
        }

        /// <summary>모든 요소가 받는 속성 (G413). 묶음(<c>Group</c>) 안에서는 부모 기준이다.</summary>
        public static readonly IReadOnlyDictionary<string, LookAttributeType> Common = new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
        {
            ["layer"] = LookAttributeType.Integer,
            ["offset"] = LookAttributeType.Vector,
        };

        public IEnumerable<string> ElementNames => _elements.Keys;

        public IEnumerable<string> SpaceNames => _spaces.Keys;

        /// <summary>기본 종류를 넣은 등록 자리.</summary>
        public static LookKinds Standard()
        {
            var kinds = new LookKinds();
            kinds.Add(new LookElementKind("Sprite", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["src"] = LookAttributeType.Path,
                ["size"] = LookAttributeType.Vector2,
                ["tint"] = LookAttributeType.Color,
            }, acceptsChildren: false));
            kinds.Add(new LookElementKind("Text", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["text"] = LookAttributeType.Text,
                ["size"] = LookAttributeType.Number,
                ["color"] = LookAttributeType.Color,
            }, acceptsChildren: false));
            kinds.Add(new LookElementKind("Model", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["src"] = LookAttributeType.Path,
                ["state"] = LookAttributeType.Name,
                ["fade"] = LookAttributeType.Number,
            }, acceptsChildren: true, check: (name, value) => name == "src" ? kinds.CheckModel(value.Text!) : NotNegative(name, value),
                validate: ValidateModel));
            kinds.Add(new LookElementKind("State", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["name"] = LookAttributeType.Name,
                ["clip"] = LookAttributeType.Name,
                ["once"] = LookAttributeType.Flag,
                ["next"] = LookAttributeType.Name,
                ["fade"] = LookAttributeType.Number,
                ["blend"] = LookAttributeType.Number,
            }, acceptsChildren: true, check: NotNegative, parents: new[] { "Model" }, validate: ValidateState));
            kinds.Add(new LookElementKind("Clip", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["clip"] = LookAttributeType.Name,
                ["at"] = LookAttributeType.Number,
            }, acceptsChildren: false, parents: new[] { "State" }, validate: ValidateClip));
            kinds.Add(new LookElementKind("Group", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal), acceptsChildren: true));
            kinds.Add(new GridSpace());
            return kinds;
        }

        // ---- 모델 애니메이션 (G434) ----

        private static string? NotNegative(string name, LookAttributeValue value) =>
            name == "fade" && value.Numbers[0] < 0 ? "a time is not negative" : null;

        private static string? Written(LookElement element, string name)
        {
            foreach (var pair in element.Attributes)
                if (pair.Key == name) return pair.Value.Text;
            return null;
        }

        private static bool Has(LookElement element, string name) =>
            element.Attributes.Any(a => a.Key == name) || element.Bindings.Any(b => b.Attribute == name);

        private static IEnumerable<string> ValidateModel(LookElement model)
        {
            var states = model.Children.Where(c => c.Kind == "State").ToList();
            var names = new List<string>();
            foreach (var state in states)
            {
                string? name = Written(state, "name");
                if (name == null) continue;
                if (names.Contains(name)) yield return "two <State>s are named '" + name + "'";
                else names.Add(name);
            }
            foreach (var state in states)
            {
                string? next = Written(state, "next");
                if (next != null && !names.Contains(next))
                    yield return "next=\"" + next + "\": no <State> in this <Model> is named '" + next + "'" + UiSuggest.Hint(next, names);
            }
            string? start = Written(model, "state");
            if (start != null && !names.Contains(start))
                yield return "state=\"" + start + "\": no <State> in this <Model> is named '" + start + "'" + UiSuggest.Hint(start, names);
            if (states.Count == 0 && Has(model, "state")) yield return "state= picks one of the <State>s inside, but there are none";
        }

        private static IEnumerable<string> ValidateState(LookElement state)
        {
            if (Written(state, "name") == null) yield return "a <State> needs name=\"…\" written (spec-gaps G434)";
            bool clips = state.Children.Any(c => c.Kind == "Clip");
            if (Has(state, "clip") && clips) yield return "a <State> plays clip= or blends the <Clip>s inside, not both";
            if (!Has(state, "clip") && !clips) yield return "a <State> needs clip=\"…\" or <Clip>s inside to blend";
            if (Has(state, "blend") && !clips) yield return "blend= mixes the <Clip>s inside, but there are none";
            if (Has(state, "next") && !Has(state, "once")) yield return "next= is where a once=\"true\" state goes when it ends";
        }

        private static IEnumerable<string> ValidateClip(LookElement clip)
        {
            if (!Has(clip, "clip")) yield return "a <Clip> needs clip=\"…\"";
            if (!Has(clip, "at")) yield return "a <Clip> needs at=\"…\", its place on the blend";
        }

        /// <summary>같은 이름이 있거나 공통 속성 이름을 쓰면 던진다(게임 코드의 실수).</summary>
        public void Add(LookElementKind kind)
        {
            if (_elements.ContainsKey(kind.Name)) throw new ArgumentException("the look element '" + kind.Name + "' is already registered");
            if (kind.Name == "Look" || kind.Name == "Looks" || kind.Name == "Include")
                throw new ArgumentException("'" + kind.Name + "' is reserved in look files");
            foreach (var attribute in kind.Attributes.Keys)
                if (Common.ContainsKey(attribute) || attribute == "for" || attribute.StartsWith("bind-", StringComparison.Ordinal))
                    throw new ArgumentException("the look element '" + kind.Name + "' cannot declare '" + attribute + "' (the interpreter handles it)");
            _elements.Add(kind.Name, kind);
        }

        public void Add(SpaceKind kind)
        {
            if (_spaces.ContainsKey(kind.Name)) throw new ArgumentException("the space kind '" + kind.Name + "' is already registered");
            _spaces.Add(kind.Name, kind);
        }

        public bool TryGetElement(string name, out LookElementKind kind) => _elements.TryGetValue(name, out kind!);

        public bool TryGetSpace(string name, out SpaceKind kind) => _spaces.TryGetValue(name, out kind!);
    }
}
