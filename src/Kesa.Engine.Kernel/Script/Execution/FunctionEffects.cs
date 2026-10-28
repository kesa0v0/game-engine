using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 함수 효과 판정 (spec S2 함수 효과 표, spec-gaps G117). 본문에 속성 대입(엔티티 쓰기)이 있거나 같은 모드의 쓰기 함수를
    /// 이름으로 문장 호출하는 함수는 쓰기 함수다(전이적). 다른 모드의 함수(<c>pandemic.add_cube</c>, G309)는 의존 대상이라
    /// 로드 순서상 먼저 판정이 끝나 있어 그 결과를 그대로 쓴다.
    /// 모드의 이름이 다 정해진 ②b 끝에서 한다. 이름으로 알 수 없는 호출(속성 호출, 함수 값)은 실행 중 안전망이 막는다.
    /// <list type="bullet">
    /// <item>쓰기 함수는 문장으로만 부른다: 식 안의 호출은 로드 오류다 (람다 본문은 식이라 늘 오류).</item>
    /// <item>순수 자리(<c>derived</c> 함수, <c>field</c>의 함수 기본값, <c>Cond[...]</c> 기본값)에 쓰기 함수를 둘 수 없다.
    /// Def 값과 패치로 넣은 <c>Cond[...]</c> 값은 값 검사(<c>PatchOperationCheck.Mismatch</c>)가 본다.</item>
    /// </list>
    /// </summary>
    public static class FunctionEffects
    {
        public static void Check(ModFunctions functions, ModScope scope, SchemaSet schema, CallContext context,
            HostNames? hosts = null, Func<string, bool>? sees = null, ModNames? mods = null)
        {
            var bodies = new List<Body>();
            foreach (var function in functions.Functions)
            {
                var body = new Body(function);
                var walker = new Walker(scope, body, hosts ?? HostNames.None, new UiNames(schema, sees ?? (_ => true), context, false),
                    new UiNames(schema, sees ?? (_ => true), context, true))
                {
                    Mods = mods ?? ModNames.None,
                };
                if (function.Definition != null)
                    walker.Block(function.Definition.Body, FunctionLocals.Of(function.Definition));
                else
                    walker.Expr(function.Lambda!.Body, FunctionLocals.Parameters(function.Parameters));
                bodies.Add(body);
            }

            // 쓰기 함수 판정: 정의 순서대로 고정점까지 (순서가 정해진 목록만 돈다, P2)
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var body in bodies)
                {
                    if (body.Function.Writes) continue;
                    SourceSpan? witness = body.FirstWrite;
                    foreach (var call in body.StatementCalls)
                    {
                        if (!call.Callee.Writes) continue;
                        // 대입과 쓰기 호출 중 먼저 적힌 쪽
                        if (witness == null || Before(call.Span, witness.Value)) witness = call.Span;
                        break;
                    }
                    if (witness == null) continue;
                    body.Function.WriteWitness = witness;
                    changed = true;
                }
            }

            // 난수 함수 판정: random을 쓰거나 난수 함수를 부르는(문장이든 식이든) 함수, 고정점까지 (G178)
            changed = true;
            while (changed)
            {
                changed = false;
                foreach (var body in bodies)
                {
                    if (body.Function.Random) continue;
                    SourceSpan? witness = body.FirstRandom;
                    foreach (var calls in new[] { body.StatementCalls, body.ExpressionCalls })
                    foreach (var call in calls)
                        if (call.Callee.Random && (witness == null || Before(call.Span, witness.Value))) witness = call.Span;
                    if (witness == null) continue;
                    body.Function.RandomWitness = witness;
                    changed = true;
                }
            }

            // UI 로컬 기록을 건드리는 함수: 직접 읽거나 쓰거나, 그런 함수를 부르는(문장이든 식이든) 함수 (G282)
            changed = true;
            while (changed)
            {
                changed = false;
                foreach (var body in bodies)
                {
                    if (body.Function.TouchesUi) continue;
                    SourceSpan? witness = body.FirstUiTouch;
                    foreach (var calls in new[] { body.StatementCalls, body.ExpressionCalls })
                    foreach (var call in calls)
                        if (call.Callee.TouchesUi && (witness == null || Before(call.Span, witness.Value))) witness = call.Span;
                    if (witness == null) continue;
                    body.Function.UiTouchWitness = witness;
                    changed = true;
                }
            }
            // UI를 쓰는 함수: ui.X.y에 대입하거나 submit을 하거나, 그런 함수를 문장으로 부르는 함수 (G282, 쓰기 함수 G117과 같은 모양)
            changed = true;
            while (changed)
            {
                changed = false;
                foreach (var body in bodies)
                {
                    if (body.Function.WritesUi) continue;
                    SourceSpan? witness = body.FirstUiWrite;
                    foreach (var call in body.StatementCalls)
                        if (call.Callee.WritesUi && (witness == null || Before(call.Span, witness.Value))) witness = call.Span;
                    if (witness == null) continue;
                    body.Function.UiWriteWitness = witness;
                    changed = true;
                }
            }
            // ui.… 자체(submit 빼고), bot.… 읽기·쓰기, bot.… 쓰기 (G374: 봇은 submit은 하지만 화면 기록은 못 쓴다)
            Propagate(bodies, f => f.UsesUiState, b => b.FirstUiState, true, (f, w) => f.UiStateWitness = w);
            Propagate(bodies, f => f.TouchesBot, b => b.FirstBotTouch, true, (f, w) => f.BotTouchWitness = w);
            Propagate(bodies, f => f.WritesBot, b => b.FirstBotWrite, false, (f, w) => f.BotWriteWitness = w);

            // 사전 조건은 UI가 거듭 확인하므로 같은 답을 내야 한다 (G178)
            const string randomHint = "draw random numbers in the body instead; derived values, conditions and preconditions must give " +
                                      "the same answer every time (spec-gaps G178)";
            foreach (var body in bodies)
            {
                foreach (var span in body.PreconditionRandom)
                    context.Error(Codes.CmdWriteEffect, "random numbers cannot be drawn in 'with preconditions:'", span, randomHint);
                foreach (var call in body.PreconditionCalls)
                {
                    if (!call.Callee.Random) continue;
                    context.Error(Codes.CmdWriteEffect,
                        "'" + call.Callee.Name + "' draws random numbers, so it cannot be used in 'with preconditions:'", call.Span,
                        randomHint, new[] { call.Callee.RandomWitness!.Value });
                }
            }

            // 커맨드는 제출로만 실행된다: 로직에서 부르거나 값으로 쓰면 사전 조건과 권한을 건너뛴다 (G118)
            foreach (var body in bodies)
            foreach (var use in body.CommandUses)
            {
                context.Error(Codes.CmdWriteEffect,
                    "'" + use.Callee.Name + "' is a " + use.Callee.EntryKind + ", so logic cannot call it or use it as a value", use.Span,
                    "move the shared part into a plain function that the commands call (spec-gaps G118)",
                    new[] { use.Callee.Definition!.Span });
            }

            const string statementHint =
                "call it on its own line, or as 'x = ...(...)', in a @command body or in another function that changes " +
                "the world (spec-gaps G117, G135)";
            foreach (var body in bodies)
            foreach (var call in body.ExpressionCalls)
            {
                if (!call.Callee.Writes || call.Callee.IsEntry) continue;
                context.Error(Codes.CmdWriteEffect,
                    "'" + call.Callee.Name + "' changes the world, so it can only be called as a statement", call.Span,
                    statementHint, new[] { call.Callee.WriteWitness!.Value });
            }
            foreach (var body in bodies)
            foreach (var call in body.BuiltinExpressionCalls)
            {
                context.Error(Codes.CmdWriteEffect,
                    call.Key + "() " + PureBuiltins.EffectPhrase(call.Key) + ", so it can only be called as a statement",
                    call.Value, statementHint);
            }

            CheckUiEffects(functions, bodies, context);

            // 보기 규칙은 순수하다: 거른 읽기(G197·G315)는 상태를 건드리지 않는다
            foreach (var policy in functions.ViewRules)
            {
                if (policy.Function.Random)
                    context.Error(Codes.CmdWriteEffect,
                        "the " + policy.Noun + " '" + policy.Function.Name + "' draws random numbers; it must give the same answer every time",
                        policy.Function.Definition!.Span, randomHint, new[] { policy.Function.RandomWitness!.Value });
                if (!policy.Function.Writes) continue;
                context.Error(Codes.CmdWriteEffect,
                    "the " + policy.Noun + " '" + policy.Function.Name + "' changes the world; it can only read",
                    policy.Function.Definition!.Span,
                    "reading the world for a viewer must not change anything (spec-gaps G197)",
                    new[] { policy.Function.WriteWitness!.Value });
            }

            // when 조건은 순수하다: 정착마다 다시 평가하므로 읽기만 한다 (S5, G169)
            foreach (var when in functions.Whens)
            {
                if (when.Condition.Random)
                    context.Error(Codes.CmdWriteEffect,
                        "the condition of @when on '" + when.Function.Name + "' is '" + when.Condition.Name +
                        "', which draws random numbers; it must give the same answer every time",
                        when.Span, "draw random numbers in the reaction's body instead (spec-gaps G178)",
                        new[] { when.Condition.RandomWitness!.Value });
                if (!when.Condition.Writes) continue;
                context.Error(Codes.CmdWriteEffect,
                    "the condition of @when on '" + when.Function.Name + "' is '" + when.Condition.Name + "', which changes the world; it can only read",
                    when.Span, "change the world in the reaction's body instead (spec-gaps G117, G169)",
                    new[] { when.Condition.WriteWitness!.Value });
            }

            CheckPureSlots(schema, context);
        }

        private static bool Before(SourceSpan a, SourceSpan b) =>
            a.Line < b.Line || (a.Line == b.Line && a.Col < b.Col);

        /// <summary>본문에 직접 있거나 그런 함수를 부르면(<paramref name="expressions"/>면 식 안의 호출도) 표시한다, 고정점까지.</summary>
        private static void Propagate(List<Body> bodies, Func<FunctionValue, bool> has, Func<Body, SourceSpan?> first,
            bool expressions, Action<FunctionValue, SourceSpan> set)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var body in bodies)
                {
                    if (has(body.Function)) continue;
                    SourceSpan? witness = first(body);
                    foreach (var calls in expressions ? new[] { body.StatementCalls, body.ExpressionCalls } : new[] { body.StatementCalls })
                    foreach (var call in calls)
                        if (has(call.Callee) && (witness == null || Before(call.Span, witness.Value))) witness = call.Span;
                    if (witness == null) continue;
                    set(body.Function, witness.Value);
                    changed = true;
                }
            }
        }

        /// <summary>
        /// UI 로컬 효과의 자리 (G278·G282). UI 기록은 연결마다 다르고 저장되지 않으므로 게임 쪽 코드가 건드리면 컴퓨터마다 판이 갈라진다.
        /// 거꾸로 <c>@ui_action</c>은 게임을 바꾸지 않는다: 바꾸려면 커맨드를 <c>submit</c>한다.
        /// </summary>
        private static void CheckUiEffects(ModFunctions functions, List<Body> bodies, CallContext context)
        {
            const string gameHint = "UI state is different on every screen and is not saved; pass what the game needs as a command " +
                                    "argument with submit(...) (spec-gaps G278, G282)";
            const string botHint = "a bot's memory belongs to that bot and is not game state; read and write it only in a @bot and the " +
                                   "functions it calls (spec-gaps G374)";

            void Game(FunctionValue? fn, string what, SourceSpan span)
            {
                if (fn == null) return;
                if (fn.TouchesUi)
                    context.Error(Codes.CmdUiEffect, what + " uses UI state (ui.… or submit), so it cannot run as game code", span, gameHint,
                        new[] { fn.UiTouchWitness!.Value });
                else if (fn.TouchesBot)
                    context.Error(Codes.CmdUiEffect, what + " uses bot state (bot.…), so it cannot run as game code", span, botHint,
                        new[] { fn.BotTouchWitness!.Value });
            }

            const string statementHint = "call it on its own line in a @ui_action or in another function that changes UI state (spec-gaps G282)";
            foreach (var body in bodies)
            {
                foreach (var call in body.ExpressionCalls)
                {
                    if (!call.Callee.WritesUi || call.Callee.IsEntry) continue;
                    context.Error(Codes.CmdUiEffect,
                        "'" + call.Callee.Name + "' changes UI state, so it can only be called as a statement", call.Span,
                        statementHint, new[] { call.Callee.UiWriteWitness!.Value });
                }
                foreach (var call in body.ExpressionCalls)
                {
                    if (!call.Callee.WritesBot || call.Callee.IsEntry || call.Callee.WritesUi) continue;
                    context.Error(Codes.CmdUiEffect,
                        "'" + call.Callee.Name + "' changes bot state, so it can only be called as a statement", call.Span,
                        "call it on its own line in a @bot or in another function that changes bot state (spec-gaps G374)",
                        new[] { call.Callee.BotWriteWitness!.Value });
                }
                foreach (var span in body.SubmitExpressions)
                    context.Error(Codes.CmdUiEffect, "submit() asks the host to submit a command, so it can only be called as a statement",
                        span, statementHint);

                var fn = body.Function;
                if (fn.IsUiAction)
                {
                    if (fn.Writes)
                        context.Error(Codes.CmdUiEffect,
                            "the @ui_action '" + fn.Name + "' changes the game; it can only change ui.… and submit commands",
                            fn.Definition!.Span, "to change the game, submit(\"Command\", ...) a command that does it (spec-gaps G278)",
                            new[] { fn.WriteWitness!.Value });
                    if (fn.Random)
                        context.Error(Codes.CmdUiEffect,
                            "the @ui_action '" + fn.Name + "' draws random numbers; that would use up the game's random numbers",
                            fn.Definition!.Span, "draw them in the command it submits (spec-gaps G278)", new[] { fn.RandomWitness!.Value });
                    if (fn.TouchesBot)
                        context.Error(Codes.CmdUiEffect,
                            "the @ui_action '" + fn.Name + "' uses bot state (bot.…); only a @bot has a bot's memory",
                            fn.Definition!.Span, botHint, new[] { fn.BotTouchWitness!.Value });
                }
                else if (fn.IsBot)
                {
                    // 봇은 판을 읽고 submit으로만 바꾼다. 난수는 봇의 난수열이라 쓸 수 있다 (G373·G374)
                    if (fn.Writes)
                        context.Error(Codes.CmdUiEffect,
                            "the @bot '" + fn.Name + "' changes the game; it can only change bot.… and submit commands",
                            fn.Definition!.Span, "to change the game, submit(\"Command\", ...) a command that does it (spec-gaps G374)",
                            new[] { fn.WriteWitness!.Value });
                    if (fn.UsesUiState)
                        context.Error(Codes.CmdUiEffect,
                            "the @bot '" + fn.Name + "' uses UI state (ui.…); a bot has no screen",
                            fn.Definition!.Span, "keep what the bot remembers in a bot_state and read it as bot.State.field (spec-gaps G374)",
                            new[] { fn.UiStateWitness!.Value });
                }
                else if (fn.IsCommand || fn.IsHandler || fn.IsWhen)
                    Game(fn, "the " + fn.EntryKind + " '" + fn.Name + "'", fn.Definition!.Span);
            }
            foreach (var command in functions.Commands) Game(command.ActsFor, "the acts_for of '" + command.Id + "'", command.Span);
            foreach (var when in functions.Whens)
                Game(when.Condition, "the condition of @when on '" + when.Function.Name + "'", when.Span);
            foreach (var policy in functions.ViewRules)
                Game(policy.Function, "the " + policy.Noun + " '" + policy.Function.Name + "'", policy.Function.Definition!.Span);
        }

        /// <summary>스키마가 선언한 순수 자리에 이 모드의 쓰기 함수가 있으면 오류다.</summary>
        private static void CheckPureSlots(SchemaSet schema, CallContext context)
        {
            void Pure(FunctionValue? fn, string what, SourceSpan span)
            {
                if (fn == null || fn.Mod != context.Mod) return;
                if (fn.IsEntry)
                {
                    context.Error(Codes.CmdWriteEffect, what + " uses '" + fn.Name + "', which is a " + fn.EntryKind, span,
                        "a command only runs when it is submitted; use a plain function (spec-gaps G118)",
                        new[] { fn.Definition!.Span });
                    return;
                }
                if (fn.Random)
                {
                    context.Error(Codes.CmdWriteEffect,
                        what + " uses '" + fn.Name + "', which draws random numbers; it must give the same answer every time", span,
                        "draw random numbers in a @command body instead (spec-gaps G178)", new[] { fn.RandomWitness!.Value });
                    return;
                }
                if (fn.TouchesUi)
                {
                    context.Error(Codes.CmdUiEffect,
                        what + " uses '" + fn.Name + "', which uses UI state (ui.… or submit); game values cannot depend on a screen",
                        span, "UI state is different on every screen and is not saved (spec-gaps G282)", new[] { fn.UiTouchWitness!.Value });
                    return;
                }
                if (fn.TouchesBot)
                {
                    context.Error(Codes.CmdUiEffect,
                        what + " uses '" + fn.Name + "', which uses bot state (bot.…); game values cannot depend on a bot",
                        span, "a bot's memory belongs to that bot and is not game state (spec-gaps G374)", new[] { fn.BotTouchWitness!.Value });
                    return;
                }
                if (!fn.Writes) return;
                context.Error(Codes.CmdWriteEffect,
                    what + " uses '" + fn.Name + "', which changes the world; it can only read", span,
                    "change the world in a @command body instead (spec-gaps G117)", new[] { fn.WriteWitness!.Value });
            }

            void Members(string owner, IEnumerable<MemberDecl> members)
            {
                foreach (var m in members)
                {
                    string display = "'" + owner + "." + m.Name + "'";
                    if (m.Kind == MemberKind.Derived) Pure(m.Spec.Function, "derived " + display, m.Span);
                    else if (m.Kind == MemberKind.Field) Pure(m.Spec.Default as FunctionValue, "the default of " + display, m.Span);
                    else if (m.Spec.Type is FunctionTypeRef slot && slot.Kind == FunctionSlotKind.Cond)
                        Pure(m.Spec.Default as FunctionValue, "the default of " + display + " (" + slot.Display + ")", m.Span);
                }
            }

            foreach (var component in schema.Components) Members(component.Name, component.Members);
            foreach (var type in schema.Types) Members(type.Name, type.Consts);
        }

        private readonly struct Call
        {
            public Call(FunctionValue callee, SourceSpan span)
            {
                Callee = callee;
                Span = span;
            }

            public FunctionValue Callee { get; }
            public SourceSpan Span { get; }
        }

        private sealed class Body
        {
            public Body(FunctionValue function) => Function = function;

            public FunctionValue Function { get; }

            /// <summary>본문의 첫 속성 대입.</summary>
            public SourceSpan? FirstWrite { get; set; }

            /// <summary>문장으로 부른 같은 모드의 함수 (적힌 순서).</summary>
            public List<Call> StatementCalls { get; } = new List<Call>();

            /// <summary>식 안에서 부른 같은 모드의 함수.</summary>
            public List<Call> ExpressionCalls { get; } = new List<Call>();

            /// <summary>본문에서 커맨드 이름을 쓴 곳 (호출이든 값이든, G118).</summary>
            public List<Call> CommandUses { get; } = new List<Call>();

            /// <summary>본문의 첫 <c>random</c> 사용 (G178).</summary>
            public SourceSpan? FirstRandom { get; set; }

            /// <summary><c>with preconditions:</c> 안의 <c>random</c> 사용과 함수 호출 (G178).</summary>
            public List<SourceSpan> PreconditionRandom { get; } = new List<SourceSpan>();

            public List<Call> PreconditionCalls { get; } = new List<Call>();

            /// <summary>식 안에서 부른 쓰는 내장 (이름, 위치; G135).</summary>
            public List<KeyValuePair<string, SourceSpan>> BuiltinExpressionCalls { get; } =
                new List<KeyValuePair<string, SourceSpan>>();

            /// <summary>본문의 첫 <c>ui.…</c> 읽기·쓰기나 <c>submit</c> (G282).</summary>
            public SourceSpan? FirstUiTouch { get; set; }

            /// <summary>본문의 첫 <c>ui.X.y</c> 대입이나 문장 <c>submit</c> (G282).</summary>
            public SourceSpan? FirstUiWrite { get; set; }

            /// <summary>식 안의 <c>submit(...)</c> (G282: 문장으로만 부른다).</summary>
            public List<SourceSpan> SubmitExpressions { get; } = new List<SourceSpan>();

            public void TouchUi(SourceSpan span)
            {
                if (FirstUiTouch == null) FirstUiTouch = span;
            }

            public void WriteUi(SourceSpan span)
            {
                TouchUi(span);
                if (FirstUiWrite == null) FirstUiWrite = span;
            }

            /// <summary>본문의 첫 <c>ui.…</c> 자체 (submit 빼고, G374).</summary>
            public SourceSpan? FirstUiState { get; set; }

            /// <summary>본문의 첫 <c>bot.…</c> 읽기·쓰기 (G374).</summary>
            public SourceSpan? FirstBotTouch { get; set; }

            /// <summary>본문의 첫 <c>bot.X.y</c> 대입 (G374).</summary>
            public SourceSpan? FirstBotWrite { get; set; }

            /// <summary><c>ui.…</c>(<paramref name="bot"/>면 <c>bot.…</c>) 기록을 건드린다.</summary>
            public void Touch(bool bot, SourceSpan span)
            {
                if (bot)
                {
                    if (FirstBotTouch == null) FirstBotTouch = span;
                    return;
                }
                TouchUi(span);
                if (FirstUiState == null) FirstUiState = span;
            }

            public void Write(bool bot, SourceSpan span)
            {
                Touch(bot, span);
                if (bot)
                {
                    if (FirstBotWrite == null) FirstBotWrite = span;
                }
                else WriteUi(span);
            }
        }

        /// <summary>
        /// <c>ui.기록.칸</c> 경로의 이름 검사 (G279). 경로는 늘 이 모양으로 적는다: <c>s = ui.Selection</c>처럼 기록을 값으로 들면
        /// 그 뒤의 <c>s.unit = …</c>가 UI 쓰기인지 엔티티 쓰기인지 알 수 없다. 봇의 <c>bot.기록.칸</c>도 같다 (G371).
        /// </summary>
        private sealed class UiNames
        {
            private readonly SchemaSet _schema;
            private readonly Func<string, bool> _sees;
            private readonly CallContext _context;
            private readonly bool _bot;

            public UiNames(SchemaSet schema, Func<string, bool> sees, CallContext context, bool bot)
            {
                _schema = schema;
                _sees = sees;
                _context = context;
                _bot = bot;
            }

            private string Noun => _bot ? "bot state" : "UI state";

            public void Check(string state, string member, SourceSpan span)
            {
                if (!(_bot ? _schema.TryGetBotState(state, out var type) : _schema.TryGetUiState(state, out type)))
                {
                    var names = new List<string>();
                    foreach (var s in _bot ? _schema.BotStates : _schema.UiStates) names.Add(s.Name);
                    _context.Error(Codes.EvlUnknownName, "there is no " + Noun + " '" + state + "'", span,
                        Suggest.Hint(state, names) ?? "declare it in a schema file with " + (_bot ? "bot_state" : "ui_state") + "(\"" +
                        state + "\", ...) (spec-gaps " + (_bot ? "G371" : "G277") + ")");
                    return;
                }
                if (!_sees(type.Mod))
                {
                    _context.Error(Codes.SchNotVisible,
                        (_bot ? "Bot state '" : "UI state '") + state + "' belongs to mod '" + type.Mod + "', which mod '" + _context.Mod +
                        "' does not depend on",
                        span, "add \"" + type.Mod + "\" to dependencies in mod.py");
                    return;
                }
                if (type.TryGetMember(member, out _)) return;
                var members = new List<string>();
                foreach (var m in type.Members) members.Add(m.Name);
                _context.Error(Codes.EvlUnknownName, (_bot ? "Bot state '" : "UI state '") + state + "' has no field '" + member + "'",
                    span, Suggest.Hint(member, members));
            }

            public void Positional(SourceSpan span) =>
                _context.Error(Codes.CmdSubmit, "submit() takes the command's arguments by name", span,
                    "write submit(\"Attack\", target=enemy) (spec-gaps G278)");

            public void SubmitValue(SourceSpan span) =>
                _context.Error(Codes.CmdUiEffect, "submit can only be called, not used as a value", span,
                    "call submit(\"Command\", ...) on its own line (spec-gaps G282)");

            public void Bare(SourceSpan span) =>
                _context.Error(Codes.CmdUiEffect, _bot
                        ? "write the bot state as bot.State.field; bot and bot.State cannot be used as values"
                        : "write the UI state as ui.State.field; ui and ui.State cannot be used as values",
                    span, _bot
                        ? "a held bot.State would hide which assignments change bot state (spec-gaps G374)"
                        : "a held ui.State would hide which assignments change UI state (spec-gaps G282)");
        }

        private sealed class Walker
        {
            private readonly ModScope _scope;
            private readonly Body _body;
            private readonly HostNames _hosts;

            /// <summary><c>with preconditions:</c> 블록 안을 걷는 중인가 (G178: 거기서는 난수를 쓸 수 없다).</summary>
            private bool _inPreconditions;

            private readonly UiNames _ui;
            private readonly UiNames _bot;

            /// <summary><c>모드id.이름</c>으로 쓸 수 있는 모드들 (G309).</summary>
            public ModNames Mods { get; set; } = ModNames.None;

            public Walker(ModScope scope, Body body, HostNames hosts, UiNames ui, UiNames bot)
            {
                _scope = scope;
                _body = body;
                _hosts = hosts;
                _ui = ui;
                _bot = bot;
            }

            /// <summary>덮어쓰지 않은 <c>ui</c>(거짓)나 <c>bot</c>(참)이면 그 뜻.</summary>
            private bool? StateRoot(Expr expr, SortedSet<string> locals) =>
                IsRuntime(expr, PureBuiltins.UiName, locals) ? false :
                IsRuntime(expr, PureBuiltins.BotName, locals) ? true : (bool?)null;

            /// <summary>지역 변수도 모드 이름도 아닌 실행 이름인가 (덮어쓰지 않은 <c>ui</c>·<c>submit</c>).</summary>
            private bool IsRuntime(Expr expr, string name, SortedSet<string> locals) =>
                expr is NameExpr n && n.Name == name && !locals.Contains(name) && !_scope.TryLookup(name, out _);

            /// <summary><c>ui.기록.칸</c>(G279)이면 거짓, <c>bot.기록.칸</c>(G371)이면 참. 이름을 검사한다. 아니면 null.</summary>
            private bool? StateField(Expr expr, SortedSet<string> locals)
            {
                if (!(expr is AttributeExpr member) || !(member.Target is AttributeExpr state)) return null;
                var bot = StateRoot(state.Target, locals);
                if (bot == null) return null;
                (bot.Value ? _bot : _ui).Check(state.Name, member.Name, member.Span);
                return bot;
            }

            /// <summary>문장 <c>submit("커맨드", 이름=값)</c>: 커맨드 인자는 이름으로만 넘긴다 (G278).</summary>
            private void Submit(CallExpr call, SortedSet<string> locals)
            {
                for (int i = 1; i < call.Arguments.Count; i++)
                    if (call.Arguments[i].Name == null)
                        _ui.Positional(call.Arguments[i].Value.Span);
                foreach (var arg in call.Arguments) Expr(arg.Value, locals);
            }

            /// <summary>지역 변수가 아니고 모드가 <c>def</c> 함수로 묶은 이름이면 그 함수 (G112의 덮어쓰기와 같은 규칙).</summary>
            /// 커맨드면 쓴 곳을 기록하고 null이다 (호출로도 세지 않는다, G118).
            private FunctionValue? Resolve(Expr callee, SortedSet<string> locals)
            {
                Value value;
                SourceSpan span;
                if (callee is AttributeExpr member && member.Target is NameExpr root && IsModRoot(root.Name, locals))
                {
                    // 다른 모드(또는 자신)의 최상위 함수 (G309)
                    if (!Mods.TryGet(root.Name, member.Name, out value)) return null;
                    span = member.Span;
                }
                else
                {
                    if (!(callee is NameExpr name) || locals.Contains(name.Name)) return null;
                    if (!_scope.TryGet(name.Name, out value, out _)) return null;
                    span = name.Span;
                }
                if (!(value is FunctionValue fn) || fn.Definition == null) return null;
                if (!fn.IsEntry) return fn;
                _body.CommandUses.Add(new Call(fn, span));
                return null;
            }

            /// <summary>지역 변수·모드 이름·스키마 이름·내장이 가리지 않은 보이는 모드 id인가 (G309, 이름 검사와 같은 차례).</summary>
            private bool IsModRoot(string name, SortedSet<string> locals) =>
                Mods.IsRoot(name) && !locals.Contains(name) && !_scope.TryLookup(name, out _) && !PureBuiltins.IsRuntimeName(name);

            /// <summary>덮어쓰지 않은 쓰는 내장의 이름이면 그 이름 (G112의 덮어쓰기와 같은 규칙).</summary>
            private string? WritingBuiltin(Expr callee, SortedSet<string> locals)
            {
                // 엔티티를 쓰는 호스트 함수도 같은 자리 규칙이다 (G216)
                if (callee is AttributeExpr && HostNames.TryChain(callee, out var root, out var attributes) &&
                    !locals.Contains(root.Name) && !_scope.TryLookup(root.Name, out _) && _hosts.IsRoot(root.Name))
                {
                    var r = _hosts.Resolve(root.Name, attributes);
                    return r.Function != null && r.Used == attributes.Count && _hosts.IsWriting(r.Function) ? r.Function : null;
                }
                if (!(callee is NameExpr name) || locals.Contains(name.Name) || _scope.TryLookup(name.Name, out _))
                    return null;
                return PureBuiltins.IsWritingName(name.Name) ? name.Name : null;
            }

            /// <summary>문장 자리의 호출: 한 줄 전체이거나 이름 대입의 오른쪽 전체 (G117·G135).</summary>
            private void StatementCall(CallExpr call, SortedSet<string> locals)
            {
                if (IsRuntime(call.Callee, PureBuiltins.SubmitName, locals))
                {
                    _body.WriteUi(call.Span);
                    Submit(call, locals);
                    return;
                }
                var callee = Resolve(call.Callee, locals);
                if (callee != null) _body.StatementCalls.Add(new Call(callee, call.Span));
                else if (WritingBuiltin(call.Callee, locals) != null)
                {
                    if (_body.FirstWrite == null) _body.FirstWrite = call.Span;
                }
                else if (!IsNamed(call.Callee, locals)) Expr(call.Callee, locals);
                foreach (var arg in call.Arguments) Expr(arg.Value, locals);
            }

            /// <summary>이름이나 <c>모드id.이름</c>으로 부른 것인가: <see cref="Resolve"/>가 이미 봤으므로 다시 걷지 않는다.</summary>
            private bool IsNamed(Expr callee, SortedSet<string> locals) =>
                callee is NameExpr || callee is AttributeExpr member && member.Target is NameExpr root && IsModRoot(root.Name, locals);

            public void Block(IReadOnlyList<Stmt> body, SortedSet<string> locals)
            {
                foreach (var stmt in body)
                {
                    switch (stmt)
                    {
                        case ExprStmt es:
                            if (es.Expr is CallExpr call) StatementCall(call, locals);
                            else Expr(es.Expr, locals);
                            break;
                        case AssignStmt assign:
                            // ui.기록.칸 = …는 UI 로컬 쓰기다. ui.기록.칸.x = …는 칸이 가리키는 엔티티 쓰기다 (G282)
                            if (StateField(assign.Target, locals) is bool written) _body.Write(written, assign.Span);
                            else if (assign.Target is AttributeExpr target)
                            {
                                if (_body.FirstWrite == null) _body.FirstWrite = assign.Span;
                                Expr(target.Target, locals);
                            }
                            if (assign.Op == "=" && assign.Target is NameExpr && assign.Value is CallExpr assigned)
                                StatementCall(assigned, locals);
                            else
                                Expr(assign.Value, locals);
                            break;
                        case IfStmt ifs:
                            foreach (var branch in ifs.Branches)
                            {
                                Expr(branch.Condition, locals);
                                Block(branch.Body, locals);
                            }
                            if (ifs.OrElse != null) Block(ifs.OrElse, locals);
                            break;
                        case ForStmt loop:
                            Expr(loop.Iterable, locals);
                            Block(loop.Body, locals);
                            break;
                        case WhileStmt loop:
                            Expr(loop.Condition, locals);
                            Block(loop.Body, locals);
                            break;
                        case WithStmt with:
                            // 사전 조건 블록의 문장은 require 호출과 이름 대입뿐이다 (G111): 그 안의 호출은 모두 식 안이다
                            _inPreconditions = true;
                            foreach (var inner in with.Body)
                            {
                                if (inner is ExprStmt e) Expr(e.Expr, locals);
                                else if (inner is AssignStmt a) Expr(a.Value, locals);
                            }
                            _inPreconditions = false;
                            break;
                        case ReturnStmt ret:
                            if (ret.Value != null) Expr(ret.Value, locals);
                            break;
                    }
                }
            }

            public void Expr(Expr expr, SortedSet<string> locals)
            {
                switch (expr)
                {
                    case CallExpr call when IsRuntime(call.Callee, PureBuiltins.SubmitName, locals):
                        _body.TouchUi(call.Span);
                        _body.SubmitExpressions.Add(call.Span);
                        Submit(call, locals);
                        break;
                    case CallExpr call:
                        var callee = Resolve(call.Callee, locals);
                        var builtin = callee == null ? WritingBuiltin(call.Callee, locals) : null;
                        if (callee != null)
                        {
                            _body.ExpressionCalls.Add(new Call(callee, call.Span));
                            if (_inPreconditions) _body.PreconditionCalls.Add(new Call(callee, call.Span));
                        }
                        else if (builtin != null)
                            _body.BuiltinExpressionCalls.Add(new KeyValuePair<string, SourceSpan>(builtin, call.Span));
                        else if (!IsNamed(call.Callee, locals)) Expr(call.Callee, locals);
                        foreach (var arg in call.Arguments) Expr(arg.Value, locals);
                        break;
                    case NameExpr name when StateRoot(name, locals) is bool bare:
                        _body.Touch(bare, name.Span);
                        (bare ? _bot : _ui).Bare(name.Span);
                        break;
                    case NameExpr name when IsRuntime(name, PureBuiltins.SubmitName, locals):
                        _body.TouchUi(name.Span);
                        _ui.SubmitValue(name.Span);
                        break;
                    case NameExpr name:
                        Resolve(name, locals); // 값으로 쓴 커맨드 이름을 기록한다 (G118)
                        // 덮어쓰지 않은 random을 쓰면 난수 함수다 (호출이든 값이든, G178)
                        if (name.Name == PureBuiltins.RandomName && !locals.Contains(name.Name) && !_scope.TryLookup(name.Name, out _))
                        {
                            if (_body.FirstRandom == null) _body.FirstRandom = name.Span;
                            if (_inPreconditions) _body.PreconditionRandom.Add(name.Span);
                        }
                        break;
                    case BinaryExpr bin:
                        Expr(bin.Left, locals);
                        Expr(bin.Right, locals);
                        break;
                    case UnaryExpr unary:
                        Expr(unary.Operand, locals);
                        break;
                    case AttributeExpr attr:
                        if (StateField(attr, locals) is bool read) _body.Touch(read, attr.Span);
                        else if (StateRoot(attr.Target, locals) is bool held)
                        {
                            _body.Touch(held, attr.Span);
                            (held ? _bot : _ui).Bare(attr.Span);
                        }
                        // 값으로 쓴 다른 모드의 커맨드 이름을 기록한다 (G118·G309)
                        else if (attr.Target is NameExpr root && IsModRoot(root.Name, locals)) Resolve(attr, locals);
                        else Expr(attr.Target, locals);
                        break;
                    case IndexExpr index:
                        Expr(index.Target, locals);
                        foreach (var i in index.Indices) Expr(i, locals);
                        break;
                    case ListExpr list:
                        foreach (var item in list.Items) Expr(item, locals);
                        break;
                    case DictExpr dict:
                        foreach (var entry in dict.Entries)
                        {
                            Expr(entry.Key, locals);
                            Expr(entry.Value, locals);
                        }
                        break;
                    case ConditionalExpr cond:
                        Expr(cond.Condition, locals);
                        Expr(cond.WhenTrue, locals);
                        Expr(cond.WhenFalse, locals);
                        break;
                    case LambdaExpr lambda:
                        var inner = new SortedSet<string>(locals, System.StringComparer.Ordinal);
                        foreach (var p in lambda.Parameters) inner.Add(p.Name);
                        Expr(lambda.Body, inner);
                        break;
                    case ComprehensionExpr comp:
                        // 식 안이라 그 안의 호출은 모두 식 안의 호출이다 (G117·G135)
                        foreach (var (part, bound) in comp.Parts())
                        {
                            var scope = new SortedSet<string>(locals, System.StringComparer.Ordinal);
                            scope.UnionWith(bound);
                            Expr(part, scope);
                        }
                        break;
                }
            }
        }
    }
}
