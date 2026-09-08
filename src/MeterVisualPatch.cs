using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RainbowJudgement
{
    /// <summary>
    /// 用彩虹渐变替换原版判定仪表盘背景（横条 / 弧形），并记录原 sprite 以便关闭时还原。
    /// 纹理内容见 MeterTexture。
    /// </summary>
    [HarmonyPatch(typeof(scrHitErrorMeter), "UpdateLayout")]
    public static class MeterVisualPatch
    {
        private static readonly Dictionary<scrHitErrorMeter, Sprite> OriginalStraight = new Dictionary<scrHitErrorMeter, Sprite>();
        private static readonly Dictionary<scrHitErrorMeter, Sprite> OriginalCurved = new Dictionary<scrHitErrorMeter, Sprite>();

        [HarmonyPostfix]
        public static void UpdateLayoutPostfix(scrHitErrorMeter __instance,
            ErrorMeterSize size = ErrorMeterSize.Normal,
            ErrorMeterShape shape = ErrorMeterShape.Straight)
        {
            try
            {
                if (__instance == null) return;
                if (!Main.Enabled || !Main.Settings.EnableRainbow)
                {
                    RestoreMeter(__instance);
                    return;
                }
                CaptureOriginals(__instance);
                if (__instance.straightMeter != null) ReplaceImage(__instance.straightMeter, MeterTexture.Straight());
                if (__instance.curvedMeter != null) ReplaceImage(__instance.curvedMeter, MeterTexture.Curved());
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

        public static void RestoreAllMeters()
        {
            ForEachMeter(RestoreMeter);
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

        private static void RestoreMeter(scrHitErrorMeter meter)
        {
            if (meter == null) return;
            Sprite sprite;
            if (meter.straightMeter != null && OriginalStraight.TryGetValue(meter, out sprite) && sprite != null)
            {
                ReplaceImage(meter.straightMeter, sprite);
                OriginalStraight.Remove(meter); // 恢复后移除，防实例残留
            }
            if (meter.curvedMeter != null && OriginalCurved.TryGetValue(meter, out sprite) && sprite != null)
            {
                ReplaceImage(meter.curvedMeter, sprite);
                OriginalCurved.Remove(meter);
            }
        }

        private static void CaptureOriginals(scrHitErrorMeter meter)
        {
            Capture(meter, meter.straightMeter, MeterTexture.Straight(), OriginalStraight);
            Capture(meter, meter.curvedMeter, MeterTexture.Curved(), OriginalCurved);
        }

        private static void Capture(scrHitErrorMeter meter, GameObject root, Sprite ours, Dictionary<scrHitErrorMeter, Sprite> store)
        {
            if (meter == null || root == null) return;
            Image image = root.GetComponent<Image>();
            if (image == null || image.sprite == null || image.sprite == ours) return;
            if (!store.ContainsKey(meter)) store[meter] = image.sprite;
        }

        private static void ReplaceImage(GameObject root, Sprite sprite)
        {
            if (root == null || sprite == null) return;
            Image image = root.GetComponent<Image>();
            if (image != null) image.sprite = sprite;
        }
    }
}
