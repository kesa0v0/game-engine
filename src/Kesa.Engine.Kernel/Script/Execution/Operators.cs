namespace Kesa.Engine.Script
{
    /// <summary>
    /// 이항 연산자의 번호 (G226 ①②). 파서가 낸 문자열을 노드를 만들 때 한 번 번호로 바꾸고, 실행기와 값 연산(<see cref="PureOps"/>)은 번호로 고른다.
    /// 오류 문구에는 <see cref="Operators.Symbol(BinaryOp)"/>로 원래 글자를 쓴다.
    /// </summary>
    internal enum BinaryOp
    {
        /// <summary>순수 함수 실행기가 지원하지 않는 연산자 (비트 연산 등, G96).</summary>
        Unsupported,
        Add,
        Subtract,
        Multiply,
        Divide,
        FloorDivide,
        Modulo,
        Equal,
        NotEqual,
        Less,
        LessEqual,
        Greater,
        GreaterEqual,
        In,
        NotIn,
        Is,
        IsNot,
        And,
        Or,
    }

    internal enum UnaryOp
    {
        Unsupported,
        Negate,
        Plus,
        Not,
    }

    internal static class Operators
    {
        public static BinaryOp Binary(string op)
        {
            switch (op)
            {
                case "+": return BinaryOp.Add;
                case "-": return BinaryOp.Subtract;
                case "*": return BinaryOp.Multiply;
                case "/": return BinaryOp.Divide;
                case "//": return BinaryOp.FloorDivide;
                case "%": return BinaryOp.Modulo;
                case "==": return BinaryOp.Equal;
                case "!=": return BinaryOp.NotEqual;
                case "<": return BinaryOp.Less;
                case "<=": return BinaryOp.LessEqual;
                case ">": return BinaryOp.Greater;
                case ">=": return BinaryOp.GreaterEqual;
                case "in": return BinaryOp.In;
                case "not in": return BinaryOp.NotIn;
                case "is": return BinaryOp.Is;
                case "is not": return BinaryOp.IsNot;
                case "and": return BinaryOp.And;
                case "or": return BinaryOp.Or;
            }
            return BinaryOp.Unsupported;
        }

        /// <summary>복합 대입(<c>+=</c> 등)의 연산자. <c>=</c>는 <see cref="BinaryOp.Unsupported"/>다.</summary>
        public static BinaryOp Compound(string assignOp) =>
            assignOp.Length >= 2 && assignOp[assignOp.Length - 1] == '='
                ? Binary(assignOp.Substring(0, assignOp.Length - 1))
                : BinaryOp.Unsupported;

        public static UnaryOp Unary(string op)
        {
            switch (op)
            {
                case "-": return UnaryOp.Negate;
                case "+": return UnaryOp.Plus;
                case "not": return UnaryOp.Not;
            }
            return UnaryOp.Unsupported;
        }

        /// <summary>오류 문구에 쓰는 원래 글자.</summary>
        public static string Symbol(BinaryOp op)
        {
            switch (op)
            {
                case BinaryOp.Add: return "+";
                case BinaryOp.Subtract: return "-";
                case BinaryOp.Multiply: return "*";
                case BinaryOp.Divide: return "/";
                case BinaryOp.FloorDivide: return "//";
                case BinaryOp.Modulo: return "%";
                case BinaryOp.Equal: return "==";
                case BinaryOp.NotEqual: return "!=";
                case BinaryOp.Less: return "<";
                case BinaryOp.LessEqual: return "<=";
                case BinaryOp.Greater: return ">";
                case BinaryOp.GreaterEqual: return ">=";
                case BinaryOp.In: return "in";
                case BinaryOp.NotIn: return "not in";
                case BinaryOp.Is: return "is";
                case BinaryOp.IsNot: return "is not";
                case BinaryOp.And: return "and";
                case BinaryOp.Or: return "or";
            }
            return "?";
        }
    }
}
