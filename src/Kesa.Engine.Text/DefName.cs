using System;
using Linguini.Shared.Types.Bundle;

namespace Kesa.Engine.Text
{
    /// <summary>
    /// 번역 인자 자리의 Def (spec-gaps G267): 자리 표시자에는 번역된 이름이 나오고, 사용자 함수(<c>JOSA</c>·<c>DEFATTR</c>)는 Def id를 안다.
    /// 이 값으로는 <c>select</c>가 늘 기본 가지라(Linguini가 <see cref="Matches"/>를 부르지 않음) Def에 따라 고르는 일은 <c>DEFATTR</c>로 한다.
    /// </summary>
    internal sealed class DefName : IFluentType
    {
        private readonly Func<string, string?> _attribute;

        /// <param name="attribute">그 Def의 속성 글을 같은 언어 목록으로 찾는 길, 없으면 null</param>
        public DefName(string defId, string name, Func<string, string?> attribute)
        {
            DefId = defId;
            Name = name;
            _attribute = attribute;
        }

        public string DefId { get; }
        public string Name { get; }

        /// <summary>Fluent 속성 <c>def-….속성</c>의 글, 없으면 빈 문자열 (G268).</summary>
        public string Attribute(string attribute) => _attribute(attribute) ?? "";

        public string AsString() => Name;

        public bool IsError() => false;

        public bool Matches(IFluentType other, IScope scope) => other is DefName d && d.DefId == DefId;

        public IFluentType Copy() => this;
    }
}
