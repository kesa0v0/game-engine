#nullable enable
using UnityEngine;

namespace Kesa.Engine.Unity
{
    /// <summary>판 GameObject에 붙어 프레임마다 다 읽힌 그림을 물체에 걸고 (G392) 카메라를 보는 요소를 돌린다 (G444).</summary>
    [AddComponentMenu("")]
    internal sealed class LookPicturePump : MonoBehaviour
    {
        internal LookPictures? Pictures;
        internal GameObjectLookRenderer? Renderer;

        private void Update() => Pictures?.Pump();

        private void LateUpdate() => Renderer?.FaceCamera();
    }
}
