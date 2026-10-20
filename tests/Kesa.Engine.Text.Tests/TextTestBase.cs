using System.Collections.Generic;
using System.IO;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Text.Tests
{
    /// <summary>모드 폴더를 임시 디렉터리에 만들어 콘텐츠와 번역을 읽는 공통 도우미 (커널 테스트의 LoaderTestBase를 줄인 것).</summary>
    public abstract class TextTestBase
    {
        protected string _root = "";

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-text-tests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Directory.CreateDirectory(Path.Combine(_root, "mods"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        protected string ModsRoot => Path.Combine(_root, "mods");

        /// <summary><c>_root</c> 기준 상대 경로에 쓴다.</summary>
        protected void Write(string relative, string text)
        {
            string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        /// <summary>모드 하나. <paramref name="extra"/>는 <c>Mod(...)</c> 안에 덧붙는 인자.</summary>
        protected void Mod(string id, string extra = ", defaultLanguage=\"en\"", string? schema = null, string? defs = null)
        {
            var uses = new StringBuilder();
            if (schema != null)
            {
                Write("mods/" + id + "/schema/types.py", schema);
                uses.Append("use_all(\"schema/\")\n");
            }
            if (defs != null)
            {
                Write("mods/" + id + "/defs/defs.py", defs);
                uses.Append("use_all(\"defs/\")\n");
            }
            Write("mods/" + id + "/mod.py", "Mod(id=\"" + id + "\"" + extra + ")\n" + uses);
        }

        protected void Ftl(string mod, string language, string text, string file = "main.ftl") =>
            Write("mods/" + mod + "/locale/" + language + "/" + file, text);

        protected ContentLoadResult LoadContent()
        {
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(ModsRoot, bag), bag);
            var result = ContentLoader.Load(ModsRoot, order, bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            return result;
        }

        protected Translations LoadTexts(out IReadOnlyList<Diagnostic> diagnostics, ContentLoadResult? content = null,
            string? hostFolder = null)
        {
            var bag = new DiagnosticBag();
            var texts = Translations.Load(content ?? LoadContent(), bag, hostFolder);
            diagnostics = bag.ToSortedList();
            return texts;
        }

        protected Translations LoadTextsClean(ContentLoadResult? content = null, string? hostFolder = null)
        {
            var texts = LoadTexts(out var diags, content, hostFolder);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            return texts;
        }
    }
}
