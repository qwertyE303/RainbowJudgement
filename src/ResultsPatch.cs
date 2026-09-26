using System;
using HarmonyLib;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 结果页：
    ///   ① 「平均判定」——插在最后一行（X-Accuracy 与 Checkpoints 之间），可分别开关 颜色 / 时间 / 角度：
    ///        全开：平均判定：■（7.01ms,12.3°）
    ///        只有时间：平均判定：7.01ms
    ///        只有角度：平均判定：12.3°
    ///   ② 「自定义判定计数：[…/…/…]」——数字按档位着色、左右对称：
    ///      · 有「错过：x  按太快：x」那一行（noFail / 安全砖 / coop）→ 塞进该行末尾；
    ///      · 没有那一行（普通关卡）→ **新起一行**，插在「太快/太慢」与「准确度/已用检查点/最大按键」之间。
    /// 两者都只改动文本内容，**不做任何版面位移**。
    /// 各自独立判重（coop 每 2 秒轮播 / 暂停恢复会重复调用 ShowForPlayer）。
    /// </summary>
    [HarmonyPatch(typeof(DetailedResults), "ShowForPlayer")]
    public static class ResultsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DetailedResults __instance, int playerIndex)
        {
            try
            {
                if (__instance == null || __instance.textComponent == null) return;
                if (!Main.Active) return;

                RainbowSettings settings = Main.Settings;
                if (settings == null) return;

                bool showAverage = settings.ShowJudgeDetails
                    && (settings.ShowAverageColor || settings.ShowAverageTime || settings.ShowAverageAngle);
                bool showCount = settings.EnableCustomJudge && settings.ShowCustomCountInResults
                    && CustomJudge.EnabledCount > 0;
                if (!showAverage && !showCount) return;

                string text = __instance.textComponent.text;
                if (string.IsNullOrEmpty(text)) return;

                bool changed = false;
                if (showAverage) changed |= InsertAverage(__instance, ref text, settings);
                if (showCount) changed |= InsertCustomCount(ref text);

                if (changed)
                {
                    __instance.textComponent.text = text;
                    Logger.Log("[RainbowJudgement] 结果页: 判定数=" + RainbowState.Count
                        + " 平均波长=" + RainbowState.AverageWavelength.ToString("F1") + "nm"
                        + " 平均时间=" + RainbowState.AverageAbsTimeMs.ToString("F2") + "ms"
                        + " 平均角度=" + RainbowState.AverageAbsDeg.ToString("F2") + "°"
                        + " 完美窗口内=" + RainbowProgress.CountedInPure
                        + " 强制PP=" + RainbowProgress.CountedForcedPP
                        + " 游戏(Perfect+Auto)=" + GameState.PerfectCount
                        + " 显示=[平均" + (showAverage ? "T" : "-")
                        + (settings.ShowAverageTime ? "t" : "-")
                        + (settings.ShowAverageAngle ? "a" : "-")
                        + (settings.ShowAverageColor ? "c" : "-") + " 计数" + (showCount ? "T" : "F") + "]");

                    // 对账用：游戏自己的 hitMargins 分布（我们的 in-window / 强制PP 应当能和它对上）
                    Logger.Log("[RainbowJudgement] 游戏 hitMargins: " + JudgeHooks.GetMarginHook.HitMarginsHistogram());
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[RainbowJudgement] 结果页 hook 异常: " + ex.Message);
            }
        }

        // ---------------- 平均判定 ----------------

        private static bool InsertAverage(DetailedResults instance, ref string text, RainbowSettings settings)
        {
            string label = Lang.ResultsLabel("average");
            if (text.Contains(label))
            {
                // 防重复插入（coop 每 2 秒轮播 / 暂停恢复会再次调用 ShowForPlayer）
                Logger.Log("[RainbowJudgement] 结果页跳过：文本已含平均判定标签");
                return false;
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

            int start, end;
            if (!FindLastLine(text, out start, out end))
            {
                LogExit("找不到任何非空行", text);
                return false;
            }

            int separator = text.IndexOf("     ", start, StringComparison.Ordinal);
            if (separator < 0 || separator >= end)
            {
                // 找不到「5 空格分隔符」定位点——「结果页不显示」的另一个可能出口，必须留证
                LogExit("最后一行无 5 空格分隔符", text);
                return false;
            }

            string line = JudgeDetails.EndPageLine(label, settings.ShowAverageColor,
                settings.ShowAverageTime, settings.ShowAverageAngle);
            text = text.Substring(0, separator) + "     " + line + text.Substring(separator);
            return true;
        }

        /// <summary>
        /// 未插入时的诊断日志（「结尾不显示」探针）：
        /// 结果页有判定数据却没显示 = 真 bug → Warn（UMM 也留一份）；确实一条都没统计到 → 普通日志。
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

        // ---------------- 自定义判定计数（塞进「错过 / 按太快」那一行） ----------------

        private static bool InsertCustomCount(ref string text)
        {
            string label = Lang.ResultsLabel("customCount");
            if (text.Contains(label))
            {
                Logger.Log("[RainbowJudgement] 结果页跳过：文本已含自定义判定计数标签");
                return false;
            }

            string line = JudgeDetails.CustomCountLine(label);
            if (string.IsNullOrEmpty(line)) return false;

            int at = FindMissLineEnd(text);
            if (at >= 0)
            {
                // 有「错过 / 按太快」那一行 → 塞进该行末尾
                text = text.Substring(0, at) + "     " + line + text.Substring(at);
                return true;
            }

            // 没有那一行（普通关卡）→ **新起一行**，插在「太快/太慢」与「准确度/已用检查点/最大按键」之间，
            // 也就是它自己成为新的第 3 个非空行
            int start = FindNthLineStart(text, 3);
            if (start < 0)
            {
                int lastStart, lastEnd;
                if (!FindLastLine(text, out lastStart, out lastEnd)) return false;
                text = text.Substring(0, lastEnd) + "\n" + line + text.Substring(lastEnd);
                Logger.Log("[RainbowJudgement] 结果页：行数不足，自定义判定计数追加到最后一行的下面");
                return true;
            }

            text = text.Substring(0, start) + line + "\n" + text.Substring(start);
            Logger.Log("[RainbowJudgement] 结果页：没有「错过/按太快」行 → 新起一行插在第 3 行位置");
            return true;
        }

        /// <summary>
        /// 找「错过：x  按太快：x」那一行的**内容末尾**（不含行尾空白）。标签直接取游戏自己的文案
        /// （RDString "status.results.missFails"，语言/翻译变化都能跟上）；找不到返回 -1。
        /// </summary>
        private static int FindMissLineEnd(string text)
        {
            try
            {
                string miss = RDString.Get("status.results.missFails", null);
                if (string.IsNullOrEmpty(miss)) return -1;

                int index = text.IndexOf(miss, StringComparison.Ordinal);
                if (index < 0) return -1;

                // 游戏文案本身可能带前导换行，跳过空白才是这一行真正的开头
                int lineStart = index;
                while (lineStart < text.Length && char.IsWhiteSpace(text[lineStart])) lineStart++;
                if (lineStart >= text.Length) return -1;

                int lineEnd = text.IndexOf('\n', lineStart);
                if (lineEnd < 0) lineEnd = text.Length;

                int contentEnd = LineContentEnd(text, lineStart, lineEnd);
                return contentEnd > lineStart ? contentEnd : -1;
            }
            catch { return -1; }
        }

        // ---------------- 行定位 ----------------

        /// <summary>最后一个非空行的区间 [start, end)：end = 内容末尾（不含行尾空白）</summary>
        private static bool FindLastLine(string text, out int start, out int end)
        {
            start = -1;
            end = -1;
            int stop = text.Length;
            while (stop > 0)
            {
                int newline = text.LastIndexOf('\n', stop - 1);
                int lineStart = newline + 1;
                int contentEnd = LineContentEnd(text, lineStart, stop);
                if (contentEnd > lineStart)
                {
                    start = lineStart;
                    end = contentEnd;
                    return true;
                }
                if (newline < 0) break;
                stop = newline;
            }
            return false;
        }

        private static int LineContentEnd(string text, int from, int to)
        {
            int i = to;
            while (i > from && char.IsWhiteSpace(text[i - 1])) i--;
            return i;
        }

        /// <summary>第 ordinal 个（1 起）非空行的**内容起始位置**（跳过行首空白）；行数不够返回 -1</summary>
        private static int FindNthLineStart(string text, int ordinal)
        {
            int count = 0;
            int position = 0;
            while (position <= text.Length)
            {
                int newline = text.IndexOf('\n', position);
                int lineEnd = newline < 0 ? text.Length : newline;
                int contentStart = position;
                while (contentStart < lineEnd && char.IsWhiteSpace(text[contentStart])) contentStart++;
                if (contentStart < lineEnd)
                {
                    count++;
                    if (count == ordinal) return contentStart;
                }
                if (newline < 0) break;
                position = newline + 1;
            }
            return -1;
        }
    }
}
