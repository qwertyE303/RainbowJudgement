using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 判定采集 hooks（**2.9.8 版**），功能与 3.3.0 那棵树完全一致。
    ///
    /// 与 3.3.0 的对应关系：
    ///   · 账本追加点：<c>scrMistakesManager.AddHit</c>（2.9.8）← <c>scrMarginTracker.AddHit</c>（3.3.0）
    ///   · 判定格：<c>chosenPlanet.currfloor</c>（Compat.CurrentFloor）← 3.3.0 的参数里直接带 scrFloor
    ///   · 中旋强制 PP：<c>scrController.midspinInfiniteMargin</c>（2.9.8 里该字段在 controller 上）
    ///
    /// **这一版踩过的两个大坑（务必不要回退）**：
    ///   ① <c>scrController.HitAutoFloors</c> 是**每帧**都被 <c>Simulated_PlayerControl_Update</c> 调用的
    ///      例行方法（它只是"检查这一格是不是自动砖"），**不能**当作"发生了 auto 命中"的信号：
    ///      挂它会每帧置位 → 所有判定都被当成 auto → 误差被清零 → 全是 0ms 紫色。
    ///      现在 auto 的判定权完全交给游戏自己的记账真值（见下面 AddHit 的 MergeGameGrade 校准）。
    ///   ② <c>scrPlanet.SwitchChosen</c> 内部会调用 <c>scrMisc.GetHitMargin</c> **两次**（一次按刻度、
    ///      一次在中旋标志置位后），因此"暂存 → 消费"必须按**判定序号**配对，不能只看"同帧新鲜"。
    /// </summary>
    public static class JudgeHooks
    {
        /// <summary>tick 颜色改为彩虹渐变（与原版档位色无关）。
        /// 2.9.8 的 CalculateTickColor 只有 (angle, marginScale) 两个参数，没有 hitFloor：
        /// angle 用的就是判定时算出来、同一份传给 AddHit 的**刻度**（与我们的 ScaledPos 同一坐标系），
        /// 缺的 marginScale/speed 从"当前判定格"补（3.3.0 是游戏直接当参数给的）。</summary>
        [HarmonyPatch(typeof(scrHitErrorMeter), "CalculateTickColor")]
        [HarmonyPriority(Priority.High)]
        public static class TickColorHook
        {
            [HarmonyPrefix]
            public static bool Prefix(ref Color __result, float angle, float marginScale)
            {
                try
                {
                    if (!Main.Active) return true;

                    Compat.CaptureCurrentFloor();
                    scrFloor floor = Compat.CurrentFloor;

                    double bpmTimesSpeed = RainbowMath.GameBpmTimesSpeedOf(floor);
                    double pitch = RainbowMath.GetPitchNow();
                    double scale = RainbowMath.MarginScaleOf(floor, marginScale);
                    double countedDeg = RainbowMath.CountedBoundaryDeg(bpmTimesSpeed, pitch, scale);
                    GradientFrame frame = GradientFrame.FromRuntime(countedDeg, scale, bpmTimesSpeed, pitch);

                    double wavelength = AnchorSet.WavelengthAt(angle, frame);
                    __result = Spectrum.WavelengthToRgb(wavelength);

                    Logger.Log("[TickColor] angle=" + angle.ToString("F2") + " countedDeg=" + countedDeg.ToString("F1")
                        + " marginScale=" + scale.ToString("F4")
                        + " wl=" + wavelength.ToString("F1") + "nm");
                    return false; // 原版档位颜色同样被锚点色覆盖
                }
                catch { return true; }
            }
        }

        /// <summary>DebugLog 交叉校验（日志由 Logger 自行门控）：仪表盘 tick 的刻度
        /// （游戏在 playerControl 里算出来、原样传给 AddHit 的那个值；auto 判定会被游戏强制为 0）
        /// 应与我们算的判定位移刻度一致。两边都是"刻度"，别拿度数去比。</summary>
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
                if (!Main.Active) return;
                if (!GameState.InGameWorld) return;
                try
                {
                    // hitangle/refangle 是弧度 → 度。**一律用真实角度误差**：
                    // auto 判定在 2.9.8 里本身就是"和这一格的角度差"，游戏只在记账时改写档位，
                    // 所以这里不做任何清零（清零过一次，结果是全部判定 0ms 紫色）。
                    double delta = (hitangle - refangle) * (isCW ? 1.0 : -1.0) * 57.29578;

                    Compat.CaptureCurrentFloor();
                    scrFloor floor = Compat.CurrentFloor;

                    // 2.9.8 的 AddHit 紧接着这次 GetHitMargin（同一个 SwitchChosen、同一格），
                    // 而 SwitchChosen 用的 floor.speed 与 hitangle/refangle 是配套的 —— 所以这里
                    // 统一用"当前判定格"的 speed 重建一次 bpm×speed，保证 tick / 账本 / 重算三者同源。
                    if (floor != null)
                    {
                        double floorSpeed = RainbowMath.GameSpeedOf(floor);
                        if (floorSpeed > 0.0001) bpmTimesSpeed = (float)(RainbowMath.GameBpm() * floorSpeed);
                        double floorScale = RainbowMath.MarginScaleOf(floor, 0.0);
                        if (floorScale > 0.0) marginScale = floorScale;
                    }

                    double countedDeg = RainbowMath.CountedBoundaryDeg(bpmTimesSpeed, conductorPitch, marginScale);
                    GradientFrame frame = GradientFrame.FromRuntime(countedDeg, marginScale, bpmTimesSpeed, conductorPitch);

                    double absScaled = Math.Abs(delta) * frame.PosPerDeg;
                    double absDeg = Math.Abs(delta);

                    // 【游戏口径的强制 PP】中旋（midspin）格上 scrPlanet.SwitchChosen 会把
                    // midspinInfiniteMargin 为真的判定**直接改写成 HitMargin.Perfect** 再记进 hitMargins
                    // （2.9.8 的 IL：读 scrController.midspinInfiniteMargin → ldc.i4.3 → AddHit）。
                    // 这种判定的角度误差其实在完美窗口之外，游戏却按 PP 记账。
                    // 关键顺序：**必须在算 InPure 之前**认出来，否则它会被当成"角度真的在窗内"
                    // 混进 7 档计数器与 X^n（这就是上一版"中旋多计入"的原因）。
                    bool midspin;
                    bool forcedPP = Compat.ReadMidspinInfiniteMargin(out midspin) && midspin
                        && (__result == HitMargin.Perfect || __result == HitMargin.Auto);

                    HitRecord record = default(HitRecord);
                    record.HasData = true;
                    record.IsAuto = (__result == HitMargin.Auto) || IsAutoNow(floor);
                    record.ForcePP = forcedPP;
                    record.Tier = -1;
                    record.DeltaDeg = delta;
                    record.PosPerDeg = frame.PosPerDeg;
                    record.AScale = frame.AScale;
                    record.BScale = frame.BScale;
                    record.PpDeg = frame.PosPerDeg > 0.0001 ? frame.PpScaled / frame.PosPerDeg : 0.0;
                    record.PureDeg = frame.PosPerDeg > 0.0001 ? frame.PureScaled / frame.PosPerDeg : 0.0;
                    record.PracticeScale = frame.PracticeScale;
                    // 判定瞬间的关卡难度：难度是全局运行时量、玩家可在局内切换，
                    // 必须逐条存下来，重放历史时才能按记录自己的难度去选自定义档位的那一行。
                    record.Difficulty = CustomJudge.CurrentDifficulty();

                    // 【统计范围】7 档计数器（F A B C D E G）只收「原版完美边界之内」的判定。
                    // EP/LP（稍快！/稍晚！）、VE/VL、Too 都在 PP 边界之外 → 不进任何档位桶、不进 X^n 的 r
                    // （它们仍然参与平均判定颜色与平均绝对时间/角度偏差——那三处统计的是全部判定）。
                    // 强制 PP（中旋）同样不进：游戏记账是 Perfect，但几何量在窗口外，走下面 ForcePP 那条路。
                    record.InPure = !record.ForcePP && absScaled <= frame.PpScaled;

                    if (record.InPure)
                    {
                        record.Tier = RainbowMath.TierOf(absScaled, delta < 0.0,
                            RainbowMath.FixedTierScaled(0, frame),
                            RainbowMath.FixedTierScaled(1, frame),
                            RainbowMath.FixedTierScaled(2, frame));
                        // X^n 的 p：|角度| / PP 边界（auto 强制中间 → p=0 → n=∞）；只在完美窗口内才有意义（0~1）
                        record.P = record.PpDeg > 0.0001 ? absDeg / record.PpDeg : 0.0;
                    }
                    else if (record.ForcePP)
                    {
                        // 强制 PP：游戏记账当完美中心 → 原版 7 档里按最严那一档（紫）计，p 记 0
                        record.Tier = RainbowCounter.TierPurple;
                        record.P = 0.0;
                    }

                    // 平均判定颜色 / 平均绝对时间偏差 / 平均绝对角度偏差：全部判定都算
                    // （强制 PP 的三项会在 RainbowProgress.CountRecord 里按游戏口径折成零误差）
                    record.Lambda = AnchorSet.WavelengthAt(absScaled, frame);
                    record.TimeMs = RainbowMath.TimeMsOfDeg(absDeg, bpmTimesSpeed, conductorPitch);

                    LastJudge.ScaledPos = absScaled;
                    LastJudge.Frame = frame;

                    Logger.Log("[GetMargin] " + __result + " delta=" + delta.ToString("F2") + " absDeg=" + absDeg.ToString("F2")
                        + " scaled=" + absScaled.ToString("F2") + " tier=" + record.Tier
                        + " countedDeg=" + countedDeg.ToString("F1") + " ppDeg=" + record.PpDeg.ToString("F1")
                        + " wl=" + record.Lambda.ToString("F1") + "nm t=" + record.TimeMs.ToString("F2") + "ms p=" + record.P.ToString("F4")
                        + " diff=" + record.Difficulty
                        + " " + ForcedSignalsText(floor)
                        + (record.InPure ? "" : "（完美窗口外：计入颜色/偏差，不计入 F~G 档位与 X^n）")
                        + (record.ForcePP ? "（强制PP：游戏记账改写成 Perfect → 补进自定义判定最严一档，并按零误差计入平均判定与 X^n）" : ""));

                    RainbowProgress.Stash(record);
                }
                catch (Exception ex)
                {
                    Logger.Log("[JudgeHooks/GetMargin] " + ex.Message);
                }
            }

            /// <summary>当场是否 auto（官方 autoplay 或自动砖）。只读游戏自己的状态、不做任何缓存，
            /// 避免再出现"标记粘住导致所有判定都被当 auto"的问题。</summary>
            private static bool IsAutoNow(scrFloor floor)
            {
                try
                {
                    if (floor != null && floor.auto) return true;
                }
                catch { }
                try
                {
                    System.Reflection.PropertyInfo prop = typeof(RDC).GetProperty("auto",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (prop != null)
                    {
                        object value = prop.GetValue(null, null);
                        if (value is bool && (bool)value) return true;
                    }
                }
                catch { }
                return false;
            }

            // ---------------- 游戏"强制 PP"的诊断 ----------------
            //
            // 2.9.8 的 scrPlanet.SwitchChosen 里写进 scrMistakesManager.AddHit 的档位来自这段决策：
            //   V_5 = GetHitMargin(...)                                     // 按刻度算出来的档位（判定文字用的也是它）
            //   if (midspinInfiniteMargin) V_5 = 3;                          // 3 = HitMargin.Perfect
            //   if (V_17 && GCS.hitMarginLimit == 2 && scrController.noFail) V_5 = 9;  // 9 = FailOverload
            //   if (midspinInfiniteMargin) V_5 = 3;
            // 而真正的"地面真值"是 AddHit 收到的那一个档位（见 MistakesAddHitHook 里的 MergeGameGrade 校准）。

            /// <summary>诊断用：把决策输入原样读出来（读不到返回 "?"），漏判时靠它定位是哪一条没对上。</summary>
            private static string ForcedSignalsText(scrFloor floor)
            {
                try
                {
                    bool midspin;
                    string mid = Compat.ReadMidspinInfiniteMargin(out midspin) ? (midspin ? "1" : "0") : "?";
                    scrFloor current = floor != null ? floor : Compat.CurrentFloor;
                    return "mid=" + mid
                        + " fA=" + (current != null && current.auto ? "1" : "0")
                        + " fM=" + (current != null && current.midSpin ? "1" : "0")
                        + " RDCauto=" + (IsAutoNow(null) ? "1" : "0");
                }
                catch { return "?"; }
            }

            /// <summary>
            /// 诊断用：把游戏 <c>hitMargins</c> 里每种档位的条数写成一行（[强Perfect] 里两位以上的才是真 Perfect）。
            /// 数都来自游戏自己的 List，用来核对"我们数出来的 == 游戏记的"。
            /// </summary>
            public static string HitMarginsHistogram()
            {
                try
                {
                    List<HitMargin> margins = GameState.HitMargins;
                    if (margins == null) return "hitMargins=null";

                    int[] counts = new int[16];
                    for (int i = 0; i < margins.Count; i++)
                    {
                        int value = (int)margins[i];
                        if (value >= 0 && value < counts.Length) counts[value]++;
                    }

                    System.Text.StringBuilder sb = new System.Text.StringBuilder(96);
                    for (int i = 0; i < counts.Length; i++)
                    {
                        if (counts[i] <= 0) continue;
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append((HitMargin)i).Append('=').Append(counts[i]);
                    }
                    sb.Append("  [条数=").Append(margins.Count)
                      .Append(" 强Perfect=").Append(counts[(int)HitMargin.Perfect])
                      .Append(" Auto=").Append(counts[(int)HitMargin.Auto]).Append(']');
                    return sb.ToString();
                }
                catch (System.Exception ex) { return "hitMargins 读取失败: " + ex.Message; }
            }
        }

        /// <summary>游戏计数入口（2.9.8：<c>scrMistakesManager.AddHit</c>）：每次"判定被计入"追加一条
        /// （与 hitMargins 索引严格对齐）。
        /// 尖刺/激光等没有判定的计数（FailMiss 等）没有暂存数据 → 写占位条目，保证索引不错位。
        /// 这里不按 HitMargin 过滤统计——有暂存数据的按几何量照单全收
        /// （游戏会把"没打在正中"的标成 EarlyPerfect/LatePerfect，那不该被丢掉）；
        /// 关卡外的判定（主界面/编辑器搭关）连同暂存一起丢弃，不进账本。
        /// **同时用游戏记下的档位校准强制 PP**：`hit` 就是最终进 hitMargins 的值，
        /// 它说 Perfect/Auto 而几何量在窗口外 → 必定是游戏按完美记账（中旋 / 旧式自动砖），
        /// 这种校准不依赖任何标志位，是这套机制的地面真值。</summary>
        [HarmonyPatch(typeof(scrMistakesManager), "AddHit")]
        public static class MistakesAddHitHook
        {
            [HarmonyPostfix]
            public static void Postfix(HitMargin hit)
            {
                try
                {
                    if (!Main.Active) return;
                    if (!GameState.InGameWorld) return;

                    HitRecord record;
                    if (!RainbowProgress.ConsumePending(out record) || !record.HasData)
                        record = RainbowProgress.MakePlaceholder();
                    else
                        record.MergeGameGrade(hit);
                    RainbowProgress.Append(record);
                }
                catch { }
            }
        }
    }
}
