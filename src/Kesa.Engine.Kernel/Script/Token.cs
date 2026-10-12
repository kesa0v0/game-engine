using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    public enum TokenKind
    {
        Name,
        Int,
        Fixed,
        String,
        Op,
        Newline,
        Indent,
        Dedent,
        EndOfFile,
    }

    public sealed class Token
    {
        public Token(TokenKind kind, string text, SourceSpan span, long intValue = 0, string? stringValue = null)
        {
            Kind = kind;
            Text = text;
            Span = span;
            IntValue = intValue;
            StringValue = stringValue;
        }

        public TokenKind Kind { get; }

        /// <summary>원문 그대로의 토큰 텍스트 (연산자는 기호, 이름은 식별자).</summary>
        public string Text { get; }

        public SourceSpan Span { get; }

        /// <summary>Int: 값, Fixed: Raw(10^6 배).</summary>
        public long IntValue { get; }

        /// <summary>String: 이스케이프를 푼 값.</summary>
        public string? StringValue { get; }

        public bool IsOp(string op) => Kind == TokenKind.Op && Text == op;

        public bool IsName(string name) => Kind == TokenKind.Name && Text == name;

        public override string ToString() => Kind + "(" + Text + ")@" + Span;
    }
}
