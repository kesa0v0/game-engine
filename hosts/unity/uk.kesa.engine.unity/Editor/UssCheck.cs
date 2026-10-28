#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Kesa.Engine.Ui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kesa.Engine.Unity.Editor
{
    /// <summary>
    /// 런타임 USS 확인 (G359~G361): 엔진의 속성 목록이 이 Unity의 표와 같은지, 같은 글을 Unity 임포터가 만든 시트와 모양이 같은지, 런타임
    /// 패널에서 상태·자손·자식·<c>var()</c>·<c>url()</c>이 실제로 먹는지. Unity를 올려 내부가 바뀌면 여기서 실패한다.
    /// </summary>
    internal static class UssCheck
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private const string Sample =
            ":root { --accent: #ff8800; }\n" +
            ".card { background-color: rgb(20, 30, 40); border-radius: 4px; padding: 2px 4px; flex-direction: row; color: red; border-color: rgba(0, 0, 0, 0.5); }\n" +
            ".card:hover .name, #top > Button.big { color: var(--accent, red); font-size: 120%; }\n" +
            "* { transition: opacity 0.2s ease-in, color 1s; }\n" +
            ".x { width: auto; display: none; opacity: 0.5; -unity-font-style: bold; filter: blur(2px); rotate: 45deg; }\n";

        public static void Run()
        {
            CheckPropertyNames();
            CheckSameAsImporter();
            CheckApplied();
        }

        private static void Require(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("expected (USS): " + what);
        }

        private static void CheckPropertyNames()
        {
            var util = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.StyleSheets.StylePropertyUtil");
            var table = (IDictionary?)(util?.GetProperty("propertyNameToStylePropertyId", Any)?.GetValue(null) ?? util?.GetField("s_NameToId", Any)?.GetValue(null));
            Require(table != null, "Unity's USS property table is found");
            var unity = new SortedSet<string>(table!.Keys.Cast<string>(), StringComparer.Ordinal);
            var engine = new SortedSet<string>(UssProperties.Names, StringComparer.Ordinal);
            Require(unity.SetEquals(engine), "the engine's USS property list (UssProperties) matches this Unity: only in Unity [" +
                string.Join(", ", unity.Except(engine)) + "], only in the engine [" + string.Join(", ", engine.Except(unity)) + "]");
        }

        private static void CheckSameAsImporter()
        {
            const string folder = "Assets/KesaUssCheck";
            const string path = folder + "/sample.uss";
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(path, Sample);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var imported = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                Require(imported != null, "Unity imports the sample");

                var read = UssReader.Read("sample.uss", Sample);
                Require(read.Problems.Count == 0, "the sample reads: " + string.Join("; ", read.Problems));
                var problems = new List<UiProblem>();
                var built = UssStyleSheets.Build(read.Sheet, _ => null, problems);
                Require(problems.Count == 0, "the sample builds: " + string.Join("; ", problems));

                string theirs = Describe(imported!);
                string ours = Describe(built);
                if (theirs != ours) Debug.Log("KESA USS importer:\n" + theirs + "\nKESA USS ours:\n" + ours);
                Require(theirs == ours, "the built sheet has the same selectors, specificities and values as Unity's importer (see the log)");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void CheckApplied()
        {
            string mods = Path.Combine(Path.GetTempPath(), "kesa-uss-check");
            Directory.CreateDirectory(Path.Combine(mods, "art"));
            var png = new Texture2D(2, 2);
            File.WriteAllBytes(Path.Combine(mods, "art", "dot.png"), png.EncodeToPNG());
            var assets = new ModFolderAssets(new[] { mods });

            var read = UssReader.Read("check.uss",
                ".name { color: blue; }\n.card:hover .name { color: red; }\n.card > .big { font-size: 30px; }\n" +
                ":root { --accent: rgb(0, 255, 0); }\n.v { color: var(--accent); background-image: url(\"art/dot.png\"); }\n");
            Require(read.Problems.Count == 0, "the check sheet reads: " + string.Join("; ", read.Problems));

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            string? theme = AssetDatabase.FindAssets("t:ThemeStyleSheet").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            if (theme != null) settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(theme);
            var go = new GameObject("kesa-uss-check");
            try
            {
                var document = go.AddComponent<UIDocument>();
                document.panelSettings = settings;
                var root = document.rootVisualElement;
                Require(root?.panel != null, "a runtime panel for the check");

                var renderer = new UiToolkitRenderer(root!, assets);
                var top = renderer.Create("VisualElement", null, 0, new KeyValuePair<string, string>[0]);
                var card = renderer.Create("VisualElement", top, 0, new[] { new KeyValuePair<string, string>("class", "card") });
                var name = renderer.Create("Label", card, 0, new[] { new KeyValuePair<string, string>("class", "name") });
                var big = renderer.Create("Label", card, 1, new[] { new KeyValuePair<string, string>("class", "big") });
                var deep = renderer.Create("VisualElement", card, 2, new KeyValuePair<string, string>[0]);
                var grandchild = renderer.Create("Label", deep, 0, new[] { new KeyValuePair<string, string>("class", "big") });
                var v = renderer.Create("Label", top, 1, new[] { new KeyValuePair<string, string>("class", "v") });
                var problems = renderer.AddStyleSheet(top, read.Sheet);
                Require(problems.Count == 0, "the sheet builds: " + string.Join("; ", problems));

                Layout(root!);
                Require(renderer.Element(name).resolvedStyle.color == Color.blue, ".name is blue, got " + renderer.Element(name).resolvedStyle.color);
                Require(Mathf.Approximately(renderer.Element(big).resolvedStyle.fontSize, 30), "a child .big is 30px");
                Require(!Mathf.Approximately(renderer.Element(grandchild).resolvedStyle.fontSize, 30), "a grandchild .big is not a child");
                Require(renderer.Element(v).resolvedStyle.color == Color.green, "var(--accent) is green, got " + renderer.Element(v).resolvedStyle.color);
                Require(renderer.Element(v).resolvedStyle.backgroundImage.texture != null, "url() loaded the image from the mod folder");

                var states = typeof(VisualElement).GetProperty("pseudoStates", Any)!;
                states.SetValue(renderer.Element(card), Enum.Parse(states.PropertyType, "Hover"));
                Layout(root!);
                Require(renderer.Element(name).resolvedStyle.color == Color.red, ".card:hover .name is red, got " + renderer.Element(name).resolvedStyle.color);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                Directory.Delete(mods, true);
            }
        }

        private static void Layout(VisualElement root)
        {
            var panel = root.panel;
            panel.GetType().GetMethod("ValidateLayout", Any, null, Type.EmptyTypes, null)!.Invoke(panel, null);
        }

        // 시트의 모양: 선택자(우선순위, 조각, 관계)와 규칙의 속성(이름, 값의 종류와 값)
        private static string Describe(StyleSheet sheet)
        {
            var text = new StringBuilder();
            foreach (var rule in (IEnumerable)Get(sheet, "rules"))
            {
                text.AppendLine("rule");
                foreach (var complex in (IEnumerable)Get(rule, "complexSelectors"))
                {
                    text.Append("  selector ").Append(Get(complex, "specificity")).Append(':');
                    foreach (var selector in (IEnumerable)Get(complex, "selectors"))
                    {
                        text.Append(" [").Append(Get(selector, "previousRelationship"));
                        foreach (var part in (IEnumerable)Get(selector, "parts")) text.Append(' ').Append(Get(part, "type")).Append('=').Append(Get(part, "value"));
                        text.Append(']');
                    }
                    text.AppendLine();
                }
                foreach (var property in (IEnumerable)Get(rule, "properties"))
                {
                    text.Append("  ").Append(Get(property, "name")).Append(':');
                    foreach (var handle in (IEnumerable)Get(property, "values")) text.Append(' ').Append(Value(sheet, handle));
                    text.AppendLine();
                }
            }
            return text.ToString();
        }

        private static string Value(StyleSheet sheet, object handle)
        {
            string type = Get(handle, "valueType").ToString();
            string? reader = type switch
            {
                "Float" => "ReadFloat", "Dimension" => "ReadDimension", "Color" => "ReadColor", "Keyword" => "ReadKeyword",
                "Enum" => "ReadEnum", "String" => "ReadString", "Variable" => "ReadVariable", "Function" => "ReadFunction", _ => null,
            };
            if (reader == null) return type;
            var method = typeof(StyleSheet).GetMethods(Any)
                .First(m => m.Name == reader && !m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
            return type + "(" + method.Invoke(sheet, new[] { handle }) + ")";
        }

        private static object Get(object target, string name)
        {
            var type = target.GetType();
            object? value = type.GetProperty(name, Any)?.GetValue(target) ?? type.GetField(name, Any)?.GetValue(target)
                ?? type.GetField("m_" + char.ToUpperInvariant(name[0]) + name.Substring(1), Any)?.GetValue(target);
            return value ?? throw new MissingMemberException(type.Name, name);
        }
    }
}
