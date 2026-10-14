using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 순수 함수의 연산 규칙 (spec P2, spec-gaps G95·G96). <c>int</c>끼리는 <c>int</c>, 하나라도 <c>fixed</c>면 <c>fixed</c>로 올리고,
    /// <c>/</c>는 늘 <c>fixed</c>다. 오버플로와 0 나눗셈은 오류다.
    /// </summary>
    internal static class PureOps
    {
        public static ScriptRuntimeException TypeError(string message, string? hint = null) =>
            new ScriptRuntimeException(Codes.RunScript, message, hint);

        public static ScriptRuntimeException ArithmeticError(string message) =>
            new ScriptRuntimeException(Codes.RunArithmetic, message);

        public static bool IsNumber(Value v) => v is IntValue || v is FixedValue;

        public static Fixed ToFixed(Value v)
        {
            if (v is FixedValue f) return f.Value;
            if (v is IntValue i)
            {
                if (FixedMath.TryFromInt(i.Value, out var promoted)) return promoted;
                throw ArithmeticError("integer " + i.Value + " is too large to mix with a fixed value");
            }
            throw TypeError("expected a number, not " + v.KindName);
        }

        /// <summary>
        /// 파이썬의 참/거짓 판정 (spec-gaps G104): <c>None</c>, <c>False</c>, 0, 0.0, 빈 문자열·리스트·맵·<c>range</c>는 거짓이고
        /// 그 밖(엔티티 Ref와 호스트 값 포함)은 참이다.
        /// </summary>
        public static bool Truthy(Value v)
        {
            switch (v)
            {
                case BoolValue b: return b.Value;
                case NoneValue _: return false;
                case IntValue i: return i.Value != 0;
                case FixedValue f: return f.Value.Raw != 0;
                case StrValue s: return s.Value.Length > 0;
                case ListValue l: return l.Items.Count > 0;
                case DictValue d: return d.Items.Count > 0;
                case RangeValue r: return r.Count > 0;
            }
            return true;
        }

        // ---- 산술 ----

        public static Value Arithmetic(string op, Value left, Value right)
        {
            if (op == "+")
            {
                if (left is StrValue ls && right is StrValue rs) return new StrValue(ls.Value + rs.Value);
                if (left is ListValue ll && right is ListValue rl)
                {
                    var items = new List<Value>(ll.Items.Count + rl.Items.Count);
                    items.AddRange(ll.Items);
                    items.AddRange(rl.Items);
                    return new ListValue(items);
                }
            }

            if (!IsNumber(left) || !IsNumber(right))
            {
                throw TypeError("unsupported operand types for '" + op + "': " + left.KindName + " and " + right.KindName);
            }

            if (op == "/") return Divide(left, right);

            if (left is IntValue a && right is IntValue b) return IntArithmetic(op, a.Value, b.Value);
            return FixedArithmetic(op, ToFixed(left), ToFixed(right));
        }

        private static Value Divide(Value left, Value right)
        {
            var a = ToFixed(left);
            var b = ToFixed(right);
            if (b.Raw == 0) throw ArithmeticError("division by zero");
            if (!FixedMath.TryDivide(a, b, out var result)) throw ArithmeticError("fixed overflow in '/'");
            return new FixedValue(result);
        }

        private static Value IntArithmetic(string op, long a, long b)
        {
            long result;
            bool ok;
            switch (op)
            {
                case "+": ok = FixedMath.TryAdd(a, b, out result); break;
                case "-": ok = FixedMath.TrySubtract(a, b, out result); break;
                case "*": ok = FixedMath.TryMultiply(a, b, out result); break;
                case "//":
                    if (b == 0) throw ArithmeticError("division by zero");
                    ok = FixedMath.TryFloorDivide(a, b, out result);
                    break;
                case "%":
                    if (b == 0) throw ArithmeticError("modulo by zero");
                    ok = FixedMath.TryFloorModulo(a, b, out result);
                    break;
                default: throw TypeError("operator '" + op + "' is not supported");
            }
            if (!ok) throw ArithmeticError("integer overflow in '" + op + "'");
            return new IntValue(result);
        }

        private static Value FixedArithmetic(string op, Fixed a, Fixed b)
        {
            Fixed result;
            bool ok;
            switch (op)
            {
                case "+": ok = FixedMath.TryAdd(a, b, out result); break;
                case "-": ok = FixedMath.TrySubtract(a, b, out result); break;
                case "*": ok = FixedMath.TryMultiply(a, b, out result); break;
                case "//":
                    if (b.Raw == 0) throw ArithmeticError("division by zero");
                    ok = FixedMath.TryFloorDivide(a, b, out result);
                    break;
                case "%":
                    if (b.Raw == 0) throw ArithmeticError("modulo by zero");
                    ok = FixedMath.TryFloorModulo(a, b, out result);
                    break;
                default: throw TypeError("operator '" + op + "' is not supported");
            }
            if (!ok) throw ArithmeticError("fixed overflow in '" + op + "'");
            return new FixedValue(result);
        }

        public static Value Negate(Value operand)
        {
            if (operand is IntValue i)
            {
                if (!FixedMath.TryNegate(i.Value, out long r)) throw ArithmeticError("integer overflow in unary '-'");
                return new IntValue(r);
            }
            if (operand is FixedValue f)
            {
                if (!FixedMath.TryNegate(f.Value, out var r)) throw ArithmeticError("fixed overflow in unary '-'");
                return new FixedValue(r);
            }
            throw TypeError("unary '-' needs a number, not " + operand.KindName);
        }

        public static Value Plus(Value operand)
        {
            if (IsNumber(operand)) return operand;
            throw TypeError("unary '+' needs a number, not " + operand.KindName);
        }

        // ---- 비교 ----

        public static int CompareNumbers(Value left, Value right)
        {
            if (left is IntValue a && right is IntValue b) return a.Value.CompareTo(b.Value);
            if (left is FixedValue fa && right is FixedValue fb) return FixedMath.Compare(fa.Value, fb.Value);
            if (left is IntValue ia && right is FixedValue fb2) return FixedMath.Compare(ia.Value, fb2.Value);
            if (left is FixedValue fa2 && right is IntValue ib) return -FixedMath.Compare(ib.Value, fa2.Value);
            throw TypeError("cannot compare " + left.KindName + " and " + right.KindName);
        }

        public static BoolValue Ordering(string op, Value left, Value right)
        {
            int c;
            if (IsNumber(left) && IsNumber(right)) c = CompareNumbers(left, right);
            else if (left is StrValue ls && right is StrValue rs) c = string.CompareOrdinal(ls.Value, rs.Value);
            else throw TypeError("'" + op + "' is not supported between " + left.KindName + " and " + right.KindName);

            bool result;
            switch (op)
            {
                case "<": result = c < 0; break;
                case "<=": result = c <= 0; break;
                case ">": result = c > 0; break;
                default: result = c >= 0; break;
            }
            return result ? BoolValue.True : BoolValue.False;
        }

        public static bool ValuesEqual(Value left, Value right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (IsNumber(left) && IsNumber(right)) return CompareNumbers(left, right) == 0;
            switch (left)
            {
                case BoolValue lb: return right is BoolValue rb && lb.Value == rb.Value;
                case StrValue ls: return right is StrValue rs && string.Equals(ls.Value, rs.Value, StringComparison.Ordinal);
                case NoneValue _: return right is NoneValue;
                case ListValue ll:
                    if (!(right is ListValue rl) || ll.Items.Count != rl.Items.Count) return false;
                    for (int i = 0; i < ll.Items.Count; i++)
                        if (!ValuesEqual(ll.Items[i], rl.Items[i])) return false;
                    return true;
                case DictValue ld:
                    if (!(right is DictValue rd) || ld.Items.Count != rd.Items.Count) return false;
                    foreach (var item in ld.Items)
                    {
                        var other = FindKey(rd, item.Key);
                        if (other == null || !ValuesEqual(item.Value, other)) return false;
                    }
                    return true;
                case IValueEquality eq: return eq.ValueEquals(right);
            }
            return false;
        }

        public static Value? FindKey(DictValue dict, Value key)
        {
            foreach (var item in dict.Items)
                if (ValuesEqual(item.Key, key)) return item.Value;
            return null;
        }

        public static bool Contains(Value container, Value item)
        {
            switch (container)
            {
                case ListValue list:
                    foreach (var x in list.Items)
                        if (ValuesEqual(x, item)) return true;
                    return false;
                case DictValue dict: return FindKey(dict, item) != null;
                case StrValue s:
                    if (item is StrValue sub) return s.Value.IndexOf(sub.Value, StringComparison.Ordinal) >= 0;
                    throw TypeError("'in <str>' needs a str on the left, not " + item.KindName);
                case RangeValue range:
                    if (!(item is IntValue n)) return false;
                    if (!FixedMath.TrySubtract(n.Value, range.Start, out long diff)) return false;
                    if (diff % range.Step != 0) return false;
                    long index = diff / range.Step;
                    return index >= 0 && index < range.Count;
            }
            throw TypeError("argument of type " + container.KindName + " is not iterable");
        }
    }
}
