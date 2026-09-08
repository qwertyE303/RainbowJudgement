using System;
using HarmonyLib;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 判定采集 hooks。
    /// 数据流：GetHitMargin 算出本次判定的分档/波长/时间/完美度并暂存 → scrMarginTracker.AddHit 消费并追加到账本。
    /// 这样账本与游戏的 hitMargins 严格同长同序，且"实时累加"与"回档重放"共用同一份数据。
    /// </summary>
    public static class JudgeHooks
    {
        /// <summary>tick 颜色改为彩虹渐变（与原版档位色无关）</summary>
        [HarmonyPatch(typeof(scrHitErrorMeter), "CalculateTickColor")]
        [HarmonyPriority(Priority.High)]
        public static class TickColorHook
        {
            [HarmonyPrefix]
            public static bool Prefix(ref Color __result, float angle, float marginScale, scrFloor hitFloor)
            {
                try
                {
                    if (!Main.Enabled || !Main.Settings.EnableRainbow) return true;

                    double bpmTimesSpeed = RainbowMath.GameBpmTimesSpeedOf(hitFloor);
                    double pitch = RainbowMath.GetPitchNow();
                    double countedDeg = scrMisc.GetAdjustedAngleBoundaryInDeg(HitMarginGeneral.Counted, bpmTimesSpeed, pitch, marginScale);
                    if (double.IsNaN(countedDeg) || countedDeg <= 0.0001) countedDeg = 60.0; // NaN 防御

                    double wavelength = RainbowMath.WavelengthForGradient(Math.Abs((double)angle), countedDeg, bpmTimesSpeed, pitch, marginScale);
                    __result = Spectrum.WavelengthToRgb(wavelength);

                    if (Main.Settings.DebugLog)
                        Logger.Log("[TickColor] angle=" + angle.ToString("F2") + " countedDeg=" + countedDeg.ToString("F1")
                            + " wl=" + wavelength.ToString("F1") + "nm");
                    return false; // 原版档位颜色同样被锚点色覆盖
                }
                catch { return true; }
            }
        }

        /// <summary>DebugLog 交叉校验：仪表盘 tick 的原始角度误差（游戏值，auto 强制 0）应与我们算的 |delta| 一致</summary>
        [HarmonyPatch(typeof(scrHitErrorMeter), "AddHit")]
        public static class TickAngleCrossCheckHook
        {
            [HarmonyPostfix]
            public static void Postfix(float angleDiff)
            {
                try
                {
                    if (!Main.Settings.DebugLog) return;
                    double meter = Math.Abs((double)angleDiff);
                    double ours = LastJudge.RawDeg;
                    if (Math.Abs(meter - ours) > 0.01)
                        Logger.Log("[TickAngleCheck] meter=" + meter.ToString("F3") + " ours=" + ours.ToString("F3"));
                }
                catch { }
            }
        }

        /// <summary>auto 判定检测：scrPlayer.Hit 的 isAuto/auto（官方 autoplay 与自动砖块均置位）</summary>
        [HarmonyPatch(typeof(scrPlayer), "Hit")]
        public static class AutoDetectPatch
        {
            [HarmonyPrefix]
            public static void Prefix(scrPlayer __instance, bool isAuto)
            {
                try { LastJudge.AutoActive = isAuto || (__instance != null && __instance.auto); }
                catch { }
            }
        }

        /// <summary>判定数据采集：只计算并暂存，不累加统计</summary>
        [HarmonyPatch(typeof(scrMisc), "GetHitMargin")]
        public static class GetMarginHook
        {
            [HarmonyPostfix]
            public static void Postfix(ref HitMargin __result, float hitangle, float refangle, bool isCW,
                float bpmTimesSpeed, float conductorPitch, double marginScale)
            {
                if (!Main.Enabled || !Main.Settings.EnableRainbow) return;
                try
                {
                    // hitangle/refangle 是弧度 → 度；auto 判定强制完美中心
                    double delta = (hitangle - refangle) * (isCW ? 1.0 : -1.0) * 57.29578;
                    bool auto = LastJudge.AutoActive;
                    if (auto) delta = 0.0;

                    double countedDeg = scrMisc.GetAdjustedAngleBoundaryInDeg(HitMarginGeneral.Counted, bpmTimesSpeed, conductorPitch, marginScale);
                    if (double.IsNaN(countedDeg) || countedDeg <= 0.0001) countedDeg = 60.0;

                    // 判定瞬间基准（与 tick 刻度同源），供判定文字颜色使用
                    LastJudge.CountedDeg = countedDeg;
                    LastJudge.ScaledPos = Math.Abs(delta) * (double)RainbowMath.CountedScaled / countedDeg;
                    LastJudge.BpmTimesSpeed = bpmTimesSpeed;
                    LastJudge.Pitch = conductorPitch;
                    LastJudge.MarginScale = marginScale;
                    LastJudge.RawDeg = auto ? 0.0 : Math.Abs(delta);

                    bool isPerfect = (__result == HitMargin.Perfect) || (__result == HitMargin.Auto) || auto;

                    HitRecord record = default(HitRecord);
                    record.HasData = true;
                    record.IsPerfect = isPerfect;
                    record.IsAuto = auto;
                    record.Tier = -1;

                    if (isPerfect)
                    {
                        // 新增档位边界角 = max(角度下限×marginScale, 时间下限对应角度)，与原版同构
                        double a1 = RainbowMath.LevelBoundaryDeg(RainbowMath.Tier1Of3PP, bpmTimesSpeed, conductorPitch, marginScale);
                        double a2 = RainbowMath.LevelBoundaryDeg(RainbowMath.TierHalfPP, bpmTimesSpeed, conductorPitch, marginScale);
                        double a3 = RainbowMath.LevelBoundaryDeg(RainbowMath.Tier2Of3PP, bpmTimesSpeed, conductorPitch, marginScale);
                        record.Tier = RainbowCounter.TierOf(Math.Abs(delta), delta < 0.0, a1, a2, a3);

                        // X^n 的 p：|角度| / PP 边界（auto 强制中间 → p=0 → n=∞）
                        double ppDeg = RainbowMath.LevelBoundaryDeg(RainbowMath.TierPP, bpmTimesSpeed, conductorPitch, marginScale);
                        record.P = ppDeg > 0.0001
                            ? LastJudge.RawDeg / (double)RainbowMath.CountedScaled * countedDeg / ppDeg
                            : 0.0;

                        // 平均判定颜色 / 平均绝对偏差：与 tick 同一套渐变
                        record.Lambda = RainbowMath.WavelengthForGradient(LastJudge.RawDeg, countedDeg, bpmTimesSpeed, conductorPitch, marginScale);
                        record.TimeMs = RainbowMath.TimeMsFromScaledAngleAccurate((float)LastJudge.RawDeg, countedDeg, bpmTimesSpeed, conductorPitch);

                        if (Main.Settings.DebugLog)
                            Logger.Log("[GetMargin] " + __result + " delta=" + delta.ToString("F2") + " rawDeg=" + LastJudge.RawDeg.ToString("F2")
                                + " tier=" + record.Tier + " countedDeg=" + countedDeg.ToString("F1") + " wl=" + record.Lambda.ToString("F1")
                                + "nm t=" + record.TimeMs.ToString("F2") + "ms p=" + record.P.ToString("F4"));
                    }
                    else if (Main.Settings.DebugLog)
                    {
                        Logger.Log("[GetMargin] " + __result + " delta=" + delta.ToString("F1") + "（不计入统计）");
                    }

                    RainbowProgress.Stash(record);
                }
                catch (Exception ex)
                {
                    Logger.Log("[JudgeHooks/GetMargin] " + ex.Message);
                }
            }
        }

        /// <summary>游戏计数入口：每次"判定被计入"追加一条（与 hitMargins 索引严格对齐）。
        /// 尖刺/激光等没有判定的计数（FailMiss）没有暂存数据 → 写占位条目，保证索引不错位。</summary>
        [HarmonyPatch(typeof(scrMarginTracker), "AddHit")]
        public static class MarginTrackerAddHitHook
        {
            [HarmonyPostfix]
            public static void Postfix(scrMarginTracker __instance, HitMargin hit)
            {
                try
                {
                    if (!Main.Enabled || !Main.Settings.EnableRainbow) return;
                    if (!RainbowProgress.IsPlayerOneTracker(__instance)) return;

                    HitRecord record;
                    if (RainbowProgress.ConsumePending(out record))
                    {
                        // 以游戏记录的档位为准
                        record.IsPerfect = hit == HitMargin.Perfect || hit == HitMargin.Auto;
                        if (record.IsPerfect && record.Tier < 0) record.Tier = RainbowCounter.TierPurple;
                        if (!record.IsPerfect) record.Tier = -1;
                    }
                    else
                    {
                        record = RainbowProgress.MakePlaceholder();
                    }
                    RainbowProgress.Append(record);
                }
                catch { }
            }
        }
    }
}
