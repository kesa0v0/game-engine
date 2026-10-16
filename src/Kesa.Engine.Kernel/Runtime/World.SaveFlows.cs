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
                if (flow.Request != null) saved.Add("request", SaveRequest(flow.Request));
                else saved.Add("wake_at", new JsonNumber(flow.WakeAt!.Value));
                flows.Add(saved
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
            public FlowWriter(List<PureEvaluator.Frame> frames) => Frames = frames;

            public List<PureEvaluator.Frame> Frames { get; }
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
                if (!ReferenceEquals(closure.CapturedLocals, writer.Frames[i].LocalNames))
                    throw new InvalidOperationException("engine: a lambda's local names are not its frame's");
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
            foreach (var name in closure.CapturedLocals) names.Add(new JsonString(name));
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
            foreach (var c in request.Choices) choices.Add(SaveValue(c));
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

        private JsonObject SaveFrame(PureEvaluator.Frame frame, FlowWriter writer)
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
                            break;
                        default:
                            throw new InvalidOperationException("engine: a paused block was not opened by an if or a for");
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
        private static Stmt? Opener(PureEvaluator.Block block) => block.Next >= 1 ? block.Body[block.Next - 1] : null;

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

        private bool Relink(SourceSpan entry, List<PureEvaluator.Frame> frames, out string? answerTo, bool check)
        {
            answerTo = null;
            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                SourceSpan site;
                var result = PureEvaluator.ResultTo.Discard;
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
                    if (resultName != null) result = PureEvaluator.ResultTo.Local;
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
                    var names = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var pair in t.Args) names.Add(pair.Key);
                    var args = new JsonObject();
                    foreach (var name in names) args.Add(name, SaveScriptValue(t.Args[name], writer));
                    return new JsonObject().Add("text", new JsonString(t.Key)).Add("args", args);
                case ComponentView c:
                    return Tag("component", new JsonObject()
                        .Add("entity", new JsonNumber(c.Id.Value))
                        .Add("name", new JsonString(c.Component.Name))
                        .Add("row", c.Row == null ? (JsonNode)JsonNull.Instance : SaveKey(c.Row.Value)));
                case RowsView r:
                    return Tag("rows", new JsonObject()
                        .Add("entity", new JsonNumber(r.Id.Value))
                        .Add("name", new JsonString(r.Component.Name)));
                case DefView d:
                    return Tag("def_view", new JsonObject()
                        .Add("entity", new JsonNumber(d.Id))
                        .Add("type", new JsonString(d.Type.Name)));
                case AnswerValue a:
                    return Tag("answer", new JsonObject()
                        .Add("response", a.Response == null ? (JsonNode)JsonNull.Instance : new JsonString(a.Response))
                        .Add("index", a.Index == null ? (JsonNode)JsonNull.Instance : new JsonNumber(a.Index.Value))
                        .Add("choice", a.Choice == null ? (JsonNode)JsonNull.Instance : SaveValue(a.Choice))
                        .Add("by", a.By == null ? (JsonNode)JsonNull.Instance : new JsonNumber(a.By.Value.Value)));
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

        // ---- 불러오기 ----

        private void RestoreFlows(JsonArray saved, LoadProblems problems)
        {
            long lastFlow = 0;
            var requestIds = new HashSet<long>();
            foreach (var node in saved.Items)
            {
                var obj = AsObject(node, "a flow");
                long id = Long(Field(obj, "id"), "a flow id");
                if (id <= lastFlow || id >= _nextFlow)
                    throw new JsonFormatException("flow ids must be increasing and below counters.flow");
                lastFlow = id;
                // 핸들러가 만든 Flow는 "command" 대신 "handler"를 적는다 (G148)
                var handlerNode = obj.Get("handler");
                bool handler = handlerNode != null;
                string command = handler
                    ? Text(handlerNode!, "a flow handler")
                    : Text(Field(obj, "command"), "a flow command");
                string where = "flow " + id.ToString(CultureInfo.InvariantCulture) + " (" + command + ")";
                if (handler ? !_handlerById.ContainsKey(command) : !_commands.ContainsKey(command))
                {
                    problems.Add(where + (handler ? ": the handler no longer exists" : ": the command no longer exists"));
                    continue;
                }

                var wakeNode = obj.Get("wake_at");
                long? wakeAt = null;
                if (wakeNode != null)
                {
                    wakeAt = Long(wakeNode, "a wake tick");
                    if (wakeAt.Value <= _tick) throw new JsonFormatException(where + " should have woken by tick " + wakeAt.Value);
                }
                var request = wakeAt == null ? LoadRequest(obj, id, command, where, requestIds, problems) : null;

                // 람다는 프레임이나 묶음의 사전을 잡고, 사전은 람다를 든다(자기를 잡을 수도 있다).
                // 그래서 빈 프레임 → 빈 묶음 → 람다 → 값 채우기 순서로 만든다 (G143)
                var frameNodes = new List<JsonObject>();
                foreach (var f in AsArray(Field(obj, "frames"), "frames").Items) frameNodes.Add(AsObject(f, "a frame"));
                if (frameNodes.Count == 0) throw new JsonFormatException(where + " has no frames");
                var frames = new List<PureEvaluator.Frame>();
                foreach (var f in frameNodes)
                {
                    var fn = PausedFunction(f, where, problems);
                    if (fn != null) frames.Add(Evaluator.NewFrame(fn, new SortedDictionary<string, Value>(StringComparer.Ordinal)));
                }
                if (frames.Count != frameNodes.Count)
                {
                    // 바뀐 함수를 한 번에 모두 보이도록 람다가 든 함수도 마저 본다 (S8 "바뀐 함수 목록")
                    foreach (var c in AsArray(Field(obj, "closures"), "closures").Items)
                        SavedLambda(AsObject(c, "a closure"), where, problems, out _);
                    continue;
                }

                var envNodes = new List<JsonObject>();
                var envs = new List<KeyValuePair<SortedDictionary<string, Value>, SortedSet<string>>>();
                foreach (var e in AsArray(Field(obj, "envs"), "envs").Items)
                {
                    var env = AsObject(e, "an env");
                    var envNames = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var n in AsArray(Field(env, "names"), "env names").Items) envNames.Add(Text(n, "a local name"));
                    envNodes.Add(env);
                    envs.Add(new KeyValuePair<SortedDictionary<string, Value>, SortedSet<string>>(
                        new SortedDictionary<string, Value>(StringComparer.Ordinal), envNames));
                }

                var closures = RestoreClosures(AsArray(Field(obj, "closures"), "closures"), frames, envs, where, problems);
                if (closures == null) continue;
                problems.Closures = closures;
                for (int k = 0; k < envNodes.Count; k++)
                    foreach (var pair in AsObject(Field(envNodes[k], "locals"), "env locals").Members)
                        envs[k].Key[pair.Key] = LoadScriptValue(pair.Value, where, problems);
                bool broken = false;
                for (int i = 0; i < frames.Count; i++)
                    broken |= RestoreFrame(frames[i], frameNodes[i], where, problems) == null;
                problems.Closures = null;
                if (broken) continue;
                if (!Relink(EntrySpan(command, handler), frames, out var answerTo, check: false))
                {
                    problems.Add(where + ": the function '" + frames[0].Function.Name +
                                 "' or a function it called changed, so the paused position no longer exists");
                    continue;
                }

                Value payload = request == null
                    ? new WaitPayload(wakeAt!.Value - _tick)
                    : new AskPayload(request.Who, request.Title, request.Choices, request.Responses, request.ResponseNames,
                        request.Hint, request.Data) { BlocksTime = request.BlocksTime };
                var run = new PureEvaluator.Suspended(frames, answerTo, payload);
                _flows.Add(id, new FlowRecord(id, command, run, request, handler, wakeAt));
            }
        }

        private Request LoadRequest(JsonObject obj, long flow, string command, string where, HashSet<long> requestIds,
            LoadProblems problems)
        {
            var r = AsObject(Field(obj, "request"), "a request");
            long requestId = Long(Field(r, "id"), "a request id");
            if (requestId < 1 || requestId >= _nextRequest || !requestIds.Add(requestId))
                throw new JsonFormatException("request ids must be unique and below counters.request");
            var who = new List<EntityId>();
            foreach (var w in AsArray(Field(r, "who"), "who").Items) who.Add(new EntityId(Long(w, "an entity id")));
            var title = LoadValue(Field(r, "title"));
            var choices = new List<Value>();
            foreach (var c in AsArray(Field(r, "choices"), "choices").Items) choices.Add(LoadValue(c));
            var responses = new List<string>();
            foreach (var c in AsArray(Field(r, "responses"), "responses").Items)
            {
                string response = Text(c, "a response command");
                if (!_commands.TryGetValue(response, out var decl) || decl.Free)
                    problems.Add(where + ": the response command " + response + " no longer exists or is now free");
                responses.Add(response);
            }
            var names = new List<string>();
            foreach (var c in AsArray(Field(r, "response_names"), "response names").Items) names.Add(Text(c, "a response name"));
            if (names.Count != responses.Count) throw new JsonFormatException("a request has unmatched response names");
            var hintNode = Field(r, "hint");
            string? hint = hintNode is JsonNull ? null : Text(hintNode, "a hint");
            var data = LoadValue(Field(r, "data"));
            var request = new Request(requestId, flow, command, who, title, choices, responses, names, hint, data)
            {
                BlocksTime = Bool(Field(r, "blocks_time"), "blocks_time"),
                Deadline = Field(r, "deadline") is JsonNull ? (long?)null : Long(Field(r, "deadline"), "a deadline"),
            };
            var timeout = Field(r, "on_timeout");
            if (!(timeout is JsonNull))
            {
                var t = AsObject(timeout, "on_timeout");
                if (t.Members.Count != 1) throw new JsonFormatException("on_timeout should be {\"choice\": i} or {\"response\": i}");
                long index = Long(t.Members[0].Value, "an on_timeout index");
                switch (t.Members[0].Key)
                {
                    case "choice" when index >= 0 && index < choices.Count:
                        request.TimeoutChoice = index;
                        break;
                    case "response" when index >= 0 && index < responses.Count:
                        request.TimeoutResponse = index;
                        break;
                    default:
                        throw new JsonFormatException(where + ": on_timeout is not one of its choices or responses");
                }
            }
            // 열린 질문이 지킬 것 (G159~G161): 막으면서 마감이 있을 수 없고, 기본 답은 마감이 있어야 하며, 기본 답이 있는 마감은 지나지 않았다
            if (request.BlocksTime && request.Deadline != null)
                throw new JsonFormatException(where + ": a question that blocks time cannot have a deadline");
            if ((request.Deadline == null) != (request.TimeoutChoice == null && request.TimeoutResponse == null))
                throw new JsonFormatException(where + ": a question has a deadline without on_timeout or on_timeout without a deadline");
            if (request.TimesOut && request.Deadline!.Value <= _tick)
                throw new JsonFormatException(where + ": the question should have timed out at tick " + request.Deadline.Value);
            return request;
        }

        /// <summary>
        /// 람다 목록. 람다 식은 (든 함수, 순서 번호)로 다시 찾고 잡은 사전은 프레임이나 묶음의 것을 잇는다.
        /// 람다를 찾을 수 없으면 문제로 적고 null.
        /// </summary>
        private List<ClosureValue>? RestoreClosures(JsonArray saved, List<PureEvaluator.Frame> frames,
            List<KeyValuePair<SortedDictionary<string, Value>, SortedSet<string>>> envs, string where, LoadProblems problems)
        {
            var closures = new List<ClosureValue>();
            bool broken = false;
            foreach (var node in saved.Items)
            {
                var entry = AsObject(node, "a closure");
                var lambda = SavedLambda(entry, where, problems, out string mod);
                var env = AsObject(Field(entry, "env"), "a lambda env");
                if (env.Members.Count != 1) throw new JsonFormatException("a lambda env should be {\"frame\": i} or {\"env\": i}");
                long index = Long(env.Members[0].Value, "a lambda env");
                IReadOnlyDictionary<string, Value> captured;
                SortedSet<string> names;
                switch (env.Members[0].Key)
                {
                    case "frame":
                        if (index < 0 || index >= frames.Count) throw new JsonFormatException("a lambda env frame is out of range");
                        captured = frames[(int)index].Locals;
                        names = frames[(int)index].LocalNames;
                        break;
                    case "env":
                        if (index < 0 || index >= envs.Count) throw new JsonFormatException("a lambda env is out of range");
                        captured = envs[(int)index].Key;
                        names = envs[(int)index].Value;
                        break;
                    default:
                        throw new JsonFormatException("a lambda env should be {\"frame\": i} or {\"env\": i}");
                }

                if (lambda == null) broken = true;
                else closures.Add(new ClosureValue(new FunctionValue(lambda, mod), captured, names));
            }
            return broken ? null : closures;
        }

        /// <summary>
        /// 저장된 람다의 식: 든 함수를 찾아 본문 해시를 맞춰 보고(G144) 순서 번호로 고른다. 함수가 없거나 바뀌었으면 문제로 적고 null.
        /// </summary>
        private LambdaExpr? SavedLambda(JsonObject entry, string where, LoadProblems problems, out string mod)
        {
            var site = AsArray(Field(entry, "lambda"), "a lambda");
            if (site.Items.Count != 3) throw new JsonFormatException("a lambda should be [mod, function, number]");
            mod = Text(site.Items[0], "a lambda mod");
            string root = Text(site.Items[1], "a lambda function");
            long ordinal = Long(site.Items[2], "a lambda number");
            string hash = Text(Field(entry, "hash"), "a lambda function hash");

            List<LambdaExpr>? found = null;
            if (_scopes.TryGetValue(mod, out var scope) && scope.TryGet(root, out var bound, out _) && bound is FunctionValue fn)
            {
                if (!string.Equals(BodyHash.Of(fn), hash, StringComparison.Ordinal))
                {
                    problems.FunctionChanged(where + ": the function '" + mod + "." + root + "' changed since the save");
                    return null;
                }
                found = LambdaOrdinals.Of(fn);
            }
            if (found != null && ordinal >= 0 && ordinal < found.Count) return found[(int)ordinal];
            problems.Add(where + ": the lambda " + ordinal.ToString(CultureInfo.InvariantCulture) + " of '" + mod + "." + root +
                         "' that the flow holds no longer exists");
            return null;
        }

        /// <summary>멈춘 프레임의 함수. 지금 내용에 없으면 문제로 적고 null.</summary>
        private FunctionValue? PausedFunction(JsonObject obj, string where, LoadProblems problems)
        {
            string mod = Text(Field(obj, "mod"), "a frame mod");
            string name = Text(Field(obj, "function"), "a frame function");
            string hash = Text(Field(obj, "hash"), "a frame function hash");
            if (!(_scopes.TryGetValue(mod, out var scope) && scope.TryGet(name, out var bound, out _) &&
                  bound is FunctionValue fn && fn.Definition != null && fn.Name == name))
            {
                problems.Add(where + ": the paused function '" + mod + "." + name + "' no longer exists");
                return null;
            }
            if (string.Equals(BodyHash.Of(fn), hash, StringComparison.Ordinal)) return fn;
            problems.FunctionChanged(where + ": the function '" + mod + "." + name + "' changed since the save");
            return null;
        }

        /// <summary>빈 프레임에 지역 변수와 블록을 채운다. 멈춘 자리가 본문에 없으면 문제로 적고 null.</summary>
        private PureEvaluator.Frame? RestoreFrame(PureEvaluator.Frame frame, JsonObject obj, string where, LoadProblems problems)
        {
            var fn = frame.Function;
            string mod = fn.Mod;
            string name = fn.Name;
            foreach (var pair in AsObject(Field(obj, "locals"), "locals").Members)
                frame.Locals[pair.Key] = LoadScriptValue(pair.Value, where, problems);

            string changed = where + ": the function '" + mod + "." + name +
                             "' changed, so the paused position no longer exists";
            var blocks = AsArray(Field(obj, "blocks"), "blocks").Items;
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = AsObject(blocks[i], "a block");
                long next = Long(Field(b, "next"), "a block position");
                PureEvaluator.Block block;
                if (i == 0) block = new PureEvaluator.Block(fn.Definition!.Body);
                else
                {
                    switch (Opener(frame.Blocks[i - 1]))
                    {
                        case IfStmt ifs:
                            long branch = Long(Field(b, "branch"), "a branch");
                            if (branch < 0 || branch > ifs.Branches.Count || (branch == ifs.Branches.Count && ifs.OrElse == null))
                            {
                                problems.Add(changed);
                                return null;
                            }
                            block = new PureEvaluator.Block(branch == ifs.Branches.Count ? ifs.OrElse! : ifs.Branches[(int)branch].Body);
                            break;
                        case ForStmt loop:
                            block = new PureEvaluator.Block(loop.Body) { Loop = loop };
                            block.Index = Long(Field(b, "index"), "a loop index");
                            var range = b.Get("range");
                            if (range != null) block.Range = LoadRange(range);
                            else
                            {
                                var items = new List<Value>();
                                foreach (var item in AsArray(Field(b, "items"), "loop items").Items)
                                    items.Add(LoadScriptValue(item, where, problems));
                                block.Items = items;
                            }
                            block.Count = block.Range?.Count ?? block.Items!.Count;
                            if (block.Index < -1 || block.Index >= block.Count)
                                throw new JsonFormatException("a loop index is out of range");
                            break;
                        default:
                            problems.Add(changed);
                            return null;
                    }
                }
                if (next < 0 || next > block.Body.Count)
                {
                    problems.Add(changed);
                    return null;
                }
                block.Next = (int)next;
                frame.Blocks.Add(block);
            }
            if (frame.Blocks.Count == 0) throw new JsonFormatException("a paused frame has no blocks");
            return frame;
        }

        private static RangeValue LoadRange(JsonNode node)
        {
            var a = AsArray(node, "a range");
            if (a.Items.Count != 3) throw new JsonFormatException("a range should be [start, step, count]");
            long step = Long(a.Items[1], "a range step");
            long count = Long(a.Items[2], "a range count");
            if (step == 0 || count < 0) throw new JsonFormatException("a range has a bad step or count");
            return new RangeValue(Long(a.Items[0], "a range start"), step, count);
        }

        /// <summary>
        /// <see cref="SaveScriptValue"/>의 반대. 가리키는 컴포넌트나 함수가 지금 내용에 없으면 문제로 적고 None으로 둔다(불러오기는 실패한다).
        /// 가리키는 엔티티가 살아 있는지는 보지 않는다: 저장 전에도 죽은 엔티티를 가리킬 수 있었다.
        /// </summary>
        private Value LoadScriptValue(JsonNode node, string where, LoadProblems problems)
        {
            switch (node)
            {
                case JsonArray a:
                    var items = new List<Value>();
                    foreach (var item in a.Items) items.Add(LoadScriptValue(item, where, problems));
                    return new ListValue(items);
                case JsonObject o when o.Members.Count == 1:
                    var tag = o.Members[0];
                    switch (tag.Key)
                    {
                        case "entity_view":
                            return new EntityView(this, new EntityId(SavedId(tag.Value, "an entity view")));
                        case "dict":
                            var pairs = new List<DictItem>();
                            foreach (var p in AsArray(tag.Value, "a dict").Items)
                            {
                                var kv = AsArray(p, "a dict item");
                                if (kv.Items.Count != 2) throw new JsonFormatException("a dict item should be [key, value]");
                                pairs.Add(new DictItem(LoadScriptValue(kv.Items[0], where, problems),
                                    LoadScriptValue(kv.Items[1], where, problems), SourceSpan.None));
                            }
                            return new DictValue(pairs);
                        case "component":
                        {
                            var v = AsObject(tag.Value, "a component view");
                            var id = new EntityId(SavedId(Field(v, "entity"), "a component view"));
                            var component = SavedComponent(Field(v, "name"), where, problems);
                            if (component == null) return NoneValue.Instance;
                            var rowNode = Field(v, "row");
                            if ((rowNode is JsonNull) == component.IsKeyed)
                            {
                                problems.Add(where + ": '" + component.Name + "' changed whether it is keyed");
                                return NoneValue.Instance;
                            }
                            RowKey? row = rowNode is JsonNull ? (RowKey?)null : SavedKey(rowNode, component, where, problems);
                            if (component.IsKeyed && row == null) return NoneValue.Instance;
                            return new ComponentView(this, id, component, row);
                        }
                        case "rows":
                        {
                            var v = AsObject(tag.Value, "a rows view");
                            var id = new EntityId(SavedId(Field(v, "entity"), "a rows view"));
                            var component = SavedComponent(Field(v, "name"), where, problems);
                            if (component == null) return NoneValue.Instance;
                            if (!component.IsKeyed)
                            {
                                problems.Add(where + ": '" + component.Name + "' is no longer a keyed component");
                                return NoneValue.Instance;
                            }
                            return new RowsView(this, id, component);
                        }
                        case "def_view":
                        {
                            var v = AsObject(tag.Value, "a Def view");
                            long id = SavedId(Field(v, "entity"), "a Def view");
                            string type = Text(Field(v, "type"), "a Def type");
                            if (!_schema.TryGetType(type, out var defType))
                            {
                                problems.Add(where + ": the Def type '" + type + "' no longer exists");
                                return NoneValue.Instance;
                            }
                            return new DefView(this, id, defType);
                        }
                        case "answer":
                        {
                            var v = AsObject(tag.Value, "an answer");
                            var response = Field(v, "response");
                            var index = Field(v, "index");
                            var choice = Field(v, "choice");
                            var by = Field(v, "by");
                            return new AnswerValue(this,
                                response is JsonNull ? null : Text(response, "an answer response"),
                                index is JsonNull ? (long?)null : Long(index, "an answer index"),
                                index is JsonNull ? null : LoadValue(choice),
                                by is JsonNull ? (EntityId?)null : new EntityId(SavedId(by, "an answer entity")));
                        }
                        case "component_value":
                        {
                            var v = AsObject(tag.Value, "a component value");
                            var component = SavedComponent(Field(v, "name"), where, problems);
                            var values = new List<KeyValuePair<MemberDecl, Value>>();
                            foreach (var p in AsArray(Field(v, "values"), "component values").Items)
                            {
                                var kv = AsArray(p, "a component value item");
                                if (kv.Items.Count != 2) throw new JsonFormatException("a component value item should be [member, value]");
                                string member = Text(kv.Items[0], "a member name");
                                var value = LoadScriptValue(kv.Items[1], where, problems);
                                if (component == null) continue;
                                if (!component.TryGetMember(member, out var decl))
                                {
                                    problems.Add(where + ": '" + component.Name + "." + member + "' no longer exists");
                                    continue;
                                }
                                values.Add(new KeyValuePair<MemberDecl, Value>(decl, value));
                            }
                            return component == null ? (Value)NoneValue.Instance : new ComponentInitValue(component, values);
                        }
                        case "signal":
                        {
                            // 지금 선언과 맞춘다: 없어진 신호, 더해지거나 빠진 인자, 타입이 바뀐 인자는 SAV002 (G145)
                            var v = AsObject(tag.Value, "a signal value");
                            string name = Text(Field(v, "name"), "a signal name");
                            var given = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                            foreach (var pair in AsObject(Field(v, "values"), "signal values").Members)
                                given[pair.Key] = LoadValue(pair.Value);
                            if (!_schema.TryGetSignal(name, out var signal))
                            {
                                problems.Add(where + ": the signal '" + name + "' no longer exists");
                                return NoneValue.Instance;
                            }
                            var values = SignalValues(signal, given, out var mismatches);
                            foreach (var m in mismatches) problems.Add(where + ": signal " + name + ": " + m.Message);
                            return new SignalValue(this, signal, values);
                        }
                        case "contribution":
                        {
                            var row = LoadContributionRow(AsObject(tag.Value, "a contribution"), where, problems);
                            return row == null ? (Value)NoneValue.Instance : new ContributionView(this, row);
                        }
                        case "range":
                            return LoadRange(tag.Value);
                        case "closure":
                        {
                            long index = Long(tag.Value, "a lambda number");
                            var closures = problems.Closures;
                            if (closures == null || index < 0 || index >= closures.Count)
                                throw new JsonFormatException("a lambda number is out of range");
                            return closures[(int)index];
                        }
                        case "function":
                        {
                            var v = AsArray(tag.Value, "a function");
                            if (v.Items.Count != 2) throw new JsonFormatException("a function should be [mod, name]");
                            string mod = Text(v.Items[0], "a function mod");
                            string name = Text(v.Items[1], "a function name");
                            if (_scopes.TryGetValue(mod, out var scope) && scope.TryGet(name, out var fn, out _) && fn is FunctionValue)
                                return fn;
                            problems.Add(where + ": the function '" + mod + "." + name + "' no longer exists");
                            return NoneValue.Instance;
                        }
                        case "builtin":
                        {
                            string name = Text(tag.Value, "a builtin name");
                            if (Evaluator.Builtins.TryGetValue(name, out var builtin)) return builtin;
                            problems.Add(where + ": the builtin '" + name + "' no longer exists");
                            return NoneValue.Instance;
                        }
                    }
                    break;
                case JsonObject o when o.Members.Count == 2 && o.Members[0].Key == "text" && o.Members[1].Key == "args":
                    var args = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                    foreach (var arg in AsObject(o.Members[1].Value, "text arguments").Members)
                        args[arg.Key] = LoadScriptValue(arg.Value, where, problems);
                    return new TextValue(Text(o.Members[0].Value, "a text key"), args);
            }
            return LoadValue(node);
        }

        private long SavedId(JsonNode node, string what)
        {
            long id = Long(node, what);
            if (id < 1 || id >= _nextId) throw new JsonFormatException(what + " has an id outside counters.entity");
            return id;
        }

        private ComponentType? SavedComponent(JsonNode node, string where, LoadProblems problems)
        {
            string name = Text(node, "a component name");
            if (_schema.TryGetComponent(name, out var component)) return component;
            problems.Add(where + ": the component '" + name + "' no longer exists");
            return null;
        }

        /// <summary>뷰가 든 줄 키. 줄이 지금 있는지는 보지 않는다(뷰는 읽을 때 확인한다).</summary>
        private RowKey? SavedKey(JsonNode node, ComponentType component, string where, LoadProblems problems)
        {
            switch (component.KeyKind)
            {
                case KeyKind.Def:
                    return RowKey.Def(Text(node, "a row key"));
                case KeyKind.Entity:
                    return RowKey.Entity(new EntityId(EntityIdOf(node, "a row key")));
                default:
                    if (node is JsonNumber n) return RowKey.Int(n.Value);
                    problems.Add(where + ": '" + component.Name + "' changed its key kind");
                    return null;
            }
        }

        /// <summary>함수 본문이 들고 있던 기여 행. 이미 떼어진 행일 수 있어 월드의 행 목록과 잇지 않는다.</summary>
        private Contribution? LoadContributionRow(JsonObject obj, string where, LoadProblems problems)
        {
            long id = Long(Field(obj, "id"), "a contribution id");
            long target = SavedId(Field(obj, "target"), "a contribution target");
            var sourceNode = Field(obj, "source");
            long? source = sourceNode is JsonNull ? (long?)null : SavedId(sourceNode, "a contribution source");
            var component = SavedComponent(Field(obj, "component"), where, problems);
            if (component == null) return null;
            string memberName = Text(Field(obj, "member"), "a contribution member");
            if (!component.TryGetMember(memberName, out var member))
            {
                problems.Add(where + ": '" + component.Name + "." + memberName + "' no longer exists");
                return null;
            }
            var rowNode = Field(obj, "row");
            RowKey? row = null;
            if (!(rowNode is JsonNull))
            {
                row = SavedKey(rowNode, component, where, problems);
                if (row == null) return null;
            }
            return new Contribution(id, target, component, row, member, Text(Field(obj, "op"), "a contribution op"),
                Fixed.FromRaw(Long(Field(obj, "value"), "a contribution value")), source);
        }
    }
}
