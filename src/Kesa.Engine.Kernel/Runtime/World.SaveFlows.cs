using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 멈춘 Flow의 저장 (spec S2 평가 모델·S8, C1, spec-gaps G141). 프레임 스택은 데이터라 그대로 적는다: 프레임마다 함수 이름,
    /// 지역 변수, 블록 커서. 블록은 바깥 블록의 "방금 시작한 문장"으로 어느 문장 목록인지 정해지므로 번호만 적는다.
    /// 부른 위치·결과 자리·쓰기 모드·답을 받을 변수도 부른 문장에서 다시 계산되므로 적지 않는다(저장할 때 계산이 실제와 맞는지 확인한다).
    /// 런타임 값은 가리키는 대상으로 적는다(엔티티 id, 컴포넌트 이름, 키). 람다는 아직 저장하지 않는다(저장 ③).
    /// </summary>
    public sealed partial class World
    {
        // ---- 저장 ----

        private JsonArray SaveFlows()
        {
            var flows = new JsonArray();
            foreach (var flow in _flows.Values)
            {
                var run = flow.Run;
                Relink(EntrySpan(flow.Command, flow.Handler), run.Frames, out var answerTo, check: true);
                if (answerTo != run.AnswerTo)
                    throw new InvalidOperationException("engine: the answer variable of flow " + flow.Id + " does not match its ask statement");

                var writer = new FlowWriter(run.Frames);
                var frames = new JsonArray();
                foreach (var frame in run.Frames) frames.Add(SaveFrame(frame, writer));
                // 질문을 든 Flow는 "request", wait로 자는 Flow는 깨울 틱 "wake_at"을 적는다
                var saved = new JsonObject()
                    .Add("id", new JsonNumber(flow.Id))
                    .Add(flow.Handler ? "handler" : "command", new JsonString(flow.Command));
                // 응답 본문을 기다리는 Flow는 그 Flow 번호 "awaits"를 적는다 (G243)
                if (flow.Request != null) saved.Add("request", SaveRequest(flow.Request));
                else if (flow.WakeAt != null) saved.Add("wake_at", new JsonNumber(flow.WakeAt.Value));
                else if (flow.AwaitsChild != null) saved.Add("awaits", new JsonNumber(flow.AwaitsChild.Value));
                else throw new InvalidOperationException("engine: flow " + flow.Id + " is paused at advance(), which finishes within its call");
                if (flow.Parent != null)
                    saved.Add("parent", new JsonObject()
                        .Add("flow", new JsonNumber(flow.Parent.Flow))
                        .Add("response", new JsonString(flow.Parent.Response))
                        .Add("by", new JsonNumber(flow.Parent.By.Value)));
                var subjects = new JsonArray();
                foreach (var s in flow.Subjects) subjects.Add(new JsonNumber(s.Value));
                JsonNode about = JsonNull.Instance;
                if (flow.About != null)
                {
                    var list = new JsonArray();
                    foreach (var s in flow.About) list.Add(new JsonNumber(s.Value));
                    about = list;
                }
                flows.Add(saved
                    .Add("subjects", subjects)
                    .Add("about", about)
                    .Add("rand", SaveStream(flow))
                    .Add("frames", frames)
                    .Add("closures", writer.Closures)
                    .Add("envs", writer.Envs));
            }
            return flows;
        }

        /// <summary>
        /// Flow 하나를 적는 동안의 람다 목록과 잡은 지역 변수 묶음 목록 (G143). 처음 만난 순서로 번호를 매긴다: 같은 람다 객체는 같은 번호라
        /// 불러온 뒤에도 같은 객체다. 목록에 먼저 자리를 잡고 채우므로 자기를 잡은 람다(묶음 안에 자기 람다)도 적힌다.
        /// </summary>
        private sealed class FlowWriter
        {
            public FlowWriter(List<Frame> frames) => Frames = frames;

            public List<Frame> Frames { get; }
            public Dictionary<ClosureValue, int> ClosureIds { get; } = new Dictionary<ClosureValue, int>();
            public JsonArray Closures { get; } = new JsonArray();
            public Dictionary<IReadOnlyDictionary<string, Value>, int> EnvIds { get; } =
                new Dictionary<IReadOnlyDictionary<string, Value>, int>();
            public JsonArray Envs { get; } = new JsonArray();
        }

        private JsonNode SaveClosure(ClosureValue closure, FlowWriter writer)
        {
            if (writer.ClosureIds.TryGetValue(closure, out int id)) return Tag("closure", new JsonNumber(id));
            id = writer.Closures.Items.Count;
            writer.ClosureIds.Add(closure, id);
            var entry = new JsonObject();
            writer.Closures.Add(entry);

            var place = LambdaPlace(closure.Function);
            entry.Add("lambda", new JsonArray()
                .Add(new JsonString(place.Mod)).Add(new JsonString(place.Root)).Add(new JsonNumber(place.Ordinal)));
            entry.Add("hash", new JsonString(place.Hash));

            JsonNode env = JsonNull.Instance;
            for (int i = 0; i < writer.Frames.Count && env is JsonNull; i++)
            {
                if (!ReferenceEquals(closure.Captured, writer.Frames[i].Locals)) continue;
                env = Tag("frame", new JsonNumber(i));
            }
            if (env is JsonNull) env = Tag("env", new JsonNumber(SaveEnv(closure, writer)));
            entry.Add("env", env);
            return Tag("closure", new JsonNumber(id));
        }

        /// <summary>이미 끝난 함수의 지역 변수 묶음 (더 바뀌지 않는다). 지역 변수 이름 목록과 값을 적는다.</summary>
        private int SaveEnv(ClosureValue closure, FlowWriter writer)
        {
            var captured = closure.Captured;
            if (writer.EnvIds.TryGetValue(captured, out int id)) return id;
            id = writer.Envs.Items.Count;
            writer.EnvIds.Add(captured, id);
            var entry = new JsonObject();
            writer.Envs.Add(entry);

            var names = new JsonArray();
            foreach (var name in captured.Layout.Names()) names.Add(new JsonString(name));
            entry.Add("names", names);
            var sorted = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var pair in captured) sorted.Add(pair.Key);
            var locals = new JsonObject();
            entry.Add("locals", locals);
            foreach (var name in sorted) locals.Add(name, SaveScriptValue(captured[name], writer));
            return id;
        }

        private readonly struct LambdaSite
        {
            public LambdaSite(string mod, string root, int ordinal, string hash)
            {
                Mod = mod;
                Root = root;
                Ordinal = ordinal;
                Hash = hash;
            }

            public string Mod { get; }
            public string Root { get; }
            public int Ordinal { get; }

            /// <summary>든 함수의 본문 해시 (순서 번호는 그 구조에 기댄다, G144).</summary>
            public string Hash { get; }
        }

        /// <summary>람다 식 → (든 함수의 모드·이름, 그 안의 순서 번호). 처음 쓸 때 모든 모드의 함수에서 만든다.</summary>
        private Dictionary<LambdaExpr, LambdaSite>? _lambdaSites;

        private LambdaSite LambdaPlace(FunctionValue lambda)
        {
            if (_lambdaSites == null)
            {
                _lambdaSites = new Dictionary<LambdaExpr, LambdaSite>();
                foreach (var pair in _scopes)
                {
                    foreach (var name in pair.Value.Names)
                    {
                        if (!pair.Value.TryGet(name, out var bound, out _) || !(bound is FunctionValue fn) || fn.Mod != pair.Key) continue;
                        if (fn.Definition != null && fn.Name != name) continue; // 별칭이 아닌 제 이름으로만
                        var found = LambdaOrdinals.Of(fn);
                        if (found.Count == 0) continue;
                        string hash = BodyHash.Of(fn);
                        for (int i = 0; i < found.Count; i++)
                            if (!_lambdaSites.ContainsKey(found[i])) _lambdaSites.Add(found[i], new LambdaSite(pair.Key, name, i, hash));
                    }
                }
            }
            if (lambda.Lambda == null || !_lambdaSites.TryGetValue(lambda.Lambda, out var site) || site.Mod != lambda.Mod)
                throw new InvalidOperationException("a paused flow holds a lambda that is not inside a named function of its mod, " +
                                                    "so it cannot be saved yet");
            return site;
        }

        private static JsonObject SaveRequest(Request request)
        {
            var who = new JsonArray();
            foreach (var id in request.Who) who.Add(new JsonNumber(id.Value));
            var choices = new JsonArray();
            // 꾸밈 없는 선택지는 값만 적는다 (G271): 맨 값을 감싼 것과 같다
            foreach (var c in request.Choices) choices.Add(c.IsPlain ? SaveValue(c.Value) : SaveValue(c));
            var responses = new JsonArray();
            foreach (var r in request.Responses) responses.Add(new JsonString(r));
            var names = new JsonArray();
            foreach (var n in request.ResponseNames) names.Add(new JsonString(n));
            return new JsonObject()
                .Add("id", new JsonNumber(request.Id))
                .Add("who", who)
                .Add("title", SaveValue(request.Title))
                .Add("choices", choices)
                .Add("responses", responses)
                .Add("response_names", names)
                .Add("hint", request.Hint == null ? (JsonNode)JsonNull.Instance : new JsonString(request.Hint))
                .Add("data", SaveValue(request.Data))
                .Add("blocks_time", new JsonBool(request.BlocksTime))
                .Add("deadline", request.Deadline == null ? (JsonNode)JsonNull.Instance : new JsonNumber(request.Deadline.Value))
                .Add("on_timeout", request.TimeoutChoice != null ? Tag("choice", new JsonNumber(request.TimeoutChoice.Value))
                    : request.TimeoutResponse != null ? Tag("response", new JsonNumber(request.TimeoutResponse.Value))
                    : (JsonNode)JsonNull.Instance);
        }

        private JsonObject SaveFrame(Frame frame, FlowWriter writer)
        {
            var fn = frame.Function;
            if (fn.Definition == null)
                throw new InvalidOperationException("engine: a paused frame runs a lambda");
            if (!IsBoundAs(fn, fn.Mod, fn.Name))
                throw new InvalidOperationException("the paused function '" + fn.Mod + "." + fn.Name +
                                                    "' cannot be saved: that name in its mod now holds something else");

            var locals = new JsonObject();
            foreach (var pair in frame.Locals) locals.Add(pair.Key, SaveScriptValue(pair.Value, writer));

            var blocks = new JsonArray();
            for (int i = 0; i < frame.Blocks.Count; i++)
            {
                var block = frame.Blocks[i];
                var obj = new JsonObject().Add("next", new JsonNumber(block.Next));
                if (i == 0)
                {
                    if (!ReferenceEquals(block.Body, fn.Definition.Body))
                        throw new InvalidOperationException("engine: the first block of a paused frame is not the function body");
                }
                else
                {
                    switch (Opener(frame.Blocks[i - 1]))
                    {
                        case IfStmt ifs:
                            int branch = BranchOf(ifs, block.Body);
                            if (branch < 0) throw new InvalidOperationException("engine: a paused block is not a branch of its if");
                            obj.Add("branch", new JsonNumber(branch));
                            break;
                        case ForStmt loop:
                            if (!ReferenceEquals(block.Loop, loop))
                                throw new InvalidOperationException("engine: a paused block is not the body of its for");
                            obj.Add("index", new JsonNumber(block.Index));
                            if (block.Range != null) obj.Add("range", SaveRange(block.Range));
                            else
                            {
                                var items = new JsonArray();
                                foreach (var item in block.Items!) items.Add(SaveScriptValue(item, writer));
                                obj.Add("items", items);
                            }
                            // 엔진이 준 묶음을 돌던 중이면 그 묶음도: 이어서 돌 때도 사라진 항목을 건너뛴다 (G194)
                            if (block.Live != null) obj.Add("live", SaveScriptValue((Value)block.Live, writer));
                            break;
                        case WhileStmt loop:
                            // 위치만 적는다: 조건은 이어서 실행할 때 다시 평가한다 (G195)
                            if (!ReferenceEquals(block.While, loop))
                                throw new InvalidOperationException("engine: a paused block is not the body of its while");
                            break;
                        default:
                            throw new InvalidOperationException("engine: a paused block was not opened by an if, a for or a while");
                    }
                }
                blocks.Add(obj);
            }

            return new JsonObject()
                .Add("mod", new JsonString(fn.Mod))
                .Add("function", new JsonString(fn.Name))
                .Add("hash", new JsonString(BodyHash.Of(fn)))
                .Add("locals", locals)
                .Add("blocks", blocks);
        }

        /// <summary>블록을 연 문장 (블록이 방금 실행한 문장). 아직 아무것도 실행하지 않았으면 null.</summary>
        private static Stmt? Opener(Block block) => block.Next >= 1 ? block.Body[block.Next - 1] : null;

        /// <summary><c>if</c>의 몇 번째 갈래인가. <c>else</c>는 갈래 수와 같은 번호다. 아니면 -1.</summary>
        private static int BranchOf(IfStmt ifs, IReadOnlyList<Stmt> body)
        {
            for (int i = 0; i < ifs.Branches.Count; i++)
                if (ReferenceEquals(ifs.Branches[i].Body, body)) return i;
            return ifs.OrElse != null && ReferenceEquals(ifs.OrElse, body) ? ifs.Branches.Count : -1;
        }

        /// <summary>문장 하나 전체인 호출(<c>f(...)</c>)이나 이름 대입의 오른쪽 전체인 호출(<c>x = f(...)</c>)인가.</summary>
        private static bool CallStatement(Stmt? stmt, out CallExpr call, out string? target)
        {
            target = null;
            switch (stmt)
            {
                case ExprStmt { Expr: CallExpr c }:
                    call = c;
                    return true;
                case AssignStmt { Op: "=", Target: NameExpr t, Value: CallExpr c }:
                    call = c;
                    target = t.Name;
                    return true;
            }
            call = null!;
            return false;
        }

        /// <summary>
        /// 프레임마다 부른 위치·결과 자리·쓰기 모드와 맨 위 프레임의 답 변수를 부른 문장에서 계산한다. <paramref name="check"/>면 실제
        /// 프레임과 비교만 하고(다르면 엔진 결함), 아니면 채운다(불러오기). 부른 문장이 호출이 아니면 false(함수 본문이 바뀌었다).
        /// </summary>
        /// <summary>Flow를 만든 입구(커맨드나 핸들러)의 위치: 맨 아래 프레임의 부른 위치다.</summary>
        private SourceSpan EntrySpan(string id, bool handler) => handler ? _handlerById[id].Span : _commands[id].Span;

        private bool Relink(SourceSpan entry, List<Frame> frames, out string? answerTo, bool check)
        {
            answerTo = null;
            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                SourceSpan site;
                var result = ResultTo.Discard;
                string? resultName = null;
                bool writable;
                if (i == 0)
                {
                    site = entry;
                    writable = true;
                }
                else
                {
                    var caller = frames[i - 1];
                    if (caller.Blocks.Count == 0 ||
                        !CallStatement(Opener(caller.Blocks[caller.Blocks.Count - 1]), out var call, out resultName))
                    {
                        if (check) throw new InvalidOperationException("engine: a paused frame was not called by a statement");
                        return false;
                    }
                    site = call.Span;
                    if (resultName != null) result = ResultTo.Local;
                    writable = caller.Writable && frame.Function.Writes;
                }

                if (!check)
                {
                    frame.CallSite = site;
                    frame.Result = result;
                    frame.ResultName = resultName;
                    frame.Writable = writable;
                }
                else if (!frame.CallSite.Equals(site) || frame.Result != result || frame.ResultName != resultName ||
                         frame.Writable != writable)
                {
                    throw new InvalidOperationException("engine: paused frame " + i + " (" + frame.Function.Name +
                                                        ") is not linked the way its caller's statement says");
                }
            }

            var top = frames[frames.Count - 1];
            if (top.Blocks.Count == 0 || !CallStatement(Opener(top.Blocks[top.Blocks.Count - 1]), out _, out answerTo))
            {
                if (check) throw new InvalidOperationException("engine: a paused flow is not at an ask or wait statement");
                return false;
            }
            return true;
        }

        private bool IsBoundAs(Value value, string mod, string name) =>
            _scopes.TryGetValue(mod, out var scope) && scope.TryGet(name, out var bound, out _) && ReferenceEquals(bound, value);

        private static JsonArray SaveRange(RangeValue r) =>
            new JsonArray().Add(new JsonNumber(r.Start)).Add(new JsonNumber(r.Step)).Add(new JsonNumber(r.Count));

        private static JsonObject Tag(string tag, JsonNode value) => new JsonObject().Add(tag, value);

        /// <summary>
        /// 함수 본문이 들고 있는 값 하나 (G141). 월드 상태의 값(<see cref="SaveValue"/>)에 더해 뷰·답·컴포넌트 값·기여 행·<c>range</c>·
        /// 함수 값을 가리키는 대상으로 적는다. 엔티티 뷰(<c>{"entity_view": id}</c>)와 상태 값의 엔티티(<c>{"entity": id}</c>)는 구별한다.
        /// </summary>
        private JsonNode SaveScriptValue(Value value, FlowWriter writer)
        {
            switch (value)
            {
                case EntityView v:
                    return Tag("entity_view", new JsonNumber(v.Id.Value));
                case ListValue l:
                    var items = new JsonArray();
                    foreach (var item in l.Items) items.Add(SaveScriptValue(item, writer));
                    return items;
                case DictValue d:
                    var pairs = new JsonArray();
                    foreach (var item in d.Items)
                        pairs.Add(new JsonArray().Add(SaveScriptValue(item.Key, writer)).Add(SaveScriptValue(item.Value, writer)));
                    return Tag("dict", pairs);
                case TextValue t:
                    return SaveText(t, v => SaveScriptValue(v, writer));
                case OptionValue o:
                    return SaveOption(o, v => SaveScriptValue(v, writer));
                case ComponentView c:
                    return Tag("component", new JsonObject()
                        .Add("entity", new JsonNumber(c.Id.Value))
                        .Add("name", new JsonString(c.Component.Name))
                        .Add("row", c.Row == null ? (JsonNode)JsonNull.Instance : SaveKey(c.Row.Value)));
                case EntityGroup g:
                    var ids = new JsonArray();
                    foreach (var id in g.Ids) ids.Add(new JsonNumber(id.Value));
                    return Tag("entity_group", new JsonObject().Add("ids", ids).Add("label", new JsonString(g.Label)));
                case RowsView r:
                    return Tag("rows", new JsonObject()
                        .Add("entity", new JsonNumber(r.Id.Value))
                        .Add("name", new JsonString(r.Component.Name)));
                case DefView d:
                    return Tag("def_view", new JsonObject()
                        .Add("entity", new JsonNumber(d.Id))
                        .Add("type", new JsonString(d.Type.Name)));
                case AnswerValue a:
                    var answer = new JsonObject()
                        .Add("response", a.Response == null ? (JsonNode)JsonNull.Instance : new JsonString(a.Response))
                        .Add("index", a.Index == null ? (JsonNode)JsonNull.Instance : new JsonNumber(a.Index.Value))
                        .Add("choice", a.Choice == null ? (JsonNode)JsonNull.Instance : SaveValue(a.Choice))
                        .Add("by", a.By == null ? (JsonNode)JsonNull.Instance : new JsonNumber(a.By.Value.Value));
                    // 끊긴 응답일 때만 적는다 (G243)
                    if (a.Interrupted) answer.Add("interrupted", new JsonBool(true));
                    return Tag("answer", answer);
                case ComponentInitValue c:
                    var given = new JsonArray();
                    foreach (var pair in c.Values)
                        given.Add(new JsonArray().Add(new JsonString(pair.Key.Name)).Add(SaveScriptValue(pair.Value, writer)));
                    return Tag("component_value", new JsonObject()
                        .Add("name", new JsonString(c.Component.Name))
                        .Add("values", given));
                case SignalValue s:
                    var fields = new JsonObject();
                    foreach (var pair in s.Values) fields.Add(pair.Key.Name, SaveValue(pair.Value));
                    return Tag("signal", new JsonObject()
                        .Add("name", new JsonString(s.Signal.Name))
                        .Add("values", fields));
                case ContributionView c:
                    return Tag("contribution", SaveContribution(c.Row));
                case RangeValue r:
                    return Tag("range", SaveRange(r));
                case FunctionValue f:
                    if (f.Definition != null)
                    {
                        if (!IsBoundAs(f, f.Mod, f.Name))
                            throw new InvalidOperationException("the function value '" + f.Mod + "." + f.Name +
                                                                "' cannot be saved: that name in its mod now holds something else");
                        return Tag("function", new JsonArray().Add(new JsonString(f.Mod)).Add(new JsonString(f.Name)));
                    }
                    // 모드 최상위에 묶은 람다(f = lambda ...): 그 모드의 이름으로 적는다
                    if (_scopes.TryGetValue(f.Mod, out var scope))
                        foreach (var name in scope.Names)
                            if (IsBoundAs(f, f.Mod, name))
                                return Tag("function", new JsonArray().Add(new JsonString(f.Mod)).Add(new JsonString(name)));
                    break;
                case PureBuiltin b:
                    if (Evaluator.Builtins.TryGetValue(b.Name, out var builtin) && ReferenceEquals(builtin, b))
                        return Tag("builtin", new JsonString(b.Name));
                    // 컴포넌트·신호 생성자는 내장과 이름이 같을 수 있어 따로 적는다 (G230)
                    if (ReferenceEquals(((WorldGlobals)Evaluator.Globals).SchemaValue(b.Name), b))
                        return Tag("schema", new JsonString(b.Name));
                    break;
                case HostCallable host:
                    // 이름 문자열만 남긴다: C# 이름은 저장에 들어가지 않는다 (S7, G221)
                    if (SavableHostName(host) is { } hostName) return Tag("host", new JsonString(hostName));
                    break;
                case ClosureValue closure:
                    return SaveClosure(closure, writer);
                default:
                    return SaveValue(value);
            }
            throw new InvalidOperationException("a paused flow holds a " + value.KindName + " value, which cannot be saved yet");
        }

        private static JsonObject SaveContribution(Contribution c) =>
            new JsonObject()
                .Add("id", new JsonNumber(c.Id))
                .Add("target", new JsonNumber(c.Target))
                .Add("component", new JsonString(c.Component.Name))
                .Add("row", c.Row == null ? (JsonNode)JsonNull.Instance : SaveKey(c.Row.Value))
                .Add("member", new JsonString(c.Member.Name))
                .Add("op", new JsonString(c.Op))
                .Add("value", new JsonNumber(c.Value.Raw))
                .Add("source", c.Source == null ? (JsonNode)JsonNull.Instance : new JsonNumber(c.Source.Value));
    }
}
