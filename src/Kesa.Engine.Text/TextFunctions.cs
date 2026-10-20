using System.Collections.Generic;
using Linguini.Bundle;
using Linguini.Shared.Types.Bundle;

namespace Kesa.Engine.Text
{
    /// <summary>
    /// 엔진이 모든 언어의 번역에 넣는 함수 (spec-gaps G268). 이름이 대문자인 것은 Fluent 내장 함수(<c>NUMBER</c>)의 관례다.
    /// </summary>
    internal static class TextFunctions
    {
        public const string JosaName = "JOSA";
        public const string DefAttrName = "DEFATTR";

        public static void AddTo(FluentBundle bundle)
        {
            bundle.AddFunctionOverriding(JosaName, JosaFunction);
            bundle.AddFunctionOverriding(DefAttrName, DefAttrFunction);
        }

        /// <summary><c>JOSA($x, "을")</c> → 이름에 맞는 조사를 붙인 글.</summary>
        private static IFluentType JosaFunction(IList<IFluentType> args, IDictionary<string, IFluentType> named)
        {
            if (args.Count != 2) return new FluentErrType();
            return new FluentString(Josa.Attach(args[0].AsString(), args[1].AsString()));
        }

        /// <summary><c>DEFATTR($ship, "gender")</c> → 그 Def의 속성 글. Def가 아니거나 속성이 없으면 빈 문자열이라 기본 가지로 간다.</summary>
        private static IFluentType DefAttrFunction(IList<IFluentType> args, IDictionary<string, IFluentType> named)
        {
            if (args.Count != 2) return new FluentErrType();
            return new FluentString(args[0] is DefName def ? def.Attribute(args[1].AsString()) : "");
        }
    }
}
