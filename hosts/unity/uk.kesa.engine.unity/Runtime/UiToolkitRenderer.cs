#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Kesa.Engine.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kesa.Engine.Unity
{
    /// <summary>
    /// 해석기가 시키는 대로 UI Toolkit의 <c>VisualElement</c>를 만들고 고친다 (spec-gaps G343·G345). 판단과 타입 변환은 이미 해석기가
    /// 했으므로 여기는 요소 종류와 속성 이름을 Unity의 것으로 옮길 뿐이다.
    /// </summary>
    public sealed class UiToolkitRenderer : IUiRenderer
    {
        private readonly VisualElement _host;
        private readonly IUiAssets _assets;
        private readonly Dictionary<long, VisualElement> _elements = new Dictionary<long, VisualElement>();
        private readonly Dictionary<VisualElement, long> _ids = new Dictionary<VisualElement, long>();
        private long _next;

        /// <param name="host">맨 위 요소를 넣을 곳(보통 <c>UIDocument.rootVisualElement</c>).</param>
        public UiToolkitRenderer(VisualElement host, IUiAssets assets)
        {
            _host = host;
            _assets = assets;
        }

        /// <summary>해석기가 준 핸들의 실제 요소.</summary>
        public VisualElement Element(ElementId id) => _elements[id.Value];

        public ElementId Create(string kind, ElementId? parent, int index, IReadOnlyList<KeyValuePair<string, string>> attributes)
        {
            var element = Make(kind);
            foreach (var attribute in attributes) Apply(element, attribute.Key, attribute.Value);
            long id = ++_next;
            _elements[id] = element;
            _ids[element] = id;
            if (parent == null)
            {
                _host.Add(element);
            }
            else
            {
                var container = _elements[parent.Value.Value];
                container.Insert(Math.Min(index, container.childCount), element);
            }
            return new ElementId(id);
        }

        public void AddStyleSheet(ElementId element, string path)
        {
            var sheet = _assets.LoadStyleSheet(path);
            if (sheet == null) Debug.LogWarning("Kesa UI: the style sheet '" + path + "' could not be found");
            else _elements[element.Value].styleSheets.Add(sheet);
        }

        public void SetText(ElementId element, string property, string value)
        {
            var target = _elements[element.Value];
            switch (target)
            {
                case TextElement text: text.text = value; break;
                case ProgressBar bar: bar.title = value; break;
                case TextField field: field.value = value; break;
                default: Debug.LogWarning("Kesa UI: " + target.GetType().Name + " has no '" + property + "' to set"); break;
            }
        }

        public void SetFlag(ElementId element, string property, bool value)
        {
            var target = _elements[element.Value];
            if (property == "display") target.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            else if (property == "enabled") target.SetEnabled(value);
            else Debug.LogWarning("Kesa UI: '" + property + "' is not a true/false property");
        }

        public void SetNumber(ElementId element, string property, long raw)
        {
            // 고정소수점의 원래 정수값을 화면에 쓸 때만 실수로 바꾼다 (G326)
            float number = raw / (float)Kesa.Engine.Bridge.BridgeFixed.Scale;
            var target = _elements[element.Value];
            if (property == "opacity")
            {
                target.style.opacity = number;
            }
            else if (property == "value")
            {
                switch (target)
                {
                    case ProgressBar bar: bar.value = number; break;
                    case Slider slider: slider.value = number; break;
                    default: Debug.LogWarning("Kesa UI: " + target.GetType().Name + " has no number 'value'"); break;
                }
            }
            else
            {
                Debug.LogWarning("Kesa UI: '" + property + "' is not a number property");
            }
        }

        public void SetImage(ElementId element, string path)
        {
            var texture = _assets.LoadImage(path);
            if (texture == null)
            {
                Debug.LogWarning("Kesa UI: the image '" + path + "' could not be found");
                return;
            }
            var target = _elements[element.Value];
            if (target is Image image) image.image = texture;
            else target.style.backgroundImage = new StyleBackground(texture);
        }

        public void SetClass(ElementId element, string className, bool on)
        {
            var target = _elements[element.Value];
            if (on) target.AddToClassList(className);
            else target.RemoveFromClassList(className);
        }

        public void Move(ElementId element, ElementId parent, int index)
        {
            var container = _elements[parent.Value];
            var target = _elements[element.Value];
            target.RemoveFromHierarchy();
            container.Insert(Math.Min(index, container.childCount), target);
        }

        public void Remove(ElementId element)
        {
            var target = _elements[element.Value];
            target.RemoveFromHierarchy();
            Forget(target);
        }

        public void OnClick(ElementId element, Func<ValueTask> handler)
        {
            var target = _elements[element.Value];
            if (target is Button button) button.clicked += () => { _ = Run(handler); };
            else target.RegisterCallback<ClickEvent>(evt => { _ = Run(handler); });
        }

        private static async Task Run(Func<ValueTask> handler)
        {
            try
            {
                await handler();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void Forget(VisualElement element)
        {
            if (_ids.TryGetValue(element, out long id))
            {
                _elements.Remove(id);
                _ids.Remove(element);
            }
            foreach (var child in element.Children()) Forget(child);
        }

        private static VisualElement Make(string kind)
        {
            switch (kind)
            {
                case "Label": return new Label();
                case "Button": return new Button();
                case "Image": return new Image();
                case "ProgressBar": return new ProgressBar();
                case "ScrollView": return new ScrollView();
                case "TextField": return new TextField();
                case "Toggle": return new Toggle();
                case "Slider": return new Slider();
                case "VisualElement": return new VisualElement();
                default:
                    Debug.LogWarning("Kesa UI: the element kind '" + kind + "' is not known, using a VisualElement");
                    return new VisualElement();
            }
        }

        private static void Apply(VisualElement element, string name, string value)
        {
            switch (name)
            {
                case "name": element.name = value; break;
                case "class":
                    foreach (var c in value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) element.AddToClassList(c);
                    break;
                case "text":
                    if (element is TextElement text) text.text = value;
                    else if (element is ProgressBar bar) bar.title = value;
                    break;
                case "tooltip": element.tooltip = value; break;
                case "low-value":
                    if (element is ProgressBar low && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float lowValue)) low.lowValue = lowValue;
                    break;
                case "high-value":
                    if (element is ProgressBar high && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float highValue)) high.highValue = highValue;
                    break;
                default:
                    Debug.LogWarning("Kesa UI: the attribute '" + name + "' is not supported yet (element " + element.GetType().Name + ")");
                    break;
            }
        }
    }
}
