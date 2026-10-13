using System.Collections.Generic;
using System.IO;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>모드 폴더를 임시 디렉터리에 만들어 ContentLoader를 돌리는 테스트의 공통 도우미.</summary>
    public abstract class LoaderTestBase
    {
        protected string _root = "";

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-tests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        protected void Write(string relative, string text)
        {
            string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        /// <summary>schema/와 defs/를 쓰는 모드 하나.</summary>
        protected void Mod(string id, string? schema = null, string? defs = null, string extra = "")
        {
            var uses = new StringBuilder();
            if (schema != null)
            {
                Write(id + "/schema/types.py", schema);
                uses.Append("use_all(\"schema/\")\n");
            }
            if (defs != null)
            {
                Write(id + "/defs/defs.py", defs);
                uses.Append("use_all(\"defs/\")\n");
            }
            Write(id + "/mod.py", "Mod(id=\"" + id + "\"" + extra + ")\n" + uses);
        }

        protected ContentLoadResult Load(out IReadOnlyList<Diagnostic> diagnostics)
        {
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag);
            var result = ContentLoader.Load(_root, order, bag);
            diagnostics = bag.ToSortedList();
            return result;
        }

        protected ContentLoadResult LoadClean()
        {
            var result = Load(out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            return result;
        }
    }
}
