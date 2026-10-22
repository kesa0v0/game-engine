using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>데코레이터 선언: <c>@on</c>·<c>@when</c>·<c>@command</c>·정책 함수의 모양 검사와 등록 (G109·G130·G145·G169·G197).</summary>
    public sealed partial class Interpreter
    {
        public const string CommandName = "command";
        public const string SubmitPolicyName = "submit_policy";
        public const string ViewPolicyName = "view_policy";
        public const string ViewRuleName = "view_rule";
        public const string ActsForName = "acts_for";
        public const string UiActionName = "ui_action";

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
            bool now = false;
            int positional = 0;
            foreach (var arg in call.Arguments)
            {
                if (arg.Name == null)
                {
                    positional++;
                    continue;
                }
                if (arg.Name == "now")
                {
                    // 즉시 핸들러: emit한 흐름 안에서 그 자리에서 돈다 (spec-gaps G310)
                    if (!(Evaluate(arg.Value) is BoolValue b))
                    {
                        _context.Error(Codes.CmdBadDecorator, "now= takes True or False", arg.Span,
                            hint: "@on(Signal, now=True) runs inside the flow that emits, right at emit() (spec-gaps G310)");
                        return;
                    }
                    now = b.Value;
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
                    hint: Suggest.Hint(arg.Name, new[] { "stage", "now" }) ?? usage);
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
                    Now = now,
                });
        }

        public const string WrapName = "wrap";

        /// <summary>
        /// <c>@wrap(pandemic.cure_cost)</c> (spec-gaps G311·G312). 대상은 이름(<c>모드id.이름</c>이나 자기 모드의 맨 이름)으로만 적고 여기서는
        /// 평가하지 않는다: 다른 모드의 이름은 로드 시점 값이 아니다(G309). 함수는 첫 매개변수(안쪽 함수)가 있어야 하고 매개변수에
        /// 기본값·타입 표기를 쓰지 않는다(대상의 것을 따른다).
        /// </summary>
        private void DeclareWrap(FunctionDef def, FunctionValue function, Expr decorator)
        {
            const string usage = "write @wrap(mod.function) above def f(original, ...same parameters...): ... (spec-gaps G312)";
            if (!(decorator is CallExpr call) || call.Arguments.Count != 1 || call.Arguments[0].Name != null)
            {
                _context.Error(Codes.CmdBadDecorator, "@wrap takes the function it wraps", decorator.Span, hint: usage);
                return;
            }
            var target = call.Arguments[0].Value;
            string? targetMod;
            string targetName;
            switch (target)
            {
                case NameExpr n:
                    targetMod = null;
                    targetName = n.Name;
                    break;
                case AttributeExpr { Target: NameExpr root } a:
                    targetMod = root.Name;
                    targetName = a.Name;
                    break;
                default:
                    _context.Error(Codes.CmdBadDecorator, "@wrap needs a function name, like @wrap(pandemic.cure_cost)", target.Span,
                        hint: usage);
                    return;
            }
            if (def.Parameters.Count == 0)
            {
                _context.Error(Codes.CmdBadDecorator, "a @wrap function takes the inner function as its first parameter", def.Span,
                    hint: usage);
                return;
            }
            foreach (var p in def.Parameters)
            {
                if (p.Default == null && p.Annotation == null) continue;
                _context.Error(Codes.CmdBadDecorator,
                    "a @wrap function's parameter follows the wrapped function, so it has no " +
                    (p.Default != null ? "default" : "type annotation"), p.Span, hint: usage);
                return;
            }
            _context.Functions.Wraps.Add(new WrapDecl(_context.Mod, function, targetMod, targetName, decorator.Span, target.Span));
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
        /// <c>def</c> 위의 데코레이터. <c>@command</c>(<c>free=</c>, <c>acts_for=</c>)와 <c>@view_policy</c>(<c>override=</c>)를
        /// 받는다 (spec S6, spec-gaps G109·G123·G130·G203). 없어진 <c>@submit_policy</c>는 오류로 알린다(G237). 커맨드 id는 <c>모드id.함수이름</c>이다.
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
            if ((name == CommandName || name == SubmitPolicyName || name == ViewPolicyName || name == ViewRuleName || name == OnName || name == WhenName ||
                 name == UiActionName || name == WrapName) && !IsBuiltin(name))
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
                _context.Error(Codes.CmdSubmitPolicy,
                    "@submit_policy was removed: the gate checks the kernel's seat table, not a policy function", decorator.Span,
                    hint: "delete this function; the host seats connections with World.Seat, and acts_for=None lets anyone in (spec-gaps G236·G237)");
                return;
            }
            if (name == ViewPolicyName)
            {
                _context.Error(Codes.CmdViewPolicy,
                    "@view_policy was replaced by view rules: each rule names the component or Def type it decides", decorator.Span,
                    hint: "write @view_rule(Hand) def f(viewer, entity): return True, False, or None to leave it to earlier rules " +
                          "(spec-gaps G315)");
                return;
            }
            if (name == ViewRuleName)
            {
                DeclareViewRule(def, function, decorator);
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
            if (name == UiActionName)
            {
                DeclareUiAction(function, decorator);
                return;
            }
            if (name == WrapName)
            {
                DeclareWrap(def, function, decorator);
                return;
            }
            if (name != CommandName)
            {
                _context.Error(Codes.CmdBadDecorator,
                    "unknown decorator; only @command, @on, @when, @ui_action, @wrap and @view_rule are supported",
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
        /// <c>@ui_action</c> (spec S10 3항, spec-gaps G276·G280): 인자 없는 데코레이터. 매개변수는 커맨드처럼 타입을 적고 그 연결이 보는
        /// 엔티티도 매개변수로 받는다(G278). 누구로서 행동하지 않으므로 <c>acts_for</c>가 없다.
        /// </summary>
        private void DeclareUiAction(FunctionValue function, Expr decorator)
        {
            if (decorator is CallExpr call)
            {
                var span = call.Arguments.Count > 0 ? call.Arguments[0].Span : call.Span;
                bool actsFor = call.Arguments.Count > 0 && call.Arguments[0].Name == ActsForName;
                _context.Error(Codes.CmdBadDecorator, actsFor
                        ? "@ui_action has no acts_for: it changes only the screen it is called on, so there is no gate to check"
                        : "@ui_action takes no arguments", span,
                    hint: "write @ui_action, and take the entity the screen is looking at as a parameter (spec-gaps G278)");
                return;
            }
            _context.Functions.UiActions.Add(new CommandDecl(_context.Mod, function, false, decorator.Span));
        }

        /// <summary>
        /// <c>@view_rule(Hand)</c> (G315): 대상은 맨 이름 하나(컴포넌트나 Def 타입)이고 무엇인지는 로더가 스키마로 푼다. 기본값 없는 매개변수 둘
        /// <c>(viewer, entity)</c>. 같은 대상에 건 규칙의 개수와 모드 사이 순서는 모든 모드를 모은 뒤 본다.
        /// </summary>
        private void DeclareViewRule(FunctionDef def, FunctionValue function, Expr decorator)
        {
            const string usage = "write @view_rule(Hand) for a component, or @view_rule(Unit) for whether entities of a Def type are seen " +
                                 "(spec-gaps G315)";
            if (!(decorator is CallExpr call) || call.Arguments.Count == 0)
            {
                _context.Error(Codes.CmdBadDecorator, "@view_rule needs the component or Def type it decides", decorator.Span, hint: usage);
                return;
            }
            foreach (var arg in call.Arguments)
            {
                if (arg.Name == null) continue;
                _context.Error(Codes.CmdBadDecorator, "@view_rule has no argument '" + arg.Name + "'", arg.Span,
                    hint: arg.Name == "override"
                        ? "a later mod's rule on the same target is asked first; return None to leave it to earlier rules (spec-gaps G315)"
                        : usage);
                return;
            }
            if (call.Arguments.Count != 1)
            {
                _context.Error(Codes.CmdBadDecorator, "@view_rule takes one component or Def type", call.Arguments[1].Span, hint: usage);
                return;
            }
            if (!(call.Arguments[0].Value is NameExpr target))
            {
                _context.Error(Codes.CmdBadDecorator, "@view_rule needs the bare name of a component or Def type",
                    call.Arguments[0].Span, hint: usage);
                return;
            }

            bool shapeOk = def.Parameters.Count == 2;
            foreach (var p in def.Parameters)
                if (p.Default != null) shapeOk = false;
            if (!shapeOk)
            {
                _context.Error(Codes.CmdViewPolicy,
                    "a view rule takes exactly two parameters without defaults: (viewer, entity)", def.Span,
                    hint: "viewer is an entity the connection sits at, or None for a spectator; return True to show, False to hide, " +
                          "or None to leave it to earlier rules (spec-gaps G238·G315)");
                return;
            }
            _context.Functions.ViewRules.Add(new ViewRuleDecl(_context.Mod, function, target.Name, target.Span, decorator.Span));
        }
    }
}
