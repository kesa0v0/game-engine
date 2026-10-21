using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>멈춘 Flow 불러오기: 프레임·블록·람다·질문을 다시 만들고 값을 잇는다, 어긋남은 <c>SAV</c> 진단 (S8, G141·G143·G214).</summary>
    public sealed partial class World
    {
        // ---- 불러오기 ----

        private void RestoreFlows(JsonArray saved, LoadProblems problems)
        {
            long lastFlow = 0;
            var requestIds = new HashSet<long>();
            var savedFlows = new Dictionary<long, string>();
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
                // 이 Flow에서 난 어긋남은 모아 두었다가, 호스트가 허락했으면 Flow를 취소하는 경고 하나로 바꾼다 (G214)
                problems.EnterFlow();
                var record = RestoreFlow(obj, id, handler, command, where, requestIds, problems);
                var requestNode = obj.Get("request");
                long? request = requestNode is JsonObject r && r.Get("id") is JsonNumber n ? n.Value : (long?)null;
                bool broken = problems.LeaveFlow(where, request);
                if (record != null && !broken) _flows.Add(id, record);
                savedFlows.Add(id, where);
            }
            // 불러오며 응답 본문 Flow를 취소했으면 그것을 기다리던 바깥도 취소한다: 불러오기는 스크립트를 돌리지 않아 바깥을
            // r.interrupted로 이어 갈 수 없다. 안쪽부터 보아 여러 겹도 함께 (G243)
            var ids = new List<long>(_flows.Keys);
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                var flow = _flows[ids[i]];
                if (!(flow.AwaitsChild is long child) || _flows.ContainsKey(child)) continue;
                if (!savedFlows.TryGetValue(child, out var childWhere))
                    throw new JsonFormatException(savedFlows[flow.Id] + " awaits flow " + child + ", which is not in the save");
                _flows.Remove(flow.Id);
                problems.CanceledWith(savedFlows[flow.Id] + " was canceled: the response it awaited, " + childWhere + ", was canceled");
            }
            // 바깥 Flow와 응답 본문 Flow는 서로를 가리켜야 한다 (G243). 바깥이 먼저 끝났거나 취소되었으면 안쪽은 짝 없이 남는다
            foreach (var flow in _flows.Values)
            {
                if (flow.Parent != null && _flows.TryGetValue(flow.Parent.Flow, out var outer) && outer.AwaitsChild != flow.Id)
                    throw new JsonFormatException("flow " + flow.Id + " names flow " + outer.Id + " as its parent, which does not await it");
                if (flow.AwaitsChild is long child && _flows.TryGetValue(child, out var inner) && inner.Parent?.Flow != flow.Id)
                    throw new JsonFormatException("flow " + flow.Id + " awaits flow " + child + ", which does not name it as its parent");
            }
        }

        /// <summary>Flow 하나를 되살린다. 어긋나면(문제는 <paramref name="problems"/>에 적었다) 또는 함께 버렸으면 null.</summary>
        private FlowRecord? RestoreFlow(JsonObject obj, long id, bool handler, string command, string where, HashSet<long> requestIds,
            LoadProblems problems)
        {
            {
                if (handler ? !_handlerById.ContainsKey(command) : !_commands.ContainsKey(command))
                {
                    problems.Add(where + (handler ? ": the handler no longer exists" : ": the command no longer exists"));
                    return null;
                }

                var wakeNode = obj.Get("wake_at");
                long? wakeAt = null;
                if (wakeNode != null)
                {
                    wakeAt = Long(wakeNode, "a wake tick");
                    if (wakeAt.Value <= _tick) throw new JsonFormatException(where + " should have woken by tick " + wakeAt.Value);
                }
                // 응답 본문을 기다리는 Flow는 질문도 깨울 틱도 없다 (G243)
                var awaitsNode = obj.Get("awaits");
                long? awaits = null;
                if (awaitsNode != null)
                {
                    if (wakeNode != null) throw new JsonFormatException(where + " cannot both sleep and await a response");
                    awaits = Long(awaitsNode, "an awaited flow");
                    if (awaits.Value <= id || awaits.Value >= _nextFlow)
                        throw new JsonFormatException(where + " awaits flow " + awaits.Value + ", which must come after it");
                }
                ParentLink? parent = null;
                if (obj.Get("parent") is JsonNode parentNode)
                {
                    var p = AsObject(parentNode, "a flow parent");
                    long outer = Long(Field(p, "flow"), "a parent flow");
                    if (outer >= id) throw new JsonFormatException(where + " has parent flow " + outer + ", which must come before it");
                    parent = new ParentLink(outer, Text(Field(p, "response"), "a parent response"),
                        new EntityId(SavedId(Field(p, "by"), "a parent responder")));
                }
                var request = wakeAt == null && awaits == null ? LoadRequest(obj, id, command, where, requestIds, problems) : null;

                // 람다는 프레임이나 묶음의 사전을 잡고, 사전은 람다를 든다(자기를 잡을 수도 있다).
                // 그래서 빈 프레임 → 빈 묶음 → 람다 → 값 채우기 순서로 만든다 (G143)
                var frameNodes = new List<JsonObject>();
                foreach (var f in AsArray(Field(obj, "frames"), "frames").Items) frameNodes.Add(AsObject(f, "a frame"));
                if (frameNodes.Count == 0) throw new JsonFormatException(where + " has no frames");
                var frames = new List<Frame>();
                foreach (var f in frameNodes)
                {
                    var fn = PausedFunction(f, where, problems);
                    if (fn != null) frames.Add(Evaluator.NewFrame(fn));
                }
                if (frames.Count != frameNodes.Count)
                {
                    // 바뀐 함수를 한 번에 모두 보이도록 람다가 든 함수도 마저 본다 (S8 "바뀐 함수 목록")
                    foreach (var c in AsArray(Field(obj, "closures"), "closures").Items)
                        SavedLambda(AsObject(c, "a closure"), where, problems, out _);
                    return null;
                }

                var envNodes = new List<JsonObject>();
                var envs = new List<LocalTable>();
                foreach (var e in AsArray(Field(obj, "envs"), "envs").Items)
                {
                    var env = AsObject(e, "an env");
                    var envNames = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var n in AsArray(Field(env, "names"), "env names").Items) envNames.Add(Text(n, "a local name"));
                    envNodes.Add(env);
                    envs.Add(new LocalTable(new FrameLayout(envNames)));
                }

                var closures = RestoreClosures(AsArray(Field(obj, "closures"), "closures"), frames, envs, where, problems);
                if (closures == null) return null;
                problems.Closures = closures;
                for (int k = 0; k < envNodes.Count; k++)
                    foreach (var pair in AsObject(Field(envNodes[k], "locals"), "env locals").Members)
                    {
                        if (!envs[k].TrySet(pair.Key, LoadScriptValue(pair.Value, where, problems)))
                            throw new JsonFormatException(where + ": an env holds '" + pair.Key + "', which is not among its names");
                    }
                bool broken = false;
                for (int i = 0; i < frames.Count; i++)
                    broken |= RestoreFrame(frames[i], frameNodes[i], where, problems) == null;
                problems.Closures = null;
                if (broken) return null;
                if (!Relink(EntrySpan(command, handler), frames, out var answerTo, check: false))
                {
                    problems.Add(where + ": the function '" + frames[0].Function.Name +
                                 "' or a function it called changed, so the paused position no longer exists");
                    return null;
                }

                Value payload = awaits != null ? NoneValue.Instance
                    : request == null
                    ? new WaitPayload(wakeAt!.Value - _tick)
                    : new AskPayload(request.Who, request.Title, request.Choices, request.Responses, request.ResponseNames,
                        request.Hint, request.Data) { BlocksTime = request.BlocksTime };
                var run = new PureEvaluator.Suspended(frames, answerTo, payload);
                var subjects = new List<EntityId>();
                foreach (var s in AsArray(Field(obj, "subjects"), "flow subjects").Items)
                    subjects.Add(new EntityId(SavedId(s, "a flow subject")));
                List<EntityId>? about = null;
                var aboutNode = Field(obj, "about");
                if (!(aboutNode is JsonNull))
                {
                    about = new List<EntityId>();
                    foreach (var s in AsArray(aboutNode, "flow about").Items) about.Add(new EntityId(SavedId(s, "a flow about entity")));
                }
                var record = new FlowRecord(id, command, run, request, handler, wakeAt)
                {
                    Subjects = subjects, About = about, Rand = LoadStream(Field(obj, "rand"), command),
                    AwaitsChild = awaits, Parent = parent,
                };
                // 실행이 끝날 때마다 버리므로 대상이 사라진 Flow는 저장에 있을 수 없다 (G164)
                var stale = StaleReason(record);
                if (stale != null)
                {
                    // Def가 없어져 만들지 못한 엔티티 때문이면 버릴 때는 런타임의 대상 소멸처럼 취소하고(G211), 거부할 때는 원인을
                    // 이미 적었으니 따로 적지 않는다. 그 밖은 저장이 깨진 것이다
                    if (!Involves(record, problems.Skipped))
                        throw new JsonFormatException(where + " should have been canceled: " + stale);
                    problems.DroppedWith(where + " was canceled: " + stale);
                    return null;
                }
                return record;
            }
        }

        /// <summary>Flow의 대상이나 질문의 답할 엔티티 중에 <paramref name="ids"/>가 있는가.</summary>
        private static bool Involves(FlowRecord flow, HashSet<long> ids)
        {
            foreach (var e in flow.About ?? flow.Subjects)
                if (ids.Contains(e.Value)) return true;
            if (flow.Request != null)
                foreach (var e in flow.Request.Who)
                    if (ids.Contains(e.Value)) return true;
            return false;
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
            var choices = new List<OptionValue>();
            foreach (var c in AsArray(Field(r, "choices"), "choices").Items)
                choices.Add(LoadValue(c) is OptionValue option ? option : new OptionValue(LoadValue(c)));
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
        private List<ClosureValue>? RestoreClosures(JsonArray saved, List<Frame> frames,
            List<LocalTable> envs, string where, LoadProblems problems)
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
                LocalTable captured;
                switch (env.Members[0].Key)
                {
                    case "frame":
                        if (index < 0 || index >= frames.Count) throw new JsonFormatException("a lambda env frame is out of range");
                        captured = frames[(int)index].Locals;
                        break;
                    case "env":
                        if (index < 0 || index >= envs.Count) throw new JsonFormatException("a lambda env is out of range");
                        captured = envs[(int)index];
                        break;
                    default:
                        throw new JsonFormatException("a lambda env should be {\"frame\": i} or {\"env\": i}");
                }

                if (lambda == null) broken = true;
                else closures.Add(new ClosureValue(new FunctionValue(lambda, mod), captured));
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
        private Frame? RestoreFrame(Frame frame, JsonObject obj, string where, LoadProblems problems)
        {
            var fn = frame.Function;
            string mod = fn.Mod;
            string name = fn.Name;
            foreach (var pair in AsObject(Field(obj, "locals"), "locals").Members)
            {
                if (!frame.Locals.TrySet(pair.Key, LoadScriptValue(pair.Value, where, problems)))
                    throw new JsonFormatException(where + ": the paused function '" + mod + "." + name + "' has no local '" + pair.Key + "'");
            }

            string changed = where + ": the function '" + mod + "." + name +
                             "' changed, so the paused position no longer exists";
            var blocks = AsArray(Field(obj, "blocks"), "blocks").Items;
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = AsObject(blocks[i], "a block");
                long next = Long(Field(b, "next"), "a block position");
                Block block;
                if (i == 0) block = new Block(fn.Definition!.Body);
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
                            block = new Block(branch == ifs.Branches.Count ? ifs.OrElse! : ifs.Branches[(int)branch].Body);
                            break;
                        case ForStmt loop:
                            block = new Block(loop.Body) { Loop = loop };
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
                            var live = b.Get("live");
                            if (live != null) block.Live = LoadScriptValue(live, where, problems) as IRuntimeLiveCollection;
                            block.Count = block.Range?.Count ?? block.Items!.Count;
                            if (block.Index < -1 || block.Index >= block.Count)
                                throw new JsonFormatException("a loop index is out of range");
                            break;
                        case WhileStmt loop:
                            block = new Block(loop.Body) { While = loop };
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
                        case "entity_group":
                        {
                            var v = AsObject(tag.Value, "an entity group");
                            var ids = new List<EntityId>();
                            foreach (var n in AsArray(Field(v, "ids"), "entity group ids").Items)
                                ids.Add(new EntityId(SavedId(n, "an entity group member")));
                            return new EntityGroup(this, ids, Text(Field(v, "label"), "an entity group label"));
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
                                by is JsonNull ? (EntityId?)null : new EntityId(SavedId(by, "an answer entity")),
                                v.Get("interrupted") is JsonNode cut && Bool(cut, "an answer's interrupted flag"));
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
                            return component == null ? (Value)NoneValue.Instance : new ComponentInitValue(this, component, values);
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
                        case "declared_contribution":
                        {
                            var v = AsObject(tag.Value, "a declared contribution");
                            var keyNode = Field(v, "key");
                            return new DeclaredContributionView(this, SavedId(Field(v, "source"), "a contribution source"),
                                Text(Field(v, "component"), "a contribution component"),
                                keyNode is JsonNull ? null : Text(keyNode, "a contribution key"),
                                Text(Field(v, "op"), "a contribution op"),
                                Fixed.FromRaw(Long(Field(v, "value"), "a contribution value")));
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
                            // G230 전의 저장은 컴포넌트·신호 생성자도 builtin으로 적었다
                            if (((WorldGlobals)Evaluator.Globals).SchemaValue(name) is { } old) return old;
                            problems.Add(where + ": the builtin '" + name + "' no longer exists");
                            return NoneValue.Instance;
                        }
                        case "schema":
                        {
                            string name = Text(tag.Value, "a component or signal name");
                            if (((WorldGlobals)Evaluator.Globals).SchemaValue(name) is { } constructor) return constructor;
                            problems.Add(where + ": the component or signal '" + name + "' no longer exists");
                            return NoneValue.Instance;
                        }
                        case "host":
                        {
                            string name = Text(tag.Value, "a host function name");
                            if (HostValue(name) is { } host) return host;
                            problems.Add(where + ": the host function '" + name + "' is not registered");
                            return NoneValue.Instance;
                        }
                    }
                    break;
                case JsonObject o when IsTextNode(o):
                    return LoadText(o, n => LoadScriptValue(n, where, problems));
                case JsonObject o when IsOptionNode(o):
                    return LoadOption(o, n => LoadScriptValue(n, where, problems));
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
