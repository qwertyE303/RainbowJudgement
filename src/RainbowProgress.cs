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
        /// <summary>【旧字段】v1.0.2 起与 <see cref="InPure"/> 同义，保留是为了不改动已落盘 `.sav` 的列序号
        /// （第 2 列原本是"游戏判定为 Perfect/Auto"，现在写"是否落在原版完美窗口内"）。</summary>
        public bool IsPerfect;
        /// <summary>本次判定是否 auto 触发（官方 autoplay / 自动砖块）</summary>
        public bool IsAuto;
        /// <summary>0~6（见 RainbowCounter.Tier*）；-1 = 无档位（占位条目，或完美窗口之外）</summary>
        public int Tier;
        /// <summary>波长 nm</summary>
        public double Lambda;
        /// <summary>误差时间 ms（带符号，重放时取绝对值）</summary>
        public double TimeMs;
        /// <summary>归一化完美度 = |角度|/θ_PP（仅完美窗口内有意义，0~1；窗口外恒为 0）</summary>
        public double P;
        /// <summary>是否落在**原版完美窗口**（PP 边界）之内 —— 只有它为 true 才进 7 档计数器与 X^n 的 r；
        /// 平均判定颜色 / 平均绝对偏差则是所有 HasData 的判定都算（两套范围，刻意分开）。
        /// 另一个字段 `IsPerfect` 只是它的别名，用来维持落盘列序不变。</summary>
        public bool InPure;
    }

    /// <summary>
    /// 与游戏同构的判定账本（唯一统计口径）：
    ///   · 追加：scrMarginTracker.AddHit(HitMargin) —— 游戏"记下这一判定"的入口，索引与 hitMargins 对齐
    ///   · 回档：scrMistakesManager.RevertToLastCheckpoint 之后，按游戏 hitMargins.Count 截断并整体重放
    ///   · 清零：scrMistakesManager.Reset（游戏"从头开始"才调用）
    ///   · 续关：ProgressStore 负责把每格数据落盘/读回
    ///   · 关卡外（主界面/选歌/编辑器搭关）不采集：JudgeHooks 用 GameState.InGameWorld 挡住
    ///
    /// 两套统计范围（v1.0.2 定稿）：
    ///   · **全部有判定数据的判定**（包括完美窗口外的 EP/LP/VE/VL/Too）→ 平均判定颜色、平均绝对偏差
    ///   · **仅原版完美窗口内**（InPure）→ 7 档计数器（F A B C D E G）、X^n 的 r
    /// </summary>
    public static class RainbowProgress
    {
        private static readonly List<HitRecord> _hits = new List<HitRecord>();
        private static HitRecord _pending;
        private static bool _pendingFresh;
        private static int _pendingFrame = -1;
        private static string _lastWarnKey;
        private static int _counted;       // 参与统计的条数（有判定数据）
        private static int _countedInPure; // 其中落在原版完美窗口内的条数（7 档计数器 / X^n 的来源）

        public static int Count { get { return _hits.Count; } }
        /// <summary>参与统计的判定数（= RainbowState.Count 的来源）</summary>
        public static int Counted { get { return _counted; } }
        /// <summary>落在原版完美窗口内的判定数（= 7 档计数之和）</summary>
        public static int CountedInPure { get { return _countedInPure; } }

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

        /// <summary>追加一条并同步更新聚合值（热路径，不分配）。
        /// 颜色/偏差：所有有判定数据的都算；7 档计数器：只有完美窗口内（InPure）才算。</summary>
        public static void Append(HitRecord record)
        {
            _hits.Add(record);
            if (record.HasData)
            {
                CountRecord(record);
            }
            CheckInvariant(false);
        }

        /// <summary>把一条有效判定的数据计入聚合值（实时追加与回档重放共用同一条路径，保证口径一致）</summary>
        private static void CountRecord(HitRecord r)
        {
            _counted++;
            RainbowState.Add(r.Lambda, r.TimeMs);       // 平均判定颜色 / 平均绝对偏差：全部判定
            if (r.InPure && r.Tier >= 0)
            {
                _countedInPure++;                        // 7 档计数器 / X^n 的 r：仅原版完美窗口内
                RainbowCounter.AddTier(r.Tier, r.P);
            }
        }

        public static void Clear()
        {
            _hits.Clear();
            _pendingFresh = false;
            _pendingFrame = -1;
            _counted = 0;
            _countedInPure = 0;
            RainbowState.Reset();
            RainbowCounter.Reset();
            CounterDisplay.Refresh();
        }

        /// <summary>从列表整体重放全部统计（回档/续关/对齐后调用）</summary>
        public static void RebuildAll()
        {
            RainbowState.Reset();
            RainbowCounter.ResetCounts();
            _counted = 0;
            _countedInPure = 0;
            for (int i = 0; i < _hits.Count; i++)
            {
                HitRecord r = _hits[i];
                if (!r.HasData) continue;
                CountRecord(r);
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
                Logger.Log("[RainbowProgress] 新关卡：清零");
            }
            else
            {
                AlignToGame();
                Logger.Log("[RainbowProgress] 场景加载：与游戏对齐 游戏条数=" + gameCount
                    + " checkpointNum=" + checkpointNum + " → 我们=" + _hits.Count);
            }
        }

        /// <summary>回档后（scrMistakesManager.RevertToLastCheckpoint）：与游戏条数对齐（多了截断、少了补占位）并重放</summary>
        public static void OnGameRevert()
        {
            int gameCount = GameState.MarginCount;
            if (gameCount != _hits.Count)
                Logger.Log("[RainbowProgress] 回档前不一致：我们=" + _hits.Count + " 游戏=" + gameCount);
            AlignToGame();
            Logger.Log("[RainbowProgress] 回档同步：保留=" + _hits.Count + "（游戏 " + gameCount + "）");
        }

        /// <summary>游戏从头开始（scrMistakesManager.Reset）</summary>
        public static void OnGameReset()
        {
            Clear();
            Logger.Log("[RainbowProgress] 游戏 Reset：清零");
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

        /// <summary>不变量（v1.0.2 双范围口径）：
        ///   ① 账本长度 == 游戏 hitMargins.Count
        ///   ② 参与统计条数 == 游戏可判定条数（条数 − 故障类）
        ///   ③ 7 档之和 == InPure 条数 == 游戏**严格 Perfect**(+Auto)
        /// ③ 专门盯「有没有把完美窗口之外的判定算进新增档位里」。
        /// 注意这里必须比 **Perfect**、不能比 Perfect+EarlyPerfect+LatePerfect：游戏的 EP/LP 是"命中时刻落在
        /// 完美时间窗内、但角度已超出 PP 边界"的近失判定（EP 可以是 42° 这种大角度），
        /// 而我们要的"完美"就是角度口径 —— 实测两者严格对齐（例如某局 24 Perfect / 24 条窗口内，
        /// 而 P+EP+LP=27），拿 EP/LP 去比会产生误报。</summary>
        private static void CheckInvariant(bool verbose)
        {
            try
            {
                int gameCount = GameState.MarginCount;
                int gameCountable = GameState.CountableCount;
                int gamePerfect = GameState.PerfectCount;
                int tiers = RainbowCounter.TotalTiers();
                if (gameCount != _hits.Count || _counted != gameCountable
                    || tiers != _countedInPure || _countedInPure != gamePerfect)
                {
                    string key = _hits.Count + "/" + gameCount + "/" + _counted + "/" + gameCountable
                        + "/" + _countedInPure + "/" + gamePerfect + "/" + tiers;
                    if (key == _lastWarnKey) return; // 同一种不一致只报一次，避免刷屏
                    _lastWarnKey = key;
                    Logger.Warn("[RainbowProgress] 不变量不一致：条数 我们=" + _hits.Count + " 游戏=" + gameCount
                        + "；统计条数 我们=" + _counted + " 游戏(可判定)=" + gameCountable
                        + "；完美窗口内 我们=" + _countedInPure + " 游戏(Perfect+Auto)=" + gamePerfect
                        + "；7档之和=" + tiers);
                }
                else
                {
                    _lastWarnKey = null;
                    if (verbose)
                        Logger.Log("[RainbowProgress] 一致：条数=" + gameCount + " 统计=" + _counted
                            + " 完美窗口内=" + _countedInPure + " 7档之和=" + tiers);
                }
            }
            catch { }
        }
    }
}
