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

                string text = __instance.textComponent.text;
                if (string.IsNullOrEmpty(text)) return;

                string label = Label();
                if (text.Contains(label))
                {
                    // 防重复插入（coop 每 2 秒轮播 / 暂停恢复会再次调用 ShowForPlayer）
                    Logger.Log("[RainbowJudgement] 结果页跳过：文本已含标签（本轮已插入过）");
                    return;
                }

                if (RainbowState.Count <= 0)
                {
                    // 没有任何可统计的判定（正常只会出现在「一个判定都还没打」的空局；
                    // 若账本里有条目却统计为 0，说明计数与账本脱节 → 报警）
                    string why = "[RainbowJudgement] 结果页无样本：统计数=" + RainbowState.Count
                        + " 账本=" + RainbowProgress.Count + "(已统计" + RainbowProgress.Counted + ")"
                        + " 游戏=" + GameState.MarginCount + "(可判定" + GameState.CountableCount + ")";
                    if (RainbowProgress.Counted > 0 || GameState.CountableCount > 0) Logger.Warn(why);
                    else Logger.Log(why + "（本局没有判定，属正常）");
                }

                int insertAt = FindInsertPosition(text);
                if (insertAt < 0)
                {
                    // 找不到「5 空格分隔符」定位点——「结果页不显示」的另一个可能出口，必须留证
                    LogExit("未找到插入点（最后一行无 5 空格分隔符）", text);
                    return;
                }

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

        /// <summary>
        /// 未插入时的诊断日志（Bug 1「结尾不显示」探针）：
        /// 结果页有判定数据却没显示 = 真 bug → Warn（UMM 也留一份）；确实一条都没统计到 → 普通日志。
        /// 两种情况下都把定位信息（判定数 / 账本条数 / 游戏条数 / 最后一行原文）写清楚，
        /// 便于下一次出现时一眼判定是「统计为空」还是「插入点定位失败」。
        /// </summary>
        private static void LogExit(string reason, string text)
        {
            Logger.Warn("结果页未插入：" + reason
                + " | 统计数=" + RainbowState.Count
                + " 账本=" + RainbowProgress.Count + "(已统计" + RainbowProgress.Counted + ")"
                + " 游戏=" + GameState.MarginCount + "(可判定" + GameState.CountableCount + ")"
                + " | 末行=[" + LastNonEmptyLine(text) + "]");
        }

        /// <summary>取文本最后一个非空行（截断到 160 字符），用于诊断日志</summary>
        private static string LastNonEmptyLine(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return "";
                string[] lines = text.Replace("\r", "").Split('\n');
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0) continue;
                    return line.Length > 160 ? line.Substring(0, 160) + "…" : line;
                }
            }
            catch { }
            return "";
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
