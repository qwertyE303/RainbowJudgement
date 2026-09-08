using System;
using HarmonyLib;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 结果页：在 X-Accuracy 与 Checkpoints 之间（最后一行）插入「平均判定」。
    /// 显示规则（仅 ■ 使用平均判定颜色，其余为默认文本色）：
    ///   时间+颜色都开：平均判定：■（-7.01ms）
    ///   只开时间：    平均判定：-7.01ms
    ///   只开颜色：    平均判定：■
    /// </summary>
    [HarmonyPatch(typeof(DetailedResults), "ShowForPlayer")]
    public static class ResultsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DetailedResults __instance, int playerIndex)
        {
            try
            {
                if (!Main.Enabled || !Main.Settings.EnableRainbow || !Main.Settings.ShowAverageJudgment) return;
                bool showTime = Main.Settings.ShowAverageTime;
                bool showColor = Main.Settings.ShowAverageColor;
                if (!showTime && !showColor) return;
                if (__instance == null || __instance.textComponent == null) return;
                if (RainbowState.Count <= 0) return;

                string text = __instance.textComponent.text;
                if (string.IsNullOrEmpty(text)) return;

                string label = Label();
                if (text.Contains(label)) return;

                int insertAt = FindInsertPosition(text);
                if (insertAt < 0) return;

                __instance.textComponent.text = text.Substring(0, insertAt) + "     " + Build(label, showColor, showTime) + text.Substring(insertAt);

                Logger.Log("[RainbowJudgement] 结果页: 判定数=" + RainbowState.Count
                    + " 平均波长=" + RainbowState.AverageWavelength.ToString("F1") + "nm"
                    + " 平均时间=" + RainbowState.AverageAbsTimeMs.ToString("F2") + "ms"
                    + " 显示=[" + (showTime ? "T" : "") + (showColor ? "C" : "") + "]");
            }
            catch (Exception ex)
            {
                Logger.Warn("[RainbowJudgement] 结果页 hook 异常: " + ex.Message);
            }
        }

        /// <summary>构建「平均判定：…」（只有 ■ 带颜色标签）</summary>
        private static string Build(string label, bool showColor, bool showTime)
        {
            string result = label + "：";
            if (showColor)
                result += "<color=#" + Spectrum.ToHex(Spectrum.WavelengthToRgb(RainbowState.AverageWavelength)) + ">\u25A0</color>";

            if (showTime)
            {
                string time = Fmt.Sig3Ms(RainbowState.AverageAbsTimeMs);
                result += showColor ? "（" + time + "ms）" : time + "ms";
            }
            return result;
        }

        /// <summary>定位插入点：最后一个非空行里第一个 5 空格分隔符之后（文本以换行结尾，不能直接用 LastIndexOf）</summary>
        private static int FindInsertPosition(string text)
        {
            int end = text.Length;
            while (end > 0)
            {
                int previousNewline = text.LastIndexOf("\n", end - 1);
                if (previousNewline < 0) break;

                string line = text.Substring(previousNewline + 1, end - previousNewline - 1);
                if (line.Trim().Length > 0)
                {
                    int separator = line.IndexOf("     ");
                    return separator >= 0 ? previousNewline + 1 + separator : -1;
                }
                end = previousNewline;
            }
            return -1;
        }

        /// <summary>多语言标签：平均判定（跟随游戏语言，未知语言回退英文）</summary>
        private static string Label()
        {
            try
            {
                switch (Persistence.language)
                {
                    case SystemLanguage.Chinese:
                    case SystemLanguage.ChineseSimplified:
                    case SystemLanguage.ChineseTraditional:
                    case SystemLanguage.Japanese:
                        return "平均判定";
                    case SystemLanguage.Korean:
                        return "평균 판정";
                    case SystemLanguage.Spanish:
                        return "Juicio promedio";
                    case SystemLanguage.French:
                        return "Jugement moyen";
                    case SystemLanguage.German:
                        return "Durchschnittliches Urteil";
                    case SystemLanguage.Russian:
                        return "Среднее суждение";
                    case SystemLanguage.Portuguese:
                        return "Julgamento médio";
                    case SystemLanguage.Polish:
                        return "Średni osąd";
                    case SystemLanguage.Italian:
                        return "Giudizio medio";
                    case SystemLanguage.Turkish:
                        return "Ortalama yargı";
                    default:
                        return "Average Judgement";
                }
            }
            catch { return "Average Judgement"; }
        }
    }
}
