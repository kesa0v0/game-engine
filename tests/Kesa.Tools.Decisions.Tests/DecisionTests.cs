using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Kesa.Tools.Decisions.Tests
{
    /// <summary>결정 기록의 읽기와 머리 검사 (CLAUDE.md "결정 기록"). 저장소의 실제 결정도 검사를 지나야 한다.</summary>
    public class DecisionTests
    {
        private static string Header(string id, string status = "확인", string changedBy = "", string tags = "UI", string refs = "",
            string body = "본문") =>
            "---\nid: " + id + "\narea: S6-commands\ntitle: 제목 " + id + "\nstatus: " + status + "\nchangedBy: " + changedBy +
            "\ntags: " + tags + "\nrefs: " + refs + "\nsummary: 요약\n---\n\n" + body + "\n";

        private static Decision D(string id, string status = "확인", string changedBy = "", string tags = "UI", string refs = "",
            string body = "본문")
        {
            var d = Decision.Parse(Header(id, status, changedBy, tags, refs, body));
            d.FileName = id;
            return d;
        }

        private static List<string> Check(params Decision[] all) => DecisionCheck.Run(all);

        [Test]
        public void TheRepositorysDecisionsPassTheCheck()
        {
            string? dir = null;
            for (var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); at != null && dir == null; at = at.Parent)
                if (Directory.Exists(Path.Combine(at.FullName, "docs", "decisions"))) dir = Path.Combine(at.FullName, "docs", "decisions");
            Assert.That(dir, Is.Not.Null);
            var all = Decision.LoadAll(dir!);
            Assert.That(all.Count, Is.GreaterThan(300));
            var problems = DecisionCheck.Run(all);
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void ParseReadsTheHeaderListsAndBody()
        {
            var d = Decision.Parse(Header("G7", "일부바뀜", "G9", "UI, 번역", "G1, G2", "첫 줄\n\n**바뀜:** G9").Replace("\n", "\r\n"));
            Assert.That((d.Id, d.Number, d.Area, d.Status), Is.EqualTo(("G7", 7, "S6-commands", "일부바뀜")));
            Assert.That(d.ChangedBy, Is.EqualTo(new[] { "G9" }));
            Assert.That(d.Tags, Is.EqualTo(new[] { "UI", "번역" }));
            Assert.That(d.Refs, Is.EqualTo(new[] { "G1", "G2" }));
            Assert.That(d.Body, Is.EqualTo("첫 줄\n\n**바뀜:** G9"));
            Assert.That(d.Active, Is.True, "a partly changed decision still holds");
            Assert.That(D("G8", "바뀜", "G9").Active, Is.False);
        }

        [Test]
        public void AFileWithoutAHeaderIsRefused()
        {
            Assert.Throws<System.FormatException>(() => Decision.Parse("id: G1\n"));
        }

        [Test]
        public void ACleanSetHasNoProblems()
        {
            Assert.That(Check(D("G1", "일부바뀜", "G2", body: "x\n**바뀜:** G2"), D("G2", refs: "G1")), Is.Empty);
        }

        [Test]
        public void StatusAndChangedByMustAgree()
        {
            Assert.That(Check(D("G1", "보류")), Has.Some.Contains("status '보류'"));
            Assert.That(Check(D("G1", "바뀜", body: "**바뀜:** G2")), Has.Some.Contains("changedBy is empty"));
            Assert.That(Check(D("G1", "확인", "G2"), D("G2")), Has.Some.Contains("changedBy is set but status is 확인"));
            Assert.That(Check(D("G1", "바뀜", "G2"), D("G2")), Has.Some.Contains("no **바뀜:** line"));
        }

        [Test]
        public void ChangedByPointsToALaterDecisionThatExists()
        {
            Assert.That(Check(D("G5", "바뀜", "G9", body: "**바뀜:** G9")), Has.Some.Contains("changedBy G9 does not exist"));
            Assert.That(Check(D("G5", "바뀜", "G3", body: "**바뀜:** G3"), D("G3")), Has.Some.Contains("not a later decision"));
            Assert.That(Check(D("G5", refs: "G4")), Has.Some.Contains("refs G4 does not exist"));
        }

        [Test]
        public void TheFileNameIdAndHeaderKeysAreChecked()
        {
            var renamed = D("G4");
            renamed.FileName = "G40";
            Assert.That(Check(renamed), Has.Some.Contains("file name differs"));
            Assert.That(Check(D("G4"), D("G4")), Has.Some.Contains("used twice"));
            Assert.That(Check(D("X4")), Has.Some.Contains("not G<number>"));
            Assert.That(Check(D("G4", tags: "")), Has.Some.Contains("no tags"));

            var missing = Decision.Parse("---\nid: G4\ntitle: t\nstatus: 확인\nsummary: s\n---\nb");
            missing.FileName = "G4";
            var problems = Check(missing);
            Assert.That(problems, Has.Some.Contains("no 'area'"));
            Assert.That(problems, Has.Some.Contains("no 'refs'"));
        }
    }
}
