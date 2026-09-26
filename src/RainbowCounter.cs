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

        private static double _sumPerfectRatio; // Σp（p=|角度|/PP边界；0~1 在 PP 内，>1 为 PP 开外的提前/落后）
        private static int _perfectCount;       // 计入的判定次数（p=0 也计入分母）

        // ---------------- 派生量 ----------------

        /// <summary>7 档计数之和（= 完美窗口内的判定数，不变量自检用：应等于游戏的严格 Perfect + Auto）</summary>
        public static int TotalTiers()
        {
            return GreenEarly + BlueEarly + CyanEarly + Purple + CyanLate + BlueLate + GreenLate;
        }

        /// <summary>平均归一化完美度 r = Σp / 次数</summary>
        public static double GetPerfectRatio()
        {
            return _perfectCount > 0 ? _sumPerfectRatio / _perfectCount : 0.0;
        }

        // ---------------- 累加 / 重置 ----------------

        /// <summary>按档位号累加一次判定（实时追加与全量重放共用同一条路径）。
        /// 只统计**原版完美窗口内**（InPure）的判定：PP 开外的 EP/LP/VE/VL/Too 由 RainbowProgress 挡住，
        /// 与平均判定颜色 / 偏差（全部判定）刻意分成两套口径。
        /// 占位条目（Tier&lt;0，尖刺/激光等无判定数据）不进任何统计。</summary>
        public static void AddTier(int tier, double p)
        {
            if (tier < 0) return;
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

        /// <summary>
        /// 强制 PP（中旋 midspin / 旧式 autoplay，游戏改写档位后记成 Perfect）对 X^n 的贡献：
        /// **不进任何档位桶**（那 7 档是"角度真的落在完美窗口内"的计数，不变量自检依赖它），
        /// 但按游戏口径**算一次完美中心**参与 r 的平均 —— 即 p 记 0（与 auto/正中同口径）。
        /// 游戏自己也是这么算的：X-Accuracy 里 HitMargin.Perfect 拿满分权重 1.0，
        /// <c>deadTiles</c> 还把 midSpin 砖显式排除，所以中旋在游戏账本里就是零瑕疵。
        /// </summary>
        public static void AddForcedPerfect()
        {
            _sumPerfectRatio += 0.0;
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

        // ---------------- 原版完美绿（X^n 在没有可用自定义档位时的回退色） ----------------

        private static string _greenHex = "5FFF4E"; // 原版完美绿 fallback
        private static bool _greenLoaded;

        /// <summary>原版完美绿，运行时读 RDConstants.hitMarginColoursUI.colourPerfect。
        /// 用途：X^n 的 "X" 在**没有任何可用自定义档位**时的回退色（有档位时跟随该档的自定义颜色）。</summary>
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
    }
}
