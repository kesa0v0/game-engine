using Linguini.Shared.Types.Bundle;

namespace Kesa.Engine.Text
{
    /// <summary>
    /// 커널의 int·fixed를 번역 파일의 수로 (spec-gaps G267). 복수형 <c>select</c>는 <see cref="FluentNumber"/>의 값으로 고르고,
    /// 화면에는 커널의 십진 표기를 그대로 낸다. <see cref="FluentNumber"/>는 double을 거쳐 <c>0.000001</c>을 <c>1E-06.000000</c>로,
    /// 2^53을 넘는 int를 지수 표기로 바꾼다. 두 메서드는 상속한 것이 재정의되지 않아 인터페이스를 다시 구현한다(Linguini는 <see cref="IFluentType"/>로 부른다).
    /// </summary>
    internal sealed record ExactNumber : FluentNumber, IFluentType
    {
        private readonly string _text;

        public ExactNumber(string text)
            : base(FromString(text)) =>
            _text = text;

        public new string AsString() => _text;

        public new IFluentType Copy() => new ExactNumber(_text);
    }
}
