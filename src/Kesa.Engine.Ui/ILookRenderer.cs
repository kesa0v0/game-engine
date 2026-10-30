namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 판 위 그림을 그리는 쪽 (G412·G413). 해석기는 요소별 메서드가 아니라 종류 이름과 속성 값으로 알리므로 새 요소 종류가 이 인터페이스를
    /// 바꾸지 않는다. 적지 않은 속성은 렌더러의 기본값이다(<c>Sprite</c>의 <c>size</c>는 긴 변 1, 그림 비율 유지).
    /// </summary>
    public interface ILookRenderer
    {
        /// <summary>판 위 물체 하나(표시나 엔티티)를 만든다. <paramref name="key"/>는 알아보기용이다.</summary>
        ElementId CreateObject(string key);

        /// <summary>물체를 놓는다. 위치는 공간 종류가 계산한 Unity 축의 고정소수점 원래 정수값이다.</summary>
        void PlaceObject(ElementId obj, LookPosition position);

        /// <summary>그림 정의가 없는 물체에 눈에 띄는 대체 그림을 보인다 (G392).</summary>
        void ShowMissing(ElementId obj);

        /// <summary>요소를 만들어 <paramref name="parent"/>(물체나 요소)의 <paramref name="index"/>번째에 넣는다.</summary>
        ElementId Create(string kind, ElementId parent, int index);

        /// <summary>속성 값을 넣는다. 값 종류는 그 요소 종류가 등록한 것이다.</summary>
        void Set(ElementId element, string attribute, LookAttributeValue value);

        /// <summary>요소와 그 아래를 보이거나 숨긴다(<c>bind-display</c>).</summary>
        void SetVisible(ElementId element, bool visible);

        /// <summary>부모 안의 순서를 바꾼다(<c>for</c>가 새 목록의 순서에 맞출 때).</summary>
        void Move(ElementId element, ElementId parent, int index);

        /// <summary>물체나 요소와 그 아래를 없앤다.</summary>
        void Remove(ElementId element);
    }
}
