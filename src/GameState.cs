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

        /// <summary>游戏计入的"完美"条数 = Perfect + Auto。
        /// 实测它正是"角度落在 PP 边界内"的条数（本 Mod 7 档计数器 / X^n 的统计范围），用作不变量自检。
        /// 不要拿 Perfect+EarlyPerfect+LatePerfect 去比：EP/LP 是"命中时刻在完美时间窗内、角度已超出 PP 边界"
        /// 的近失判定（可以是 42° 这种大角度），与角度口径不是同一件事。</summary>
        public static int PerfectCount
        {
            get
            {
                scrMarginTracker t = PlayerTracker;
                try { return t != null ? t.GetHits(HitMargin.Perfect) + t.GetHits(HitMargin.Auto) : 0; }
                catch { return 0; }
            }
        }

        /// <summary>游戏计入的"原版完美窗口内"条数 = Perfect + EarlyPerfect + LatePerfect。
        /// 对应本 Mod 7 档计数器（紫/青/蓝/绿）的统计范围——超过 PP 边界的 EP/LP 虽然也带角度数据，
        /// 但不该被算进新增档位的"完美"里（v1.0.2 用户明确要求）。</summary>
        public static int PureCount
        {
            get
            {
                scrMarginTracker t = PlayerTracker;
                try
                {
                    return t != null
                        ? t.GetHits(HitMargin.Perfect) + t.GetHits(HitMargin.EarlyPerfect) + t.GetHits(HitMargin.LatePerfect)
                        : 0;
                }
                catch { return 0; }
            }
        }

        /// <summary>"有判定数据"的判定条数 = hitMargins.Count − 故障类（Multipress/FailMiss/FailOverload/OverPress）。
        /// 这四种只会由尖刺/激光/多按/Overspress 触发（没有 GetHitMargin、没有角度误差），
        /// 所以 Mod 账本里它们是占位条目；其余条数都应参与统计（v1.0.2 口径）。</summary>
        public static int CountableCount
        {
            get
            {
                scrMarginTracker t = PlayerTracker;
                if (t == null) return 0;
                try
                {
                    int faults = t.GetHits(HitMargin.Multipress) + t.GetHits(HitMargin.FailMiss)
                        + t.GetHits(HitMargin.FailOverload) + t.GetHits(HitMargin.OverPress);
                    int n = t.hitMargins.Count - faults;
                    return n > 0 ? n : 0;
                }
                catch { return 0; }
            }
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
