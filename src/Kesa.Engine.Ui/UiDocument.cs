using System.Collections.Generic;

namespace Kesa.Engine.Ui
{
    /// <summary>읽은 UXML 파일 하나 (spec-gaps G340): 받는 이름과 앉은 엔티티를 받을 이름, 그리고 요소 트리.</summary>
    public sealed class UiDocument
    {
        public UiDocument(string name, IReadOnlyList<string> parameters, string? seatParameter, UiElement root, IReadOnlyList<string> styleSheets)
        {
            Name = name;
            Parameters = parameters;
            SeatParameter = seatParameter;
            Root = root;
            StyleSheets = styleSheets;
        }

        /// <summary><c>&lt;Style src="…"/&gt;</c>로 적힌 스타일시트 경로들, 적은 순서. 렌더러가 맨 위 요소에 붙인다.</summary>
        public IReadOnlyList<string> StyleSheets { get; }

        /// <summary>오류에 붙일 파일 이름.</summary>
        public string Name { get; }

        /// <summary><c>params</c>로 선언한 이름들, 적은 순서.</summary>
        public IReadOnlyList<string> Parameters { get; }

        /// <summary><c>seat</c>가 가리키는 매개변수. 호스트가 앉은 엔티티를 여기에 넣는다.</summary>
        public string? SeatParameter { get; }

        public UiElement Root { get; }
    }

    /// <summary>요소 속성 하나를 함수 값에 묶는 <c>bind-…</c> (G331·G333).</summary>
    public enum UiBindKind
    {
        /// <summary><c>bind-text</c>·<c>bind-display</c>·<c>bind-value</c> 등: 요소 속성 하나.</summary>
        Property,

        /// <summary><c>bind-class-&lt;이름&gt;</c>: 참거짓으로 그 클래스를 붙이고 뗀다.</summary>
        ClassToggle,

        /// <summary><c>bind-class</c>: 함수가 돌려준 글자(들)가 클래스다.</summary>
        ClassNames,
    }

    public sealed class UiBinding
    {
        public UiBinding(UiBindKind kind, string name, UiExpr expression, string source)
        {
            Kind = kind;
            Name = name;
            Expression = expression;
            Source = source;
        }

        public UiBindKind Kind { get; }

        /// <summary>요소 속성 이름(<c>text</c>)이나 토글하는 클래스 이름. <see cref="UiBindKind.ClassNames"/>는 빈 글자.</summary>
        public string Name { get; }

        public UiExpr Expression { get; }

        /// <summary>적힌 그대로의 식(오류 메시지용).</summary>
        public string Source { get; }
    }

    /// <summary><c>on-click="모드.이름(인자)"</c> (G336).</summary>
    public sealed class UiEvent
    {
        public UiEvent(string name, CallExpr call, string source)
        {
            Name = name;
            Call = call;
            Source = source;
        }

        /// <summary>사건 이름(<c>click</c>).</summary>
        public string Name { get; }

        public CallExpr Call { get; }

        public string Source { get; }
    }

    /// <summary><c>for="변수 in 식"</c> (G338): 이 요소가 항목들을 담고, 자식들이 항목 하나의 모양이다.</summary>
    public sealed class UiRepeat
    {
        public UiRepeat(string variable, UiExpr source, string text)
        {
            Variable = variable;
            Source = source;
            Text = text;
        }

        public string Variable { get; }

        public UiExpr Source { get; }

        public string Text { get; }
    }

    /// <summary><c>&lt;Include src="…" 이름="식" /&gt;</c> (G340).</summary>
    public sealed class UiInclude
    {
        public UiInclude(string source, IReadOnlyList<KeyValuePair<string, UiExpr>> arguments)
        {
            Source = source;
            Arguments = arguments;
        }

        public string Source { get; }

        public IReadOnlyList<KeyValuePair<string, UiExpr>> Arguments { get; }
    }

    /// <summary>창의 종류 (G350): 모달은 보통 창보다 늘 위이고 그 아래 입력을 막는다.</summary>
    public enum UiWindowKind
    {
        Normal,
        Modal,
    }

    /// <summary><c>window="…"</c>가 붙은 요소의 창 성질 (G348~G351·G354).</summary>
    public sealed class UiWindow
    {
        public UiWindow(UiWindowKind kind, bool closeOnEscape, bool closeOnOutside, bool raise)
        {
            Kind = kind;
            CloseOnEscape = closeOnEscape;
            CloseOnOutside = closeOnOutside;
            Raise = raise;
        }

        public UiWindowKind Kind { get; }

        /// <summary>ESC가 <c>on-close</c>를 부르나(<c>on-close</c>가 있으면 기본 참).</summary>
        public bool CloseOnEscape { get; }

        /// <summary>바깥 클릭이 <c>on-close</c>를 부르나(기본 거짓).</summary>
        public bool CloseOnOutside { get; }

        /// <summary>누르면 보통 창들 중 맨 위로 오나(기본 참).</summary>
        public bool Raise { get; }
    }

    public sealed class UiElement
    {
        public UiElement(string kind, string place, IReadOnlyList<KeyValuePair<string, string>> attributes,
            IReadOnlyList<UiBinding> bindings, IReadOnlyList<UiEvent> events, UiRepeat? repeat, UiInclude? include,
            IReadOnlyList<UiElement> children, UiWindow? window = null, bool dragHandle = false)
        {
            Kind = kind;
            Place = place;
            Attributes = attributes;
            Bindings = bindings;
            Events = events;
            Repeat = repeat;
            Include = include;
            Children = children;
            Window = window;
            DragHandle = dragHandle;
        }

        /// <summary><c>window="…"</c>가 있으면 이 요소는 창이다.</summary>
        public UiWindow? Window { get; }

        /// <summary><c>window-drag="true"</c>: 이 요소를 끌면 이 요소를 담은 창이 움직인다.</summary>
        public bool DragHandle { get; }

        /// <summary>요소 이름(<c>Label</c>·<c>Button</c> …). 접두사는 뗀다.</summary>
        public string Kind { get; }

        /// <summary>오류에 붙일 <c>파일:줄</c>.</summary>
        public string Place { get; }

        /// <summary>UXML에 직접 적은 고정 속성(<c>name</c>·<c>class</c>·<c>text</c> 등), 적은 순서.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Attributes { get; }

        public IReadOnlyList<UiBinding> Bindings { get; }

        public IReadOnlyList<UiEvent> Events { get; }

        public UiRepeat? Repeat { get; }

        public UiInclude? Include { get; }

        public IReadOnlyList<UiElement> Children { get; }
    }

    /// <summary>읽거나 검사하다 찾은 문제 하나. <see cref="Place"/>는 <c>파일:줄</c>.</summary>
    public sealed class UiProblem
    {
        public UiProblem(string place, string message)
        {
            Place = place;
            Message = message;
        }

        public string Place { get; }

        public string Message { get; }

        public override string ToString() => Place + ": " + Message;
    }

    /// <summary>UXML 읽기 결과. 문제가 있으면 <see cref="Document"/>가 null이다.</summary>
    public sealed class UiReadResult
    {
        public UiReadResult(UiDocument? document, IReadOnlyList<UiProblem> problems)
        {
            Document = document;
            Problems = problems;
        }

        public UiDocument? Document { get; }

        public IReadOnlyList<UiProblem> Problems { get; }

        public bool Succeeded => Problems.Count == 0 && Document != null;
    }
}
