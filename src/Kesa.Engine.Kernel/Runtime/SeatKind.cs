namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 앉은 연결의 종류 (spec S6, spec-gaps G241). 호스트가 <see cref="World.Seat"/>로 앉힐 때 주고, 스크립트는 <c>controller(e)</c>로
    /// <c>"human"</c>·<c>"ai"</c>를 읽는다(빈자리는 <c>None</c>). 연결 꼬리표 자체는 스크립트에 보이지 않는다.
    /// </summary>
    public enum SeatKind
    {
        Human,
        Ai,
    }
}
