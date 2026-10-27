using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>시험용 렌더러 (spec-gaps G343): 시킨 대로 메모리에 요소 트리를 만든다.</summary>
    public sealed class MemoryRenderer : IUiRenderer
    {
        public sealed class El
        {
            public long Id;
            public string Kind = "";
            public El? Parent;
            public List<El> Children = new List<El>();
            public SortedDictionary<string, string> Attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            public SortedDictionary<string, string> Texts = new SortedDictionary<string, string>(StringComparer.Ordinal);
            public SortedDictionary<string, bool> Flags = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            public SortedDictionary<string, long> Numbers = new SortedDictionary<string, long>(StringComparer.Ordinal);
            public SortedSet<string> Classes = new SortedSet<string>(StringComparer.Ordinal);
            public string? Image;
            public Func<ValueTask>? Click;
            public bool Removed;

            public string? Text => Texts.TryGetValue("text", out var t) ? t : null;

            public bool Enabled => !Flags.TryGetValue("enabled", out var f) || f;

            public ValueTask Press() => Click!();
        }

        private readonly Dictionary<long, El> _all = new Dictionary<long, El>();
        private long _next;

        public El? Root { get; private set; }

        public List<string> Log { get; } = new List<string>();

        public El Get(ElementId id) => _all[id.Value];

        public ElementId Create(string kind, ElementId? parent, int index, IReadOnlyList<KeyValuePair<string, string>> attributes)
        {
            var el = new El { Id = ++_next, Kind = kind };
            foreach (var a in attributes)
            {
                el.Attributes[a.Key] = a.Value;
                if (a.Key == "class")
                    foreach (var c in a.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) el.Classes.Add(c);
            }
            _all[el.Id] = el;
            if (parent == null)
            {
                Root = el;
            }
            else
            {
                var p = _all[parent.Value.Value];
                el.Parent = p;
                p.Children.Insert(index, el);
            }
            Log.Add("create " + kind);
            return new ElementId(el.Id);
        }

        public void SetText(ElementId element, string property, string value) => Get(element).Texts[property] = value;

        public void SetFlag(ElementId element, string property, bool value) => Get(element).Flags[property] = value;

        public void SetNumber(ElementId element, string property, long raw) => Get(element).Numbers[property] = raw;

        public void SetImage(ElementId element, string path) => Get(element).Image = path;

        public void SetClass(ElementId element, string className, bool on)
        {
            var el = Get(element);
            if (on) el.Classes.Add(className);
            else el.Classes.Remove(className);
        }

        public void Move(ElementId element, ElementId parent, int index)
        {
            var el = Get(element);
            var p = Get(parent);
            el.Parent!.Children.Remove(el);
            p.Children.Insert(index, el);
            el.Parent = p;
            Log.Add("move");
        }

        public void Remove(ElementId element)
        {
            var el = Get(element);
            el.Parent?.Children.Remove(el);
            Mark(el);
            Log.Add("remove " + el.Kind);
        }

        public void OnClick(ElementId element, Func<ValueTask> handler) => Get(element).Click = handler;

        private static void Mark(El el)
        {
            el.Removed = true;
            foreach (var c in el.Children) Mark(c);
        }

        /// <summary>요소 이름(<c>name</c> 속성)으로 아직 붙어 있는 요소를 찾는다.</summary>
        public El Named(string name) =>
            Walk(Root!).Single(e => e.Attributes.TryGetValue("name", out var n) && n == name);

        public IEnumerable<El> Walk(El start)
        {
            yield return start;
            foreach (var c in start.Children)
                foreach (var d in Walk(c))
                    yield return d;
        }
    }
}
