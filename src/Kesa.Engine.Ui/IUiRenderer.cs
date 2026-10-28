using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>렌더러가 만든 요소를 가리키는 핸들 (spec-gaps G345). 값의 뜻은 렌더러가 정한다.</summary>
    public readonly struct ElementId : IEquatable<ElementId>
    {
        public ElementId(long value)
        {
            Value = value;
        }

        public long Value { get; }

        public bool Equals(ElementId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is ElementId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => "#" + Value;
    }

    /// <summary>값을 화면 글자로 (번역기를 호스트가 꽂는다, G345): 글자 속성에 들어갈 값마다 부른다.</summary>
    public delegate string UiTextFormatter(BridgeValue value);

    /// <summary>
    /// 해석기가 시키는 대로 요소를 만들고 고치는 쪽 (spec-gaps G345). 판단과 타입 변환은 해석기가 이미 했으므로 값은 변환이 끝난 것만 온다.
    /// Unity 렌더러는 <c>VisualElement</c>를, 시험용 렌더러는 메모리 트리를 만든다.
    /// </summary>
    public interface IUiRenderer
    {
        /// <summary>
        /// 요소를 만들어 <paramref name="parent"/>(없으면 맨 위)의 <paramref name="index"/>번째에 넣는다.
        /// <paramref name="attributes"/>는 UXML에 직접 적은 고정 속성(<c>name</c>·<c>class</c>·<c>text</c> 등)이다.
        /// </summary>
        ElementId Create(string kind, ElementId? parent, int index, IReadOnlyList<KeyValuePair<string, string>> attributes);

        /// <summary>파일에 <c>&lt;Style src="…"/&gt;</c>로 적힌 스타일시트를 그 파일의 맨 위 요소에 붙인다. 경로를 찾는 것은 렌더러 몫이다.</summary>
        void AddStyleSheet(ElementId element, string path);

        /// <summary>글자 속성(<c>text</c>)에 글자를 넣는다.</summary>
        void SetText(ElementId element, string property, string value);

        /// <summary>참거짓 속성(<c>display</c>·<c>enabled</c>)을 정한다.</summary>
        void SetFlag(ElementId element, string property, bool value);

        /// <summary>숫자 속성(<c>value</c>·<c>opacity</c>)에 고정소수점의 원래 정수값(<c>BridgeFixed.Raw</c>)을 넣는다. 실수로 바꾸는 것은 화면에 쓸 때 렌더러가 한다.</summary>
        void SetNumber(ElementId element, string property, long raw);

        /// <summary>이미지 경로를 정한다(G335). 경로를 찾는 것은 렌더러 몫이다.</summary>
        void SetImage(ElementId element, string path);

        /// <summary>USS 클래스를 붙이거나 뗀다.</summary>
        void SetClass(ElementId element, string className, bool on);

        /// <summary>부모 안의 순서를 바꾼다(반복이 새 리스트의 순서에 맞출 때).</summary>
        void Move(ElementId element, ElementId parent, int index);

        /// <summary>요소와 그 아래를 없앤다.</summary>
        void Remove(ElementId element);

        /// <summary>눌렀을 때 부를 것을 단다. 렌더러는 입력이 오면 이것을 부르고 기다린다.</summary>
        void OnClick(ElementId element, Func<ValueTask> handler);

        /// <summary>
        /// 요소를 창으로 삼는다 (G348·G354). 그 뒤로 이 요소의 보임과 겹침은 <see cref="ArrangeWindows"/>만 정하고, 그때까지는 숨긴다.
        /// 창을 다른 창과 겹치게 하려고 어디로 옮기든 핸들과 부모 안의 순서(<see cref="Create"/>·<see cref="Move"/>의 자리)는 그대로여야 한다.
        /// </summary>
        void MakeWindow(ElementId element);

        /// <summary>이 요소를 끌면 이 요소를 담은 가장 가까운 창이 움직인다 (G349).</summary>
        void MakeDragHandle(ElementId element);

        /// <summary>
        /// 보이는 창과 겹침을 정한다 (G350·G351·G354). <paramref name="windows"/>는 이 렌더러의 보일 창, 아래에서 위 순서이고 여기 없는
        /// 창은 숨긴다. 모달 막이는 <paramref name="blockerAt"/>번째 창 바로 아래에 둔다: -1이면 막이가 없고, <c>windows.Count</c>면
        /// 이 렌더러의 모든 것 위(다른 렌더러의 모달이 맨 위). 막이는 그 아래 전부의 입력을 막는다.
        /// </summary>
        void ArrangeWindows(IReadOnlyList<ElementId> windows, int blockerAt);

        /// <summary>
        /// 화면이 눌렸을 때 부를 것을 단다 (G351·G354). 렌더러는 누름이 다른 처리보다 먼저 이것을 부른다: 눌린 곳을 담은 맨 위 창, 창 밖이나
        /// 막이면 null.
        /// </summary>
        void OnPress(Func<ElementId?, ValueTask> handler);
    }
}
