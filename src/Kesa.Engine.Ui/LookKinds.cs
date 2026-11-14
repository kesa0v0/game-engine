using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 그림 요소 종류, 공간 종류, 카메라 리그를 등록하는 자리 (G390·G394·G413·G442). <see cref="Standard"/>가 기본 종류(<c>Sprite</c>·<c>Text</c>·<c>Model</c>·<c>Group</c>·<c>grid</c>·<c>top</c>·<c>tilted</c>)를
    /// 넣은 것을 주고 게임은 여기에 제 종류를 더한다. 입력의 모양 종류(<c>path</c>·<c>lasso</c>, G482)도 여기다. 해석기는 기본 종류를 따로 알지 않는다.
    /// </summary>
    public sealed class LookKinds
    {
        private readonly SortedDictionary<string, LookElementKind> _elements = new SortedDictionary<string, LookElementKind>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, SpaceKind> _spaces = new SortedDictionary<string, SpaceKind>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, ViewRig> _rigs = new SortedDictionary<string, ViewRig>(StringComparer.Ordinal);
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

        /// <summary>
        /// 게임 코드 없이 모드 폴더만 보는 도구용 (G480). 등록되지 않은 요소·공간·리그·모델 확장자를 틀렸다고 하지 않고
        /// <see cref="UiProblem.Unverified"/>로 알린다. 게임이 등록했을 수 있어서다.
        /// </summary>
        public bool AllowUnknown { get; set; }

        /// <summary>그 경로를 맡을 모델 로더가 없으면 이유.</summary>
        public string? CheckModel(string path) => AllowUnknown ? null : ModelProblem(path);

        internal string? ModelProblem(string path)
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
                ["turn"] = LookAttributeType.Number,
                ["face"] = LookAttributeType.Name,
            }, acceptsChildren: false, check: CheckFace));
            kinds.Add(new LookElementKind("Text", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["text"] = LookAttributeType.Text,
                ["size"] = LookAttributeType.Number,
                ["color"] = LookAttributeType.Color,
                ["turn"] = LookAttributeType.Number,
                ["face"] = LookAttributeType.Name,
            }, acceptsChildren: false, check: CheckFace));
            kinds.Add(new LookElementKind("Model", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["src"] = LookAttributeType.Path,
                ["state"] = LookAttributeType.Name,
                ["fade"] = LookAttributeType.Number,
                ["tint"] = LookAttributeType.Color,
                ["scale"] = LookAttributeType.Number,
                ["turn"] = LookAttributeType.Number,
            }, acceptsChildren: true, check: (name, value) => name == "src" ? kinds.CheckModel(value.Text!)
                : name == "scale" ? (value.Numbers[0] > 0 ? null : "a scale is more than 0") : NotNegative(name, value),
                validate: model => ValidateModel(model, kinds)));
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
            kinds.Add(new LookElementKind("Attach", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["bone"] = LookAttributeType.Name,
            }, acceptsChildren: true, parents: new[] { "Model" }, validate: ValidateAttach));
            kinds.Add(new LookElementKind("Paint", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["material"] = LookAttributeType.Name,
                ["color"] = LookAttributeType.Color,
            }, acceptsChildren: false, parents: new[] { "Model" }, validate: ValidatePaint));
            kinds.Add(new LookElementKind("Group", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["turn"] = LookAttributeType.Number,
            }, acceptsChildren: true));
            kinds.Add(new GridSpace());
            kinds.Add(new PathGesture());
            kinds.Add(new LassoGesture());
            kinds.Add(new ViewRig("top", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["size"] = LookAttributeType.Number,
                ["zoom"] = LookAttributeType.Vector2,
            }, new SortedDictionary<string, string>(StringComparer.Ordinal) { ["size"] = "4" }, CheckRig));
            kinds.Add(new ViewRig("tilted", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["tilt"] = LookAttributeType.Number,
                ["turn"] = LookAttributeType.Number,
                ["distance"] = LookAttributeType.Number,
                ["zoom"] = LookAttributeType.Vector2,
                ["fov"] = LookAttributeType.Number,
            }, new SortedDictionary<string, string>(StringComparer.Ordinal) { ["tilt"] = "50", ["distance"] = "10" }, CheckRig));
            return kinds;
        }

        // ---- 카메라 리그 (G442·G446) ----

        private static string? CheckRig(string name, LookAttributeValue value)
        {
            long n = value.Numbers[0];
            switch (name)
            {
                case "size":
                case "distance":
                    return n > 0 ? null : "a " + name + " is more than 0";
                case "tilt":
                    return n >= 0 && n <= 90 * BridgeFixed.Scale ? null : "a tilt is from 0 (straight down) to 90 (level)";
                case "fov":
                    return n > 0 && n < 180 * BridgeFixed.Scale ? null : "a fov is more than 0 and less than 180 degrees";
                case "zoom":
                    return n > 0 && value.Numbers[1] >= n ? null : "a zoom is \"least,most\", both more than 0 and least first";
                default:
                    return null;
            }
        }

        private static readonly string[] Faces = { "floor", "camera" };

        // 평면 그림은 틀의 바닥에 눕거나 카메라를 본다 (G444)
        private static string? CheckFace(string name, LookAttributeValue value) =>
            name == "face" && Array.IndexOf(Faces, value.Text) < 0
                ? "a face is floor or camera" + UiSuggest.Hint(value.Text ?? "", Faces) : null;

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

        private static IEnumerable<string> ValidateModel(LookElement model, LookKinds kinds)
        {
            // 제 부모가 정해진 종류(Clip)는 읽을 때 이미 알렸다
            foreach (var child in model.Children)
                if (kinds.TryGetElement(child.Kind, out var kind) ? kind.Parents == null : !kinds.AllowUnknown)
                    yield return "inside a <Model> go only <State>s, <Attach>es and <Paint>s; put <" + child.Kind + "> in an <Attach bone=\"…\"> or beside the <Model>";
            var painted = new List<string>();
            foreach (var paint in model.Children.Where(c => c.Kind == "Paint"))
            {
                string? material = Written(paint, "material");
                if (material == null) continue;
                if (painted.Contains(material)) yield return "two <Paint>s color the material '" + material + "' (spec-gaps G436)";
                else painted.Add(material);
            }
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

        private static IEnumerable<string> ValidateAttach(LookElement attach)
        {
            if (!Has(attach, "bone")) yield return "an <Attach> needs bone=\"…\", the name of the part it follows (spec-gaps G435)";
        }

        private static IEnumerable<string> ValidatePaint(LookElement paint)
        {
            if (!Has(paint, "material")) yield return "a <Paint> needs material=\"…\", the name of the model's material it colors";
            if (!Has(paint, "color")) yield return "a <Paint> needs color=\"…\"";
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

        /// <summary>이 리그는 모든 View가 받는 속성(<see cref="ViewCommon"/>)을 선언할 수 없다.</summary>
        public void Add(ViewRig rig)
        {
            if (_rigs.ContainsKey(rig.Name)) throw new ArgumentException("the camera rig '" + rig.Name + "' is already registered");
            foreach (var attribute in rig.Attributes.Keys)
                if (Array.IndexOf(ViewCommon, attribute) >= 0)
                    throw new ArgumentException("the camera rig '" + rig.Name + "' cannot declare '" + attribute + "' (every view takes it)");
            foreach (var attribute in rig.Required.Keys)
                if (!rig.Attributes.ContainsKey(attribute))
                    throw new ArgumentException("the camera rig '" + rig.Name + "' requires '" + attribute + "' but does not take it");
            _rigs.Add(rig.Name, rig);
        }

        /// <summary>모든 View가 받는 속성 (G441·G445).</summary>
        public static readonly string[] ViewCommon = { "name", "rig", "space", "look-at", "offset" };

        public IEnumerable<string> RigNames => _rigs.Keys;

        public bool TryGetRig(string name, out ViewRig rig) => _rigs.TryGetValue(name, out rig!);

        public bool TryGetElement(string name, out LookElementKind kind) => _elements.TryGetValue(name, out kind!);

        public bool TryGetSpace(string name, out SpaceKind kind) => _spaces.TryGetValue(name, out kind!);

        private readonly SortedDictionary<string, GestureKind> _gestures = new SortedDictionary<string, GestureKind>(StringComparer.Ordinal);

        /// <summary>입력의 모양 종류 (G482·G489, 기본 <c>path</c>·<c>lasso</c>).</summary>
        public void Add(GestureKind kind)
        {
            if (_gestures.ContainsKey(kind.Name)) throw new ArgumentException("the input kind '" + kind.Name + "' is already registered");
            _gestures.Add(kind.Name, kind);
        }

        public IEnumerable<string> GestureNames => _gestures.Keys;

        public bool TryGetGesture(string name, out GestureKind kind) => _gestures.TryGetValue(name, out kind!);
    }
}
