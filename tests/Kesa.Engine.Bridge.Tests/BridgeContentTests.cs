using NUnit.Framework;

namespace Kesa.Engine.Bridge.Tests
{
    /// <summary>연출 방식 (G429): 로드 순서가 뒤인 모드가 적은 것이 이기고, 아무도 안 적으면 instant.</summary>
    public class BridgeContentTests
    {
        private static BridgeMod Mod(string id, string? presentation) => new BridgeMod(id, id, "/mods/" + id, null, presentation);

        [Test]
        public void TheLastModThatSaysAPresentationWins()
        {
            Assert.That(new BridgeContent(new[] { Mod("core", null), Mod("extra", null) }, new string[0]).Presentation, Is.EqualTo(BridgePresentation.Instant));
            Assert.That(new BridgeContent(new[] { Mod("core", "staged"), Mod("extra", null) }, new string[0]).Presentation, Is.EqualTo("staged"));
            Assert.That(new BridgeContent(new[] { Mod("core", "staged"), Mod("extra", "instant") }, new string[0]).Presentation, Is.EqualTo("instant"));
        }
    }
}
