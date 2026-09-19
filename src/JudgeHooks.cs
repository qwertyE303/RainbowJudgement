using System;
using HarmonyLib;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 判定采集 hooks。
    /// 数据流：GetHitMargin 算出本次判定的几何基准（60 刻度）并暂存 → scrMarginTracker.AddHit 消费并追加到账本。
    /// 账本与游戏的 hitMargins 严格同长同序，且"实时累加"与"回档重放"共用同一份数据。
    /// 颜色一律走 <see cref="AnchorSet"/>（锚点表由自定义档位 + 原版三档边界动态生成）。
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
                    double countedDeg = RainbowMath.CountedBoundaryDeg(bpmTimesSpeed, pitch, marginScale);
                    GradientFrame frame = GradientFrame.FromRuntime(countedDeg, marginScale, bpmTimesSpeed, pitch);

                    double wavelength = AnchorSet.WavelengthAt(angle, frame);
                    __result = Spectrum.WavelengthToRgb(wavelength);

                    Logger.Log("[TickColor] angle=" + angle.ToString("F2") + " countedDeg=" + countedDeg.ToString("F1")
                        + " wl=" + wavelength.ToString("F1") + "nm");
                    return false; // 原版档位颜色同样被锚点色覆盖
                }
                catch { return true; }
            }
        }

        /// <summary>DebugLog 交叉校验（日志由 Logger 自行门控）：仪表盘 tick 的刻度（游戏值，auto 强制 0）
        /// 应与我们算的判定位移刻度一致。两边都是 60 刻度，别拿度数去比。</summary>
        [HarmonyPatch(typeof(scrHitErrorMeter), "AddHit")]
        public static class TickAngleCrossCheckHook
        {
            [HarmonyPostfix]
            public static void Postfix(float angleDiff)
            {
                try
                {
                    double meter = Math.Abs((double)angleDiff);
                    double ours = LastJudge.ScaledPos;
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

        /// <summary>判定数据采集：只计算并暂存，不累加统计。
        /// **只在真正开始一局之后采集**（`GameState.InGameWorld`）：主界面/选歌、以及编辑器里搭关那些
        /// 不属于任何一局的判定一律丢弃，既不进账本也不写日志（否则会出现 delta 上万度的垃圾行）。</summary>
        [HarmonyPatch(typeof(scrMisc), "GetHitMargin")]
        public static class GetMarginHook
        {
            [HarmonyPostfix]
            public static void Postfix(ref HitMargin __result, float hitangle, float refangle, bool isCW,
                float bpmTimesSpeed, float conductorPitch, double marginScale)
            {
                if (!Main.Enabled || !Main.Settings.EnableRainbow) return;
                if (!GameState.InGameWorld) return;
                try
                {
                    // hitangle/refangle 是弧度 → 度；auto 判定强制完美中心
                    double delta = (hitangle - refangle) * (isCW ? 1.0 : -1.0) * 57.29578;
                    bool auto = LastJudge.AutoActive;
                    if (auto) delta = 0.0;

                    double countedDeg = RainbowMath.CountedBoundaryDeg(bpmTimesSpeed, conductorPitch, marginScale);
                    GradientFrame frame = GradientFrame.FromRuntime(countedDeg, marginScale, bpmTimesSpeed, conductorPitch);

                    double absScaled = Math.Abs(delta) * frame.PosPerDeg;
                    double absDeg = Math.Abs(delta);

                    HitRecord record = default(HitRecord);
                    record.HasData = true;
                    record.IsAuto = auto;
                    record.Tier = -1;
                    record.DeltaDeg = delta;
                    record.PosPerDeg = frame.PosPerDeg;
                    record.AScale = frame.AScale;
                    record.BScale = frame.BScale;
                    record.PpDeg = frame.PosPerDeg > 0.0001 ? frame.PpScaled / frame.PosPerDeg : 0.0;
                    record.PureDeg = frame.PosPerDeg > 0.0001 ? frame.PureScaled / frame.PosPerDeg : 0.0;
                    record.PracticeScale = frame.PracticeScale;

                    // 【统计范围】7 档计数器（F A B C D E G）只收「原版完美边界之内」的判定。
                    // EP/LP（稍快！/稍晚！）、VE/VL、Too 都在 PP 边界之外 → 不进任何档位桶、不进 X^n 的 r
                    // （它们仍然参与平均判定颜色与平均绝对时间/角度偏差——那三处统计的是全部判定）。
                    record.InPure = absScaled <= frame.PpScaled;

                    if (record.InPure)
                    {
                        record.Tier = RainbowMath.TierOf(absScaled, delta < 0.0,
                            RainbowMath.FixedTierScaled(0, frame),
                            RainbowMath.FixedTierScaled(1, frame),
                            RainbowMath.FixedTierScaled(2, frame));
                        // X^n 的 p：|角度| / PP 边界（auto 强制中间 → p=0 → n=∞）；只在完美窗口内才有意义（0~1）
                        record.P = record.PpDeg > 0.0001 ? absDeg / record.PpDeg : 0.0;
                    }

                    // 平均判定颜色 / 平均绝对时间偏差 / 平均绝对角度偏差：全部判定都算
                    record.Lambda = AnchorSet.WavelengthAt(absScaled, frame);
                    record.TimeMs = RainbowMath.TimeMsOfDeg(absDeg, bpmTimesSpeed, conductorPitch);

                    LastJudge.ScaledPos = absScaled;
                    LastJudge.Frame = frame;

                    Logger.Log("[GetMargin] " + __result + " delta=" + delta.ToString("F2") + " absDeg=" + absDeg.ToString("F2")
                        + " scaled=" + absScaled.ToString("F2") + " tier=" + record.Tier
                        + " countedDeg=" + countedDeg.ToString("F1") + " ppDeg=" + record.PpDeg.ToString("F1")
                        + " wl=" + record.Lambda.ToString("F1") + "nm t=" + record.TimeMs.ToString("F2") + "ms p=" + record.P.ToString("F4")
                        + (record.InPure ? "" : "（完美窗口外：计入颜色/偏差，不计入 F~G 档位与 X^n）"));

                    RainbowProgress.Stash(record);
                }
                catch (Exception ex)
                {
                    Logger.Log("[JudgeHooks/GetMargin] " + ex.Message);
                }
            }
        }

        /// <summary>游戏计数入口：每次"判定被计入"追加一条（与 hitMargins 索引严格对齐）。
        /// 尖刺/激光等没有判定的计数（FailMiss 等）没有暂存数据 → 写占位条目，保证索引不错位。
        /// 这里不按 HitMargin 过滤统计——有暂存数据的按几何量照单全收
        /// （游戏会把"没打在正中"的标成 EarlyPerfect/LatePerfect，那不该被丢掉）；
        /// 关卡外的判定（主界面/编辑器搭关）连同暂存一起丢弃，不进账本。</summary>
        [HarmonyPatch(typeof(scrMarginTracker), "AddHit")]
        public static class MarginTrackerAddHitHook
        {
            [HarmonyPostfix]
            public static void Postfix(scrMarginTracker __instance, HitMargin hit)
            {
                try
                {
                    if (!Main.Enabled || !Main.Settings.EnableRainbow) return;
                    if (!GameState.InGameWorld) return;
                    if (!RainbowProgress.IsPlayerOneTracker(__instance)) return;

                    HitRecord record;
                    if (!RainbowProgress.ConsumePending(out record) || !record.HasData)
                        record = RainbowProgress.MakePlaceholder();
                    RainbowProgress.Append(record);
                }
                catch { }
            }
        }
    }
}
