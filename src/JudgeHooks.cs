using System;
using System.Collections.Generic;
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
                    if (!Main.Active) return true;

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

        /// <summary>auto 判定检测：scrPlayer.Hit 的 isAuto/auto（官方 autoplay 与自动砖块均置位）。
        /// 同时抓一份"游戏即将用来决定记账档位的原始输入"快照 —— 这一步在 `Hit` 开头，
        /// `currFloor` 还指着**这次判定对应的那一格**，正好是游戏读 `scrFloor.auto` 的时机。</summary>
        [HarmonyPatch(typeof(scrPlayer), "Hit")]
        public static class AutoDetectPatch
        {
            [HarmonyPrefix]
            public static void Prefix(scrPlayer __instance, bool isAuto)
            {
                try
                {
                    LastJudge.AutoActive = isAuto || (__instance != null && __instance.auto);
                    GetMarginHook.CaptureHitSignals(__instance, isAuto);
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
                    // 判定瞬间的关卡难度：难度是全局运行时量、玩家可在局内切换，
                    // 必须逐条存下来，重放历史时才能按记录自己的难度去选自定义档位的那一行。
                    record.Difficulty = CustomJudge.CurrentDifficulty();

                    // 【统计范围】7 档计数器（F A B C D E G）只收「原版完美边界之内」的判定。
                    // EP/LP（稍快！/稍晚！）、VE/VL、Too 都在 PP 边界之外 → 不进任何档位桶、不进 X^n 的 r
                    // （它们仍然参与平均判定颜色与平均绝对时间/角度偏差——那三处统计的是全部判定）。
                    record.InPure = absScaled <= frame.PpScaled;

                    // 【游戏口径的强制 PP】中旋（midspin）格上 scrPlanet.SwitchChosen 会把
                    // midspinInfiniteMargin 为真的判定**直接改写成 HitMargin.Perfect** 再记进 hitMargins
                    // （IL：midspinInfiniteMargin → ldc.i4.3 → AddHit；autoplay 同理 → Auto），
                    // 所以这种判定的角度误差其实在完美窗口之外，游戏却按 PP 记账。
                    // 这里照游戏口径认出来：只有"游戏确实记成 Perfect/Auto"且"几何量在窗口外"才算，
                    // 免得把 midspin 上真正打死的判定（Too）也算进去。
                    if (!record.InPure)
                    {
                        record.ForcePP = MirrorsForcedPerfect(__result);
                        if (!record.ForcePP && (__result == HitMargin.Perfect || __result == HitMargin.Auto))
                        {
                            // 游戏把这条记成 PP，几何量却在窗口外，而且**不是**中旋强制那条路
                            // → 说明"游戏 PP 总数"里还有别的来源，或者本 Mod 的窗口算错了。
                            // 两种都是必须留证据的事（否则差值会被静默吞掉），所以直接写 UMM 日志。
                            Logger.Warn("[GetMargin] 游戏记 PP 但几何量在窗口外且非中旋强制："
                                + " result=" + __result + " absDeg=" + absDeg.ToString("F3")
                                + " ppDeg=" + record.PpDeg.ToString("F3")
                                + " absScaled=" + absScaled.ToString("F3") + " ppScaled=" + frame.PpScaled.ToString("F3")
                                + " marginScale=" + marginScale.ToString("F4") + " bpmSpeed=" + bpmTimesSpeed.ToString("F3")
                                + " practice=" + frame.PracticeScale.ToString("F3"));
                        }
                    }

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
                    record.Lambda = AnchorSet.WavelengthAt(absScaled, frame);
                    record.TimeMs = RainbowMath.TimeMsOfDeg(absDeg, bpmTimesSpeed, conductorPitch);

                    LastJudge.ScaledPos = absScaled;
                    LastJudge.Frame = frame;

                    Logger.Log("[GetMargin] " + __result + " delta=" + delta.ToString("F2") + " absDeg=" + absDeg.ToString("F2")
                        + " scaled=" + absScaled.ToString("F2") + " tier=" + record.Tier
                        + " countedDeg=" + countedDeg.ToString("F1") + " ppDeg=" + record.PpDeg.ToString("F1")
                        + " wl=" + record.Lambda.ToString("F1") + "nm t=" + record.TimeMs.ToString("F2") + "ms p=" + record.P.ToString("F4")
                        + " diff=" + record.Difficulty
                        + " " + ForcedSignalsText()
                        + (record.InPure ? "" : "（完美窗口外：计入颜色/偏差，不计入 F~G 档位与 X^n）")
                        + (record.ForcePP ? "（强制PP：游戏记账改写成 Perfect → 补进自定义判定最严一档，并按零误差计入平均判定与 X^n）" : ""));

                    RainbowProgress.Stash(record);
                }
                catch (Exception ex)
                {
                    Logger.Log("[JudgeHooks/GetMargin] " + ex.Message);
                }
            }

            // ---------------- 游戏"强制 PP"的复刻（中旋 / 旧式 autoplay） ----------------
            //
            // scrPlanet.SwitchChosen 里真正写进 scrMarginTracker.AddHit 的值是 V_6，它的决策是：
            //   V_6 = GetHitMargin(...)                      // 按角度算出来的档位（判定文字用的也是它）
            //   if (midspinInfiniteMargin) V_6 = 3;          // 3 = HitMargin.Perfect
            //   if ((player.auto || floor.auto) && !useOldAuto) V_6 = 3;
            //   if (floor.auto) V_6 = 10;                    // 10 = HitMargin.Auto
            // 也就是说"游戏记账的档位"和"GetHitMargin 按角度给出的档位"在中旋 / 旧式自动砖上会分叉。
            // 本 Mod 要复刻的正是这个分叉 —— 认出来之后：自定义判定补进最严一档，
            // 平均判定与 X^n 按零误差计入（游戏 X-Accuracy 里 Perfect 拿满分权重 1.0）。

            private static System.Reflection.FieldInfo _midspinField;
            private static bool _fieldsSearched;
            private static System.Reflection.PropertyInfo _useOldAutoProp;

            // scrPlayer.Hit 开头抓的快照：此刻 currFloor 正是"这次判定对应的那一格"，
            // 是游戏读 scrFloor.auto / midSpin 的时机（AutoDetectPatch.Prefix 里写入）。
            private static bool _hitIsAuto;
            private static bool _hitPlayerAuto;
            private static bool _hitFloorAuto;
            private static bool _hitFloorMidspin;
            private static bool _hitSnapshotValid;

            /// <summary>由 AutoDetectPatch.Prefix 调用：抓一份游戏决策要用的原始输入。</summary>
            public static void CaptureHitSignals(scrPlayer player, bool isAuto)
            {
                try
                {
                    EnsureFields();
                    _hitIsAuto = isAuto;
                    _hitPlayerAuto = player != null && player.auto;
                    _hitFloorAuto = false;
                    _hitFloorMidspin = false;
                    scrFloor floor = PlayerCurrFloor(player);
                    if (floor != null)
                    {
                        _hitFloorAuto = floor.auto;
                        _hitFloorMidspin = floor.midSpin;
                    }
                    _hitSnapshotValid = true;
                }
                catch { _hitSnapshotValid = false; }
            }

            /// <summary>
            /// 这次判定是否被游戏改写成 PP。判据对着 SwitchChosen 的 V_6 决策逐条来，
            /// 并且**只认游戏确实记成 Perfect/Auto 的那些**（VE/VL/Too 是游戏照实记账的真失误，不算）。
            /// 采集点（GetHitMargin 的 postfix）就在 SwitchChosen 内部、决策之前，所以读到的
            /// `midspinInfiniteMargin` 就是游戏即将用来记账的值。
            /// </summary>
            private static bool MirrorsForcedPerfect(HitMargin hit)
            {
                if (hit != HitMargin.Perfect && hit != HitMargin.Auto) return false;
                try
                {
                    EnsureFields();

                    scrController ctrl = scrController.instance;
                    scrPlayer player = ctrl != null ? ctrl.playerOne : null;
                    if (player == null) return false;

                    // ① 中旋：scrPlayer.Hit / SwitchChosen 里读 currFloor.midSpin 置位，随后强制 Perfect
                    if (_midspinField != null)
                    {
                        object value = _midspinField.GetValue(player);
                        if (value is bool && (bool)value) return true;
                    }

                    // ② 旧式 autoplay 的自动砖：(player.auto || floor.auto) && !useOldAuto → 强制 Perfect
                    if (UseOldAuto()) return false;
                    if (_hitSnapshotValid && (_hitIsAuto || _hitPlayerAuto || _hitFloorAuto)) return true;
                    if (player.auto) return true;

                    scrFloor floor = PlayerCurrFloor(player);
                    return floor != null && floor.auto;
                }
                catch { return false; }
            }

            /// <summary>诊断用：把决策输入原样读出来（读不到返回 "?"），漏判时靠它定位是哪一条没对上。</summary>
            private static string ForcedSignalsText()
            {
                try
                {
                    EnsureFields();
                    scrController ctrl = scrController.instance;
                    scrPlayer player = ctrl != null ? ctrl.playerOne : null;
                    if (player == null) return "?";

                    string mid = "?";
                    if (_midspinField != null)
                    {
                        object value = _midspinField.GetValue(player);
                        if (value is bool) mid = (bool)value ? "1" : "0";
                    }
                    scrFloor floor = PlayerCurrFloor(player);
                    return "mid=" + mid
                        + " pA=" + ((_hitSnapshotValid ? _hitPlayerAuto : player.auto) ? "1" : "0")
                        + " fA=" + ((_hitSnapshotValid && _hitFloorAuto) || (floor != null && floor.auto) ? "1" : "0")
                        + " fM=" + ((_hitSnapshotValid && _hitFloorMidspin) || (floor != null && floor.midSpin) ? "1" : "0")
                        + " old=" + (UseOldAuto() ? "1" : "0");
                }
                catch { return "?"; }
            }

            private static System.Reflection.PropertyInfo _currFloorProp;
            private static System.Reflection.FieldInfo _planetaryField;
            private static System.Reflection.FieldInfo _chosenPlanetField;
            private static System.Reflection.FieldInfo _planetCurrfloorField;
            /// <summary>判定所对应的那一格（= scrPlayer.get_currFloor() 的等价读取）。
            /// 注意 `scrPlayer.currFloor` 是**属性**不是字段：IL 是
            /// <c>chosenPlanet → scrPlanet.currfloor</c>，所以优先反射属性，取不到再顺着
            /// planetarySystem.chosenPlanet.currfloor 手动走一遍。</summary>
            private static scrFloor PlayerCurrFloor(scrPlayer player)
            {
                if (player == null) return null;
                try
                {
                    if (_currFloorProp != null)
                    {
                        scrFloor floor = _currFloorProp.GetValue(player, null) as scrFloor;
                        if (floor != null) return floor;
                    }
                    if (_planetaryField == null || _chosenPlanetField == null || _planetCurrfloorField == null) return null;
                    object system = _planetaryField.GetValue(player);
                    if (system == null) return null;
                    object planet = _chosenPlanetField.GetValue(system);
                    if (planet == null) return null;
                    return _planetCurrfloorField.GetValue(planet) as scrFloor;
                }
                catch { return null; }
            }

            private static bool UseOldAuto()
            {
                if (_useOldAutoProp == null) return false;
                try
                {
                    object value = _useOldAutoProp.GetValue(null, null);
                    return value is bool && (bool)value;
                }
                catch { return false; }
            }

            /// <summary>一次性解析需要的非 public 成员
            /// （scrPlayer.midspinInfiniteMargin / currFloor 属性、scrPlanet.currfloor、RDC.useOldAuto）。</summary>
            private static void EnsureFields()
            {
                if (_fieldsSearched) return;
                _fieldsSearched = true;
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

                _midspinField = typeof(scrPlayer).GetField("midspinInfiniteMargin", flags);
                _currFloorProp = typeof(scrPlayer).GetProperty("currFloor",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);
                // 属性拿不到时的备用路径：scrPlayer.planetarySystem → scrPlanet.chosenPlanet → scrPlanet.currfloor
                _planetaryField = typeof(scrPlayer).GetField("planetarySystem", flags);
                _chosenPlanetField = typeof(PlanetarySystem).GetField("chosenPlanet", flags);
                _planetCurrfloorField = typeof(scrPlanet).GetField("currfloor", flags);
                try { _useOldAutoProp = typeof(RDC).GetProperty("useOldAuto", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static); }
                catch { _useOldAutoProp = null; }

                if (_midspinField == null || (_currFloorProp == null && _planetCurrfloorField == null))
                    Logger.Warn("[JudgeHooks] 反射失败：midspinInfiniteMargin=" + (_midspinField != null)
                        + " currFloorProp=" + (_currFloorProp != null) + " planetCurrfloor=" + (_planetCurrfloorField != null)
                        + " → 中旋强制 PP 的辅助信号不可用（主判据是 AddHit 的地面真值对账，仍能对上账）");
            }

            /// <summary>
            /// 诊断用：把游戏 <c>hitMargins</c> 里每种档位的条数写成一行（[强Perfect] 里两位以上的才是真 Perfect）。
            /// 数都来自游戏自己的 List，用来核对"我们数出来的 == 游戏记的"。
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

        /// <summary>游戏计数入口：每次"判定被计入"追加一条（与 hitMargins 索引严格对齐）。
        /// 尖刺/激光等没有判定的计数（FailMiss 等）没有暂存数据 → 写占位条目，保证索引不错位。
        /// 这里不按 HitMargin 过滤统计——有暂存数据的按几何量照单全收
        /// （游戏会把"没打在正中"的标成 EarlyPerfect/LatePerfect，那不该被丢掉）；
        /// 关卡外的判定（主界面/编辑器搭关）连同暂存一起丢弃，不进账本。
        /// **同时用游戏记下的档位校准强制 PP**：`hit` 就是最终进 hitMargins 的值，
        /// 它说 Perfect/Auto 而几何量在窗口外 → 必定是游戏按完美记账（中旋 / 旧式自动砖），
        /// 这种校准不依赖任何标志位，是这套机制的地面真值。</summary>
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
                        record.MergeGameGrade(hit);
                    RainbowProgress.Append(record);
                }
                catch { }
            }
        }
    }
}
