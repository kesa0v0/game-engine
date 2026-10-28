// OneJS(https://github.com/Singtaa/OneJS, MIT, Copyright (c) 2024 Singtaa)의 Runtime/Styling/StyleSheetBuilderWrapper.cs를 고쳐 옮김.
// 고지 전문은 이 패키지의 THIRD_PARTY_NOTICES.md.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kesa.Engine.Unity
{
    /// <summary>
    /// Unity 내부 <c>StyleSheetBuilder</c>를 리플렉션으로 부른다 (G359). Unity 판이 바뀌어 깨지는 곳은 여기에 모은다. 열거형 값은 번호로 적지
    /// 않고 이름으로 찾는다(판마다 번호가 다르다).
    /// </summary>
    internal sealed class UssBuilder
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Assembly UiAssembly = typeof(VisualElement).Assembly;
        private static readonly Type BuilderType = Require("UnityEngine.UIElements.StyleSheets.StyleSheetBuilder");
        private static readonly Type PartType = Require("UnityEngine.UIElements.StyleSelectorPart");
        private static readonly Type RelationshipType = Require("UnityEngine.UIElements.StyleSelectorRelationship");
        private static readonly Type DimensionType = Require("UnityEngine.UIElements.StyleSheets.Dimension");
        private static readonly Type UnitType = DimensionType.GetNestedType("Unit", Instance | Static)
            ?? throw new TypeLoadException("Kesa UI: Unity's Dimension.Unit was not found");
        private static readonly Type KeywordType = Require("UnityEngine.UIElements.StyleValueKeyword");
        private static readonly Type ValueTypeType = Require("UnityEngine.UIElements.StyleValueType");
        private static readonly Type FunctionType = Require("UnityEngine.UIElements.StyleValueFunction");

        private static readonly MethodInfo BeginRuleMethod = Method("BeginRule", typeof(int));
        private static readonly MethodInfo EndRuleMethod = Method("EndRule");
        private static readonly MethodInfo BeginComplexSelectorMethod = Method("BeginComplexSelector", typeof(int));
        private static readonly MethodInfo AddSimpleSelectorMethod = Method("AddSimpleSelector", PartType.MakeArrayType(), RelationshipType);
        private static readonly MethodInfo BeginPropertyMethod = Method("BeginProperty", typeof(string), typeof(int));
        private static readonly MethodInfo EndPropertyMethod = Method("EndProperty");
        private static readonly MethodInfo AddCommaMethod = Method("AddCommaSeparator");
        private static readonly MethodInfo AddFloatMethod = Method("AddValue", typeof(float));
        private static readonly MethodInfo AddColorMethod = Method("AddValue", typeof(Color));
        private static readonly MethodInfo AddObjectMethod = Method("AddValue", typeof(UnityEngine.Object));
        private static readonly MethodInfo AddDimensionMethod = Method("AddValue", DimensionType);
        private static readonly MethodInfo AddKeywordMethod = Method("AddValue", KeywordType);
        private static readonly MethodInfo AddTypedStringMethod = Method("AddValue", typeof(string), ValueTypeType);
        private static readonly MethodInfo AddFunctionMethod = Method("AddValue", FunctionType);
        private static readonly MethodInfo BuildToMethod = Method("BuildTo", typeof(StyleSheet));

        private static readonly Dictionary<string, MethodInfo> PartFactories = new Dictionary<string, MethodInfo>(StringComparer.Ordinal)
        {
            ["Class"] = PartFactory("CreateClass"),
            ["Id"] = PartFactory("CreateId"),
            ["PseudoClass"] = PartFactory("CreatePseudoClass"),
            ["Type"] = PartFactory("CreateType"),
        };
        private static readonly MethodInfo WildcardFactory = PartType.GetMethod("CreateWildCard", Static, null, Type.EmptyTypes, null)
            ?? throw new MissingMethodException("Kesa UI: StyleSelectorPart.CreateWildCard was not found");

        private static readonly Dictionary<string, object> Keywords = EnumByLowerName(KeywordType);
        private static readonly Dictionary<string, object> Units = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["px"] = Enum.Parse(UnitType, "Pixel"), ["%"] = Enum.Parse(UnitType, "Percent"), ["s"] = Enum.Parse(UnitType, "Second"),
            ["ms"] = Enum.Parse(UnitType, "Millisecond"), ["deg"] = Enum.Parse(UnitType, "Degree"), ["grad"] = Enum.Parse(UnitType, "Gradian"),
            ["rad"] = Enum.Parse(UnitType, "Radian"), ["turn"] = Enum.Parse(UnitType, "Turn"),
        };
        private static readonly MethodInfo? FunctionFromName = Require("UnityEngine.UIElements.StyleValueFunctionExtension")
            .GetMethod("FromUssString", Static, null, new[] { typeof(string) }, null);

        private readonly object _builder = Activator.CreateInstance(BuilderType, true);

        public static bool IsKeyword(string word) => Keywords.ContainsKey(word);

        public static bool IsUnit(string unit) => Units.ContainsKey(unit);

        /// <summary>Unity가 아는 USS 함수인가(<c>var</c>를 뺀 것: 필터, 그라디언트 등).</summary>
        public static bool IsFunction(string name)
        {
            if (FunctionFromName == null) return false;
            try
            {
                string found = FunctionFromName.Invoke(null, new object[] { name }).ToString();
                return found != "Unknown" && found != "Var";
            }
            catch (TargetInvocationException)
            {
                return false;
            }
        }

        public void BeginRule(int line) => BeginRuleMethod.Invoke(_builder, new object[] { line });

        public void EndRule() => EndRuleMethod.Invoke(_builder, null);

        public IDisposable BeginComplexSelector(int specificity) =>
            (IDisposable)BeginComplexSelectorMethod.Invoke(_builder, new object[] { specificity });

        /// <param name="parts">(종류 이름, 값): 종류는 <c>Class</c>·<c>Id</c>·<c>PseudoClass</c>·<c>Type</c>·<c>Wildcard</c>.</param>
        /// <param name="next">오른쪽 조각과의 관계: <c>None</c>·<c>Child</c>·<c>Descendent</c>.</param>
        public void AddSimpleSelector(IReadOnlyList<(string Kind, string Value)> parts, string next)
        {
            var array = Array.CreateInstance(PartType, parts.Count);
            for (int i = 0; i < parts.Count; i++)
            {
                object part = parts[i].Kind == "Wildcard"
                    ? WildcardFactory.Invoke(null, null)
                    : PartFactories[parts[i].Kind].Invoke(null, new object[] { parts[i].Value });
                array.SetValue(part, i);
            }
            AddSimpleSelectorMethod.Invoke(_builder, new[] { array, Enum.Parse(RelationshipType, next) });
        }

        public void BeginProperty(string name, int line) => BeginPropertyMethod.Invoke(_builder, new object[] { name, line });

        public void EndProperty() => EndPropertyMethod.Invoke(_builder, null);

        public void AddComma() => AddCommaMethod.Invoke(_builder, null);

        public void AddFloat(float value) => AddFloatMethod.Invoke(_builder, new object[] { value });

        public void AddColor(Color color) => AddColorMethod.Invoke(_builder, new object[] { color });

        public void AddAsset(UnityEngine.Object asset) => AddObjectMethod.Invoke(_builder, new object[] { asset });

        public void AddDimension(float value, string unit)
        {
            var dimension = Activator.CreateInstance(DimensionType, value, Units[unit]);
            AddDimensionMethod.Invoke(_builder, new[] { dimension });
        }

        public void AddKeyword(string word) => AddKeywordMethod.Invoke(_builder, new[] { Keywords[word] });

        public void AddEnum(string word) => AddTypedStringMethod.Invoke(_builder, new[] { word, Enum.Parse(ValueTypeType, "Enum") });

        public void AddString(string text) => AddTypedStringMethod.Invoke(_builder, new[] { text, Enum.Parse(ValueTypeType, "String") });

        public void AddVariable(string name) => AddTypedStringMethod.Invoke(_builder, new[] { name, Enum.Parse(ValueTypeType, "Variable") });

        /// <summary>함수 값: 함수, 인자 수(쉼표 포함), 그다음 인자들 순서로 쓴다(Unity 임포터와 같은 모양).</summary>
        public void AddFunction(string name, int argumentCount)
        {
            object function = name == "var" ? Enum.Parse(FunctionType, "Var") : FunctionFromName!.Invoke(null, new object[] { name });
            AddFunctionMethod.Invoke(_builder, new[] { function });
            AddFloat(argumentCount);
        }

        public void BuildTo(StyleSheet sheet) => BuildToMethod.Invoke(_builder, new object[] { sheet });

        private static Type Require(string name) =>
            UiAssembly.GetType(name) ?? throw new TypeLoadException(
                "Kesa UI: Unity's internal type '" + name + "' was not found (a Unity version change, or IL2CPP stripping without the package's link.xml)");

        private static MethodInfo Method(string name, params Type[] parameters) =>
            BuilderType.GetMethod(name, Instance, null, parameters, null)
            ?? throw new MissingMethodException("Kesa UI: StyleSheetBuilder." + name + " was not found (a Unity version change?)");

        private static MethodInfo PartFactory(string name) =>
            PartType.GetMethod(name, Static, null, new[] { typeof(string) }, null)
            ?? throw new MissingMethodException("Kesa UI: StyleSelectorPart." + name + " was not found (a Unity version change?)");

        private static Dictionary<string, object> EnumByLowerName(Type type)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var value in Enum.GetValues(type)) map[value.ToString().ToLowerInvariant()] = value;
            return map;
        }
    }
}
