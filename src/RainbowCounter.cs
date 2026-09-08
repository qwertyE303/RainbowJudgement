using System;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 彩虹计数（纯数据）：PurePerfect（不含 EP/LP）按误差分四段 —— 紫 / 青 / 蓝 / 完美绿，
    /// 每段再分提前(早)/落后(晚)，共 7 个计数。
    /// 数据由 RainbowProgress 追加与重放，这里只保存数值与派生量。
    /// </summary>
    public static class RainbowCounter
    {
        // 档位编号：0=紫 1=青早 2=青晚 3=蓝早 4=蓝晚 5=绿早 6=绿晚
        public const int TierPurple = 0;
        public const int TierCyanEarly = 1;
        public const int TierCyanLate = 2;
        public const int TierBlueEarly = 3;
        public const int TierBlueLate = 4;
        public const int TierGreenEarly = 5;
        public const int TierGreenLate = 6;
        public const int TierCount = 7;

        /// <summary>提前·完美绿(2/3PP~PP)</summary>
        public static int GreenEarly;
        /// <summary>提前·蓝(0.5PP~2/3PP)</summary>
        public static int BlueEarly;
        /// <summary>提前·青(1/3PP~0.5PP)</summary>
        public static int CyanEarly;
        /// <summary>紫(≤1/3PP，提前+落后合并)</summary>
        public static int Purple;
        /// <summary>落后·青</summary>
        public static int CyanLate;
        /// <summary>落后·蓝</summary>
        public static int BlueLate;
        /// <summary>落后·完美绿</summary>
        public static int GreenLate;

        private static double _sumPerfectRatio; // Σp（p=|角度|/PP边界，0~1）
        private static int _perfectCount;       // 计入的判定次数（p=0 也计入分母）

        // ---------------- 派生量 ----------------

        /// <summary>7 档计数之和（应与游戏 hitMarginsCount[Perfect]+[Auto] 相等，不变量自检用）</summary>
        public static int TotalTiers()
        {
            return GreenEarly + BlueEarly + CyanEarly + Purple + CyanLate + BlueLate + GreenLate;
        }

        /// <summary>平均归一化完美度 r = Σp / 次数</summary>
        public static double GetPerfectRatio()
        {
            return _perfectCount > 0 ? _sumPerfectRatio / _perfectCount : 0.0;
        }

        /// <summary>X^n 的颜色档位：存在绿→3(540nm)、蓝→2(487nm)、青→1(460nm)，否则 0(433nm)</summary>
        public static int GetXColorIndex()
        {
            if (GreenEarly != 0 || GreenLate != 0) return 3;
            if (BlueEarly != 0 || BlueLate != 0) return 2;
            if (CyanEarly != 0 || CyanLate != 0) return 1;
            return 0;
        }

        /// <summary>按绝对角度误差分档（纯函数，不累加）：a1/a2/a3 = 1/3PP、0.5PP、2/3PP 边界角</summary>
        public static int TierOf(double absDeg, bool isEarly, double a1, double a2, double a3)
        {
            if (absDeg <= a1) return TierPurple;
            if (absDeg <= a2) return isEarly ? TierCyanEarly : TierCyanLate;
            if (absDeg <= a3) return isEarly ? TierBlueEarly : TierBlueLate;
            return isEarly ? TierGreenEarly : TierGreenLate;
        }

        // ---------------- 累加 / 重置 ----------------

        /// <summary>按档位号累加一次判定（实时追加与回档重放共用同一条路径）</summary>
        public static void AddTier(int tier, double p)
        {
            switch (tier)
            {
                case TierPurple: Purple++; break;
                case TierCyanEarly: CyanEarly++; break;
                case TierCyanLate: CyanLate++; break;
                case TierBlueEarly: BlueEarly++; break;
                case TierBlueLate: BlueLate++; break;
                case TierGreenEarly: GreenEarly++; break;
                default: GreenLate++; break;
            }
            _sumPerfectRatio += p; // p=0（auto/正中）也算入分母，保证重放与实时口径一致
            _perfectCount++;
        }

        /// <summary>只清计数与 p 统计（重放前调用）</summary>
        public static void ResetCounts()
        {
            GreenEarly = BlueEarly = CyanEarly = Purple = CyanLate = BlueLate = GreenLate = 0;
            _sumPerfectRatio = 0.0;
            _perfectCount = 0;
        }

        /// <summary>全量重置（含 auto 标记）</summary>
        public static void Reset()
        {
            ResetCounts();
            LastJudge.Clear();
        }

        // ---------------- 原版完美绿 ----------------

        private static string _greenHex = "5FFF4E"; // 原版完美绿 fallback
        private static bool _greenLoaded;

        /// <summary>原版完美绿（供 X^n 档位色使用），运行时读 RDConstants.hitMarginColoursUI.colourPerfect</summary>
        public static Color32 GetPerfectGreenColor()
        {
            EnsureGreenHex();
            return Spectrum.ParseHex(_greenHex, new Color32(95, 255, 78, 255));
        }

        private static void EnsureGreenHex()
        {
            if (_greenLoaded) return;
            _greenLoaded = true;
            try
            {
                var data = RDConstants.data;
                if (data == null) return;
                var field = data.GetType().GetField("hitMarginColoursUI",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field == null) return;
                object scheme = field.GetValue(data);
                if (scheme == null) return;
                var perfectField = scheme.GetType().GetField("colourPerfect",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (perfectField == null) return;
                UnityEngine.Color pc = (UnityEngine.Color)perfectField.GetValue(scheme);
                _greenHex = string.Format("{0:X2}{1:X2}{2:X2}", (int)(pc.r * 255f), (int)(pc.g * 255f), (int)(pc.b * 255f));
            }
            catch { }
        }

        /// <summary>原版完美绿的 hex（计数器文本用），保证已尝试读取过</summary>
        public static string PerfectGreenHex
        {
            get { EnsureGreenHex(); return _greenHex; }
        }
    }
}
