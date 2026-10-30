#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Kesa.Engine.Unity
{
    /// <summary>
    /// 모델 파일 로더 (G391): <c>Model</c>의 <c>src</c> 확장자로 고른다. 기본은 glTF(<see cref="GltfModelLoader"/>)이고 게임은
    /// <see cref="GameObjectLookRenderer.AddModelLoader"/>와 <see cref="Kesa.Engine.Ui.LookKinds.AddModelFormat"/>로 제 로더를 더한다.
    /// </summary>
    public interface IModelLoader
    {
        /// <summary>맡는 확장자(소문자, 점 포함).</summary>
        IReadOnlyList<string> Extensions { get; }

        /// <summary>
        /// 파일을 읽어 꺼내 쓸 원본(꺼진 GameObject)을 <paramref name="holder"/> 아래에 만든다. 못 읽으면 null. 주 스레드에서 불리고 에디터·게임
        /// 프레임을 타고 끝나도 된다.
        /// </summary>
        Task<ModelAsset?> Load(string path, IUiAssets assets, Transform holder);
    }

    /// <summary>로더가 읽은 모델 (G391·G434): 복제할 원본과 이름 붙은 클립(옛 <c>Animation</c>용, <c>legacy</c>).</summary>
    public sealed class ModelAsset
    {
        public ModelAsset(GameObject template, IReadOnlyDictionary<string, AnimationClip> clips)
        {
            Template = template;
            Clips = clips;
        }

        public GameObject Template { get; }
        public IReadOnlyDictionary<string, AnimationClip> Clips { get; }
    }
}
