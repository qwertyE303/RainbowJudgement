using System;
using HarmonyLib;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 关卡结果页：在 X-Accuracy 与 Checkpoints 之间（最后一行）插入"平均判定"信息。
    /// 显示规则（仅 ■ 使用平均判定颜色，其余字符为默认文本色）：
    ///   时间+颜色都开: 平均判定：■（-7.01ms）
    ///   只开时间:       平均判定：-7.01ms
    ///   只开颜色:       平均判定：■
    /// </summary>
    [HarmonyPatch(typeof(DetailedResults), "ShowForPlayer")]
    public static class ResultsPatch
    {
        /// <summary>多语言标签：平均判定（跟随游戏语言）</summary>
        private static string GetMarker()
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
                        return "Average Judgment";
                }
            }
            catch
            {
                return "Average Judgment";
            }
        }

        [HarmonyPostfix]
        public static void Postfix(DetailedResults __instance, int playerIndex)
        {
            try
            {
                if (!Main.Enabled || !Main.Settings.EnableRainbow || !Main.Settings.ShowAverageJudgment)
                    return;
                bool showTime = Main.Settings.ShowAverageTime;
                bool showColor = Main.Settings.ShowAverageColor;
                if (!showTime && !showColor)
                    return;
                if (__instance == null || __instance.textComponent == null)
                    return;
                if (RainbowState.Count <= 0)
                    return;

                string text = __instance.textComponent.text;
                if (string.IsNullOrEmpty(text))
                    return;
                string marker = GetMarker();
                if (text.Contains(marker))
                    return;

                // 构建"平均判定：..."（只有 ■ 带颜色标签，其余为默认文本色）
                string avgText = marker + "：";
                if (showColor)
                {
                    double avg = RainbowState.Average;
                    Color32 c = Spectrum.WavelengthToRgb(avg);
                    string hex = Spectrum.ToHex(c);
                    avgText += "<color=#" + hex + ">\u25A0</color>";
                }
                if (showTime)
                {
                    string timeStr = FormatSig3(RainbowState.AverageTimeMs);
                    if (showColor) avgText += "（" + timeStr + "ms）";
                    else avgText += timeStr + "ms";
                }

                // 结果页最后一行（accuracy 行）：<accuracy 段>     <checkpoints 段>
                // 定位最后一个非空行，在其第一个 5 空格分隔符之后插入（文本以换行结尾，不能直接用 LastIndexOf）
                int sep = -1;
                int nl = text.Length;
                while (nl > 0)
                {
                    int prev = text.LastIndexOf("\n", nl - 1);
                    if (prev < 0) break;
                    string line = text.Substring(prev + 1, nl - prev - 1);
                    if (line.Trim().Length > 0)
                    {
                        int i = line.IndexOf("     ");
                        if (i >= 0) sep = prev + 1 + i;
                        break;
                    }
                    nl = prev;
                }
                if (sep < 0) return;

                string insert = "     " + avgText;
                __instance.textComponent.text = text.Substring(0, sep) + insert + text.Substring(sep);

                Logger.Log("[RainbowJudgement] 结果页: 判定数=" + RainbowState.Count
                    + " 平均波长=" + RainbowState.Average.ToString("F1") + "nm"
                    + " 平均时间=" + RainbowState.AverageTimeMs.ToString("F2") + "ms"
                    + " 显示=[" + (showTime ? "T" : "") + (showColor ? "C" : "") + "]");
            }
            catch (Exception ex)
            {
                Logger.Warn("[RainbowJudgement] 结果页 hook 异常: " + ex.Message);
            }
        }

        /// <summary>三位有效数字格式化（如 7.01 / -12.3 / 0.456）</summary>
        private static string FormatSig3(double ms)
        {
            double abs = Math.Abs(ms);
            int f;
            if (abs >= 100.0) f = 0;
            else if (abs >= 10.0) f = 1;
            else if (abs >= 1.0) f = 2;
            else f = 3;
            return ms.ToString("F" + f);
        }
    }
}

