using System;

namespace RainbowJudgement
{
    /// <summary>
    /// 游戏状态访问的统一出口：把散落在各处的 try/catch 取值收敛到一处。
    /// 全部为"读不到就返回安全默认值"的只读封装，不做任何修改游戏状态的操作。
    /// </summary>
    public static class GameState
    {
        /// <summary>当前关卡名（读不到返回空串）</summary>
        public static string LevelName
        {
            get
            {
                try
                {
                    scrController ctrl = scrController.instance;
                    return ctrl != null ? (ctrl.levelName ?? "") : "";
                }
                catch { return ""; }
            }
        }

        /// <summary>当前存档点 seq（读不到返回 0）</summary>
        public static int CheckpointNum
        {
            get { try { return GCS.checkpointNum; } catch { return 0; } }
        }

        /// <summary>单人模式下 playerOne 的判定追踪器（marginTrackers 是静态数组，场景加载早期也可用）</summary>
        public static scrMarginTracker PlayerTracker
        {
            get
            {
                try
                {
                    scrMarginTracker[] trackers = scrMistakesManager.marginTrackers;
                    if (trackers != null && trackers.Length > 0 && trackers[0] != null) return trackers[0];
                }
                catch { }
                try
                {
                    scrController ctrl = scrController.instance;
                    if (ctrl != null && ctrl.playerOne != null) return ctrl.playerOne.marginTracker;
                }
                catch { }
                return null;
            }
        }

        /// <summary>游戏已计入的判定条数（= hitMargins.Count）</summary>
        public static int MarginCount
        {
            get
            {
                scrMarginTracker t = PlayerTracker;
                try { return t != null ? t.hitMargins.Count : 0; }
                catch { return 0; }
            }
        }

        /// <summary>游戏计入的"完美"条数 = 完美− / X完美 / 完美+。
        /// 3.4.0 起游戏把"角度落在 PP 边界内"的判定拆成 PerfectMinus / XPerfect / PerfectPlus 三类
        /// （旧的单一 <c>HitMargin.Perfect</c> 已不存在），这三类合起来正好等于本 Mod 7 档计数器 /
        /// X^n 的统计范围，用作不变量自检。
        /// 不要拿 EP/LP 去比：EP/LP 是"命中时刻在完美时间窗内、角度已超出 PP 边界"的近失判定
        /// （可以是 42° 这种大角度），与角度口径不是同一件事。
        /// 也不要拿 <c>HitMarginHelper.PerfectHitMargins</c> 去比：那个静态数组还含 Auto/Midspin/Multipress
        /// （它们按游戏口径不是"玩家判定"，本 Mod 与 X-Accuracy 一样把它们排除在统计之外）。</summary>
        public static int PerfectCount
        {
            get
            {
                scrMarginTracker t = PlayerTracker;
                try
                {
                    return t != null
                        ? t.GetHits(HitMargin.PerfectMinus) + t.GetHits(HitMargin.XPerfect) + t.GetHits(HitMargin.PerfectPlus)
                        : 0;
                }
                catch { return 0; }
            }
        }

        /// <summary>"该由玩家打出来、且有判定数据"的条数 =
        /// hitMargins.Count − 故障类（Multipress/FailMiss/FailOverload/OverPress/FailedFloor）
        /// − 非玩家判定（Auto 自动砖 / Midspin 中旋）。
        /// 故障类与 Auto/Midspin 都没有"玩家打出来的角度误差"：故障类压根没有 GetHitMargin；
        /// 自动砖与中旋由 <c>scrPlanet.SwitchChosen</c> 在记账前把档位改写成 Auto(12) / Midspin(14)，
        /// 游戏的 X-Accuracy 也把它们排除在分子分母之外。所以它们在 Mod 账本里只是占位条目，
        /// 不进任何判定统计。</summary>
        public static int CountableCount
        {
            get
            {
                scrMarginTracker t = PlayerTracker;
                if (t == null) return 0;
                try
                {
                    int faults = t.GetHits(HitMargin.Multipress) + t.GetHits(HitMargin.FailMiss)
                        + t.GetHits(HitMargin.FailOverload) + t.GetHits(HitMargin.OverPress)
                        + t.GetHits(HitMargin.FailedFloor);
                    int notJudged = t.GetHits(HitMargin.Auto) + t.GetHits(HitMargin.Midspin);
                    int n = t.hitMargins.Count - faults - notJudged;
                    return n > 0 ? n : 0;
                }
                catch { return 0; }
            }
        }

        /// <summary>该档位是否"不是玩家判定"（自动砖 / 中旋）——这两种档位是游戏在记账前改写的，
        /// 没有对应的角度误差，Mod 账本只记一条占位、不进任何统计（与游戏 X-Accuracy 一致）。</summary>
        public static bool IsNonJudgedGrade(HitMargin grade)
        {
            return grade == HitMargin.Auto || grade == HitMargin.Midspin;
        }

        /// <summary>是否处于关卡世界且未暂停（计数器 UI 的显示条件）</summary>
        public static bool InGameWorld
        {
            get
            {
                try
                {
                    scrConductor conductor = scrConductor.instance;
                    scrController ctrl = scrController.instance;
                    return conductor != null && ctrl != null && conductor.isGameWorld && !ctrl.paused;
                }
                catch { return false; }
            }
        }
    }
}
