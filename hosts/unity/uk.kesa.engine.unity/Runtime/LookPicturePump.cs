#nullable enable
using UnityEngine;

namespace Kesa.Engine.Unity
{
    /// <summary>판 GameObject에 붙어 프레임마다 다 읽힌 그림을 물체에 건다 (G392).</summary>
    [AddComponentMenu("")]
    internal sealed class LookPicturePump : MonoBehaviour
    {
        internal LookPictures? Pictures;

        private void Update() => Pictures?.Pump();
    }
}
