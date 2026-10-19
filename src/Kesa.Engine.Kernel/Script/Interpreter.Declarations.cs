using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>데코레이터 선언: <c>@on</c>·<c>@when</c>·<c>@command</c>·정책 함수의 모양 검사와 등록 (G109·G130·G145·G169·G197).</summary>
    public sealed partial class Interpreter
    {
        public const string CommandName = "command";
        public const string SubmitPolicyName = "submit_policy";
        public const string ViewPolicyName = "view_policy";
        public const string ActsForName = "acts_for";

        /// <summary>S5의 <c>on_stale=</c>. 이름만 예약했다: 지금은 기본 취소만 있다 (spec-gaps G166).</summary>
        public const string OnStaleName = "on_stale";

        private void OnStaleNotSupported(string decorator, Argument arg) =>
            _context.Error(Codes.CmdBadDecorator, decorator + "(on_stale=...) is not supported yet: a paused flow whose subject is " +
                                                  "destroyed is always canceled", arg.Span,
                hint: "to keep going when an entity is destroyed, leave it out of about= on wait() or ask() (spec-gaps G165, G166)");
        public const string OnName = "on";

        /// <summary>
        /// <c>@on(Damaged)</c> (spec S5, spec-gaps G145). 인자는 신호 하나이고, 함수의 매개변수는 신호의 인자 중 필요한 것을 이름으로
        /// 고른다(기본값·타입 표기 없음: 타입은 신호 선언이 정한다).
        /// </summary>
        private void DeclareHandler(FunctionDef def, FunctionValue function, Expr decorator)
        {
            const string usage = "write @on(SignalName) with a signal declared by signal(\"SignalName\", ...) in a schema file";
            if (!(decorator is CallExpr call) || call.Arguments.Count == 0)
            {
                _context.Error(Codes.CmdBadDecorator, "@on needs the signal it handles", decorator.Span, hint: usage);
                return;
            }
            string? stage = null;
            var stageSpan = SourceSpan.None;
            int positional = 0;
            foreach (var arg in call.Arguments)
            {
                if (arg.Name == null)
                {
                    positional++;
                    continue;
                }
                if (arg.Name == "stage")
                {
                    // 단계 id는 문자열이다. 있는지·보이는지는 스키마가 정해진 뒤 로더가 본다 (spec-gaps G152·G153)
                    if (!(Evaluate(arg.Value) is StrValue s))
                    {
                        _context.Error(Codes.CmdBadDecorator, "stage= takes a stage id string, like stage=\"core.economy\"", arg.Span,
                            hint: "stages are declared with stage(\"mod.name\", after=[...]) in a schema file (spec-gaps G152)");
                        return;
                    }
                    stage = s.Value;
                    stageSpan = arg.Value.Span;
                    continue;
                }
                if (arg.Name == "acts_for")
                {
                    // 핸들러는 제출자가 없어 입구 검사가 없다. 누구인지는 신호 인자에서 꺼낸다 (spec-gaps G149)
                    _context.Error(Codes.CmdBadDecorator, "@on has no acts_for: nobody submits a handler, so there is no gate to check",
                        arg.Span, hint: "take whom it is about from the signal's values, like def f(target): ... (spec-gaps G149)");
                    return;
                }
                if (arg.Name == OnStaleName)
                {
                    OnStaleNotSupported("@on", arg);
                    return;
                }
                _context.Error(Codes.CmdBadDecorator, "@on has no argument '" + arg.Name + "'", arg.Span,
                    hint: Suggest.Hint(arg.Name, new[] { "stage" }) ?? usage);
                return;
            }
            if (positional != 1 || call.Arguments[0].Name != null)
            {
                _context.Error(Codes.CmdBadDecorator, positional == 0 ? "@on needs the signal it handles" : "@on takes one signal",
                    positional == 0 ? decorator.Span : call.Arguments[1].Span, hint: usage);
                return;
            }
            var value = Evaluate(call.Arguments[0].Value);
            if (!(value is Loading.DefBuiltins.SignalName signal))
            {
                _context.Error(Codes.CmdBadDecorator, "@on needs a signal, not " + value.KindName, call.Arguments[0].Span, hint: usage);
                return;
            }

            bool ok = true;
            foreach (var p in def.Parameters)
            {
                if (!signal.Signal.TryGetField(p.Name, out _))
                {
                    var names = new List<string>();
                    foreach (var f in signal.Signal.Fields) names.Add(f.Name);
                    _context.Error(Codes.CmdBadDecorator, "signal '" + signal.Signal.Name + "' has no value '" + p.Name + "'", p.Span,
                        hint: Suggest.Hint(p.Name, names) ??
                              "a handler's parameters pick values of the signal by name: " + string.Join(", ", names));
                    ok = false;
                }
                else if (p.Default != null || p.Annotation != null)
                {
                    _context.Error(Codes.CmdBadDecorator,
                        "a handler's parameter takes its type from the signal, so it has no " +
                        (p.Default != null ? "default" : "type annotation"), p.Span,
                        hint: "write just the name: def " + def.Name + "(" + p.Name + "): ... (spec-gaps G145)");
                    ok = false;
                }
            }
            if (ok)
                _context.Functions.Handlers.Add(new HandlerDecl(_context.Mod, function, signal.Signal, decorator.Span)
                {
                    Stage = stage,
                    StageSpan = stageSpan,
                });
        }

        public const string WhenName = "when";

        /// <summary>
        /// <c>@when(조건)</c> (spec S5, spec-gaps G169). 조건은 이름 붙은 함수나 람다이고 본문 함수와 매개변수 수가 같다. 본문은
        /// 매개변수가 없거나(전역 조건) <c>Ref[Def타입]</c> 하나다(그 타입의 엔티티마다). 타입은 스키마가 정해진 뒤 로더가 푼다.
        /// </summary>
        private void DeclareWhen(FunctionDef def, FunctionValue function, Expr decorator)
        {
            const string usage = "write @when(lambda u: u.Health.current <= 0) above def f(u: Ref[Unit]): ..., or @when(cond) above def f(): ...";
            if (!(decorator is CallExpr call) || call.Arguments.Count == 0)
            {
                _context.Error(Codes.CmdBadDecorator, "@when needs the condition it watches", decorator.Span, hint: usage);
                return;
            }
            string? stage = null;
            var stageSpan = SourceSpan.None;
            bool repeat = false;
            long? cooldown = null;
            var once = WhenOnce.None;
            Argument? onceArg = null;
            Argument? condition = null;
            foreach (var arg in call.Arguments)
            {
                if (arg.Name == null)
                {
                    if (condition != null)
                    {
                        _context.Error(Codes.CmdBadDecorator, "@when takes one condition", arg.Span, hint: usage);
                        return;
                    }
                    condition = arg;
                    continue;
                }
                if (arg.Name == "stage")
                {
                    if (!(Evaluate(arg.Value) is StrValue s))
                    {
                        _context.Error(Codes.CmdBadDecorator, "stage= takes a stage id string, like stage=\"core.economy\"", arg.Span,
                            hint: "stages are declared with stage(\"mod.name\", after=[...]) in a schema file (spec-gaps G152)");
                        return;
                    }
                    stage = s.Value;
                    stageSpan = arg.Value.Span;
                    continue;
                }
                if (arg.Name == "repeat")
                {
                    if (!(arg.Value is BoolLiteral literal))
                    {
                        _context.Error(Codes.CmdBadDecorator, "repeat= must be True or False", arg.Value.Span,
                            hint: "repeat=True runs the reaction once each tick while its condition holds (spec-gaps G173)");
                        return;
                    }
                    repeat = literal.Value;
                    continue;
                }
                if (arg.Name == "cooldown")
                {
                    if (!(arg.Value is IntLiteral ticks) || ticks.Value < 1)
                    {
                        _context.Error(Codes.CmdBadDecorator, "cooldown= takes a number of ticks of at least 1, like cooldown=50", arg.Value.Span,
                            hint: "after it runs, the reaction waits that many ticks before it can run again for the same entity (spec-gaps G174)");
                        return;
                    }
                    cooldown = ticks.Value;
                    continue;
                }
                if (arg.Name == "once")
                {
                    if (arg.Value is BoolLiteral b) once = b.Value ? WhenOnce.PerEntity : WhenOnce.None;
                    else if (arg.Value is StringLiteral s && s.Value == "game") once = WhenOnce.Game;
                    else
                    {
                        _context.Error(Codes.CmdBadDecorator, "once= must be True or \"game\"", arg.Value.Span,
                            hint: "once=True runs once for each entity; once=\"game\" runs once in the whole game (spec-gaps G175)");
                        return;
                    }
                    onceArg = arg;
                    continue;
                }
                if (arg.Name == "acts_for")
                {
                    _context.Error(Codes.CmdBadDecorator, "@when has no acts_for: nobody submits a reaction, so there is no gate to check",
                        arg.Span, hint: "the entity it is about is its parameter (spec-gaps G149, G169)");
                    return;
                }
                if (arg.Name == OnStaleName)
                {
                    OnStaleNotSupported("@when", arg);
                    return;
                }
                _context.Error(Codes.CmdBadDecorator, "@when has no argument '" + arg.Name + "'", arg.Span,
                    hint: Suggest.Hint(arg.Name, new[] { "stage", "repeat", "cooldown", "once" }) ?? usage);
                return;
            }
            if (once != WhenOnce.None && (repeat || cooldown != null))
            {
                _context.Error(Codes.CmdBadDecorator,
                    "once= cannot go with " + (repeat ? "repeat=True" : "cooldown=") + ": a reaction that runs once never runs again",
                    onceArg!.Span, hint: "keep only one of them (spec-gaps G175)");
                return;
            }
            if (condition == null)
            {
                _context.Error(Codes.CmdBadDecorator, "@when needs the condition it watches", decorator.Span, hint: usage);
                return;
            }
            var value = Evaluate(condition.Value);
            if (!(value is FunctionValue cond))
            {
                _context.Error(Codes.CmdBadDecorator, "@when needs a function or a lambda as its condition, not " + value.KindName,
                    condition.Span, hint: usage);
                return;
            }
            if (cond.IsEntry)
            {
                _context.Error(Codes.CmdBadDecorator, "the condition of @when is '" + cond.Name + "', which is a " + cond.EntryKind,
                    condition.Span, hint: "a condition only reads; use a plain function (spec-gaps G118)");
                return;
            }

            bool ok = true;
            if (def.Parameters.Count > 1)
            {
                _context.Error(Codes.CmdBadDecorator, "a @when reaction takes no parameter or one entity, not " + def.Parameters.Count,
                    def.Parameters[1].Span, hint: "write def " + def.Name + "(u: Ref[Unit]): ... to run once for each Unit (spec-gaps G169)");
                ok = false;
            }
            foreach (var p in def.Parameters)
            {
                if (p.Default != null)
                {
                    _context.Error(Codes.CmdBadDecorator, "the parameter of a @when reaction has no default", p.Span,
                        hint: "write def " + def.Name + "(" + p.Name + ": Ref[Unit]): ... (spec-gaps G169)");
                    ok = false;
                }
                else if (p.Annotation == null)
                {
                    _context.Error(Codes.CmdBadDecorator, "the parameter of a @when reaction needs its entity type", p.Span,
                        hint: "write def " + def.Name + "(" + p.Name + ": Ref[Unit]): ... (spec-gaps G169)");
                    ok = false;
                }
            }
            if (ok && cond.Parameters.Count != def.Parameters.Count)
            {
                _context.Error(Codes.CmdBadDecorator,
                    "the condition takes " + cond.Parameters.Count + " parameter" + (cond.Parameters.Count == 1 ? "" : "s") +
                    ", but the reaction '" + def.Name + "' takes " + def.Parameters.Count, condition.Span,
                    hint: def.Parameters.Count == 0
                        ? "a reaction with no parameter watches one condition for the whole world: write @when(lambda: ...) (spec-gaps G169)"
                        : "the condition gets the same entity as the reaction: write @when(lambda " + def.Parameters[0].Name + ": ...) (spec-gaps G169)");
                ok = false;
            }
            if (ok)
                _context.Functions.Whens.Add(new WhenDecl(_context.Mod, function, cond, decorator.Span)
                {
                    Stage = stage,
                    StageSpan = stageSpan,
                    Repeat = repeat,
                    Cooldown = cooldown,
                    Once = once,
                });
        }

        /// <summary>
        /// <c>def</c> 위의 데코레이터. <c>@command</c>(<c>free=</c>, <c>acts_for=</c>)와 <c>@submit_policy</c>(<c>override=</c>)를
        /// 받는다 (spec S6, spec-gaps G109·G122~G124·G130). 커맨드 id는 <c>모드id.함수이름</c>이다.
        /// </summary>
        private void DeclareCommand(FunctionDef def, FunctionValue function)
        {
            var decorator = def.Decorators[0];
            if (def.Decorators.Count > 1)
            {
                _context.Error(Codes.CmdBadDecorator, "a function can have only one decorator", def.Decorators[1].Span);
                return;
            }

            string? name = def.DecoratorName;
            if ((name == CommandName || name == SubmitPolicyName || name == ViewPolicyName || name == OnName || name == WhenName) &&
                !IsBuiltin(name))
            {
                bool schema = _scope.IsSchemaName(name);
                _context.Error(Codes.CmdBadDecorator,
                    (schema ? "this mod sees a component or signal named '" : "this mod defines its own '") + name +
                    "', so the built-in @" + name + " cannot be used here",
                    decorator.Span, hint: schema
                        ? "rename that component or signal (spec-gaps G230)"
                        : "rename this mod's '" + name + "' (spec-gaps G112)");
                return;
            }

            if (name == SubmitPolicyName)
            {
                DeclarePolicy(def, function, decorator, PolicyKind.Submit);
                return;
            }
            if (name == ViewPolicyName)
            {
                DeclarePolicy(def, function, decorator, PolicyKind.View);
                return;
            }
            if (name == OnName)
            {
                DeclareHandler(def, function, decorator);
                return;
            }
            if (name == WhenName)
            {
                DeclareWhen(def, function, decorator);
                return;
            }
            if (name != CommandName)
            {
                _context.Error(Codes.CmdBadDecorator, "unknown decorator; only @command, @on, @when, @submit_policy and @view_policy are supported",
                    decorator.Span, hint: "write @command or @command(free=True, acts_for=...)");
                return;
            }

            bool free = false;
            Argument? actsFor = null;
            if (decorator is CallExpr call)
            {
                foreach (var arg in call.Arguments)
                {
                    if (arg.Name == "free")
                    {
                        if (!(arg.Value is BoolLiteral literal))
                        {
                            _context.Error(Codes.CmdBadDecorator, "free= must be True or False", arg.Value.Span);
                            return;
                        }
                        free = literal.Value;
                    }
                    else if (arg.Name == ActsForName)
                    {
                        if (!(arg.Value is LambdaExpr) && !(arg.Value is NoneLiteral))
                        {
                            _context.Error(Codes.CmdBadDecorator, "acts_for= must be a lambda or None", arg.Value.Span,
                                hint: "write acts_for=lambda party: party, or acts_for=None if the command acts for no one " +
                                      "(spec-gaps G130)");
                            return;
                        }
                        actsFor = arg;
                    }
                    else if (arg.Name == OnStaleName)
                    {
                        OnStaleNotSupported("@command", arg);
                        return;
                    }
                    else
                    {
                        _context.Error(Codes.CmdBadDecorator,
                            arg.Name == null
                                ? "@command takes no positional arguments"
                                : "@command has no argument '" + arg.Name + "'",
                            arg.Span, hint: "@command accepts free= and acts_for=");
                        return;
                    }
                }
            }

            // 모든 커맨드가 누구로서 행동하는지 적는다. 응답 커맨드는 답하는 엔티티를 인자에서 고른다 (G123·G139)
            if (actsFor == null)
            {
                _context.Error(Codes.CmdBadDecorator,
                    free ? "a free command must say whom it acts for" : "a response command must say which entity answers",
                    decorator.Span,
                    hint: free
                        ? "add acts_for=lambda party: party (a parameter of the command), or acts_for=None if it acts " +
                          "for no one (spec-gaps G123)"
                        : "add acts_for=lambda party: party (a parameter of the command) (spec-gaps G139)");
                return;
            }
            if (!free && actsFor.Value is NoneLiteral)
            {
                _context.Error(Codes.CmdBadDecorator,
                    "a response command cannot use acts_for=None; an answer always comes from an entity",
                    actsFor.Span, hint: "write acts_for=lambda party: party (spec-gaps G139)");
                return;
            }

            FunctionValue? chooser = null;
            if (actsFor != null && actsFor.Value is LambdaExpr)
                chooser = (FunctionValue)Evaluate(actsFor.Value); // 람다 본문 검사와 함수 목록 등록 (G108·G117)
            _context.Functions.Commands.Add(new CommandDecl(_context.Mod, function, free, decorator.Span, chooser));
        }

        /// <summary>
        /// <c>@submit_policy</c> / <c>@submit_policy(override=True)</c> (G122·G124): 기본값 없는 매개변수 둘 <c>(issuer, target)</c>.
        /// <c>@view_policy</c> (G197·G203): 기본값 없는 매개변수 셋 <c>(issuer, entity, component)</c>.
        /// 개수와 교체는 모든 모드를 모은 뒤 본다.
        /// </summary>
        private void DeclarePolicy(FunctionDef def, FunctionValue function, Expr decorator, PolicyKind kind)
        {
            string at = "@" + (kind == PolicyKind.Submit ? SubmitPolicyName : ViewPolicyName);
            bool isOverride = false;
            if (decorator is CallExpr call)
            {
                foreach (var arg in call.Arguments)
                {
                    if (arg.Name != "override")
                    {
                        _context.Error(Codes.CmdBadDecorator,
                            arg.Name == null
                                ? at + " takes no positional arguments"
                                : at + " has no argument '" + arg.Name + "'",
                            arg.Span, hint: at + " accepts override=True");
                        return;
                    }
                    if (!(arg.Value is BoolLiteral literal))
                    {
                        _context.Error(Codes.CmdBadDecorator, "override= must be True or False", arg.Value.Span);
                        return;
                    }
                    isOverride = literal.Value;
                }
            }

            bool shapeOk = def.Parameters.Count == (kind == PolicyKind.Submit ? 2 : 3);
            foreach (var p in def.Parameters)
                if (p.Default != null) shapeOk = false;
            if (!shapeOk)
            {
                if (kind == PolicyKind.Submit)
                    _context.Error(Codes.CmdSubmitPolicy,
                        "a submit policy takes exactly two parameters without defaults: (issuer, target)", def.Span,
                        hint: "issuer is the connection's tag, target is the entity chosen by acts_for or None (spec-gaps G122)");
                else
                    _context.Error(Codes.CmdViewPolicy,
                        "a view policy takes exactly three parameters without defaults: (issuer, entity, component)", def.Span,
                        hint: "component is a component name, or None when asking whether the entity itself is seen (spec-gaps G197)");
                return;
            }
            _context.Functions.Policies.Add(new PolicyDecl(_context.Mod, function, isOverride, decorator.Span, kind));
        }
    }
}
