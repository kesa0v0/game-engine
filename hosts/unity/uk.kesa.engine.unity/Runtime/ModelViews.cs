#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Ui;
using UnityEngine;

namespace Kesa.Engine.Unity
{
    /// <summary>자식 요소 뷰를 아는 뷰 (G434: <c>Model</c>은 <c>State</c>를, <c>State</c>는 <c>Clip</c>을). 렌더러가 잇는다.</summary>
    internal interface ILookParentView
    {
        void Insert(ILookElementView child, int index);
        void Remove(ILookElementView child);
    }

    /// <summary>
    /// <c>Model</c>: 로더가 읽은 원본을 복제해 단다. 모델 단위에 <c>scale</c>(기본 1)을 곱한 것이 공간 단위다 (G438). 다 읽을 때까지 비어 있고, 못 읽으면 보라색 상자 (G391).
    /// 자식 <c>State</c>가 있으면 그 클립을 옛 <c>Animation</c>에 걸어 시간·가중치를 손으로 넣고 <see cref="Tick"/>마다 그 자세로 둔다 (G434).
    /// </summary>
    internal sealed class ModelView : ILookElementView, ILookParentView
    {
        private readonly LookModels _models;
        private readonly List<StateView> _states = new List<StateView>();
        private readonly List<AttachView> _attaches = new List<AttachView>();
        private readonly List<PaintView> _paints = new List<PaintView>();
        private Color? _tint;
        // 칠할 때만 만드는 복제본 재질과 원래 기본 색
        private List<(Material Material, string Name, Color Base, int Property)>? _colored;
        private string? _src;
        private int _order;
        private GameObject? _shown;
        private ModelAsset? _asset;
        private string? _wanted;
        private float _fade;

        private Animation? _animation;
        private bool _dirty = true;
        private string? _lastWanted;
        private StateView? _current;
        private float _fadeTime;
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        public ModelView(LookModels models)
        {
            _models = models;
            GameObject = new GameObject("Model");
        }

        public GameObject GameObject { get; }

        public void Set(string attribute, LookAttributeValue value)
        {
            switch (attribute)
            {
                case "src":
                    Load(value.Text!);
                    break;
                case "state":
                    _wanted = value.Text;
                    break;
                case "fade":
                    _fade = GameObjectLookRenderer.Real(value.Numbers[0]);
                    break;
                case "tint":
                    _tint = GameObjectLookRenderer.ColorOf(value.Color);
                    Recolor();
                    break;
                case "scale":
                    GameObject.transform.localScale = Vector3.one * GameObjectLookRenderer.Real(value.Numbers[0]);
                    break;
            }
        }

        public void SetSortingOrder(int order)
        {
            _order = order;
            if (_shown == null) return;
            foreach (var renderer in _shown.GetComponentsInChildren<Renderer>(true))
                if (!IsAttached(renderer.transform)) renderer.sortingOrder = order;
        }

        public void Insert(ILookElementView child, int index)
        {
            if (child is PaintView paint)
            {
                paint.Owner = this;
                _paints.Add(paint);
                Recolor();
                return;
            }
            if (child is AttachView attach)
            {
                attach.Owner = this;
                _attaches.Add(attach);
                Place(attach);
                return;
            }
            if (!(child is StateView state)) return;
            state.Owner = this;
            _states.Insert(Math.Min(index, _states.Count), state);
            Changed();
        }

        public void Remove(ILookElementView child)
        {
            if (child is PaintView paint && _paints.Remove(paint))
            {
                paint.Owner = null;
                Recolor();
                return;
            }
            if (child is AttachView attach && _attaches.Remove(attach))
            {
                attach.Owner = null;
                return;
            }
            if (child is StateView state && _states.Remove(state)) Changed();
        }

        // ---- 뼈에 붙이기 (G435) ----

        /// <summary>붙는 것을 그 이름의 뼈 아래로 옮긴다. 모델을 읽는 동안이나 없는 뼈면 모델 원점.</summary>
        public void Place(AttachView attach)
        {
            Transform? bone = null;
            if (_shown != null && _asset != null && attach.Bone != null)
            {
                bone = Bone(_shown.transform, attach.Bone);
                if (bone == null) Warn("no part is named '" + attach.Bone + "' for <Attach bone>; attaching at the model's origin (spec-gaps G435)");
            }
            attach.GameObject.transform.SetParent(bone != null ? bone : GameObject.transform, false);
        }

