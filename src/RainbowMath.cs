using System;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 判定相关的运行期数学：角度↔时间换算、原版边界角、7 档边界的刻度换算。
    /// 与原版 <c>scrMisc.GetHitMargin</c> / <c>GetAdjustedAngleBoundaryInDeg</c> / <c>TimeToAngleInRad</c> 同构：
    ///   角度(度) = 时间(ms) × bpm×speed×pitch×3 / 1000
    ///   边界角   = max(角度下限 × marginScale, 时间下限对应角度)   （PP/Pure 的时间下限按 currentSpeedTrial 折算并下限 25ms）
    /// 坐标系一律使用**仪表盘的 60 刻度**（60 = 原版 Counted 边界），见 <see cref="GradientFrame"/>。
    /// </summary>
    public static class RainbowMath
    {
        /// <summary>仪表盘满刻度（原版把 Counted 边界画在 ±60 处）</summary>
        public const double CountedScaled = 60.0;
        /// <summary>红端波长（仪表盘渐变的 100% 位置，= Spectrum.FullScaleRedNm）</summary>
        public const double RedWavelengthNm = Spectrum.FullScaleRedNm;
        /// <summary>原版 Perfect 边界的名义角度(度) —— 只用于取"该档锚点色"（位置另按游戏实际边界算）</summary>
        public const double PerfectNominalDeg = 30.0;
        /// <summary>原版 Pure（稍快/稍晚）边界的名义角度(度)</summary>
        public const double PureNominalDeg = 45.0;

        // ---------------- 速度 / 时间基准 ----------------

        /// <summary>每 1 毫秒误差对应多少度 = bpm×speed×pitch×3 / 1000（与 TimeToAngleInRad 逐位一致）</summary>
        public static double DegPerMs(double bpmTimesSpeed, double conductorPitch)
        {
            double value = bpmTimesSpeed * conductorPitch * 3.0 / 1000.0;
            if (double.IsNaN(value) || value <= 0.0000001) return 0.0;
            return value;
        }

        /// <summary>误差角度(度，取绝对值) → 误差时间(ms)</summary>
        public static double TimeMsOfDeg(double absDeg, double bpmTimesSpeed, double conductorPitch)
        {
            double perMs = DegPerMs(bpmTimesSpeed, conductorPitch);
            if (perMs <= 0.0000001) return 0.0;
            return absDeg / perMs;
        }

        /// <summary>练习速度（currentSpeedTrial）：原版把各档的时间下限除以它，低速练习时窗口会变宽</summary>
        public static double PracticeSpeed()
        {
            try
            {
                double value = GCS.currentSpeedTrial;
                return value > 0.0001 ? value : 1.0;
            }
            catch { return 1.0; }
        }

        /// <summary>复刻原版 AddHit / CalculateTickColor 的 speed 取值：hitFloor.speed → playerOne.prevfloor.speed → 1</summary>
        public static float GameSpeedOf(scrFloor hitFloor)
        {
            try
            {
                if (hitFloor != null) return hitFloor.speed;
                scrController ctrl = scrController.instance;
                if (ctrl != null && ctrl.playerOne != null && ctrl.playerOne.currFloor != null && ctrl.playerOne.currFloor.prevfloor != null)
                    return ctrl.playerOne.currFloor.prevfloor.speed;
                return 1f;
            }
            catch { return 1f; }
        }

        /// <summary>判定/刻度同款 bpm×speed（hitFloor 基准，与原版 AddHit/CalculateTickColor 一致）</summary>
        public static double GameBpmTimesSpeedOf(scrFloor hitFloor)
        {
            try { return scrConductor.instance.bpm * GameSpeedOf(hitFloor); }
            catch { return 120.0; }
        }

        public static double GetPitchNow()
        {
            try { return scrConductor.instance.song.pitch; }
            catch { return 1.0; }
        }

        // ---------------- 原版边界角（直接用游戏自己的函数，保证与判定分类逐位一致） ----------------

        /// <summary>Counted 边界角(度)：max(HITMARGIN_COUNTED×marginScale, 时间下限对应角度)</summary>
        public static double CountedBoundaryDeg(double bpmTimesSpeed, double conductorPitch, double marginScale)
        {
            return GameBoundary(HitMarginGeneral.Counted, bpmTimesSpeed, conductorPitch, marginScale, 60.0, 0.065);
        }

        /// <summary>Pure 边界角(度)：PP 之外那一档（稍快!/稍晚!）的外边界，原版取 45° / 30ms</summary>
        public static double PureBoundaryDeg(double bpmTimesSpeed, double conductorPitch, double marginScale)
        {
            // 原版 HitMarginGeneral 的命名与边界交叉：Perfect=1 给 45°，Pure=2 给 30°
            return GameBoundary(HitMarginGeneral.Perfect, bpmTimesSpeed, conductorPitch, marginScale, 45.0, 0.03);
        }

        /// <summary>Perfect 边界角(度)：原版 PP 边界，30° / 20ms（PP 与 EP/LP 的分界）</summary>
        public static double PerfectBoundaryDeg(double bpmTimesSpeed, double conductorPitch, double marginScale)
        {
            return GameBoundary(HitMarginGeneral.Pure, bpmTimesSpeed, conductorPitch, marginScale, 30.0, 0.02);
        }

        /// <summary>交给游戏算；失败时回退到同构公式（角度下限 × marginScale 与时间下限对应角度取大）</summary>
        private static double GameBoundary(HitMarginGeneral kind, double bpmTimesSpeed, double conductorPitch,
            double marginScale, double baseDeg, double baseSec)
        {
            try
            {
                double value = scrMisc.GetAdjustedAngleBoundaryInDeg(kind, bpmTimesSpeed, conductorPitch, marginScale);
                if (!double.IsNaN(value) && value > 0.0) return value;
            }
            catch { }

            double trial = 1.0;
            try { if (GCS.currentSpeedTrial > 0.0001f) trial = GCS.currentSpeedTrial; }
            catch { }
            double timeSec = kind == HitMarginGeneral.Counted ? baseSec : baseSec / trial;
            timeSec = Math.Max(timeSec, 0.025);
            double timeDeg = timeSec * 1000.0 * DegPerMs(bpmTimesSpeed, conductorPitch);
            return Math.Max(baseDeg * marginScale, timeDeg);
        }

        // ---------------- 固定 7 档（紫 / 青 / 蓝 / PP）的边界刻度 ----------------

        /// <summary>固定 7 档的角度下限(度)：紫 / 青 / 蓝（PP 用原版 Perfect 边界）</summary>
        private static readonly double[] FixedDeg = { 10.0, 15.0, 20.0 };
        /// <summary>固定 7 档的时间下限(ms)：紫 / 青 / 蓝（这三档不按 currentSpeedTrial 折算，与原实现一致）</summary>
        private static readonly double[] FixedMs = { 7.5, 12.5, 16.67 };

        /// <summary>固定 7 档边界（刻度）：0=紫 1=青 2=蓝；3=PP（用原版 Perfect 边界）</summary>
        public static double FixedTierScaled(int level, GradientFrame frame)
        {
            if (level >= 3) return frame.PpScaled;
            return Math.Max(FixedDeg[level] * frame.AScale, FixedMs[level] * frame.BScale);
        }

        /// <summary>7 档分档（纯函数，不累加）：a1/a2/a3 = 紫/青/蓝 的边界刻度</summary>
        public static int TierOf(double absScaled, bool isEarly, double a1, double a2, double a3)
        {
            if (absScaled <= a1) return RainbowCounter.TierPurple;
            if (absScaled <= a2) return isEarly ? RainbowCounter.TierCyanEarly : RainbowCounter.TierCyanLate;
            if (absScaled <= a3) return isEarly ? RainbowCounter.TierBlueEarly : RainbowCounter.TierBlueLate;
            return isEarly ? RainbowCounter.TierGreenEarly : RainbowCounter.TierGreenLate;
        }
    }
}
