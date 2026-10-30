#nullable enable
using System;
using System.Collections.Generic;
using Kesa.Engine.Ui;
using UnityEngine;

namespace Kesa.Engine.Unity
{
    /// <summary>
    /// 판 위 그림을 GameObject로 그린다 (G412·G413). 물체 하나는 GameObject 하나이고 공간 단위만큼 키워 그 안의 <c>size</c>·<c>offset</c>이 칸 단위가 된다.
    /// 고정소수점은 여기서 처음 실수가 된다. 기본 종류는 <c>Sprite</c>(SpriteRenderer)와 <c>Group</c>이고 게임은 <see cref="AddKind"/>로 더한다.
    /// </summary>
    public sealed class GameObjectLookRenderer : ILookRenderer
    {
        private sealed class Node
        {
            public Node(GameObject gameObject, ILookElementView? view, Node? parent)
            {
                GameObject = gameObject;
                View = view;
                Parent = parent;
            }

            public GameObject GameObject { get; }
            public ILookElementView? View { get; }
            public Node? Parent { get; set; }
            public long Id;

            /// <summary>물체면 그 열쇠.</summary>
            public string? Key;
            public int Layer;
            public readonly List<Node> Children = new List<Node>();

            public int Order => (Parent?.Order ?? 0) + Layer;
        }

        private readonly Transform _board;
        private readonly IUiAssets _assets;
        private readonly Dictionary<string, Func<IUiAssets, ILookElementView>> _kinds = new Dictionary<string, Func<IUiAssets, ILookElementView>>(StringComparer.Ordinal);
        private readonly Dictionary<long, Node> _nodes = new Dictionary<long, Node>();
        private long _next;

        /// <param name="board">물체를 넣을 곳.</param>
        public GameObjectLookRenderer(Transform board, IUiAssets assets)
        {
            _board = board;
            _assets = assets;
            AddKind("Sprite", a => new SpriteView(a));
            AddKind("Group", _ => new GroupView());
        }

        /// <summary>요소 종류를 더한다. 이름은 <see cref="LookKinds"/>에 등록한 것과 같아야 한다.</summary>
        public void AddKind(string kind, Func<IUiAssets, ILookElementView> create) => _kinds[kind] = create;

        /// <summary>핸들의 GameObject.</summary>
        public GameObject Object(ElementId id) => _nodes[id.Value].GameObject;

        public ElementId CreateObject(string key)
        {
            var go = new GameObject(key);
            go.transform.SetParent(_board, false);
            return Add(new Node(go, null, null) { Key = key });
        }

        /// <summary>
        /// 광선이 지나는 물체의 열쇠, 위에 그린 것부터(그리는 순서가 같으면 가까운 것부터). 보이는 SpriteRenderer의 경계로 잰다.
        /// 누름은 이것을 <c>LookBoard.PressAsync</c>에 넘긴다.
        /// </summary>
        public IReadOnlyList<string> KeysAt(Ray ray)
        {
            var hits = new List<(string Key, int Order, float Distance)>();
            foreach (var node in _nodes.Values)
            {
                if (node.Key == null || !node.GameObject.activeInHierarchy) continue;
                int best = int.MinValue;
                float near = float.MaxValue;
                foreach (var sprite in node.GameObject.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!sprite.enabled || sprite.sprite == null || !sprite.bounds.IntersectRay(ray, out float distance)) continue;
                    if (sprite.sortingOrder > best || (sprite.sortingOrder == best && distance < near))
                    {
                        best = sprite.sortingOrder;
                        near = distance;
                    }
                }
                if (best != int.MinValue) hits.Add((node.Key, best, near));
            }
            hits.Sort((a, b) => a.Order != b.Order ? b.Order.CompareTo(a.Order) : a.Distance.CompareTo(b.Distance));
            return hits.ConvertAll(h => h.Key);
        }

        /// <summary>
        /// 광선이 닿는 공간의 자리 (G422): 판 기준 좌표로 바꿔 <see cref="LookStage.PlaceOnRay"/>에 묻는다. 누름은 이것을 <c>LookBoard.InputAsync</c>에
        /// 함께 넘긴다.
        /// </summary>
        public Bridge.BridgeWhere? PlaceAt(LookStage stage, Ray ray)
        {
            var from = _board.InverseTransformPoint(ray.origin);
            var direction = _board.InverseTransformDirection(ray.direction);
            return stage.PlaceOnRay(Raw(from), Raw(direction));
        }

        private static LookPosition Raw(Vector3 v) => new LookPosition(Raw(v.x), Raw(v.y), Raw(v.z));

        private static long Raw(float real) => (long)Math.Round(real * (double)Bridge.BridgeFixed.Scale);

        public void PlaceObject(ElementId obj, LookPosition position, long unit)
        {
            var t = _nodes[obj.Value].GameObject.transform;
            t.localPosition = new Vector3(Real(position.X), Real(position.Y), Real(position.Z));
            float scale = Real(unit);
            t.localScale = new Vector3(scale, scale, scale);
        }

        public void ShowMissing(ElementId obj)
        {
            var view = new SpriteView(_assets);
            view.ShowMissing();
            view.GameObject.transform.SetParent(_nodes[obj.Value].GameObject.transform, false);
        }

        public ElementId Create(string kind, ElementId parent, int index)
        {
            var owner = _nodes[parent.Value];
            // 등록되지 않은 종류는 로드 때 걸러지므로 여기서는 게임 코드가 AddKind를 빠뜨린 것이다
            if (!_kinds.TryGetValue(kind, out var create)) throw new InvalidOperationException("no Unity view is added for the look element '" + kind + "'");
            var view = create(_assets);
            view.GameObject.transform.SetParent(owner.GameObject.transform, false);
            view.GameObject.transform.SetSiblingIndex(index);
            var node = new Node(view.GameObject, view, owner);
            owner.Children.Insert(Math.Min(index, owner.Children.Count), node);
            view.SetSortingOrder(node.Order);
            return Add(node);
        }

