using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 화면의 창 목록 (spec-gaps G348~G354): 겹침 순서, 모달, ESC 차례, 바깥 클릭을 판단하고 렌더러마다 그릴 순서를 알린다. 해석기 여럿이
    /// 하나를 함께 쓸 수 있다(호스트가 만들어 넘긴다). 해석기와 같은 스레드에서만 쓴다.
    /// </summary>
    public sealed class UiWindows
    {
        private sealed class Entry
        {
            public Entry(IUiRenderer renderer, ElementId id, UiWindow window, Func<ValueTask>? close)
            {
                Renderer = renderer;
                Id = id;
                Window = window;
                Close = close;
            }

            public IUiRenderer Renderer { get; }

            public ElementId Id { get; }

            public UiWindow Window { get; }

            public Func<ValueTask>? Close { get; }

            public bool Visible { get; set; }

            /// <summary>보이게 되거나 올려진 때. 같은 종류끼리는 큰 것이 위다.</summary>
            public long Opened { get; set; }
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<IUiRenderer> _renderers = new List<IUiRenderer>();
        private long _clock;
        private bool _dirty;

        /// <summary>
        /// ESC를 맨 위 창부터 건넨다: ESC로 닫히는 창이면 그 <c>on-close</c>를 부르고 참이다. ESC로 닫히지 않는 모달에 막히거나 받을 창이 없으면
        /// 거짓이고, 그러면 게임이 자기 메뉴를 열 수 있다.
        /// </summary>
        public async ValueTask<bool> Escape()
        {
            var order = VisibleOrder();
            for (int i = order.Count - 1; i >= 0; i--)
            {
                var entry = order[i];
                if (entry.Window.CloseOnEscape && entry.Close != null)
                {
                    await entry.Close();
                    return true;
                }
                if (entry.Window.Kind == UiWindowKind.Modal) return false;
            }
            return false;
        }

        internal void Attach(IUiRenderer renderer)
        {
            if (_renderers.Contains(renderer)) return;
            _renderers.Add(renderer);
            renderer.OnPress(window => PressAsync(renderer, window));
        }

        internal void Add(IUiRenderer renderer, ElementId id, UiWindow window, Func<ValueTask>? close)
        {
            _entries.Add(new Entry(renderer, id, window, close));
            renderer.MakeWindow(id);
            _dirty = true;
        }

        internal void Remove(IUiRenderer renderer, ElementId id)
        {
            int index = Find(renderer, id);
            if (index < 0) return;
            _entries.RemoveAt(index);
            _dirty = true;
        }

        internal void SetVisible(IUiRenderer renderer, ElementId id, bool visible)
        {
            int index = Find(renderer, id);
            if (index < 0) return;
            var entry = _entries[index];
            if (entry.Visible == visible) return;
            entry.Visible = visible;
            // 갱신이 같은 값을 다시 넣어도 순서가 바뀌지 않게 숨김에서 보임으로 바뀔 때만 위로 올린다
            if (visible) entry.Opened = ++_clock;
            _dirty = true;
        }

        /// <summary>바뀐 것이 있으면 렌더러마다 보일 창의 순서와 막이 자리를 알린다.</summary>
        internal void Flush()
        {
            if (!_dirty) return;
            _dirty = false;
            var order = VisibleOrder();
            var topModal = order.Count > 0 && order[order.Count - 1].Window.Kind == UiWindowKind.Modal ? order[order.Count - 1] : null;
            foreach (var renderer in _renderers)
            {
                var ids = new List<ElementId>();
                int blockerAt = -1;
                foreach (var entry in order)
                {
                    if (!ReferenceEquals(entry.Renderer, renderer)) continue;
                    if (ReferenceEquals(entry, topModal)) blockerAt = ids.Count;
                    ids.Add(entry.Id);
                }
                if (topModal != null && blockerAt < 0) blockerAt = ids.Count;
                renderer.ArrangeWindows(ids, blockerAt);
            }
        }

        private async ValueTask PressAsync(IUiRenderer renderer, ElementId? window)
        {
            var order = VisibleOrder();
            Entry? pressed = null;
            if (window != null)
                foreach (var entry in order)
                    if (ReferenceEquals(entry.Renderer, renderer) && entry.Id.Equals(window.Value)) pressed = entry;

            if (order.Count > 0 && order[order.Count - 1].Window.Kind == UiWindowKind.Modal)
            {
                var modal = order[order.Count - 1];
                if (!ReferenceEquals(pressed, modal) && modal.Window.CloseOnOutside && modal.Close != null) await modal.Close();
                return;
            }

            if (pressed != null && pressed.Window.Raise && !ReferenceEquals(order[order.Count - 1], pressed))
            {
                pressed.Opened = ++_clock;
                _dirty = true;
                Flush();
            }
            var closing = new List<Entry>();
            for (int i = order.Count - 1; i >= 0; i--)
                if (!ReferenceEquals(order[i], pressed) && order[i].Window.CloseOnOutside && order[i].Close != null) closing.Add(order[i]);
            foreach (var entry in closing) await entry.Close!();
        }

        /// <summary>보이는 창, 아래에서 위: 보통 창들 다음에 모달들, 같은 종류끼리는 보이게 되거나 올려진 차례.</summary>
        private List<Entry> VisibleOrder()
        {
            var normal = new List<Entry>();
            var modal = new List<Entry>();
            foreach (var entry in _entries)
                if (entry.Visible) (entry.Window.Kind == UiWindowKind.Modal ? modal : normal).Add(entry);
            normal.Sort((a, b) => a.Opened.CompareTo(b.Opened));
            modal.Sort((a, b) => a.Opened.CompareTo(b.Opened));
            normal.AddRange(modal);
            return normal;
        }

        private int Find(IUiRenderer renderer, ElementId id)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (ReferenceEquals(_entries[i].Renderer, renderer) && _entries[i].Id.Equals(id)) return i;
            return -1;
        }
    }
}
