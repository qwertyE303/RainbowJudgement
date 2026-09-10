using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RainbowJudgement
{
    /// <summary>
    /// 用彩虹渐变替换原版判定仪表盘背景（横条 / 弧形）。
    /// 贴图来源见 MeterTexture：**只有找到用户图片时才替换**；
    /// 找不到（或总开关关掉）就什么都不做 —— 仪表盘保持游戏原版 sprite，因此本类不需要保存/还原原图。
    /// </summary>
    [HarmonyPatch(typeof(scrHitErrorMeter), "UpdateLayout")]
    public static class MeterVisualPatch
    {
        [HarmonyPostfix]
        public static void UpdateLayoutPostfix(scrHitErrorMeter __instance,
            ErrorMeterSize size = ErrorMeterSize.Normal,
            ErrorMeterShape shape = ErrorMeterShape.Straight)
        {
            try
            {
                if (__instance == null) return;
                if (!Main.Enabled || !Main.Settings.EnableRainbow) return; // 保持/回到游戏原版

                // MeterTexture 内部已缓存：找不到图时两次调用都返回 null，不会重复读盘
                Sprite straight = MeterTexture.Straight();
                if (straight != null && __instance.straightMeter != null) ReplaceImage(__instance.straightMeter, straight);
                Sprite curved = MeterTexture.Curved();
                if (curved != null && __instance.curvedMeter != null) ReplaceImage(__instance.curvedMeter, curved);
            }
            catch (Exception ex)
            {
                Logger.Warn("[RainbowJudgement] UpdateLayout hook 异常: " + ex.Message);
            }
        }

        public static void RefreshAllMeters()
        {
            ForEachMeter(delegate(scrHitErrorMeter meter) { UpdateLayoutPostfix(meter); });
        }

        /// <summary>总开关关闭/卸载时的复位。因为我们从不改动 sprite（没有用户图时不替换、
        /// 有用户图时替换的是同一个已加载 sprite），这里已无需做任何还原；保留空实现只为兼容既有调用点。</summary>
        public static void RestoreAllMeters()
        {
        }

        private static void ForEachMeter(Action<scrHitErrorMeter> action)
        {
            try
            {
                scrHitErrorMeter[] meters = UnityEngine.Object.FindObjectsByType<scrHitErrorMeter>(FindObjectsSortMode.None);
                if (meters == null) return;
                foreach (scrHitErrorMeter meter in meters)
                {
                    if (meter == null) continue;
                    try { action(meter); }
                    catch { }
                }
            }
            catch { }
        }

        private static void ReplaceImage(GameObject root, Sprite sprite)
        {
            if (root == null || sprite == null) return;
            Image image = root.GetComponent<Image>();
            if (image != null) image.sprite = sprite;
        }
    }
}
