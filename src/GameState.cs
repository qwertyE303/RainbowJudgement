using System;
using System.Collections.Generic;

namespace RainbowJudgement
{
    /// <summary>
    /// 游戏状态访问的统一出口：把散落在各处的 try/catch 取值收敛到一处。
    /// 全部为"读不到就返回安全默认值"的只读封装，不做任何修改游戏状态的操作。
    ///
    /// **2.9.8 专属差异**：这一版没有 scrMarginTracker 类型，判定账本
    /// （<c>hitMargins</c> / <c>hitMarginsCount</c> / <c>lastHitMarginsSize</c>）是
    /// <c>scrMistakesManager</c> 的**静态**成员，由两个玩家共享（2.9.8 的双人共用同一份统计，
    /// 所以这里不需要"只认 playerOne"的判断）。对外暴露的成员名保持与 3.3.0 那棵树一致，
    /// 便于以后两边同步改动。
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

        /// <summary>游戏的判定列表（2.9.8 是 scrMistakesManager 的静态字段，读不到返回 null）</summary>
        public static List<HitMargin> HitMargins
        {
            get
            {
                try { return scrMistakesManager.hitMargins; }
                catch { return null; }
            }
        }

        /// <summary>游戏某个档位的判定条数（读不到返回 0）。
        /// **2.9.8 差异**：GetHits 是**实例**方法（3.3.0 里是 scrMarginTracker 的实例方法），
        /// 实例从 scrController.mistakesManager 取。</summary>
        public static int Hits(HitMargin margin)
        {
            try
            {
                scrController ctrl = scrController.instance;
                scrMistakesManager manager = ctrl != null ? ctrl.mistakesManager : null;
                if (manager != null) return manager.GetHits(margin);
            }
            catch { }
            // 兜底：直接从静态判定列表里数（大厅/场景早期没有 mistakesManager 时也能用）
            try
            {
                List<HitMargin> margins = scrMistakesManager.hitMargins;
                if (margins != null)
                {
                    int count = 0;
                    for (int i = 0; i < margins.Count; i++) if (margins[i] == margin) count++;
                    return count;
                }
            }
            catch { }
            return 0;
        }

        /// <summary>游戏记录的"存档点时的判定条数"快照（读不到返回 0），进度指纹用</summary>
        public static int LastHitMarginsSize
        {
            get
            {
                try { return scrMistakesManager.lastHitMarginsSize; }
                catch { return 0; }
            }
        }

        /// <summary>游戏已计入的判定条数（= hitMargins.Count）</summary>
        public static int MarginCount
        {
            get
            {
                try
                {
                    List<HitMargin> margins = scrMistakesManager.hitMargins;
                    return margins != null ? margins.Count : 0;
                }
                catch { return 0; }
            }
        }

        /// <summary>游戏计入的"完美"条数 = Perfect + Auto。
        /// 实测它正是"角度落在 PP 边界内"的条数（本 Mod 7 档计数器 / X^n 的统计范围），用作不变量自检。
        /// 不要拿 Perfect+EarlyPerfect+LatePerfect 去比：EP/LP 是"命中时刻在完美时间窗内、角度已超出 PP 边界"
        /// 的近失判定（可以是 42° 这种大角度），与角度口径不是同一件事。</summary>
        public static int PerfectCount
        {
            get { return Hits(HitMargin.Perfect) + Hits(HitMargin.Auto); }
        }

        /// <summary>"有判定数据"的判定条数 = hitMargins.Count − 故障类（Multipress/FailMiss/FailOverload/OverPress）。
        /// 这四种只会由尖刺/激光/多按/Overspress 触发（没有 GetHitMargin、没有角度误差），
        /// 所以 Mod 账本里它们是占位条目；其余条数都应参与统计。</summary>
        public static int CountableCount
        {
            get
            {
                int total = MarginCount;
                if (total <= 0) return 0;
                int faults = Hits(HitMargin.Multipress) + Hits(HitMargin.FailMiss)
                    + Hits(HitMargin.FailOverload) + Hits(HitMargin.OverPress);
                int n = total - faults;
                return n > 0 ? n : 0;
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
