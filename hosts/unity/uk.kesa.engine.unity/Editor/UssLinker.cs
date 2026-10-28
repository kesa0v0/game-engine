#nullable enable
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace Kesa.Engine.Unity.Editor
{
    /// <summary>패키지의 <c>Runtime/link.xml</c>을 링커에 넘긴다: Unity는 패키지 안의 link.xml을 읽지 않는다 (G359, IL2CPP).</summary>
    internal sealed class UssLinker : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data) =>
            Path.GetFullPath("Packages/uk.kesa.engine.unity/Runtime/link.xml");
    }
}
