using System;
using System.Collections.Generic;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>一次判定。与游戏 <c>scrMarginTracker.hitMargins</c> 索引一一对应（同长同序）。</summary>
    public struct HitRecord
    {
        /// <summary>false = 该条计数没有判定数据（尖刺/激光等 FailMiss）</summary>
        public bool HasData;
        /// <summary>是否落在**原版完美窗口**（PP 边界）之内 —— 只有它为 true 才进 7 档计数器与 X^n 的 r；
        /// 平均判定颜色 / 平均绝对时间偏差 / 平均绝对角度偏差则是所有 HasData 的判定都算（两套范围，刻意分开）。</summary>
        public bool InPure;
        /// <summary>本次判定是否 auto 触发（官方 autoplay / 自动砖块）</summary>
        public bool IsAuto;
        /// <summary>本次判定被游戏**强制记成 PP**（中旋 midspin 的无限判定窗 / 旧式 autoplay 的强制 Perfect）：
        /// 游戏记账用的是 <c>scrPlanet.SwitchChosen</c> 里被改写过的档位（V_6 = HitMargin.Perfect），
        /// 与 <c>scrMisc.GetHitMargin</c> 按角度算出来的那个档位不是一回事。
        /// 游戏自己在 X-Accuracy 里给这种判定满分权重（1.0）、且 deadTiles 显式排除 midSpin 砖，
        /// 所以本 Mod 也照游戏口径处理：补进自定义判定**最严那一档**、X^n 的 p 记 0、
        /// 平均判定按**零误差**计入（见 RainbowProgress.CountRecord / CustomCounter.Count）。</summary>
        public bool ForcePP;
        /// <summary>0~6（见 RainbowCounter.Tier*）；-1 = 无档位（占位条目，或完美窗口之外）</summary>
        public int Tier;
        /// <summary>波长 nm</summary>
        public double Lambda;
        /// <summary>误差时间 ms（带符号）</summary>
        public double TimeMs;
        /// <summary>归一化完美度 = |角度|/θ_PP（仅完美窗口内有意义，0~1；窗口外恒为 0）</summary>
        public double P;

        // ---------------- 几何数据（v2 落盘列）----------------
        // 有了这几个量，任意时刻都能按**当前**的档位定义与锚点表把这条判定精确重算一遍
        // （改档位立即影响全部历史判定），而不必依赖"重放时的游戏状态"。
        // PosPerDeg <= 0 表示来自老存档（v1）：回退到上面那几列判定瞬间的快照值。

        /// <summary>带符号角度误差（度）；early &lt; 0，late &gt; 0，auto = 0</summary>
        public double DeltaDeg;
        /// <summary>每 1 度误差对应多少刻度 = 60 / Counted边界角</summary>
        public double PosPerDeg;
        /// <summary>marginScale × PosPerDeg（自定义档位边界用）</summary>
        public double AScale;
        /// <summary>每 1ms 误差对应多少刻度 × PosPerDeg（自定义档位边界用）</summary>
        public double BScale;
        /// <summary>原版 Perfect（PP）边界角（度）</summary>
        public double PpDeg;
        /// <summary>原版 Pure（稍快/稍晚）边界角（度）</summary>
        public double PureDeg;
        /// <summary>判定瞬间的练习速度（currentSpeedTrial），用于重算自定义档位的时间项</summary>
        public double PracticeScale;
        /// <summary>判定瞬间的**关卡难度**（0=宽松 1=标准 2=严格，见 <see cref="TierDifficulty"/>）。
        /// 必须逐条存下来：难度是全局运行时量、游戏不随存档恢复它，而玩家可以在局内切换
        /// （严格打一段 → 宽松打一段 → 再切回严格）。重放历史时只能按**记录自己的**难度选行，
        /// 否则会把宽松那一段按严格重算，计数就错了。老存档缺这一列 → 默认严格。
        /// 缺省值 2（严格）= 枚举里"最严"那一档，正好当默认。</summary>
        public int Difficulty;

        /// <summary>是否带几何数据（false = 老存档条目，只能按判定瞬间的快照统计）</summary>
        public bool HasGeometry { get { return PosPerDeg > 0.0001; } }

        /// <summary>
        /// 按**游戏真正记下的档位**校准强制 PP 标记（在 scrMarginTracker.AddHit 里调用）。
        /// 这是这套机制的地面真值：AddHit 的 HitMargin 就是最终进 hitMargins、被结果页统计的那个值。
        /// 只要它是 Perfect/Auto，而本记录的几何量却在完美窗口之外，就说明游戏把这次判定
        /// 按完美记账（中旋无限判定窗 / 旧式自动砖等）→ 补上 ForcePP。
        /// 只补不撤：已经判定为强制 PP 的保持原样。
        /// </summary>
        public void MergeGameGrade(HitMargin grade)
        {
            if (ForcePP || !HasGeometry) return;
            if (grade != HitMargin.Perfect && grade != HitMargin.Auto) return;
            if (Math.Abs(DeltaDeg) * PosPerDeg <= PpDeg * PosPerDeg) return; // 几何量本来就在窗口内 → 无需校准
            // 只改判定性质：Tier / P 由 CountRecord 按记录自带的难度统一派生，避免两处口径打架
            ForcePP = true;
        }
    }

    /// <summary>
    /// 与游戏同构的判定账本（唯一统计口径）：
    ///   · 追加：scrMarginTracker.AddHit(HitMargin) —— 游戏"记下这一判定"的入口，索引与 hitMargins 对齐
    ///   · 回档：scrMistakesManager.RevertToLastCheckpoint 之后，按游戏 hitMargins.Count 截断并整体重放
    ///   · 清零：scrMistakesManager.Reset（游戏"从头开始"才调用）
    ///   · 续关：ProgressStore 负责把每格数据落盘/读回
    ///   · 关卡外（主界面/选歌/编辑器搭关）不采集：JudgeHooks 用 GameState.InGameWorld 挡住
    ///
    /// **一切派生统计都由账本重放得出**（含平均颜色、7 档、自定义档位计数），
    /// 所以改档位定义 / 开关功能后只要 RebuildAll 就能得到"当前设置下的正确结论"。
    ///
    /// 两套统计范围：
    ///   · **全部有判定数据的判定**（含 PP 边界外的 EP/LP/VE/VL/Too）→ 平均判定颜色、平均绝对时间/角度偏差
    ///   · **仅原版完美窗口内**（InPure）→ 7 档计数器（F A B C D E G）、X^n 的 r
    ///   · 另外：**几何量在窗口外、但被游戏强制记成 PP 的判定**（中旋 midspin / autoplay，见 <see cref="HitRecord.ForcePP"/>）
    ///     两者都不属于 —— 它们只补进**自定义判定**的最严一档，好让自定义计数之和与游戏结果页的 PP 总数对得上
    /// </summary>
    public static class RainbowProgress
    {
        private static readonly List<HitRecord> _hits = new List<HitRecord>();
        private static HitRecord _pending;
        private static bool _pendingFresh;
        private static int _pendingFrame = -1;
        private static string _lastWarnKey;
        private static bool _anchorLogged; // 每次关卡只打印一次锚点表（诊断用）
        private static int _counted;       // 参与统计的条数（有判定数据）
        private static int _countedInPure; // 其中落在原版完美窗口内的条数（7 档计数器 / X^n 的来源）
        private static int _countedForcedPP; // 其中"几何量在窗口外、但被游戏强制记成 PP"的条数（中旋 / autoplay）

        public static int Count { get { return _hits.Count; } }
        /// <summary>参与统计的判定数（= RainbowState.Count 的来源）</summary>
        public static int Counted { get { return _counted; } }
        /// <summary>落在原版完美窗口内的判定数（= 7 档计数之和）</summary>
        public static int CountedInPure { get { return _countedInPure; } }
        /// <summary>被游戏强制记成 PP、但几何量在完美窗口外的判定数（中旋 midspin 为主）。
        /// 它们被补进自定义判定最严那一档，所以：
        /// <c>CountedInPure + CountedForcedPP == 游戏 GetHits(Perfect) + GetHits(Auto)</c>。</summary>
        public static int CountedForcedPP { get { return _countedForcedPP; } }

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

        /// <summary>追加一条并同步更新派生统计（热路径，不分配）</summary>
        public static void Append(HitRecord record)
        {
            _hits.Add(record);
            if (record.HasData)
            {
                if (!_anchorLogged && record.HasGeometry)
                {
                    _anchorLogged = true; // 本次关卡的锚点表 + 原版两个边界（默认档位下应与 v1.1.0 的锚点集一致）
                    Logger.Log(AnchorSet.Describe(GradientFrame.FromRecord(record))
                        + " | pp=" + record.PpDeg.ToString("F1") + "° pure=" + record.PureDeg.ToString("F1") + "°");
                }
                CountRecord(record);
            }
            CheckInvariant(false);
        }

        /// <summary>取记录自带的难度；越界（老存档缺列 / 脏数据）→ 严格（老存档的约定默认值）</summary>
        private static int RecordDifficulty(HitRecord r)
        {
            return (r.Difficulty >= 0 && r.Difficulty < TierDifficulty.Count)
                ? r.Difficulty : TierDifficulty.Strict;
        }

        /// <summary>
        /// 把一条有效判定计入派生统计（实时追加与全量重放共用同一条路径，保证口径一致）。
        /// 带几何数据的条目**按当前设置重算**（锚点色 / 7 档 / 自定义档位）；
        /// 老存档条目按判定瞬间的快照值统计（没有角度数据，角度偏差记 0）。
        /// </summary>
        private static void CountRecord(HitRecord r)
        {
            _counted++;

            double lambda, timeMs, absDeg;
            bool inPure;
            bool forcePP;
            int tier;
            double p;

            if (r.HasGeometry)
            {
                GradientFrame frame = GradientFrame.FromRecord(r);
                double scaled = r.DeltaDeg * r.PosPerDeg;
                double absScaled = Math.Abs(scaled);
                absDeg = Math.Abs(r.DeltaDeg);

                double perMs = r.PosPerDeg > 0.0001 ? r.BScale / r.PosPerDeg : 0.0;
                lambda = AnchorSet.WavelengthAt(absScaled, frame);
                timeMs = perMs > 0.0000001 ? absDeg / perMs : r.TimeMs;

                inPure = absScaled <= frame.PpScaled;
                forcePP = r.ForcePP;
                tier = inPure
                    ? RainbowMath.TierOf(absScaled, r.DeltaDeg < 0.0,
                        RainbowMath.FixedTierScaled(0, frame),
                        RainbowMath.FixedTierScaled(1, frame),
                        RainbowMath.FixedTierScaled(2, frame))
                    : -1;
                // 强制 PP 的判定：游戏按 Perfect 记账 → X^n 的 p 记 0（与 auto/正中同口径，n 才不会被推成负数）
                p = inPure && r.PpDeg > 0.0001 ? absDeg / r.PpDeg : 0.0;

                if (forcePP && !inPure)
                {
                    // 【游戏口径】中旋 / 旧式 autoplay 被强制记成 Perfect 的判定：
                    // 游戏在 X-Accuracy 里给它满分权重 1.0，所以平均判定与 X^n 也一律按**零误差**计入
                    //（三角偏差 / 时间偏差 / 波长全部按 0 算），否则平均颜色会被拉到红端、偏差被夸大。
                    // 判定文字与仪表盘 tick 仍按真实误差上色 —— 那是"这一下打得多飘"的即时反馈，不是统计口径。
                    lambda = Spectrum.MinWavelengthNm; // 0 误差 → 380nm
                    timeMs = 0.0;
                    absDeg = 0.0;
                }

                // 自定义判定：普通判定按几何量分档；强制 PP 走"游戏口径"，补进最严那一档。
                // 难度取**这条记录自己的**（不是当前 GCS.difficulty），否则局内切换难度后重放会算错。
                CustomCounter.Count(absScaled, r.DeltaDeg < 0.0, frame, RecordDifficulty(r), forcePP);
            }
            else
            {
                lambda = r.Lambda;
                timeMs = r.TimeMs;
                absDeg = 0.0;
                inPure = r.InPure;
                forcePP = false; // 老存档没有这一列 → 按旧口径（不补档）
                tier = r.InPure ? r.Tier : -1;
                p = r.P;
            }

            RainbowState.Add(lambda, timeMs, absDeg);       // 平均颜色 / 平均绝对时间偏差 / 平均绝对角度偏差：全部判定
            if (inPure && tier >= 0)
            {
                _countedInPure++;                            // 7 档计数器 / X^n 的 r：仅原版完美窗口内
                RainbowCounter.AddTier(tier, p);
            }
            else if (forcePP)
            {
                // 几何量在窗口外、但游戏记成 PP：不进原版 7 档计数器（那 7 档是"角度真的在窗内"的计数），
                // 但按游戏口径算一次完美中心参与 X^n 的 r；自定义判定那边已补进最严一档
                _countedForcedPP++;
                RainbowCounter.AddForcedPerfect();
            }
        }

        public static void Clear()
        {
            _hits.Clear();
            _pendingFresh = false;
            _pendingFrame = -1;
            _anchorLogged = false;
            _counted = 0;
            _countedInPure = 0;
            _countedForcedPP = 0;
            RainbowState.Reset();
            RainbowCounter.Reset();
            CustomCounter.ResetCounts();
            RefreshDisplay();
        }

        /// <summary>从列表整体重放全部统计（回档/续关/对齐/改档位后调用）</summary>
        public static void RebuildAll()
        {
            RainbowState.Reset();
            RainbowCounter.ResetCounts();
            CustomCounter.ResetCounts();
            _counted = 0;
            _countedInPure = 0;
            _countedForcedPP = 0;
            for (int i = 0; i < _hits.Count; i++)
            {
                HitRecord r = _hits[i];
                if (!r.HasData) continue;
                CountRecord(r);
            }
            RefreshDisplay();
            CheckInvariant(true);
        }

        /// <summary>设置变化后刷新实时显示（只重绘，不重算）</summary>
        public static void RefreshDisplay()
        {
            Logger.Guard("RainbowProgress/RefreshDisplay", delegate { LiveDisplay.Refresh(); });
        }

        /// <summary>档位定义变化：全量重算 + 刷新显示</summary>
        public static void OnLayoutChanged()
        {
            CustomJudge.InvalidateLayout();
            RebuildAll();
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

        /// <summary>不变量：
        ///   ① 账本长度 == 游戏 hitMargins.Count
        ///   ② 参与统计条数 == 游戏可判定条数（条数 − 故障类）
        ///   ③ 7 档之和 == InPure 条数
        ///   ④ InPure 条数 + **强制 PP** 条数 == 游戏**严格 Perfect**(+Auto)
        /// ③④ 专门盯「有没有把完美窗口之外的判定算进新增档位里」。
        /// 注意这里必须比 **Perfect**、不能比 Perfect+EarlyPerfect+LatePerfect：游戏的 EP/LP 是"命中时刻落在
        /// 完美时间窗内、但角度已超出 PP 边界"的近失判定（EP 可以是 42° 这种大角度），
        /// 而我们要的"完美"就是角度口径。
        /// ④ 的 "+强制 PP" 是 v1.2.1 加的：中旋（midspin）砖上
        /// <c>scrPlanet.SwitchChosen</c> 会把 midspinInfiniteMargin / 旧式自动砖的判定**直接改写成 HitMargin.Perfect**
        /// 再记账，于是"游戏 Perfect 总数"里混进了几何量在窗口外的判定（实测一局 2850 判定里 3 条）。
        /// 这种判定：计入自定义判定的最严一档与 X^n 的 r（p=0），并按零误差计入平均判定；
        /// 但**不进**原版 7 档计数器（那 7 档是"角度真的在窗内"的计数，③ 依赖它）。</summary>
        private static void CheckInvariant(bool verbose)
        {
            try
            {
                int gameCount = GameState.MarginCount;
                int gameCountable = GameState.CountableCount;
                int gamePerfect = GameState.PerfectCount;
                int tiers = RainbowCounter.TotalTiers();
                if (gameCount != _hits.Count || _counted != gameCountable
                    || tiers != _countedInPure || _countedInPure + _countedForcedPP != gamePerfect)
                {
                    string key = _hits.Count + "/" + gameCount + "/" + _counted + "/" + gameCountable
                        + "/" + _countedInPure + "/" + _countedForcedPP + "/" + gamePerfect + "/" + tiers;
                    if (key == _lastWarnKey) return; // 同一种不一致只报一次，避免刷屏
                    _lastWarnKey = key;
                    Logger.Warn("[RainbowProgress] 不变量不一致：条数 我们=" + _hits.Count + " 游戏=" + gameCount
                        + "；统计条数 我们=" + _counted + " 游戏(可判定)=" + gameCountable
                        + "；完美窗口内 我们=" + _countedInPure + "+强制PP=" + _countedForcedPP
                        + " 游戏(Perfect+Auto)=" + gamePerfect
                        + "；7档之和=" + tiers);
                }
                else
                {
                    _lastWarnKey = null;
                    if (verbose)
                        Logger.Log("[RainbowProgress] 一致：条数=" + gameCount + " 统计=" + _counted
                            + " 完美窗口内=" + _countedInPure + " 强制PP=" + _countedForcedPP
                            + " 7档之和=" + tiers);
                }
            }
            catch { }
        }
    }
}