        public void Set(ElementId element, string attribute, LookAttributeValue value)
        {
            var node = _nodes[element.Value];
            switch (attribute)
            {
                case "layer":
                    node.Layer = (int)Math.Max(int.MinValue / 2, Math.Min(int.MaxValue / 2, value.Numbers[0]));
                    Reorder(node);
                    break;
                case "offset":
                    node.GameObject.transform.localPosition = new Vector3(Real(value.Numbers[0]), Real(value.Numbers[1]),
                        value.Numbers.Count > 2 ? Real(value.Numbers[2]) : 0f);
                    break;
                default:
                    node.View?.Set(attribute, value);
                    break;
            }
        }

        public void SetVisible(ElementId element, bool visible) => _nodes[element.Value].GameObject.SetActive(visible);

        public void Move(ElementId element, ElementId parent, int index)
        {
            var node = _nodes[element.Value];
            var owner = _nodes[parent.Value];
            node.Parent?.Children.Remove(node);
            node.Parent = owner;
            owner.Children.Insert(Math.Min(index, owner.Children.Count), node);
            node.GameObject.transform.SetParent(owner.GameObject.transform, false);
            node.GameObject.transform.SetSiblingIndex(index);
            Reorder(node);
        }

        public void Remove(ElementId element)
        {
            var node = _nodes[element.Value];
            node.Parent?.Children.Remove(node);
            Forget(node);
            if (Application.isPlaying) UnityEngine.Object.Destroy(node.GameObject);
            else UnityEngine.Object.DestroyImmediate(node.GameObject);
        }

        private ElementId Add(Node node)
        {
            node.Id = ++_next;
            _nodes[node.Id] = node;
            return new ElementId(node.Id);
        }

        private void Forget(Node node)
        {
            _nodes.Remove(node.Id);
            foreach (var child in node.Children) Forget(child);
        }

        private static void Reorder(Node node)
        {
            node.View?.SetSortingOrder(node.Order);
            foreach (var child in node.Children) Reorder(child);
        }

        private static float Real(long raw) => raw / (float)Bridge.BridgeFixed.Scale;

        private sealed class GroupView : ILookElementView
        {
            public GameObject GameObject { get; } = new GameObject("Group");

            public void Set(string attribute, LookAttributeValue value)
            {
            }

            public void SetSortingOrder(int order)
            {
            }
        }

        /// <summary>
        /// <c>Sprite</c>: 그림의 긴 변이 1(공간 단위)이 되도록 만들고 <c>size</c>가 있으면 그 크기로 늘인다 (G413). 그림이 없으면 대체 그림 (G392).
        /// </summary>
        private sealed class SpriteView : ILookElementView
        {
            private static readonly Dictionary<IUiAssets, Dictionary<string, Sprite?>> Sprites = new Dictionary<IUiAssets, Dictionary<string, Sprite?>>();
            private static Sprite? _missing;

            private readonly IUiAssets _assets;
            private readonly SpriteRenderer _renderer;
            private Vector2? _size;

            public SpriteView(IUiAssets assets)
            {
                _assets = assets;
                GameObject = new GameObject("Sprite");
                _renderer = GameObject.AddComponent<SpriteRenderer>();
            }

            public GameObject GameObject { get; }

            public void Set(string attribute, LookAttributeValue value)
            {
                switch (attribute)
                {
                    case "src":
                        _renderer.sprite = Load(value.Text!);
                        Resize();
                        break;
                    case "size":
                        _size = new Vector2(Real(value.Numbers[0]), Real(value.Numbers[1]));
                        Resize();
                        break;
                    case "tint":
                        uint c = value.Color;
                        _renderer.color = new Color32((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);
                        break;
                }
            }

            public void SetSortingOrder(int order) => _renderer.sortingOrder = order;

            public void ShowMissing()
            {
                _renderer.sprite = Missing();
                Resize();
            }

            private void Resize()
            {
                var sprite = _renderer.sprite;
                if (sprite == null || _size == null)
                {
                    GameObject.transform.localScale = Vector3.one;
                    return;
                }
                var bounds = sprite.bounds.size;
                GameObject.transform.localScale = new Vector3(_size.Value.x / bounds.x, _size.Value.y / bounds.y, 1f);
            }

            private Sprite Load(string path)
            {
                if (!Sprites.TryGetValue(_assets, out var cache)) Sprites[_assets] = cache = new Dictionary<string, Sprite?>(StringComparer.Ordinal);
                if (!cache.TryGetValue(path, out var sprite))
                {
                    var texture = _assets.LoadImage(path);
                    if (texture == null) Debug.LogWarning("Kesa look: the picture '" + path + "' cannot be read; showing the missing picture");
                    sprite = texture == null ? null : FromTexture(texture);
                    cache[path] = sprite;
                }
                return sprite ?? Missing();
            }

            // 긴 변을 1 단위로: 그림 해상도가 바뀌어도 판 위 크기는 그대로다
            private static Sprite FromTexture(Texture2D texture) =>
                Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), Math.Max(texture.width, texture.height));

            private static Sprite Missing()
            {
                if (_missing != null) return _missing;
                var texture = new Texture2D(8, 8) { filterMode = FilterMode.Point };
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                        texture.SetPixel(x, y, ((x / 4) + (y / 4)) % 2 == 0 ? new Color(1f, 0f, 1f) : Color.black);
                texture.Apply();
                _missing = FromTexture(texture);
                return _missing;
            }
        }
    }
}
