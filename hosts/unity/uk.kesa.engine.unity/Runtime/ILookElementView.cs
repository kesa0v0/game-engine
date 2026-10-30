#nullable enable
using Kesa.Engine.Ui;
using UnityEngine;

namespace Kesa.Engine.Unity
{
    /// <summary>
    /// 그림 요소 하나의 Unity 쪽 (G413). 종류마다 하나를 등록한다(<see cref="GameObjectLookRenderer.AddKind"/>). <c>layer</c>·<c>offset</c>은 렌더러가 맡으므로
    /// 여기에는 그 종류만의 속성만 온다.
    /// </summary>
    public interface ILookElementView
    {
        GameObject GameObject { get; }

        void Set(string attribute, LookAttributeValue value);

        /// <summary>조상의 <c>layer</c>를 더한 그리는 순서.</summary>
        void SetSortingOrder(int order);

        /// <summary>물체 전체의 투명도(0~1)를 제 색에 곱한다 (G425: 효과가 흐려질 때). 투명도가 없는 종류는 그대로 둔다.</summary>
        void SetOpacity(float opacity)
        {
        }
    }
}
