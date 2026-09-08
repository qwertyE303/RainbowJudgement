using System;

namespace RainbowJudgement
{
    /// <summary>数值格式化。
    /// 两处「三位有效数字」的历史口径不同（一个是 ms 偏差、一个是 X^n 的 n），这里保留各自语义并显式命名，
    /// 避免以后被误当成同一件事合并掉。均沿用系统区域设置（与原实现一致）。</summary>
    public static class Fmt
    {
        /// <summary>结果页的平均绝对偏差(ms)：≥100→0 位小数、≥10→1 位、≥1→2 位、否则 3 位</summary>
        public static string Sig3Ms(double ms)
        {
            double abs = Math.Abs(ms);
            int digits;
            if (abs >= 100.0) digits = 0;
            else if (abs >= 10.0) digits = 1;
            else if (abs >= 1.0) digits = 2;
            else digits = 3;
            return ms.ToString("F" + digits);
        }

        /// <summary>X^n 的 n：≥1000 用科学计数、≥100→0 位、≥10→1 位、否则 2 位小数（n&lt;1 时即两位小数）</summary>
        public static string Sig3N(double n)
        {
            if (n >= 1000.0) return n.ToString("G3");
            double abs = Math.Abs(n);
            int digits;
            if (abs >= 100.0) digits = 0;
            else if (abs >= 10.0) digits = 1;
            else digits = 2;
            return n.ToString("F" + digits);
        }
    }
}
