using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>내장 함수의 인자를 나누고 검사한다. 오류는 <see cref="Codes.EvlArguments"/>/<see cref="Codes.EvlType"/>.</summary>
    public sealed class ArgReader
    {
        private readonly CallContext _context;
        private readonly string _callee;
        private readonly SourceSpan _span;
        private readonly List<ArgValue> _positional = new List<ArgValue>();
        private readonly List<ArgValue> _keywords = new List<ArgValue>();
        private readonly HashSet<string> _taken = new HashSet<string>(StringComparer.Ordinal);

        public ArgReader(CallContext context, string callee, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            _context = context;
            _callee = callee;
            _span = span;
            foreach (var a in args)
            {
                if (a.Name == null) _positional.Add(a);
                else _keywords.Add(a);
            }
        }

        public IReadOnlyList<ArgValue> Positional => _positional;

        public void ExpectPositional(int count, string usage)
        {
            if (_positional.Count == count) return;
            var span = _positional.Count > count ? _positional[count].Span : _span;
            throw _context.Fail(Codes.EvlArguments,
                _callee + "() takes " + count + " positional argument" + (count == 1 ? "" : "s") + ", got " +
                _positional.Count, span, hint: "write " + usage);
        }

        /// <summary>이름이 있는 키워드 인자를 꺼낸다 (없으면 null).</summary>
        public ArgValue? Take(string name)
        {
            foreach (var k in _keywords)
            {
                if (k.Name != name) continue;
                _taken.Add(name);
                return k;
            }
            return null;
        }

        /// <summary>아직 꺼내지 않은 키워드 인자 (적힌 순서).</summary>
        public IEnumerable<ArgValue> Rest()
        {
            foreach (var k in _keywords)
                if (!_taken.Contains(k.Name!)) yield return k;
        }

        /// <summary>남은 키워드 인자가 있으면 오류 + "혹시 …?".</summary>
        public void RejectRest(IReadOnlyList<string> allowed)
        {
            ArgValue? first = null;
            foreach (var k in Rest())
            {
                first ??= k;
                _context.Error(Codes.EvlArguments, _callee + "() got an unexpected keyword argument '" + k.Name + "'",
                    k.Span, hint: Suggest.Hint(k.Name!, allowed));
            }
            if (first != null) throw new ScriptError();
        }

        public string String(ArgValue arg, string what)
        {
            if (arg.Value is StrValue s) return s.Value;
            throw _context.Fail(Codes.EvlType, what + " must be a string, not " + arg.Value.KindName, arg.Span);
        }

        public bool Bool(ArgValue arg, string what)
        {
            if (arg.Value is BoolValue b) return b.Value;
            throw _context.Fail(Codes.EvlType, what + " must be True or False, not " + arg.Value.KindName, arg.Span);
        }

        /// <summary>문자열 리스트. 원소마다 리터럴 위치를 함께 돌려준다.</summary>
        public List<(string Name, SourceSpan Span)> StringList(ArgValue arg, string what)
        {
            if (!(arg.Value is ListValue list))
                throw _context.Fail(Codes.EvlType, what + " must be a list of strings, not " + arg.Value.KindName, arg.Span);
            var result = new List<(string, SourceSpan)>(list.Items.Count);
            foreach (var item in list.Items)
            {
                if (!(item is StrValue s))
                    throw _context.Fail(Codes.EvlType, what + " must be a list of strings, found " + item.KindName, arg.Span);
                result.Add((s.Value, s.Origin.File.Length == 0 ? arg.Span : s.Origin));
            }
            return result;
        }

        /// <summary>문자열 인자의 위치 (리터럴이면 그 리터럴, 아니면 인자).</summary>
        public static SourceSpan SpanOf(ArgValue arg) =>
            arg.Value is StrValue s && s.Origin.File.Length != 0 ? s.Origin : arg.Span;
    }
}
