using System;
using System.Collections.Generic;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// USS가 아는 속성과 의사 클래스 이름 (G361). Unity 6000.3의 내부 속성 표에서 뽑았고, Unity 패키지의 창 없는 확인이 지금 Unity의 표와
    /// 같은지 비교한다.
    /// </summary>
    public static class UssProperties
    {
        public static IReadOnlyCollection<string> Names { get; } = new SortedSet<string>(StringComparer.Ordinal)
        {
            "-unity-background-image-tint-color", "-unity-background-scale-mode", "-unity-editor-text-rendering-mode", "-unity-font",
            "-unity-font-definition", "-unity-font-style", "-unity-material", "-unity-overflow-clip-box", "-unity-paragraph-spacing",
            "-unity-slice-bottom", "-unity-slice-left", "-unity-slice-right", "-unity-slice-scale", "-unity-slice-top", "-unity-slice-type",
            "-unity-text-align", "-unity-text-auto-size", "-unity-text-generator", "-unity-text-outline", "-unity-text-outline-color",
            "-unity-text-outline-width", "-unity-text-overflow-position",
            "align-content", "align-items", "align-self", "all", "aspect-ratio",
            "background-color", "background-image", "background-position", "background-position-x", "background-position-y",
            "background-repeat", "background-size",
            "border-bottom-color", "border-bottom-left-radius", "border-bottom-right-radius", "border-bottom-width", "border-color",
            "border-left-color", "border-left-width", "border-radius", "border-right-color", "border-right-width", "border-top-color",
            "border-top-left-radius", "border-top-right-radius", "border-top-width", "border-width", "bottom",
            "color", "cursor", "display", "filter",
            "flex", "flex-basis", "flex-direction", "flex-grow", "flex-shrink", "flex-wrap", "font-size",
            "height", "justify-content", "left", "letter-spacing",
            "margin", "margin-bottom", "margin-left", "margin-right", "margin-top", "max-height", "max-width", "min-height", "min-width",
            "opacity", "overflow",
            "padding", "padding-bottom", "padding-left", "padding-right", "padding-top", "position",
            "right", "rotate", "scale", "text-overflow", "text-shadow", "top", "transform-origin",
            "transition", "transition-delay", "transition-duration", "transition-property", "transition-timing-function", "translate",
            "visibility", "white-space", "width", "word-spacing",
        };

        public static IReadOnlyCollection<string> PseudoClasses { get; } = new SortedSet<string>(StringComparer.Ordinal)
        {
            "active", "checked", "disabled", "enabled", "focus", "hover", "inactive", "root",
        };
    }
}
