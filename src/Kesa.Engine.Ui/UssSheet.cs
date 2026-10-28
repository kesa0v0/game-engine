using System.Collections.Generic;

namespace Kesa.Engine.Ui
{
    /// <summary>읽은 USS 파일 하나 (G359·G360). 값은 글자 그대로이고 숫자·색으로 바꾸는 것은 호스트 몫이다(결정론 분석기).</summary>
    public sealed class UssSheet
    {
        public UssSheet(string name, IReadOnlyList<UssRule> rules)
        {
            Name = name;
            Rules = rules;
        }

        /// <summary>파일의 가상 경로. 오류의 위치에 쓴다.</summary>
        public string Name { get; }

        public IReadOnlyList<UssRule> Rules { get; }
    }

    /// <summary>규칙 하나: 쉼표로 나열한 선택자들과 선언들.</summary>
    public sealed class UssRule
    {
        public UssRule(int line, IReadOnlyList<UssSelector> selectors, IReadOnlyList<UssDeclaration> declarations)
        {
            Line = line;
            Selectors = selectors;
            Declarations = declarations;
        }

        public int Line { get; }

        public IReadOnlyList<UssSelector> Selectors { get; }

        public IReadOnlyList<UssDeclaration> Declarations { get; }
    }

    /// <summary>쉼표 사이의 선택자 하나: 조각을 왼쪽부터, 그리고 우선순위.</summary>
    public sealed class UssSelector
    {
        public UssSelector(IReadOnlyList<UssCompound> compounds, int specificity)
        {
            Compounds = compounds;
            Specificity = specificity;
        }

        public IReadOnlyList<UssCompound> Compounds { get; }

        /// <summary>Unity 임포터와 같은 셈: 1 + 이름 100 + 클래스·의사 클래스 10 + 타입 1.</summary>
        public int Specificity { get; }
    }

    /// <summary>띄어쓰기나 <c>&gt;</c> 사이의 조각 하나(<c>Button.big:hover</c>).</summary>
    public sealed class UssCompound
    {
        public UssCompound(IReadOnlyList<UssSelectorPart> parts, UssCombinator next)
        {
            Parts = parts;
            Next = next;
        }

        public IReadOnlyList<UssSelectorPart> Parts { get; }

        /// <summary>오른쪽 조각과의 관계. 마지막 조각은 <see cref="UssCombinator.None"/>.</summary>
        public UssCombinator Next { get; }
    }

    public enum UssCombinator
    {
        None,

        /// <summary>띄어쓰기: 오른쪽이 안 어딘가에 있다.</summary>
        Descendant,

        /// <summary><c>&gt;</c>: 오른쪽이 바로 아래 자식이다.</summary>
        Child,
    }

    public readonly struct UssSelectorPart
    {
        public UssSelectorPart(UssSelectorPartKind kind, string value)
        {
            Kind = kind;
            Value = value;
        }

        public UssSelectorPartKind Kind { get; }

        /// <summary><c>.</c>·<c>#</c>·<c>:</c>를 뗀 이름. 와일드카드는 <c>*</c>.</summary>
        public string Value { get; }

        public override string ToString() => Kind switch
        {
            UssSelectorPartKind.Class => "." + Value,
            UssSelectorPartKind.Id => "#" + Value,
            UssSelectorPartKind.PseudoClass => ":" + Value,
            _ => Value,
        };
    }

    public enum UssSelectorPartKind
    {
        Wildcard,
        Type,
        Class,
        Id,
        PseudoClass,
    }

    /// <summary>선언 하나(<c>color: red;</c>). <c>--</c>로 시작하는 이름은 사용자 변수다.</summary>
    public sealed class UssDeclaration
    {
        public UssDeclaration(int line, string name, IReadOnlyList<UssValue> values)
        {
            Line = line;
            Name = name;
            Values = values;
        }

        public int Line { get; }

        public string Name { get; }

        /// <summary>띄어쓰기로 나뉜 값들, 맨 위 쉼표는 <see cref="UssValueKind.Comma"/>로 끼어 있다.</summary>
        public IReadOnlyList<UssValue> Values { get; }
    }

    public enum UssValueKind
    {
        /// <summary>낱말 그대로: <c>10px</c>·<c>#fff</c>·<c>auto</c>·<c>--accent</c>.</summary>
        Word,

        /// <summary>따옴표 안의 글자(따옴표는 뗌).</summary>
        String,

        /// <summary><c>이름(인자…)</c>: 인자는 <see cref="UssValue.Arguments"/>에 쉼표까지 끼어 있다.</summary>
        Function,

        Comma,
    }

    public sealed class UssValue
    {
        private static readonly IReadOnlyList<UssValue> None = new UssValue[0];

        public UssValue(UssValueKind kind, string text, IReadOnlyList<UssValue>? arguments = null)
        {
            Kind = kind;
            Text = text;
            Arguments = arguments ?? None;
        }

        public UssValueKind Kind { get; }

        /// <summary>낱말, 글자, 함수 이름. 쉼표는 <c>,</c>.</summary>
        public string Text { get; }

        public IReadOnlyList<UssValue> Arguments { get; }

        public override string ToString() => Kind switch
        {
            UssValueKind.String => "\"" + Text + "\"",
            UssValueKind.Function => Text + "(" + string.Join(" ", Arguments) + ")",
            _ => Text,
        };
    }

    /// <summary>USS 읽기 결과. 문제가 있어도 읽은 규칙은 담긴다(잘못된 규칙·선언만 빠진다).</summary>
    public sealed class UssReadResult
    {
        public UssReadResult(UssSheet sheet, IReadOnlyList<UiProblem> problems)
        {
            Sheet = sheet;
            Problems = problems;
        }

        public UssSheet Sheet { get; }

        public IReadOnlyList<UiProblem> Problems { get; }
    }
}
