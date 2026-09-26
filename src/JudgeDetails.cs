using System;
using System.Text;

namespace RainbowJudgement
{
    /// <summary>
    /// 「判定详情」的数值/文案拼装（**唯一出口**：结尾页与关卡内实时显示共用，保证小数规则永远一致）。
    ///   · 颜色 = 平均波长 → Spectrum（与 tick / 判定文字同一套映射）
    ///   · 时间 = 平均绝对时间偏差，沿用 Fmt.Sig3Ms 的三位有效数字口径（与 v1.1.0 一致）
    ///   · 角度 = 平均绝对角度偏差，保留 1 位小数
    /// </summary>
    public static class JudgeDetails
    {
        /// <summary>平均判定颜色的色块（富文本，只有它带颜色）</summary>
        public static string ColorBlock()
        {
            return "<color=#" + AverageHex() + ">\u25A0</color>";
        }

        public static string AverageHex()
        {
            return Spectrum.ToHex(Spectrum.WavelengthToRgb(RainbowState.AverageWavelength));
        }

        /// <summary>平均绝对时间偏差：7.01ms</summary>
        public static string TimeText()
        {
            return Fmt.Sig3Ms(RainbowState.AverageAbsTimeMs) + "ms";
        }

        /// <summary>平均绝对角度偏差：12.3°</summary>
        public static string AngleText()
        {
            return RainbowState.AverageAbsDeg.ToString("F1") + "\u00B0";
        }

        /// <summary>
        /// 结尾页那一行：「平均判定：■（7.01ms，12.3°）」
        /// 色块只在开启平均判定颜色时出现；时间/角度按开关拼接，用全角逗号分隔（与 v1.1.0 的括号风格一致）。
        /// </summary>
        public static string EndPageLine(string label, bool showColor, bool showTime, bool showAngle)
        {
            string values = JoinValues(showTime, showAngle);
            if (values.Length == 0) return showColor ? label + "：" + ColorBlock() : "";

            if (showColor) return label + "：" + ColorBlock() + "（" + values + "）";
            return label + "：" + values;
        }

        private static string JoinValues(bool showTime, bool showAngle)
        {
            if (showTime && showAngle) return TimeText() + "," + AngleText();
            if (showTime) return TimeText();
            if (showAngle) return AngleText();
            return "";
        }

        // ---------------- 关卡内实时文本 ----------------
        // 前缀文本由玩家在设置页自定义（原样输出、不跟随语言；默认值是首次启动时按当时语言写入的"名称＋："）。

        public static string LiveColorText()
        {
            return Prefix("LiveColorText") + ColorBlock();
        }

        public static string LiveTimeText()
        {
            return Prefix("LiveTimeText") + TimeText();
        }

        public static string LiveAngleText()
        {
            return Prefix("LiveAngleText") + AngleText();
        }

        private static string Prefix(string which)
        {
            try
            {
                RainbowSettings s = Main.Settings;
                if (s == null) return "";
                s.EnsureLiveTexts();
                if (which == "LiveColorText") return s.LiveColorText ?? "";
                if (which == "LiveTimeText") return s.LiveTimeText ?? "";
                return s.LiveAngleText ?? "";
            }
            catch { return ""; }
        }

        // ---------------- 结尾页：自定义判定计数 ----------------

        /// <summary>
        /// 「自定义判定计数：[12/8/3/45/2/1/0]」
        /// 按判定边界**左右对称**排列：中间是最严格那一档（早/晚合并成 1 个数字），
        /// 越往两边越宽，左边是 early、右边是 late；数字按该档的颜色着色。
        /// 没有任何启用档位时返回空串（调用方跳过这一行）。
        /// </summary>
        public static string CustomCountLine(string label)
        {
            int count = BuildCustomSequence();
            if (count <= 0) return "";

            StringBuilder sb = new StringBuilder(64);
            sb.Append(label).Append("：[");
            AppendColorizedCounts(sb, "/");
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// 把"按档位着色的计数序列"追加到 <paramref name="sb"/>（结尾页与关卡内实时显示共用）。
        /// 序列顺序由 <see cref="BuildCustomSequence"/> 决定（左右对称），这里只负责上色与分隔符。
        /// 必须是富文本（TMP / UGUI 都吃 &lt;color&gt;）。
        /// </summary>
        public static void AppendColorizedCounts(StringBuilder sb, string separator)
        {
            int count = BuildCustomSequence();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(separator);
                string hex = CustomJudge.DigitHex(CustomJudge.EnabledAt(SequenceIndex(i)));
                sb.Append("<color=#").Append(hex).Append(">").Append(SequenceValue(i)).Append("</color>");
            }
        }

        // ---------------- 显示顺序（结果页与关卡内实时共用，无分配） ----------------

        private static readonly int[] _seqIndex = new int[CustomJudge.MaxTiers * 2];
        private static readonly int[] _seqValue = new int[CustomJudge.MaxTiers * 2];

        /// <summary>
        /// 把数字按"左右对称"的顺序展开到内部缓冲，返回个数：
        /// [最宽档.early, …, 次宽档.early, 最严档(早/晚合并), 次宽档.late, …, 最宽档.late]
        /// </summary>
        public static int BuildCustomSequence()
        {
            int n = CustomJudge.EnabledCount;
            int k = 0;
            for (int i = n - 1; i >= 1; i--) { _seqIndex[k] = i; _seqValue[k] = CustomCounter.Early[i]; k++; }
            if (n >= 1) { _seqIndex[k] = 0; _seqValue[k] = CustomCounter.Total(0); k++; }
            for (int i = 1; i <= n - 1; i++) { _seqIndex[k] = i; _seqValue[k] = CustomCounter.Late[i]; k++; }
            return k;
        }

        public static int SequenceIndex(int position) { return _seqIndex[position]; }
        public static int SequenceValue(int position) { return _seqValue[position]; }
    }
}
