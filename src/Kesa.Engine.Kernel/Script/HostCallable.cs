using System.Collections.Generic;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 스크립트가 부르는 호스트 함수 값 (spec S7). 몸체는 월드가 준다: 인자를 서명대로 묶고 검사해 C# 값으로 바꾸고, 구현을 부르고,
    /// 돌려준 값을 서명대로 검사해 스크립트 값으로 바꾼다. 실행기는 자리 규칙(<see cref="Writes"/>)만 본다.
    /// </summary>
    public abstract class HostCallable : Value
    {
        /// <summary>전체 이름 (<c>core.path.find</c>).</summary>
        public abstract string Name { get; }

        /// <summary>엔티티를 쓰는 함수인가. 쓰는 내장처럼 쓰기 자리의 문장으로만 부를 수 있다 (spec-gaps G216).</summary>
        public abstract bool Writes { get; }

        public abstract Value Call(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords);

        public override string KindName => "host function";
    }
}
