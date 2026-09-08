using System;
using System.Collections.Generic;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>一次判定。与游戏 scrMarginTracker.hitMargins 索引一一对应（同长同序）。</summary>
    public struct HitRecord
    {
        /// <summary>false = 该条计数没有判定数据（尖刺/激光等 FailMiss）</summary>
        public bool HasData;
        /// <summary>游戏判定为 Perfect / Auto → 参与本 Mod 统计</summary>
        public bool IsPerfect;
        public bool IsAuto;
        /// <summary>0~6（见 RainbowCounter.Tier*）；-1 = 无</summary>
        public int Tier;
        /// <summary>波长 nm</summary>
        public double Lambda;
        /// <summary>误差时间 ms（带符号，重放时取绝对值）</summary>
        public double TimeMs;
        /// <summary>归一化完美度 0~1</summary>
        public double P;
    }

    /// <summary>
    /// 与游戏同构的判定账本（唯一统计口径）：
    ///   · 追加：scrMarginTracker.AddHit(HitMargin) —— 游戏"记下这一判定"的入口，索引与 hitMargins 对齐
    ///   · 回档：scrMistakesManager.RevertToLastCheckpoint 之后，按游戏 hitMargins.Count 截断并整体重放
    ///   · 清零：scrMistakesManager.Reset（游戏"从头开始"才调用）
    ///   · 续关：ProgressStore 负责把每格数据落盘/读回
    /// 7 档计数、平均波长、平均绝对偏差、X^n 的 r 全部由本列表重放得出。
    /// </summary>
    public static class RainbowProgress
    {
        private static readonly List<HitRecord> _hits = new List<HitRecord>();
        private static HitRecord _pending;
        private static bool _pendingFresh;
        private static int _pendingFrame = -1;
        private static string _lastWarnKey;

        public static int Count { get { return _hits.Count; } }

        // ---------------- 暂存（GetMarginHook 填 → AddHit 消费） ----------------

        public static void Stash(HitRecord record)
        {
            _pending = record;
            _pendingFresh = true;
            _pendingFrame = Time.frameCount;
        }

        /// <summary>消费暂存数据；只有"同一帧内刚记录过"才算新鲜（避免尖刺/激光等无判定的计数拿到旧数据）</summary>
        public static bool ConsumePending(out HitRecord record)
        {
            record = _pending;
            bool fresh = _pendingFresh && _pendingFrame == Time.frameCount;
            _pendingFresh = false;
            return fresh;
        }

        public static HitRecord MakePlaceholder()
        {
            HitRecord r = default(HitRecord);
            r.Tier = -1;
            return r;
        }

        // ---------------- 追加 / 截断 / 清空 ----------------

        /// <summary>追加一条并同步更新聚合值（热路径，不分配）</summary>
        public static void Append(HitRecord record)
        {
            _hits.Add(record);
            if (record.IsPerfect)
            {
                RainbowState.Add(record.Lambda, record.TimeMs);
                RainbowCounter.AddTier(record.Tier < 0 ? RainbowCounter.TierPurple : record.Tier, record.P);
            }
            CheckInvariant(false);
        }

        public static void Clear()
        {
            _hits.Clear();
            _pendingFresh = false;
            _pendingFrame = -1;
            RainbowState.Reset();
            RainbowCounter.Reset();
            CounterDisplay.Refresh();
        }

        /// <summary>从列表整体重放全部统计（回档/续关/对齐后调用）</summary>
        public static void RebuildAll()
        {
            RainbowState.Reset();
            RainbowCounter.ResetCounts();
            for (int i = 0; i < _hits.Count; i++)
            {
                HitRecord r = _hits[i];
                if (!r.IsPerfect) continue;
                RainbowState.Add(r.Lambda, r.TimeMs);
                RainbowCounter.AddTier(r.Tier < 0 ? RainbowCounter.TierPurple : r.Tier, r.P);
            }
            CounterDisplay.Refresh();
            CheckInvariant(true);
        }

        // ---------------- 与游戏状态同步 ----------------

        /// <summary>场景加载后（scrController.Awake）：游戏有进度（续关/死亡重开）→ 与游戏对齐；完全没有进度 → 新关卡清零。
        /// 注意：不能按"关卡名变了"来清零——菜单/选歌场景也有 scrController，会把刚续关恢复的数据误清。</summary>
        public static void OnSceneLoaded()
        {
            int gameCount = GameState.MarginCount;
            int checkpointNum = GameState.CheckpointNum;

            if (gameCount == 0 && checkpointNum == 0)
            {
                Clear();
                if (Main.Settings.DebugLog) Logger.Log("[RainbowProgress] 新关卡：清零");
            }
            else
            {
                AlignToGame();
                if (Main.Settings.DebugLog)
                    Logger.Log("[RainbowProgress] 场景加载：与游戏对齐 游戏条数=" + gameCount + " checkpointNum=" + checkpointNum);
            }
        }

        /// <summary>回档后（scrMistakesManager.RevertToLastCheckpoint）：与游戏条数对齐（多了截断、少了补占位）并重放</summary>
        public static void OnGameRevert()
        {
            int gameCount = GameState.MarginCount;
            if (Main.Settings.DebugLog && gameCount != _hits.Count)
                Logger.Log("[RainbowProgress] 回档前不一致：我们=" + _hits.Count + " 游戏=" + gameCount);
            AlignToGame();
            Logger.Log("[RainbowProgress] 回档同步：保留=" + _hits.Count + "（游戏 " + gameCount + "）");
        }

        /// <summary>游戏从头开始（scrMistakesManager.Reset）</summary>
        public static void OnGameReset()
        {
            Clear();
            if (Main.Settings.DebugLog) Logger.Log("[RainbowProgress] 游戏 Reset：清零");
        }

        /// <summary>与游戏当前状态对齐（开关切换后用）：条数不足补占位，多了截断</summary>
        public static void RealignFromGame()
        {
            AlignToGame();
        }

        private static void AlignToGame()
        {
            int gameCount = GameState.MarginCount;
            if (_hits.Count < gameCount)
            {
                // 我们缺数据 → 用占位条目补齐（不伪造角度，仅保持索引对齐）
                FillPlaceholders(gameCount);
                Logger.Warn("[RainbowProgress] 数据缺失：补齐占位条目至 " + gameCount + " 条");
            }
            else if (_hits.Count > gameCount)
            {
                if (gameCount > 0) _hits.RemoveRange(gameCount, _hits.Count - gameCount);
                else _hits.Clear();
            }
            RebuildAll();
        }

        // ---------------- 供 ProgressStore 使用 ----------------

        /// <summary>取当前列表副本（落盘用）</summary>
        public static List<HitRecord> Snapshot()
        {
            return new List<HitRecord>(_hits);
        }

        /// <summary>用外部数据替换整份列表：取前缀 + 占位补齐，长度严格等于 targetCount</summary>
        public static void ReplaceAll(List<HitRecord> source, int targetCount)
        {
            _hits.Clear();
            if (targetCount < 0) targetCount = 0;
            int fromSource = source != null ? Math.Min(source.Count, targetCount) : 0;
            for (int i = 0; i < fromSource; i++) _hits.Add(source[i]);
            for (int i = fromSource; i < targetCount; i++) _hits.Add(MakePlaceholder());
            RebuildAll();
        }

        public static void FillPlaceholders(int count)
        {
            for (int i = 0; i < count; i++) _hits.Add(MakePlaceholder());
        }

        /// <summary>是否为 playerOne 的追踪器（只统计单人）</summary>
        public static bool IsPlayerOneTracker(scrMarginTracker tracker)
        {
            if (tracker == null) return false;
            scrMarginTracker mine = GameState.PlayerTracker;
            return mine != null && ReferenceEquals(mine, tracker);
        }

        // ---------------- 自检 ----------------

        /// <summary>不变量：列表长度 == 游戏 hitMargins.Count；7 档之和 == 游戏 Perfect+Auto</summary>
        private static void CheckInvariant(bool verbose)
        {
            try
            {
                int gameCount = GameState.MarginCount;
                int gamePerfect = GameState.PerfectCount;
                int ours = RainbowCounter.TotalTiers();
                if (gameCount != _hits.Count || gamePerfect != ours)
                {
                    string key = _hits.Count + "/" + gameCount + "/" + ours + "/" + gamePerfect;
                    if (key == _lastWarnKey) return; // 同一种不一致只报一次，避免刷屏
                    _lastWarnKey = key;
                    Logger.Warn("[RainbowProgress] 不变量不一致：条数 我们=" + _hits.Count + " 游戏=" + gameCount
                        + "；完美 我们=" + ours + " 游戏=" + gamePerfect);
                }
                else
                {
                    _lastWarnKey = null;
                    if (verbose && Main.Settings.DebugLog)
                        Logger.Log("[RainbowProgress] 一致：条数=" + gameCount + " 完美=" + ours);
                }
            }
            catch { }
        }
    }
}
