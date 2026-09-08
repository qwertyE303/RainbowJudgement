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

        /// <summary>游戏计入的"完美"条数 = Perfect + Auto</summary>
        public static int PerfectCount
        {
            get
            {
                scrMarginTracker t = PlayerTracker;
                try { return t != null ? t.GetHits(HitMargin.Perfect) + t.GetHits(HitMargin.Auto) : 0; }
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
