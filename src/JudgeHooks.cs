using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 判定采集 hooks（ADOFAI 3.4.0）。
    /// 数据流：GetHitMarginInDeg / GetHitMarginInSec 算出本次判定的几何基准（60 刻度）并暂存
    /// → scrMarginTracker.AddHit 消费并追加到账本。
    ///
    /// 3.4.0 的游戏侧变化（本文件的改写依据，都是从 Assembly-CSharp 的 IL 逐条核出来的）：
    ///   · <c>scrMisc.GetHitMargin</c> 拆成 <c>GetHitMarginInDeg</c>（同步按键，按角度）与
    ///     <c>GetHitMarginInSec</c>（异步输入，按 hitTick 的时间差）；边界函数改成一次返回四个值的结构体；
    ///   · <c>HitMargin</c> 重编号并新增 PerfectMinus / XPerfect / PerfectPlus / Midspin / FailedFloor，
    ///     旧的单一 <c>HitMargin.Perfect</c> 已不存在；
    ///   · 中旋不再被伪造成 Perfect：<c>scrPlanet.SwitchChosen</c> 改记 <c>HitMargin.Midspin</c>；
    ///     自动砖记 <c>HitMargin.Auto</c>（这两类游戏的 X-Accuracy 也不计入 → 本 Mod 同样只记占位）；
    ///   · <c>scrHitErrorMeter.CalculateTickColor</c> 删除，tick 颜色改由 <c>ADOFAI.ErrorMeterTick.Show</c>
    ///     的 <c>overrideColor</c> 决定 → 挂 AddHit 前缀算色 + Show 前缀改色；
    ///   · 死亡格批量补记走私有的 <c>AddHits</c>（**绕过 AddHit**），必须单独挂补丁才能与账本对齐。
    /// 账本与游戏的 hitMargins 严格同长同序，且"实时累加"与"回档重放"共用同一份数据。
    /// 颜色一律走 <see cref="AnchorSet"/>（锚点表由自定义档位 + 原版三档边界动态生成）。
    /// </summary>
    public static class JudgeHooks
    {
        // ---------------- 判定采集 ----------------

        /// <summary>同步按键路径：判定由角度差决定（<c>scrPlanet.&lt;SwitchChosen&gt;g__GetHitMargin|97_0</c> 的角度分支）。
        /// 参数名必须与游戏签名逐字一致（Harmony 按名字注入）：
        /// difficulty, hitAngle, refAngle, clockwise, floorBpm, conductorPitch, marginScale。</summary>
        [HarmonyPatch(typeof(scrMisc), "GetHitMarginInDeg")]
        public static class GetMarginInDegHook
        {
            [HarmonyPostfix]
            public static void Postfix(HitMargin __result, float hitAngle, float refAngle, bool clockwise,
                float floorBpm, float conductorPitch, double marginScale)
            {
                try
                {
                    // 与游戏逐位同构：先在 float 里算，再转 double（否则贴边判定会与游戏分类差一点点）
                    double delta = (hitAngle - refAngle) * (clockwise ? 1f : -1f) * 57.29578f;
                    Capture(delta, floorBpm, conductorPitch, marginScale, __result, "deg");
                }
                catch (Exception ex)
                {
                    Logger.Log("[JudgeHooks/GetMarginInDeg] " + ex.Message);
                }
            }
        }

        /// <summary>异步输入路径：判定由 hitTick 与格子时间的差决定（<c>hitTick.HasValue</c> 分支）。
        /// 参数名：difficulty, timeDiff, floorBpm, conductorPitch, marginScale。timeDiff 单位秒、正 = 晚。</summary>
        [HarmonyPatch(typeof(scrMisc), "GetHitMarginInSec")]
        public static class GetMarginInSecHook
        {
            [HarmonyPostfix]
            public static void Postfix(HitMargin __result, double timeDiff,
                float floorBpm, float conductorPitch, double marginScale)
            {
                try
                {
                    double perMs = RainbowMath.DegPerMs(floorBpm, conductorPitch);
                    double delta = timeDiff * 1000.0 * perMs; // 正 = 晚，与角度分支同号
                    Capture(delta, floorBpm, conductorPitch, marginScale, __result, "sec");
                }
                catch (Exception ex)
                {
                    Logger.Log("[JudgeHooks/GetMarginInSec] " + ex.Message);
                }
            }
        }

        /// <summary>判定采集的公共部分：换算成账本记录并暂存（由 AddHit 消费）。
        /// **只在真正开始一局之后采集**（<see cref="GameState.InGameWorld"/>）：主界面/选歌、
        /// 以及编辑器里搭关那些不属于任何一局的判定一律丢弃，既不进账本也不写日志
        /// （否则会出现 delta 上万度的垃圾行）。</summary>
        private static void Capture(double deltaDeg, double bpmTimesSpeed, double conductorPitch,
            double marginScale, HitMargin result, string source)
        {
            if (!Main.Active) return;
            if (!GameState.InGameWorld) return;

            // autoplay / 自动砖：游戏在记账前就把档位改写成完美档，这里也按零误差记，
            // 免得 autoplay 的抖动把 X^n 的 r 与平均判定带偏。
            bool auto = LastJudge.AutoActive;
            if (auto) deltaDeg = 0.0;

            double countedDeg = RainbowMath.CountedBoundaryDeg(bpmTimesSpeed, conductorPitch, marginScale);
            GradientFrame frame = GradientFrame.FromRuntime(countedDeg, marginScale, bpmTimesSpeed, conductorPitch);

            double absDeg = Math.Abs(deltaDeg);
            double absScaled = absDeg * frame.PosPerDeg;

            HitRecord record = default(HitRecord);
            record.HasData = true;
            record.IsAuto = auto;
            record.Tier = -1;
            record.DeltaDeg = deltaDeg;
            record.PosPerDeg = frame.PosPerDeg;
            record.AScale = frame.AScale;
            record.BScale = frame.BScale;
            record.PpDeg = frame.PosPerDeg > 0.0001 ? frame.PpScaled / frame.PosPerDeg : 0.0;
            record.PureDeg = frame.PosPerDeg > 0.0001 ? frame.PureScaled / frame.PosPerDeg : 0.0;
            record.PracticeScale = frame.PracticeScale;
            // 判定瞬间的关卡难度：难度是全局运行时量、玩家可在局内切换，
            // 必须逐条存下来，重放历史时才能按记录自己的难度去选自定义档位的那一行。
            record.Difficulty = CustomJudge.CurrentDifficulty();

            // 【统计范围】7 档计数器（F A B C D E G）与 X^n 的 r 只收「原版 PP 边界之内」的判定。
            // EP/LP（稍快！/稍晚！）、VE/VL、Too 都在 PP 边界之外 → 不进任何档位桶、不进 r
            // （它们仍然参与平均判定颜色与平均绝对时间/角度偏差——那三处统计的是全部玩家判定）。
            record.InPure = absScaled <= frame.PpScaled;

            if (record.InPure)
            {
                record.Tier = RainbowMath.TierOf(absScaled, deltaDeg < 0.0,
                    RainbowMath.FixedTierScaled(0, frame),
                    RainbowMath.FixedTierScaled(1, frame),
                    RainbowMath.FixedTierScaled(2, frame));
                // X^n 的 p：|角度| / PP 边界（auto 强制中间 → p=0 → n=∞）；只在完美窗口内才有意义（0~1）
                record.P = record.PpDeg > 0.0001 ? absDeg / record.PpDeg : 0.0;
            }

            // 平均判定颜色 / 平均绝对时间偏差 / 平均绝对角度偏差：所有玩家判定都算
            record.Lambda = AnchorSet.WavelengthAt(absScaled, frame);
            record.TimeMs = RainbowMath.TimeMsOfDeg(absDeg, bpmTimesSpeed, conductorPitch);

            LastJudge.ScaledPos = absScaled;
            LastJudge.Frame = frame;

            // 对照游戏自己的分类做一次自检：我们算的"在完美窗口内"必须与游戏给的档位一致
            // （贴边时 float 舍入可能差一点点，所以给 0.01° 的容差，只在真的分叉时报警）。
            bool gameInPure = result == HitMargin.PerfectMinus || result == HitMargin.XPerfect
                || result == HitMargin.PerfectPlus;
            if (gameInPure != record.InPure && record.PpDeg > 0.0001 && Math.Abs(absDeg - record.PpDeg) > 0.01)
            {
                Logger.Warn("[GetMargin] 完美窗口判定与游戏不一致：游戏=" + result
                    + " 我们InPure=" + record.InPure
                    + " absDeg=" + absDeg.ToString("F3") + " ppDeg=" + record.PpDeg.ToString("F3")
                    + " countedDeg=" + countedDeg.ToString("F1") + " marginScale=" + marginScale.ToString("F4")
                    + " bpmSpeed=" + bpmTimesSpeed.ToString("F3") + " pitch=" + conductorPitch.ToString("F4")
                    + " auto=" + auto + " src=" + source);
            }

            Logger.Log("[GetMargin] " + result + " delta=" + deltaDeg.ToString("F2") + " absDeg=" + absDeg.ToString("F2")
                + " scaled=" + absScaled.ToString("F2") + " tier=" + record.Tier
                + " countedDeg=" + countedDeg.ToString("F1") + " ppDeg=" + record.PpDeg.ToString("F1")
                + " pureDeg=" + record.PureDeg.ToString("F1")
                + " wl=" + record.Lambda.ToString("F1") + "nm t=" + record.TimeMs.ToString("F2") + "ms p=" + record.P.ToString("F4")
                + " diff=" + record.Difficulty + " auto=" + auto + " src=" + source
                + (record.InPure ? "" : "（完美窗口外：计入颜色/偏差，不计入 F~G 档位与 X^n）"));

            RainbowProgress.Stash(record);
        }

        /// <summary>auto 判定检测：scrPlayer.Hit 的 isAuto/auto（官方 autoplay 与自动砖块均置位）。
        /// 判定采集点（GetHitMargin*）就在这次 Hit 的调用栈里，所以这里写入的标志对本次判定有效。</summary>
        [HarmonyPatch(typeof(scrPlayer), "Hit")]
        public static class AutoDetectPatch
        {
            [HarmonyPrefix]
            public static void Prefix(scrPlayer __instance, bool isAuto)
            {
                try
                {
                    LastJudge.AutoActive = isAuto || (__instance != null && __instance.auto);
                }
                catch { }
            }
        }

        // ---------------- 仪表盘 tick 颜色 ----------------

        /// <summary>tick 颜色改为彩虹渐变。3.4.0 起没有 <c>CalculateTickColor</c>：
        /// 颜色由 <c>scrHitErrorMeter.AddHit</c> 换算刻度后传给 <c>ADOFAI.ErrorMeterTick.Show</c> 的
        /// <c>overrideColor</c>，所以分两步——这里在 AddHit 前缀把颜色算好，
        /// <see cref="TickShowColorHook"/> 在 Show 前缀里替换 overrideColor。
        /// 注意 AddHit 的入参 <c>angleDiff</c> 是**弧度**（方法内部才 ×-57.29578 转成度、再 ×60/Counted 转成刻度）。</summary>
        [HarmonyPatch(typeof(scrHitErrorMeter), "AddHit")]
        public static class TickColorHook
        {
            private static Color _color;
            private static bool _hasColor;
            private static int _frame = -1;

            [HarmonyPrefix]
            public static void Prefix(float angleDiff, float marginScale, scrFloor hitFloor)
            {
                _hasColor = false;
                try
                {
                    if (!Main.Active) return;

                    double absDeg = Math.Abs((double)angleDiff) * 57.29578;
                    double bpmTimesSpeed = RainbowMath.GameBpmTimesSpeedOf(hitFloor);
                    double pitch = RainbowMath.GetPitchNow();
                    double countedDeg = RainbowMath.CountedBoundaryDeg(bpmTimesSpeed, pitch, marginScale);
                    if (countedDeg <= 0.0001) return;

                    GradientFrame frame = GradientFrame.FromRuntime(countedDeg, marginScale, bpmTimesSpeed, pitch);
                    double scaled = absDeg * frame.PosPerDeg;
                    double wavelength = AnchorSet.WavelengthAt(scaled, frame);

                    Color color = Spectrum.WavelengthToRgb(wavelength);
                    color.a = 1f;
                    _color = color;
                    _hasColor = true;
                    _frame = Time.frameCount;

                    Logger.Log("[TickColor] scaled=" + scaled.ToString("F2") + " countedDeg=" + countedDeg.ToString("F1")
                        + " wl=" + wavelength.ToString("F1") + "nm");
                }
                catch { _hasColor = false; }
            }

            /// <summary>取走本次 tick 的颜色（一次 AddHit 只染一个 tick）</summary>
            internal static bool TryTake(out Color color)
            {
                color = _color;
                if (!_hasColor || _frame != Time.frameCount) return false;
                _hasColor = false;
                return true;
            }
        }

        /// <summary>把 <see cref="TickColorHook"/> 算好的颜色塞进 <c>ErrorMeterTick.Show</c> 的 overrideColor。
        /// 3.4.0 的 XPerfect 白描边（<c>tickOutline</c>）保持游戏原样：
        /// 它在 Show 里先被设成白色、再交给一个淡出 tween 补间，从外部改色会被补间立刻覆盖，
        /// 强行接管就得重建 tween（还得自己管 tweenId 与生命周期），风险大于收益 —— 而它与"X 完美"的语义是相符的。</summary>
        [HarmonyPatch(typeof(ADOFAI.ErrorMeterTick), "Show")]
        public static class TickShowColorHook
        {
            [HarmonyPrefix]
            public static void Prefix(ref Color? overrideColor)
            {
                try
                {
                    Color color;
                    if (!TickColorHook.TryTake(out color)) return;
                    overrideColor = color;
                }
                catch { }
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

        // ---------------- 记账入口 ----------------

        /// <summary>游戏计数入口：每次"判定被计入"追加一条（与 hitMargins 索引严格对齐）。
        /// 尖刺/激光等没有判定的计数（FailMiss 等）没有暂存数据 → 写占位条目，保证索引不错位。
        /// 这里不按 HitMargin 过滤统计——有暂存数据的按几何量照单全收
        /// （游戏会把"没打在正中"的标成 EP/LP，那不该被丢掉）；
        /// 关卡外的判定（主界面/编辑器搭关）连同暂存一起丢弃，不进账本。
        /// **同时用游戏记下的档位标记"非玩家判定"**：`hit` 就是最终进 hitMargins 的值，
        /// 它是 Auto(12) / Midspin(14) 时说明这一下不是玩家打出来的（或者中旋免判），
        /// 只记账、不进任何统计（与游戏的 X-Accuracy 口径一致）。</summary>
        [HarmonyPatch(typeof(scrMarginTracker), "AddHit")]
        public static class MarginTrackerAddHitHook
        {
            [HarmonyPostfix]
            public static void Postfix(scrMarginTracker __instance, HitMargin hit)
            {
                try
                {
                    if (!Main.Active) return;
                    if (!GameState.InGameWorld) return;
                    if (!RainbowProgress.IsPlayerOneTracker(__instance)) return;

                    HitRecord record;
                    if (!RainbowProgress.ConsumePending(out record) || !record.HasData)
                        record = RainbowProgress.MakePlaceholder();
                    else
                        record.MarkGameGrade(hit);
                    RainbowProgress.Append(record);
                }
                catch { }
            }
        }

        /// <summary>批量记账入口：<c>scrMarginTracker.RegisterDeadTiles</c> 会调私有的
        /// <c>AddHits(HitMargin.FailedFloor, count)</c> 一次性补上"死亡/复活时没打到的格子"
        /// （调用点是 scrController.OnLandOnPortal 与 scrPlayer.Revive）。它**不经过 AddHit**，
        /// 所以账本必须在这里补同样多的占位条目，否则索引会整体错位。</summary>
        [HarmonyPatch(typeof(scrMarginTracker), "AddHits")]
        public static class MarginTrackerAddHitsHook
        {
            [HarmonyPostfix]
            public static void Postfix(scrMarginTracker __instance, HitMargin hit, int count)
            {
                try
                {
                    if (!Main.Active) return;
                    if (!GameState.InGameWorld) return;
                    if (!RainbowProgress.IsPlayerOneTracker(__instance)) return;
                    if (count <= 0) return;
                    RainbowProgress.AppendPlaceholders(count, hit);
                }
                catch { }
            }
        }

        // ---------------- 诊断 ----------------

        /// <summary>
        /// 诊断用：把游戏 <c>hitMargins</c> 里每种档位的条数写成一行，用来核对"我们数出来的 == 游戏记的"。
        /// </summary>
        public static string HitMarginsHistogram()
        {
            try
            {
                scrMarginTracker tracker = GameState.PlayerTracker;
                if (tracker == null || tracker.hitMargins == null) return "hitMargins=null";

                List<HitMargin> margins = tracker.hitMargins;
                int[] counts = new int[16];
                for (int i = 0; i < margins.Count; i++)
                {
                    int value = (int)margins[i];
                    if (value >= 0 && value < counts.Length) counts[value]++;
                }

                System.Text.StringBuilder sb = new System.Text.StringBuilder(128);
                for (int i = 0; i < counts.Length; i++)
                {
                    if (counts[i] <= 0) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append((HitMargin)i).Append('=').Append(counts[i]);
                }
                int perfect = counts[(int)HitMargin.PerfectMinus] + counts[(int)HitMargin.XPerfect]
                    + counts[(int)HitMargin.PerfectPlus];
                sb.Append("  [条数=").Append(margins.Count)
                  .Append(" 完美(PM+X+PP)=").Append(perfect)
                  .Append(" Auto=").Append(counts[(int)HitMargin.Auto])
                  .Append(" Midspin=").Append(counts[(int)HitMargin.Midspin])
                  .Append(" FailedFloor=").Append(counts[(int)HitMargin.FailedFloor]).Append(']');
                return sb.ToString();
            }
            catch (System.Exception ex) { return "hitMargins 读取失败: " + ex.Message; }
        }
    }
}
