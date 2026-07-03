#if UNITY_EDITOR
using System;
using UnityEngine;

namespace TJGenerators.UI
{
    /// <summary>
    /// 左栏底部固定区：操作按钮（生成/搜索）与 <see cref="UserInfoBar"/>，绝对定位，不参与滚动。
    /// </summary>
    public static class LeftPanelBottomDock
    {
        public const float ActionButtonHeight = 30f;

        public readonly struct Layout
        {
            public readonly Rect buttonRect;

            public Layout(Rect buttonRect) => this.buttonRect = buttonRect;
        }

        static float ReservedHeight => ActionButtonHeight + UserInfoBar.Height;

        public static float GetActionAreaHeight() => ActionButtonHeight;

        public static float GetReservedHeight() => ReservedHeight;

        public static float GetScrollViewHeight(float windowHeight) =>
            windowHeight - ReservedHeight;

        public static Layout CalculateLayout(float windowHeight)
        {
            float buttonBottom = windowHeight - UserInfoBar.Height;
            float buttonTop = buttonBottom - ActionButtonHeight;

            return new Layout(new Rect(
                CommonStyles.LeftContentPadding,
                buttonTop,
                CommonStyles.LeftComponentWidth,
                ActionButtonHeight));
        }

        /// <summary>
        /// 绘制底部 Dock：先执行 <paramref name="drawAction"/>，再绘制 <see cref="UserInfoBar"/>。
        /// </summary>
        public static void Draw(
            float windowHeight,
            float panelWidth,
            Action<Layout> drawAction,
            bool hasLoadedUserInfo,
            int currentCredits,
            ref UserInfoBar.CreditsTextLayoutCache creditsCache,
            string email = null)
        {
            drawAction?.Invoke(CalculateLayout(windowHeight));

            UserInfoBar.Draw(
                windowHeight,
                panelWidth,
                hasLoadedUserInfo,
                currentCredits,
                ref creditsCache,
                email);
        }
    }
}
#endif
