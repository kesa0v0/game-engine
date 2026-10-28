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
        /// <summary>모달 막이 요소의 USS 클래스. 모양(어둡게 하기 등)은 모드의 USS가 정한다.</summary>
        public const string BlockerClass = "modal-blocker";

        private readonly VisualElement _host;
        private readonly IUiAssets _assets;
        private readonly Dictionary<long, VisualElement> _elements = new Dictionary<long, VisualElement>();
        private readonly Dictionary<VisualElement, long> _ids = new Dictionary<VisualElement, long>();
        private long _next;

        // UI Toolkit은 트리 순서로 그리므로 창은 겹침 층으로 옮기고 원래 자리에는 자리표를 남겨 부모 안의 순서를 지킨다 (G354)
        private readonly VisualElement _windowLayer;
        private readonly VisualElement _blocker;
        private readonly Dictionary<long, VisualElement> _placeholders = new Dictionary<long, VisualElement>();
        private readonly Dictionary<VisualElement, long> _windowIds = new Dictionary<VisualElement, long>();
        private readonly List<long> _windowOrder = new List<long>();

        /// <param name="host">맨 위 요소를 넣을 곳(보통 <c>UIDocument.rootVisualElement</c>).</param>
        public UiToolkitRenderer(VisualElement host, IUiAssets assets)
        {
            _host = host;
            _assets = assets;
            _windowLayer = new VisualElement { name = "kesa-windows", pickingMode = PickingMode.Ignore };
            _windowLayer.style.position = Position.Absolute;
            _windowLayer.style.left = 0;
            _windowLayer.style.top = 0;
            _windowLayer.style.right = 0;
            _windowLayer.style.bottom = 0;
            _blocker = new VisualElement { name = "kesa-modal-blocker" };
            _blocker.AddToClassList(BlockerClass);
            _blocker.style.position = Position.Absolute;
            _blocker.style.left = 0;
            _blocker.style.top = 0;
            _blocker.style.right = 0;
            _blocker.style.bottom = 0;
            _host.Add(_windowLayer);
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
                _host.Insert(_host.IndexOf(_windowLayer), element);
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
            if (sheet == null)
            {
                Debug.LogWarning("Kesa UI: the style sheet '" + path + "' could not be found");
                return;
            }
            _elements[element.Value].styleSheets.Add(sheet);
            // 겹침 층으로 옮긴 창은 파일 맨 위 요소 밖에 있으므로 같은 스타일시트를 층에도 붙인다
            if (!_windowLayer.styleSheets.Contains(sheet)) _windowLayer.styleSheets.Add(sheet);
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
            if (property == "display" && _placeholders.ContainsKey(element.Value)) return; // 창의 보임은 ArrangeWindows가 정한다
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
            var target = InPlace(element.Value);
            target.RemoveFromHierarchy();
            container.Insert(Math.Min(index, container.childCount), target);
        }

        public void Remove(ElementId element)
        {
            var target = _elements[element.Value];
            InPlace(element.Value).RemoveFromHierarchy();
            target.RemoveFromHierarchy();
            Forget(target);
        }

        public void MakeWindow(ElementId element)
        {
            var window = _elements[element.Value];
            var placeholder = new VisualElement { name = "kesa-window-place" };
            placeholder.style.display = DisplayStyle.None;
            var parent = window.parent;
            if (parent != null) parent.Insert(parent.IndexOf(window), placeholder);
            window.RemoveFromHierarchy();
            window.style.position = Position.Absolute;
            window.style.display = DisplayStyle.None;
            _windowLayer.Add(window);
            _placeholders[element.Value] = placeholder;
            _windowIds[window] = element.Value;
            _windowOrder.Add(element.Value);
        }

        public void MakeDragHandle(ElementId element) => _elements[element.Value].AddManipulator(new WindowDrag(this));

        public void ArrangeWindows(IReadOnlyList<ElementId> windows, int blockerAt)
        {
            var shown = new List<long>();
            foreach (var id in windows) shown.Add(id.Value);
            _windowLayer.Clear();
            foreach (long id in _windowOrder)
            {
                if (shown.Contains(id)) continue;
                _elements[id].style.display = DisplayStyle.None;
                _windowLayer.Add(_elements[id]);
            }
            for (int i = 0; i <= shown.Count; i++)
            {
                if (i == blockerAt) _windowLayer.Add(_blocker);
                if (i == shown.Count) break;
                var window = _elements[shown[i]];
                window.style.display = DisplayStyle.Flex;
                _windowLayer.Add(window);
            }
        }

        public void OnPress(Func<ElementId?, ValueTask> handler)
        {
            _host.RegisterCallback<PointerDownEvent>(evt =>
            {
                ElementId? pressed = null;
                for (var e = evt.target as VisualElement; e != null && e != _host; e = e.parent)
                {
                    if (e == _blocker) break;
                    if (_windowIds.TryGetValue(e, out long id))
                    {
                        pressed = new ElementId(id);
                        break;
                    }
                }
                _ = Run(() => handler(pressed));
            }, TrickleDown.TrickleDown);
        }

        /// <summary>창이면 원래 자리의 자리표, 아니면 요소 자신.</summary>
        private VisualElement InPlace(long id) => _placeholders.TryGetValue(id, out var placeholder) ? placeholder : _elements[id];

        /// <summary>요소를 담은 가장 가까운 창.</summary>
        private VisualElement? WindowOf(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
                if (_windowIds.ContainsKey(e)) return e;
            return null;
        }

        /// <summary><c>window-drag</c> 손잡이: 누른 채 움직이면 그 손잡이를 담은 창을 옮긴다 (G349).</summary>
        private sealed class WindowDrag : PointerManipulator
        {
            private readonly UiToolkitRenderer _renderer;
            private VisualElement? _window;
            private Vector2 _start;
            private Vector2 _origin;
            private int _pointer = -1;

            public WindowDrag(UiToolkitRenderer renderer)
            {
                _renderer = renderer;
                activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnDown);
                target.RegisterCallback<PointerMoveEvent>(OnMove);
                target.RegisterCallback<PointerUpEvent>(OnUp);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnDown);
                target.UnregisterCallback<PointerMoveEvent>(OnMove);
                target.UnregisterCallback<PointerUpEvent>(OnUp);
            }

            private void OnDown(PointerDownEvent evt)
            {
                if (!CanStartManipulation(evt)) return;
                _window = _renderer.WindowOf(target);
                if (_window == null) return;
                _start = new Vector2(evt.position.x, evt.position.y);
                _origin = new Vector2(_window.layout.x, _window.layout.y);
                _pointer = evt.pointerId;
                target.CapturePointer(_pointer);
                evt.StopPropagation();
            }

            private void OnMove(PointerMoveEvent evt)
            {
                if (_window == null || evt.pointerId != _pointer || !target.HasPointerCapture(_pointer)) return;
                var delta = new Vector2(evt.position.x, evt.position.y) - _start;
                _window.style.left = _origin.x + delta.x;
                _window.style.top = _origin.y + delta.y;
            }

            private void OnUp(PointerUpEvent evt)
            {
                if (evt.pointerId != _pointer) return;
                if (target.HasPointerCapture(_pointer)) target.ReleasePointer(_pointer);
                _window = null;
                _pointer = -1;
            }
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
                if (_placeholders.TryGetValue(id, out var placeholder))
                {
                    placeholder.RemoveFromHierarchy();
                    element.RemoveFromHierarchy();
                    _placeholders.Remove(id);
                    _windowIds.Remove(element);
                    _windowOrder.Remove(id);
                }
            }
            foreach (var child in element.Children()) Forget(child);
            // 창 안에 있던 창은 겹침 층으로 옮겨져 이 요소의 자식이 아니므로 자리표로 찾는다
            foreach (var pair in new List<KeyValuePair<long, VisualElement>>(_placeholders))
                if (_elements.TryGetValue(pair.Key, out var nested) && IsUnder(pair.Value, element)) Forget(nested);
        }

        private static bool IsUnder(VisualElement element, VisualElement ancestor)
        {
            for (var e = element.parent; e != null; e = e.parent)
                if (e == ancestor) return true;
            return false;
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
