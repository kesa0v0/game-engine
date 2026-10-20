using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
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
            bool runBody = false) => EvaluateRequires(decl, checkedArgs, runBody, out _);

        /// <param name="suspended">본문이 <c>ask</c>에서 멈췄으면 그 실행 (spec X1).</param>
        private PreconditionReport EvaluateRequires(CommandDecl decl, SortedDictionary<string, Value> checkedArgs,
            bool runBody, out PureEvaluator.Suspended? suspended)
        {
            suspended = null;
            string command = decl.Id;
            var scriptArgs = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var pair in checkedArgs) scriptArgs[pair.Key] = ToScript(pair.Value);

            bool outermost = _meter == null;
            if (outermost) _meter = new StepMeter(_actionStepBudget); // 사전 검사만 할 때도 제출과 같은 예산 (G224)
            try
            {
                var raw = runBody
                    ? Evaluator.RunCommand(decl.Function, scriptArgs, out suspended, decl.Span, _meter, () => StartStream(command))
                    : Evaluator.RunPreconditions(decl.Function, scriptArgs, decl.Span, _meter);
                var requires = new List<RequireOutcome>(raw.Count);
                foreach (var r in raw)
                {
                    var plain = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                    foreach (var pair in r.Args)
                        plain[pair.Key] = ToPlain("argument '" + pair.Key + "' of require()", pair.Value);
                    requires.Add(r.WithArgs(plain));
                }
                return new PreconditionReport(command, requires);
            }
            catch (ScriptRuntimeException e)
            {
                throw ToWorldException(e, (runBody ? "the command " : "the preconditions of ") + command);
            }
            finally
            {
                if (outermost && CloseActionMeter() is var heavy && heavy > 0)
                    WarnHeavy(heavy, (runBody ? "the command " : "the preconditions of ") + command, decl.Span);
            }
        }

        // ---- 인자 검사 (G115) ----

        /// <summary>
        /// 인자를 매개변수 타입과 대조한다. 문제는 모두 <paramref name="rejections"/>에 모은다 (매개변수 순서, 남는 인자는 이름순).
        /// 돌려주는 맵은 맞춘 값이다: <c>fixed</c> 자리의 <c>int</c>는 올리고 (G95), 빠진 인자는 기본값으로 채운다 (G116).
        /// 커맨드 로그에 남길 전체 인자가 이것이다.
        /// </summary>
        /// <param name="partial">참이면 빠진 인자(기본값도 없는)를 거절하지 않고 비워 둔다 (후보 나열, G184).</param>
        private SortedDictionary<string, Value> CheckArguments(CommandDecl decl, IReadOnlyDictionary<string, Value> args,
            List<ArgumentRejection> rejections, bool partial = false)
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
                    if (partial) continue;
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
                    // 보는 쪽으로 도는 중(UI 동작, G278)이면 숨긴 엔티티는 죽은 것과 똑같이 응답한다 (G198)
                    if (!_entities.TryGetValue(entity.Id.Value, out var record) || HiddenFromViewer(entity.Id.Value))
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
                        string? hint = Loading.DefIds.Hint(id.Value, ids);
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
