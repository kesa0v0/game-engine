using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 모드 봇 (spec-gaps G367~G377): 호스트가 <see cref="NewBot"/>으로 봇을 붙이고 판이 바뀔 때마다 <see cref="WorldView.Think"/>로 한 번씩
    /// 생각하게 한다. 봇은 그 연결의 눈으로 판을 읽고 <c>bot.…</c>에 기억하며 <c>submit</c>으로 수를 적는다. 게임은 바꾸지 않는다.
    /// </summary>
    public sealed partial class World
    {
        private readonly IReadOnlyDictionary<string, CommandDecl> _bots =
            new SortedDictionary<string, CommandDecl>(StringComparer.Ordinal);

        /// <summary>생각 중인 봇. 없으면 스크립트의 <c>bot</c>은 실행 오류다.</summary>
        private BotRun? _botRun;

        private sealed class BotRun
        {
            public BotRun(SortedDictionary<string, SortedDictionary<string, Value>> draft) => Draft = draft;

            public SortedDictionary<string, SortedDictionary<string, Value>> Draft { get; }
            public List<UiSubmission> Submissions { get; } = new List<UiSubmission>();
        }

        /// <summary><c>@bot</c> id들 (<c>모드id.함수이름</c>, 이름순).</summary>
        public IEnumerable<string> Bots => _bots.Keys;

        /// <summary>봇의 설정 매개변수 (첫 매개변수인 맡은 엔티티 뒤의 것, 기본값 포함, G370).</summary>
        public IReadOnlyList<CommandParameter> BotSettings(string bot)
        {
            var parameters = FindBot(bot).Parameters;
            var settings = new List<CommandParameter>();
            for (int i = 1; i < parameters.Count; i++) settings.Add(parameters[i]);
            return settings;
        }

        private CommandDecl FindBot(string bot)
        {
            if (_bots.TryGetValue(bot, out var decl)) return decl;
            throw new WorldException(Codes.RunUnknownName, "there is no bot '" + bot + "'", Suggest.Hint(bot, _bots.Keys));
        }

        private static bool HasSetting(CommandDecl bot, string name)
        {
            for (int i = 1; i < bot.Parameters.Count; i++)
                if (bot.Parameters[i].Name == name) return true;
            return false;
        }

        private static ScriptRuntimeException OutsideBot(string what) =>
            new ScriptRuntimeException(Codes.RunNotWritable, what + " can only be used while a @bot thinks",
                "a bot's memory belongs to that bot and is not game state (spec-gaps G374)");

        /// <summary>
        /// 봇을 붙인다 (G368~G370·G373·G375). <paramref name="settings"/>는 첫 매개변수 뒤의 매개변수 이름 → 값이고 빠진 것은 기본값이다.
        /// <paramref name="seed"/>가 없으면 월드 시드·맡은 엔티티·봇 id에서 만든다. <paramref name="stepBudget"/>이 없으면 판의 행동 예산이다.
        /// </summary>
        public BotState NewBot(string bot, EntityId me, IReadOnlyDictionary<string, Value>? settings = null, long? seed = null,
            int? stepBudget = null)
        {
            var decl = FindBot(bot);
            var given = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            if (settings != null)
                foreach (var pair in settings)
                {
                    if (!HasSetting(decl, pair.Key))
                    {
                        var names = new List<string>();
                        foreach (var p in BotSettings(bot)) names.Add(p.Name);
                        throw new WorldException(Codes.RunUnknownName, "the bot " + bot + " has no setting '" + pair.Key + "'",
                            Suggest.Hint(pair.Key, names));
                    }
                    given[pair.Key] = pair.Value;
                }
            if (stepBudget < 1) throw new WorldException(Codes.RunRange, "a bot's step budget must be at least 1, not " + stepBudget);
            return new BotState(_schema, decl.Id, me, given, seed ?? DefaultBotSeed(decl.Id, me), stepBudget ?? _actionStepBudget);
        }

        /// <summary>봇의 기본 시드: SHA-256(월드 시드, 엔티티 id, 봇 id)의 앞 8바이트 (G373).</summary>
        private long DefaultBotSeed(string bot, EntityId me)
        {
            var id = Encoding.UTF8.GetBytes(bot);
            var input = new byte[16 + id.Length];
            WriteLittleEndian(input, 0, _seed);
            WriteLittleEndian(input, 8, me.Value);
            Array.Copy(id, 0, input, 16, id.Length);
            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(input);
            long seed = 0;
            for (int i = 0; i < 8; i++) seed |= (long)digest[i] << (8 * i);
            return seed;
        }

        /// <summary>봇 난수열: 키 = SHA-256(봇 시드, "bot"). 게임 난수열과 따로다 (G373).</summary>
        private static RandStream BotStream(BotState bot)
        {
            var tag = Encoding.UTF8.GetBytes("bot");
            var input = new byte[8 + tag.Length];
            WriteLittleEndian(input, 0, bot.Seed);
            Array.Copy(tag, 0, input, 8, tag.Length);
            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(input);
            var key = new uint[8];
            for (int i = 0; i < 8; i++)
                key[i] = digest[4 * i] | (uint)digest[4 * i + 1] << 8 | (uint)digest[4 * i + 2] << 16 | (uint)digest[4 * i + 3] << 24;
            return new RandStream("bot:" + bot.Bot, 0, bot.RandomCalls) { Key = key };
        }

        internal ThinkResult ThinkAs(WorldView viewer, BotState bot)
        {
            NoHostWrites("Think");
            if (!bot.Belongs(_schema)) throw new ArgumentException("this BotState was made by another world", nameof(bot));
            var decl = FindBot(bot.Bot);
            if (_act != null || _botRun != null || _meter != null)
                throw new InvalidOperationException("a bot cannot think while a UI action, another bot or a command runs");

            var args = new SortedDictionary<string, Value>(StringComparer.Ordinal) { [decl.Parameters[0].Name] = new EntityValue(bot.Me) };
            foreach (var pair in bot.SortedSettings) args[pair.Key] = pair.Value;
            var run = new BotRun(bot.Draft());
            var stream = BotStream(bot);
            var savedViewer = _viewer;
            var savedRand = _rand;
            _viewer = viewer; // 맡은 엔티티가 안 보이면 죽은 것과 같다 (G198)
            _botRun = run;
            _rand = stream;
            try
            {
                var rejections = new List<ArgumentRejection>();
                var checkedArgs = CheckArguments(decl, args, rejections);
                if (rejections.Count > 0) return new ThinkResult(decl.Id, ThinkOutcome.ArgumentsRejected) { ArgumentRejections = rejections };
                var scriptArgs = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                foreach (var pair in checkedArgs) scriptArgs[pair.Key] = ToScript(pair.Value);
                _meter = new StepMeter(bot.StepBudget);
                Evaluator.RunBot(decl.Function, scriptArgs, decl.Span, _meter);
            }
            catch (ScriptRuntimeException e)
            {
                return new ThinkResult(decl.Id, ThinkOutcome.ScriptError) { Error = ToWorldException(e, "the bot " + decl.Id) };
            }
            catch (WorldException e)
            {
                return new ThinkResult(decl.Id, ThinkOutcome.ScriptError) { Error = e };
            }
            finally
            {
                _meter = null;
                _viewer = savedViewer;
                _botRun = null;
                _rand = savedRand;
            }
            bot.Commit(run.Draft, stream.Calls);
            return new ThinkResult(decl.Id, ThinkOutcome.Thought) { Submissions = run.Submissions };
        }

        // ---- 봇 저장 (G372) ----

        /// <summary>
        /// <c>{"bot", "me", "seed", "calls", "settings", "states"}</c>. 예산은 남기지 않는다(호스트가 붙일 때 정한다).
        /// 값은 월드 저장과 같은 모양이다.
        /// </summary>
        internal static byte[] SaveBot(BotState bot)
        {
            var settings = new JsonObject();
            foreach (var pair in bot.SortedSettings) settings.Add(pair.Key, SaveValue(pair.Value));
            var states = new JsonObject();
            foreach (var record in bot.Records)
            {
                var fields = new JsonObject();
                foreach (var field in record.Value) fields.Add(field.Key, SaveValue(field.Value));
                states.Add(record.Key, fields);
            }
            return CanonicalJson.Write(new JsonObject()
                .Add("bot", new JsonString(bot.Bot))
                .Add("me", new JsonNumber(bot.Me.Value))
                .Add("seed", new JsonNumber(bot.Seed))
                .Add("calls", new JsonNumber(bot.RandomCalls))
                .Add("settings", settings)
                .Add("states", states));
        }

        /// <summary>
        /// <see cref="BotState.Save"/>한 봇을 되살린다. 모드가 바뀌어 없어진 기록·칸·설정은 버리고, 새 칸은 기본값, 타입이 바뀌어 맞지 않는
        /// 값은 기본값으로 두고 그 일을 <paramref name="warnings"/>에 적는다. 봇 자체가 없어졌거나 파일이 깨졌으면 <see cref="WorldException"/>이다.
        /// </summary>
        public BotState LoadBot(byte[] saved, out IReadOnlyList<string> warnings, int? stepBudget = null)
        {
            var notes = new List<string>();
            warnings = notes;
            JsonObject root;
            try
            {
                root = AsObject(CanonicalJson.Read(saved), "a saved bot");
            }
            catch (JsonFormatException e)
            {
                throw new WorldException(Codes.RunScript, "the saved bot is broken: " + e.Message);
            }
            try
            {
                string id = Text(Field(root, "bot"), "the bot id");
                var decl = FindBot(id);
                var settings = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                foreach (var pair in AsObject(Field(root, "settings"), "bot settings").Members)
                {
                    if (HasSetting(decl, pair.Key)) settings[pair.Key] = LoadValue(pair.Value);
                    else notes.Add("the bot " + id + " no longer has the setting '" + pair.Key + "'; it was dropped");
                }
                var bot = new BotState(_schema, decl.Id, new EntityId(Long(Field(root, "me"), "the bot's entity")), settings,
                    Long(Field(root, "seed"), "the bot seed"), stepBudget ?? _actionStepBudget);
                var draft = bot.Draft();
                foreach (var record in AsObject(Field(root, "states"), "bot states").Members)
                {
                    if (!_schema.TryGetBotState(record.Key, out var state))
                    {
                        notes.Add("there is no bot state '" + record.Key + "' any more; its values were dropped");
                        continue;
                    }
                    foreach (var field in AsObject(record.Value, "a bot state").Members)
                    {
                        if (!state.TryGetMember(field.Key, out var member))
                        {
                            notes.Add("bot state '" + record.Key + "' has no field '" + field.Key + "' any more; its value was dropped");
                            continue;
                        }
                        var value = LoadValue(field.Value);
                        if (!(member.Kind == MemberKind.Ref && value is NoneValue) && !Fits(member.Spec.Type, value))
                        {
                            notes.Add("'" + member.Display + "' no longer fits its saved value; it starts from its default");
                            continue;
                        }
                        draft[record.Key][field.Key] = value;
                    }
                }
                bot.Commit(draft, Long(Field(root, "calls"), "the bot's random calls"));
                return bot;
            }
            catch (JsonFormatException e)
            {
                throw new WorldException(Codes.RunScript, "the saved bot is broken: " + e.Message);
            }
        }

        /// <summary>저장된 값이 칸의 지금 타입에 맞나 (모양만). 엔티티는 죽었어도 맞다: 읽을 때 거른다 (G279).</summary>
        private static bool Fits(TypeRef type, Value value)
        {
            switch (type)
            {
                case OptionalTypeRef o: return value is NoneValue || Fits(o.Inner, value);
                case EntityRefTypeRef _: return value is EntityValue;
                case ListTypeRef l:
                    if (!(value is ListValue list)) return false;
                    foreach (var item in list.Items)
                        if (!Fits(l.Element, item)) return false;
                    return true;
                case DictTypeRef d:
                    if (!(value is DictValue dict)) return false;
                    foreach (var item in dict.Items)
                        if (!Fits(d.Key, item.Key) || !Fits(d.Value, item.Value)) return false;
                    return true;
                case PrimitiveTypeRef p:
                    switch (p.Name)
                    {
                        case "int": return value is IntValue;
                        case "fixed": return value is FixedValue || value is IntValue;
                        case "bool": return value is BoolValue;
                        case "str": return value is StrValue;
                    }
                    return false;
            }
            return true; // DefId 같은 문자열 타입은 Def가 바뀌어도 읽을 때 드러난다
        }
    }
}
