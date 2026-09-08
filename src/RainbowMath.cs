using System;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 判定相关的数学：档位边界角、锚点位置、锚点间渐变、角度↔时间换算。
    /// 全部与原版 GetHitMargin / GetAdjustedAngleBoundaryInDeg 同构。
    /// </summary>
    public static class RainbowMath
    {
        /// <summary>仪表盘的满刻度（原版把 Counted 边界画在 ±60 处）</summary>
        public const float CountedScaled = 60f;
        public const double MinWavelengthNm = 380.0;  // 完美中心（深紫）
        public const double RedWavelengthNm = 700.0;  // 红
        public static readonly Color32 RedColor = new Color32(255, 0, 0, 255);

        /// <summary>档位下标：0=1/3PP 1=0.5PP 2=2/3PP 3=PP 4=EP/LP 5=Counted</summary>
        public const int Tier1Of3PP = 0;
        public const int TierHalfPP = 1;
        public const int Tier2Of3PP = 2;
        public const int TierPP = 3;
        public const int TierELP = 4;
        public const int TierCounted = 5;

        /// <summary>各档位的角度下限(度)——与 game 判定区间同构</summary>
        private static readonly double[] TierBaseDeg = { 10.0, 15.0, 20.0, 30.0, 45.0 };
        /// <summary>各档位的时间下限(秒)——低 BPM 下角度不足时由时间决定</summary>
        private static readonly double[] TierBaseSec = { 0.0075, 0.0125, 0.01667, 0.02, 0.03 };

        /// <summary>锚点固定波长(nm)：极低 bpm 下各档位边界在仪表盘(线性 380~700)上的对应色</summary>
        private static readonly double[] AnchorWavelength = BuildAnchorWavelength();

        private static double[] BuildAnchorWavelength()
        {
            double[] w = new double[6];
            for (int i = 0; i < 5; i++) w[i] = MinWavelengthNm + (RedWavelengthNm - MinWavelengthNm) * TierBaseDeg[i] / CountedScaled;
            w[5] = RedWavelengthNm;
            return w;
        }

        // ---------------- 速度 / 时间基准 ----------------

        /// <summary>当前速度：与游戏 GetAdjustedAngleBoundaryInDeg 一致（currentSpeedTrial 优先，回退 prevfloor.speed）</summary>
        public static double GetCurrentSpeed()
        {
            try
            {
                double speed = GCS.currentSpeedTrial;
                if (speed <= 0.0001)
                {
                    scrController ctrl = scrController.instance;
                    if (ctrl != null && ctrl.playerOne != null && ctrl.playerOne.currFloor != null && ctrl.playerOne.currFloor.prevfloor != null)
                        speed = ctrl.playerOne.currFloor.prevfloor.speed;
                    if (speed <= 0.0001) speed = 1.0;
                }
                return speed;
            }
            catch { return 1.0; }
        }

        /// <summary>Counted 时间下限(ms)：随难度 40/65/91ms，除以当前速度，绝对下限 25ms（与游戏一致）</summary>
        public static double CountedTimeMs()
        {
            try
            {
                double t = 0.065;
                int difficulty = (int)GCS.difficulty;
                if (difficulty == 0) t = 0.091;
                else if (difficulty == 2) t = 0.04;
                return Math.Max(t / GetCurrentSpeed(), 0.025) * 1000.0;
            }
            catch { return 65.0; }
        }

        /// <summary>复刻原版 AddHit 的 speed 取值：hitFloor.speed → playerOne.prevfloor.speed → 1</summary>
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

        /// <summary>缩放角度 → 真实误差时间(ms)：缩放/60×CountedDeg→真实角度→时间（角速度系数3，基准=判定瞬间）</summary>
        public static double TimeMsFromScaledAngleAccurate(float scaledAngle, double countedDeg, double bpmTimesSpeed, double conductorPitch)
        {
            double angleSpeed = bpmTimesSpeed * conductorPitch * 3.0; // 1拍=180°，与 TimeToAngleInRad 系数一致
            if (double.IsNaN(angleSpeed) || angleSpeed <= 0.0001) return 0.0;
            if (double.IsNaN(countedDeg) || countedDeg <= 0.0001) return 0.0;
            double realAngle = scaledAngle / (double)CountedScaled * countedDeg;
            if (double.IsNaN(realAngle)) return 0.0;
            return realAngle / angleSpeed * 1000.0;
        }

        // ---------------- 档位边界 / 渐变 ----------------

        /// <summary>档位边界角度(度)：max(角度下限×mult, 时间下限对应角度)。与原版 PureDeg/PerfectDeg 同构，用游戏自身的方法算。</summary>
        public static double LevelBoundaryDeg(int levelIndex, double bpmTimesSpeed, double conductorPitch, double marginScale)
        {
            if (levelIndex < 0 || levelIndex >= TierCounted)
                return scrMisc.GetAdjustedAngleBoundaryInDeg(HitMarginGeneral.Counted, bpmTimesSpeed, conductorPitch, marginScale);

            double timeSec = TierBaseSec[levelIndex];
            double baseDeg = TierBaseDeg[levelIndex];

            if (levelIndex == TierPP || levelIndex == TierELP)
            {
                // PP/ELP 的时间下限按"练习速度"折算（=原版 PureDeg/PerfectDeg 的 currentSpeedTrial）
                double trial = GCS.currentSpeedTrial;
                if (trial <= 0.0001) trial = 1.0;
                timeSec = Math.Max(timeSec / trial, 0.025);
            }

            double timeAngle = scrMisc.TimeToAngleInRad(timeSec, bpmTimesSpeed, conductorPitch, false) * 57.29578;
            return Math.Max(baseDeg * marginScale, timeAngle);
        }

        /// <summary>档位锚点位置(缩放坐标)：边界角度×60/countedDeg（与原版分档边界缩放同构）</summary>
        public static double LevelAnchorPosition(int levelIndex, double countedDeg, double bpmTimesSpeed, double conductorPitch, double marginScale)
        {
            if (double.IsNaN(countedDeg) || countedDeg <= 0.0001) return 0.0;
            double boundaryDeg = LevelBoundaryDeg(levelIndex, bpmTimesSpeed, conductorPitch, marginScale);
            if (double.IsNaN(boundaryDeg)) return 0.0;
            return boundaryDeg * CountedScaled / countedDeg;
        }

        /// <summary>档位纯色(0..5)：433.3/460/486.7/540/620/700nm（供文字/分档使用）</summary>
        public static Color32 TierColor(int tier)
        {
            if (tier < 0 || tier > 5) return Spectrum.WavelengthToRgb(RedWavelengthNm);
            return Spectrum.WavelengthToRgb(AnchorWavelength[tier]);
        }

        /// <summary>锚点间线性渐变：pos=缩放刻度；锚点位置=LevelBoundaryDeg×60/countedDeg；锚点色=AnchorWavelength 固定</summary>
        public static double WavelengthForGradient(double pos, double countedDeg, double bpmTimesSpeed, double conductorPitch, double marginScale)
        {
            double[] anchors = new double[6];
            for (int i = 0; i < 6; i++) anchors[i] = LevelAnchorPosition(i, countedDeg, bpmTimesSpeed, conductorPitch, marginScale);

            double pMax = anchors[5];
            if (pMax <= 0.0001) pMax = CountedScaled;
            if (pos >= pMax) return RedWavelengthNm;

            double w0 = MinWavelengthNm;
            for (int i = 0; i < 6; i++)
            {
                double w1 = AnchorWavelength[i];
                double a0 = i == 0 ? 0.0 : anchors[i - 1];
                if (pos <= anchors[i] || i == 5)
                {
                    double span = anchors[i] - a0;
                    double t = span <= 0.0001 ? 1.0 : Math.Min((pos - a0) / span, 1.0);
                    return w0 + (w1 - w0) * t;
                }
                w0 = w1;
            }
            return RedWavelengthNm;
        }
    }
}
