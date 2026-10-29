using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>월드 표시의 값 (spec-gaps G388·G389·G394·G406): 그림 <c>look</c>과 자리 <c>at</c>·<c>onto</c>·<c>between</c>.</summary>
    public sealed partial class World
    {
        private const string PlainHint = "numbers, str, text(...), entities and lists or dicts of them";

        private void AddLookBuiltins(SortedDictionary<string, Value> table)
        {
            table.Add(PureBuiltins.LookName, new PureBuiltin(PureBuiltins.LookName, LookBuiltin));
            table.Add(PureBuiltins.AtName, new PureBuiltin(PureBuiltins.AtName, AtBuiltin));
            table.Add(PureBuiltins.OntoName, new PureBuiltin(PureBuiltins.OntoName, OntoBuiltin));
            table.Add(PureBuiltins.BetweenName, new PureBuiltin(PureBuiltins.BetweenName, BetweenBuiltin));
        }

        private Value LookBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            if (positional.Count != 1)
                throw LookError("look() takes the name of a look, got " + positional.Count + " arguments",
                    "write look(\"infantry\", hurt=True) (spec-gaps G389)");
            string name = Name("look()", "the name of a look", positional[0]);
            var keys = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var pair in keywords)
                keys[pair.Key] = Savable(pair.Key + "= of look()", pair.Value, "a look holds " + PlainHint + " (spec-gaps G389)", hidden: true);
            return new LookValue(name, keys);
        }

        private Value AtBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            NoPlaceKeywords("at()", keywords);
            const string usage = "write at(\"hex\", (3, 4)), or at((3, 4)) for the game's default space (spec-gaps G394)";
            if (positional.Count == 1) return WhereValue.At(null, Savable("the value of at()", positional[0], usage, hidden: true));
            if (positional.Count != 2) throw LookError("at() takes a space and a value, got " + positional.Count + " arguments", usage);
            return WhereValue.At(Name("at()", "the space name", positional[0]),
                Savable("the value of at()", positional[1], "a place holds " + PlainHint + " (spec-gaps G394)", hidden: true));
        }

        private Value OntoBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            NoPlaceKeywords("onto()", keywords);
            const string usage = "write onto(ship, \"deck\") (spec-gaps G406)";
            if (positional.Count < 1 || positional.Count > 2)
                throw LookError("onto() takes an entity and where on it, got " + positional.Count + " arguments", usage);
            Value target = positional[0] switch
            {
                IEntityReference entity => new EntityValue(entity.EntityId),
                HiddenValue hidden => hidden,
                _ => throw LookError("onto() takes an entity, not " + positional[0].KindName, usage),
            };
            string? point = positional.Count == 2 ? Name("onto()", "the attach point", positional[1]) : null;
            return WhereValue.Onto(target, point);
        }

        private Value BetweenBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            NoPlaceKeywords("between()", keywords);
            if (positional.Count != 2 || !(positional[0] is WhereValue first) || !(positional[1] is WhereValue second))
                throw LookError("between() takes two places made by at(), onto() or between()",
                    "write between(at(\"hex\", a), onto(ship)) (spec-gaps G394)");
            return WhereValue.Between(first, second);
        }

        private static string Name(string function, string what, Value value)
        {
            if (value is StrValue s && s.Value.Length > 0) return s.Value;
            throw LookError(function + " takes " + what + " as a non-empty str, not " + (value is StrValue ? "an empty str" : value.KindName), null);
        }

        private static void NoPlaceKeywords(string function, IReadOnlyDictionary<string, Value> keywords)
        {
            foreach (var pair in keywords) throw LookError(function + " has no argument '" + pair.Key + "'", null);
        }

        private static ScriptRuntimeException LookError(string message, string? hint) =>
            new ScriptRuntimeException(Codes.RunType, message, hint);
    }
}