        // ---- 색 (G436) ----

        private static readonly int[] BaseColors =
            { Shader.PropertyToID("baseColorFactor"), Shader.PropertyToID("_BaseColor"), Shader.PropertyToID("_Color") };

        /// <summary>재질마다 원래 기본 색에 그 재질의 <c>Paint</c> 색, 없으면 <c>tint</c>를 곱한다.</summary>
        public void Recolor()
        {
            if (_shown == null || _asset == null) return;
            if (_colored == null)
            {
                if (_tint == null && _paints.Count == 0) return;
                _colored = new List<(Material, string, Color, int)>();
                foreach (var renderer in _shown.GetComponentsInChildren<Renderer>(true))
                {
                    if (IsAttached(renderer.transform)) continue;
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null) continue;
                        int property = -1;
                        foreach (int id in BaseColors)
                            if (materials[i].HasProperty(id))
                            {
                                property = id;
                                break;
                            }
                        if (property == -1) continue;
                        var copy = new Material(materials[i]);
                        _colored.Add((copy, materials[i].name, copy.GetColor(property), property));
                        materials[i] = copy;
                    }
                    renderer.sharedMaterials = materials;
                }
            }
            foreach (var paint in _paints)
                if (paint.Material != null && !_colored.Any(c => c.Name == paint.Material))
                    Warn("no material is named '" + paint.Material + "' for <Paint> (has: " +
                         string.Join(", ", _colored.Select(c => c.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal)) + ") (spec-gaps G436)");
            foreach (var (material, name, original, property) in _colored)
            {
                var paint = _paints.FirstOrDefault(p => p.Material == name && p.Color != null);
                var by = paint?.Color ?? _tint ?? Color.white;
                material.SetColor(property, original * by);
            }
        }

        // 넓이 우선이라 이름이 겹치면 뿌리에 가까운 것
        private static Transform? Bone(Transform root, string name)
        {
            var queue = new Queue<Transform>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                if (t != root && t.name == name) return t;
                foreach (Transform child in t) queue.Enqueue(child);
            }
            return null;
        }

        private bool IsAttached(Transform t)
        {
            for (; t != null && t != GameObject.transform; t = t.parent)
                foreach (var attach in _attaches)
                    if (attach.GameObject.transform == t) return true;
            return false;
        }

        /// <summary>상태나 클립이 바뀌어 다시 건다.</summary>
        public void Changed() => _dirty = true;

        private void Load(string path)
        {
            _src = path;
            Clear();
            _models.Request(path, asset =>
            {
                if (GameObject == null || _src != path) return;
                Clear();
                _asset = asset;
                _shown = asset != null ? UnityEngine.Object.Instantiate(asset.Template, GameObject.transform, false) : Missing();
                _shown.SetActive(true);
                foreach (var attach in _attaches) Place(attach);
                SetSortingOrder(_order);
                Recolor();
                Changed();
                Tick(0f);
            });
        }

        private void Clear()
        {
            _animation = null;
            _current = null;
            if (_colored != null)
                foreach (var colored in _colored) UnityEngine.Object.DestroyImmediate(colored.Material);
            _colored = null;
            _asset = null;
            if (_shown == null) return;
            // 복제본과 함께 지워지지 않게 붙은 것을 먼저 내린다
            foreach (var attach in _attaches) attach.GameObject.transform.SetParent(GameObject.transform, false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(_shown);
            else UnityEngine.Object.DestroyImmediate(_shown);
            _shown = null;
        }

        private GameObject Missing()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.name = "missing-model";
            cube.transform.SetParent(GameObject.transform, false);
            cube.transform.localScale = Vector3.one * 0.5f;
            var renderer = cube.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(renderer.sharedMaterial) { color = new Color(1f, 0f, 1f) };
            return cube;
        }

        // ---- 애니메이션 (G434) ----

        /// <summary>시간을 넘겨 상태를 고르고 섞어 그 자세로 둔다.</summary>
        public void Tick(float seconds)
        {
            if (_shown == null || _asset == null || _states.Count == 0) return;
            if (_dirty) Build();
            if (_animation == null) return;

            if (_current == null || _wanted != _lastWanted)
            {
                _lastWanted = _wanted;
                var state = Find(_wanted) ?? (_wanted == null ? _states[0] : null);
                if (state == null)
                {
                    Warn("no <State> is named '" + _wanted + "'; keeping the last one");
                    if (_current == null) Enter(_states[0], instant: true);
                }
                else if (state != _current) Enter(state, instant: _current == null);
            }

            foreach (var state in _states)
                if (state == _current || state.Weight > 0f) state.Time += seconds;
            if (_current != null && _current.Once && _current.Time >= _current.Length && _current.Next != null)
            {
                var next = Find(_current.Next);
                if (next == null) Warn("no <State> is named '" + _current.Next + "' for next");
                else if (next != _current) Enter(next, instant: false);
            }

            float step = _fadeTime <= 0f ? 1f : seconds / _fadeTime;
            float total = 0f;
            foreach (var state in _states)
            {
                state.Weight = state == _current ? Mathf.Min(1f, state.Weight + step) : Mathf.Max(0f, state.Weight - step);
                total += state.Weight;
            }
            foreach (var state in _states) state.Pose(total > 0f ? state.Weight / total : 0f);
            _animation.Sample();
        }

        private StateView? Find(string? name) => name == null ? null : _states.FirstOrDefault(s => s.Name == name);

        private void Enter(StateView state, bool instant)
        {
            _current = state;
            state.Time = 0f;
            _fadeTime = instant ? 0f : state.Fade ?? _fade;
        }

        private void Build()
        {
            _dirty = false;
            if (_animation == null)
            {
                _animation = _shown!.GetComponentInChildren<Animation>(true);
                if (_animation == null)
                    _animation = (_shown.transform.childCount > 0 ? _shown.transform.GetChild(0).gameObject : _shown).AddComponent<Animation>();
                _animation.playAutomatically = false;
            }
            _animation.Stop();
            for (int i = 0; i < _states.Count; i++)
            {
                _states[i].Weight = 0f;
                _states[i].Build(_animation, _asset!.Clips, "kesa-" + i + "-", Warn);
            }
            _current = null;
        }

        private void Warn(string message)
        {
            if (_warned.Add(message)) Debug.LogWarning("Kesa look: model '" + _src + "': " + message + (message.Contains("spec-gaps") ? "" : " (spec-gaps G434)"));
        }
    }

    /// <summary><c>State</c>: 클립 하나, 또는 자식 <c>Clip</c>들을 <c>blend</c>로 섞는다 (G434).</summary>
    internal sealed class StateView : ILookElementView, ILookParentView
    {
        private readonly List<ClipView> _clips = new List<ClipView>();
        // Animation에 상태마다 따로 단 이름이라 같은 클립을 두 상태가 써도 시간이 따로 간다
        private readonly List<(AnimationState State, float At)> _playing = new List<(AnimationState, float)>();
        private string? _clip;
        private float _blend;

        public StateView() => GameObject = new GameObject("State");

        public GameObject GameObject { get; }
        public ModelView? Owner;
        public string? Name;
        public bool Once;
        public string? Next;
        public float? Fade;
        public float Time;
        public float Weight;

        /// <summary>한 번 트는 길이(초): 클립 하나면 그 길이, 섞으면 가장 긴 것.</summary>
        public float Length { get; private set; }

        public void Set(string attribute, LookAttributeValue value)
        {
            switch (attribute)
            {
                case "name":
                    Name = value.Text;
                    break;
                case "clip":
                    _clip = value.Text;
                    Owner?.Changed();
                    break;
                case "once":
                    Once = value.Flag;
                    break;
                case "next":
                    Next = value.Text;
                    break;
                case "fade":
                    Fade = GameObjectLookRenderer.Real(value.Numbers[0]);
                    break;
                case "blend":
                    _blend = GameObjectLookRenderer.Real(value.Numbers[0]);
                    break;
            }
        }

        public void SetSortingOrder(int order)
        {
        }

        public void Insert(ILookElementView child, int index)
        {
            if (!(child is ClipView clip)) return;
            clip.Owner = this;
            _clips.Insert(Math.Min(index, _clips.Count), clip);
            Owner?.Changed();
        }

        public void Remove(ILookElementView child)
        {
            if (child is ClipView clip && _clips.Remove(clip)) Owner?.Changed();
        }

        public void Build(Animation animation, IReadOnlyDictionary<string, AnimationClip> clips, string prefix, Action<string> warn)
        {
            foreach (var (state, _) in _playing)
                if (state != null) animation.RemoveClip(state.name);
            _playing.Clear();
            Length = 0f;
            var wanted = _clips.Count == 0
                ? new List<(string? Clip, float At)> { (_clip, 0f) }
                : _clips.OrderBy(c => c.At).Select(c => (c.Clip, c.At)).ToList();
            for (int i = 0; i < wanted.Count; i++)
            {
                var (name, at) = wanted[i];
                if (name == null || !clips.TryGetValue(name, out var clip))
                {
                    warn("no clip is named '" + name + "' (has: " + string.Join(", ", clips.Keys.OrderBy(k => k, StringComparer.Ordinal)) + ")");
                    continue;
                }
                animation.AddClip(clip, prefix + i);
                var state = animation[prefix + i];
                state.speed = 0f;
                state.wrapMode = WrapMode.ClampForever;
                state.enabled = false;
                _playing.Add((state, at));
                Length = Mathf.Max(Length, clip.length);
            }
        }

        /// <summary>
        /// 지금 시간의 자세를 <paramref name="weight"/>만큼: 반복이면 길이로 나눈 나머지, 한 번이면 끝에서 멈춤. 섞으면 <c>at</c>이 가까운 둘을
        /// 비율로.
        /// </summary>
        public void Pose(float weight)
        {
            for (int i = 0; i < _playing.Count; i++)
            {
                var state = _playing[i].State;
                float w = weight * Blend(i);
                state.enabled = w > 0f;
                state.weight = w;
                float length = state.length;
                state.time = length <= 0f ? 0f : Once ? Mathf.Min(Time, length) : Time % length;
            }
        }

        private float Blend(int i)
        {
            int last = _playing.Count - 1;
            if (last <= 0 || _blend <= _playing[0].At) return i == 0 ? 1f : 0f;
            if (_blend >= _playing[last].At) return i == last ? 1f : 0f;
            for (int k = 0; k < last; k++)
            {
                float a = _playing[k].At, b = _playing[k + 1].At;
                if (_blend < a || _blend > b) continue;
                float t = b > a ? (_blend - a) / (b - a) : 0f;
                return i == k ? 1f - t : i == k + 1 ? t : 0f;
            }
            return 0f;
        }
    }

    /// <summary><c>Attach</c>: 안의 요소가 모델의 <c>bone</c> 이름 뼈를 따라간다 (G435). 옮기는 것은 <see cref="ModelView.Place"/>.</summary>
    internal sealed class AttachView : ILookElementView
    {
        public AttachView() => GameObject = new GameObject("Attach");

        public GameObject GameObject { get; }
        public ModelView? Owner;
        public string? Bone;

        public void Set(string attribute, LookAttributeValue value)
        {
            if (attribute != "bone") return;
            Bone = value.Text;
            Owner?.Place(this);
        }

        public void SetSortingOrder(int order)
        {
        }
    }

    /// <summary><c>Paint</c>: 모델의 <c>material</c> 이름 재질에 <c>color</c>를 곱한다 (G436).</summary>
    internal sealed class PaintView : ILookElementView
    {
        public PaintView() => GameObject = new GameObject("Paint");

        public GameObject GameObject { get; }
        public ModelView? Owner;
        public string? Material;
        public Color? Color;

        public void Set(string attribute, LookAttributeValue value)
        {
            if (attribute == "material") Material = value.Text;
            else if (attribute == "color") Color = GameObjectLookRenderer.ColorOf(value.Color);
            else return;
            Owner?.Recolor();
        }

        public void SetSortingOrder(int order)
        {
        }
    }

    /// <summary><c>Clip</c>: 블렌드 축의 자리 <c>at</c>에 둔 클립 (G434).</summary>
    internal sealed class ClipView : ILookElementView
    {
        public ClipView() => GameObject = new GameObject("Clip");

        public GameObject GameObject { get; }
        public StateView? Owner;
        public string? Clip;
        public float At;

        public void Set(string attribute, LookAttributeValue value)
        {
            if (attribute == "clip") Clip = value.Text;
            else if (attribute == "at") At = GameObjectLookRenderer.Real(value.Numbers[0]);
            else return;
            Owner?.Owner?.Changed();
        }

        public void SetSortingOrder(int order)
        {
        }
    }
}
