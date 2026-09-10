using System;
using HarmonyLib;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>判定文字颜色 + 结尾 X^n 文案</summary>
    public static class VisualHooks
    {
        /// <summary>判定文字（球上冒出的 Perfect/稍快等）颜色改为彩虹判定对应色（与 tick 同一套位置映射）</summary>
        [HarmonyPatch(typeof(scrHitTextMesh), "Show")]
        public static class HitTextColorPatch
        {
            [HarmonyPostfix]
            public static void Postfix(scrHitTextMesh __instance)
            {
                if (!Main.Enabled || !Main.Settings.EnableRainbow) return;
                try
                {
                    if (__instance == null || __instance.text == null) return;
                    double wavelength = RainbowMath.WavelengthForGradient(
                        LastJudge.ScaledPos, LastJudge.CountedDeg, LastJudge.BpmTimesSpeed, LastJudge.Pitch, LastJudge.MarginScale);
                    Color rainbow = Spectrum.WavelengthToRgb(wavelength);
                    rainbow.a = __instance.text.color.a; // 保留原 alpha（淡出动画控制）
                    __instance.text.color = rainbow;
                }
                catch (Exception ex)
                {
                    Logger.Log("[HitTextColor] " + ex.Message);
                }
            }
        }

        /// <summary>
        /// 全 PP（无 PP 开外判定）时显示"完美无瑕"，在其前加 X^n（n 为上标，TMP 渲染）。
        /// n = (1/r) - 1，r = 各判定 |角度|/PP边界的平均（权重 1）；r=0 → n=∞。
        /// X^n 颜色按档位：存在绿→原版完美绿；存在蓝→#006179；存在青→#000067；否则→#390055。
        /// </summary>
        [HarmonyPatch(typeof(scrController), "OnLandOnPortal")]
        public static class FlawlessXPatch
        {
            [HarmonyPostfix]
            public static void Postfix(scrController __instance)
            {
                try
                {
                    if (!Main.Enabled || !Main.Settings.EnableRainbow) return;
                    if (__instance == null || __instance.txtCongrats == null) return;

                    string flawless = RDString.Get("status.allPurePerfect", null);
                    if (string.IsNullOrEmpty(flawless)) return;

                    string text = __instance.txtCongrats.text;
                    if (string.IsNullOrEmpty(text) || !text.Contains(flawless))
                    {
                        // 本局不是完美无瑕（或文本被清空）→ 上一局残留的 X^n 上标必须清掉。
                        // 编辑器 playtest / 自定义关是"原地重开"（不重载场景），txtCongrats 对象会被复用，
                        // 上标是它的子对象，不主动销毁就会跟着新一局的"恭喜"一起显示出来。
                        FlawlessXOverlay.Hide();
                        return;
                    }
                    if (text.StartsWith("<color=")) return; // 已添加过（防 coop / 重复触发）

                    double ratio = RainbowCounter.GetPerfectRatio();
                    string exponent;
                    if (ratio <= 0.0001 || Math.Abs(RainbowState.AverageAbsTimeMs) < 0.005)
                        exponent = "∞"; // 平均偏差显示 0.00ms 也视为 ∞（auto 模式浮点误差特判）
                    else
                    {
                        double n = (1.0 / ratio) - 1.0;
                        exponent = n < 1.0 ? n.ToString("F2") : Fmt.Sig3N(n);
                    }

                    Color32 color = XColor(RainbowCounter.GetXColorIndex());
                    string hex = Spectrum.ToHex(color);
                    Logger.Log("[FlawlessX] before=[" + text.Replace("\n", "\\n") + "] flawless=[" + flawless
                        + "] rich=" + __instance.txtCongrats.supportRichText + " fs=" + __instance.txtCongrats.fontSize
                        + " font=" + (__instance.txtCongrats.font != null ? __instance.txtCongrats.font.name : "NULL"));

                    __instance.txtCongrats.text = text.Replace(flawless, "<color=#" + hex + ">X</color> " + flawless);

                    Logger.Log("[FlawlessX] after=[" + __instance.txtCongrats.text.Replace("\n", "\\n") + "]");
                    FlawlessXOverlay.Show(__instance, exponent, hex);
                    Logger.Log("[FlawlessX] 添加 X^" + exponent + " (r=" + ratio.ToString("F4") + ", color=#" + hex + ")");
                }
                catch (Exception ex)
                {
                    Logger.Log("[FlawlessX] " + ex.Message);
                }
            }

            /// <summary>X^n 的颜色档位：按"本局打到的最好档位"取 4 色之一（档位判定规则未变）。
            ///   · 0/1/2 档 → **彩虹映射**下的 400nm / 440nm / 480nm（与计数器 7 个数字同一套，见 RainbowCounter.TierRgb）
            ///   · 3 档（打到 PP/完美绿）→ **保持游戏原版完美绿**，与计数器 F/G 同色</summary>
            private static Color32 XColor(int index)
            {
                return RainbowCounter.TierRgb(index);
            }
        }

        /// <summary>
        /// 关卡开始 / 重开（编辑器 playtest、自定义关原地重开、场景加载都会经过 Awake_Rewind）时，
        /// 清掉上一局的 X^n 上标，避免残留到新一局。
        /// </summary>
        [HarmonyPatch(typeof(scrController), "Awake_Rewind")]
        public static class FlawlessXResetPatch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                try { FlawlessXOverlay.Hide(); }
                catch { }
            }
        }
    }
}
