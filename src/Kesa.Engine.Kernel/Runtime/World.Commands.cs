using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>제출된 인자가 매개변수 타입에 맞지 않는 이유 (spec-gaps G115).</summary>
    public enum ArgumentProblem
    {
        /// <summary>매개변수에 해당하는 인자가 없다.</summary>
        Missing,

        /// <summary>그런 이름의 매개변수가 없다.</summary>
        Unexpected,

        /// <summary>값의 종류가 다르다 (int 자리에 str 등).</summary>
        WrongType,

        /// <summary>엔티티가 이미 죽었다 (클릭과 제출 사이에 죽는 정상 상황).</summary>
        DeadEntity,

        /// <summary>살아 있지만 다른 Def 타입의 엔티티다.</summary>
        WrongEntityType,

        /// <summary>그런 id의 Def가 없다.</summary>
        UnknownDef,

        /// <summary>Def는 있지만 다른 Def 타입이다.</summary>
        WrongDefType,
    }

    /// <summary>인자 하나의 거절 (G115). <see cref="Path"/>는 컬렉션 안의 위치까지 적은 것이다 (<c>path[1]</c>, <c>goods["Iron"]</c>).</summary>
    public sealed class ArgumentRejection
    {
        public ArgumentRejection(string parameter, string path, ArgumentProblem problem, string? expected, string message)
        {
            Parameter = parameter;
            Path = path;
            Problem = problem;
            Expected = expected;
            Message = message;
        }

        public string Parameter { get; }
        public string Path { get; }
        public ArgumentProblem Problem { get; }

        /// <summary>그 자리의 타입 표기. 남는 인자면 없다.</summary>
        public string? Expected { get; }

        /// <summary>개발자용 설명 (영어). 플레이어에게 보일 문구는 <see cref="Problem"/>으로 고른다.</summary>
        public string Message { get; }

        public override string ToString() => Message;
    }

    /// <summary>커맨드 사전 검사의 결과 (spec S6·C7, spec-gaps G110·G115).</summary>
    public sealed class PreconditionReport
    {
        public PreconditionReport(string command, IReadOnlyList<RequireOutcome> requires)
            : this(command, Array.Empty<ArgumentRejection>(), requires) { }

        public PreconditionReport(string command, IReadOnlyList<ArgumentRejection> arguments,
            IReadOnlyList<RequireOutcome> requires)
        {
            Command = command;
            Arguments = arguments;
            Requires = requires;
            var failures = new List<RequireOutcome>();
            foreach (var r in requires)
                if (r.Status == RequireStatus.Failed) failures.Add(r);
            Failures = failures;
        }

        /// <summary>커맨드 id (<c>모드id.함수이름</c>, G109).</summary>
        public string Command { get; }

        /// <summary><c>require</c>마다의 결과, 적힌 순서대로 ("왜?" 도구 3).</summary>
        public IReadOnlyList<RequireOutcome> Requires { get; }

        /// <summary>실패한 <c>require</c>만 (사유 목록). 평가 안 됨은 들어 있지 않다.</summary>
        public IReadOnlyList<RequireOutcome> Failures { get; }

        /// <summary>인자 거절 (G115). 하나라도 있으면 사전 조건은 평가하지 않았으므로 <see cref="Requires"/>가 비어 있다.</summary>
        public IReadOnlyList<ArgumentRejection> Arguments { get; }

        public bool Passed => Arguments.Count == 0 && Failures.Count == 0;
    }

    /// <summary>커맨드 (spec S6). 지금은 사전 검사만 한다: 본문 실행·쓰기·롤백은 다음 단계다.</summary>
    public sealed partial class World
    {
        private readonly IReadOnlyDictionary<string, CommandDecl> _commands;

        /// <summary>
        /// 커맨드의 <c>with preconditions:</c>만 평가한다 (본문은 실행하지 않음, spec S2). 상태를 바꾸지 않는다.
        /// <paramref name="args"/>는 매개변수 이름 → 값이고 엔티티는 <see cref="EntityValue"/>로 넘긴다.
        /// 인자가 매개변수 타입에 맞지 않으면 사전 조건을 평가하지 않고 인자 거절을 돌려준다 (G115).
        /// 없는 커맨드와 스크립트 오류(앞에 실패가 없는 조건의 실행 오류 등, G110)는 <see cref="WorldException"/>이다.
        /// </summary>
        public PreconditionReport CheckPreconditions(string command, IReadOnlyDictionary<string, Value> args)
        {
            if (!_commands.TryGetValue(command, out var decl))
            {
                throw new WorldException(Codes.RunUnknownName, "there is no command '" + command + "'",
                    Suggest.Hint(command, _commands.Keys));
            }

            var rejections = new List<ArgumentRejection>();
            var checkedArgs = CheckArguments(decl, args, rejections);
            if (rejections.Count > 0) return new PreconditionReport(command, rejections, Array.Empty<RequireOutcome>());
            return EvaluateRequires(decl, checkedArgs);
        }

        /// <summary>
        /// 인자 검사를 통과한 인자로 <c>require</c>를 평가한다. <paramref name="runBody"/>면 모두 통과했을 때 본문까지 실행한다
        /// (G132, 예산은 커맨드 하나에 하나). 스크립트 오류는 <see cref="WorldException"/>이다.
        /// </summary>
        private PreconditionReport EvaluateRequires(CommandDecl decl, SortedDictionary<string, Value> checkedArgs,
            bool runBody = false)
        {
            string command = decl.Id;
            var scriptArgs = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var pair in checkedArgs) scriptArgs[pair.Key] = ToScript(pair.Value);

            bool outermost = _meter == null;
            if (outermost) _meter = new StepMeter(_stepBudget);
            try
            {
                var raw = runBody
                    ? Evaluator.RunCommand(decl.Function, scriptArgs, decl.Span, _meter)
                    : Evaluator.RunPreconditions(decl.Function, scriptArgs, decl.Span, _meter);
                var requires = new List<RequireOutcome>(raw.Count);
                foreach (var r in raw)
                {
                    var plain = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                    foreach (var pair in r.Args)
                        plain[pair.Key] = ToPlain("argument '" + pair.Key + "' of require()", pair.Value);
                    requires.Add(new RequireOutcome(r.Status, r.MessageKey, plain, r.Span));
                }
                return new PreconditionReport(command, requires);
            }
            catch (ScriptRuntimeException e)
            {
                throw ToWorldException(e, (runBody ? "the command " : "the preconditions of ") + command);
            }
            finally
            {
                if (outermost) _meter = null;
            }
        }

        // ---- 인자 검사 (G115) ----

        /// <summary>
        /// 인자를 매개변수 타입과 대조한다. 문제는 모두 <paramref name="rejections"/>에 모은다 (매개변수 순서, 남는 인자는 이름순).
        /// 돌려주는 맵은 맞춘 값이다: <c>fixed</c> 자리의 <c>int</c>는 올리고 (G95), 빠진 인자는 기본값으로 채운다 (G116).
        /// 커맨드 로그에 남길 전체 인자가 이것이다.
        /// </summary>
        private SortedDictionary<string, Value> CheckArguments(CommandDecl decl, IReadOnlyDictionary<string, Value> args,
            List<ArgumentRejection> rejections)
        {
            var result = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in decl.Parameters)
            {
                known.Add(p.Name);
                if (!args.TryGetValue(p.Name, out var value))
                {
                    // 빠진 인자는 기본값으로 채운다. 로드 때 타입에 맞춰 둔 값이다 (G116)
                    if (p.Default != null)
                    {
                        result[p.Name] = p.Default;
                        continue;
                    }
                    rejections.Add(new ArgumentRejection(p.Name, p.Name, ArgumentProblem.Missing, p.Type.Display,
                        decl.Id + " is missing the argument '" + p.Name + "' (" + p.Type.Display + ")"));
                    continue;
                }
                var fitted = Fit(p.Name, p.Name, p.Type, value, rejections);
                if (fitted != null) result[p.Name] = fitted;
            }

            var extra = new List<string>();
            foreach (var name in args.Keys)
                if (!known.Contains(name)) extra.Add(name);
            extra.Sort(StringComparer.Ordinal);
            foreach (var name in extra)
            {
                var names = new List<string>();
                foreach (var p in decl.Parameters) names.Add(p.Name);
                string? hint = Suggest.Hint(name, names);
                rejections.Add(new ArgumentRejection(name, name, ArgumentProblem.Unexpected, null,
                    decl.Id + " has no parameter '" + name + "'" + (hint == null ? "" : "; " + hint)));
            }
            return result;
        }

        /// <summary>값 하나를 타입에 맞춘다. 맞지 않으면 거절을 더하고 null.</summary>
        /// <paramref name="declared"/>는 거절에 적을 타입이다: <c>T | None</c>의 안쪽을 볼 때도 선언 그대로 보인다.
        private Value? Fit(string parameter, string path, TypeRef type, Value value, List<ArgumentRejection> rejections,
            TypeRef? declared = null)
        {
            string shown = (declared ?? type).Display;

            Value? Reject(ArgumentProblem problem, string message)
            {
                rejections.Add(new ArgumentRejection(parameter, path, problem, shown, "'" + path + "' " + message));
                return null;
            }

            Value? WrongType() => Reject(ArgumentProblem.WrongType, "must be " + shown + ", not " + value.KindName);

            switch (type)
            {
                case OptionalTypeRef optional:
                    return value is NoneValue ? value : Fit(parameter, path, optional.Inner, value, rejections, type);

                case PrimitiveTypeRef primitive:
                    if (ReferenceEquals(primitive, PrimitiveTypeRef.Fixed))
                    {
                        if (value is FixedValue) return value;
                        if (!(value is IntValue i)) return WrongType();
                        try
                        {
                            return new FixedValue(Fixed.FromRaw(checked(i.Value * Fixed.Scale)));
                        }
                        catch (OverflowException)
                        {
                            return Reject(ArgumentProblem.WrongType, "is " + i.Value.ToString(CultureInfo.InvariantCulture) +
                                                                     ", which is too large for a fixed value");
                        }
                    }
                    if (ReferenceEquals(primitive, PrimitiveTypeRef.Int))
                    {
                        if (value is IntValue) return value;
                        return value is FixedValue
                            ? Reject(ArgumentProblem.WrongType, "must be int, not fixed (a fixed value is not turned into an int)")
                            : WrongType();
                    }
                    if (ReferenceEquals(primitive, PrimitiveTypeRef.Bool)) return value is BoolValue ? value : WrongType();
                    if (ReferenceEquals(primitive, PrimitiveTypeRef.Str)) return value is StrValue ? value : WrongType();
                    return WrongType();

                case EntityRefTypeRef reference:
                    if (!(value is EntityValue entity)) return WrongType();
                    if (!_entities.TryGetValue(entity.Id.Value, out var record))
                        return Reject(ArgumentProblem.DeadEntity, "is " + entity.Id + ", which no longer exists");
                    if (record.Def.Type.Name != reference.Target.Name)
                        return Reject(ArgumentProblem.WrongEntityType,
                            "is " + entity.Id + ", a " + record.Def.Type.Name + ", not a " + reference.Target.Name);
                    return value;

                case DefIdTypeRef defId:
                    if (!(value is StrValue id)) return WrongType();
                    if (!_defs.TryGet(id.Value, out var def))
                    {
                        var ids = new List<string>();
                        foreach (var d in _defs.Defs)
                            if (d.Type.Name == defId.Target.Name) ids.Add(d.Id);
                        string? hint = Suggest.Hint(id.Value, ids);
                        return Reject(ArgumentProblem.UnknownDef,
                            "is '" + id.Value + "', which is not a Def" + (hint == null ? "" : "; " + hint));
                    }
                    if (def.Type.Name != defId.Target.Name)
                        return Reject(ArgumentProblem.WrongDefType,
                            "is '" + id.Value + "', a " + def.Type.Name + ", not a " + defId.Target.Name);
                    return value;

                case ListTypeRef list:
                {
                    if (!(value is ListValue items)) return WrongType();
                    var fitted = new List<Value>(items.Items.Count);
                    bool ok = true;
                    for (int n = 0; n < items.Items.Count; n++)
                    {
                        var item = Fit(parameter, path + "[" + n.ToString(CultureInfo.InvariantCulture) + "]", list.Element,
                            items.Items[n], rejections);
                        if (item == null) ok = false;
                        else fitted.Add(item);
                    }
                    return ok ? new ListValue(fitted) : null;
                }

                case DictTypeRef dict:
                {
                    if (!(value is DictValue map)) return WrongType();
                    var fitted = new List<DictItem>(map.Items.Count);
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    bool ok = true;
                    foreach (var item in map.Items)
                    {
                        string keyText = KeyText(item.Key);
                        var key = Fit(parameter, path + " key " + keyText, dict.Key, item.Key, rejections);
                        if (key == null)
                        {
                            ok = false;
                            continue;
                        }
                        if (!seen.Add(item.Key.KindName + ":" + keyText))
                        {
                            rejections.Add(new ArgumentRejection(parameter, path, ArgumentProblem.WrongType, type.Display,
                                "'" + path + "' has the key " + keyText + " twice"));
                            ok = false;
                            continue;
                        }
                        var entry = Fit(parameter, path + "[" + keyText + "]", dict.Value, item.Value, rejections);
                        if (entry == null) ok = false;
                        else fitted.Add(new DictItem(key, entry, item.Span));
                    }
                    return ok ? new DictValue(fitted) : null;
                }
            }
            return WrongType();
        }

        /// <summary>거절 위치에 적을 맵 키: 문자열은 따옴표, 엔티티는 id.</summary>
        private static string KeyText(Value key) =>
            key is StrValue s ? "\"" + s.Value + "\""
            : key is IntValue i ? i.Value.ToString(CultureInfo.InvariantCulture)
            : key is EntityValue e ? e.Id.ToString()
            : key.KindName;
    }
}
