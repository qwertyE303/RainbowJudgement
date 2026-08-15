using System;

namespace RainbowJudgement
{
    /// <summary>
    /// 关卡内判定统计：波长（平均判定颜色）+ 误差时间（平均绝对偏差）。
    /// 仅 Perfect 判定计入（GetMarginHook 非 Perfect 分支不调用 Add）；回档由 JudgementHistory.RebuildAll 重放。
    /// </summary>
    public static class RainbowState
    {
        private static double _sum;
        private static double _timeSum;
        private static int _count;

        public static double Sum { get { return _sum; } }
        public static int Count { get { return _count; } }

        public static void Reset()
        {
            _sum = 0;
            _timeSum = 0;
            _count = 0;
        }

        public static void Add(double wavelengthNm, double timeMs)
        {
            _sum += wavelengthNm;
            _timeSum += Math.Abs(timeMs); // 平均绝对偏差：对每次判定时间误差的绝对值平均（权重1）
            _count++;
        }

        public static double Average
        {
            get { return _count > 0 ? _sum / _count : 0.0; }
        }

        public static double AverageTimeMs
        {
            get { return _count > 0 ? _timeSum / _count : 0.0; }
        }

    }

    /// <summary>统一判定历史：记录每次判定的 seqID 与统计数据；回档（Start_Rewind）时按目标 seqID 截断并整体重算
    /// （参考原版 scrMistakesManager 按 seqID 存储判定、Truncate 撤销的实现）。</summary>
    public static class JudgementHistory
    {
        public struct Entry
        {
            public int Seq;
            public double Lambda;
            public double TimeMs;
            public bool IsPerfect;
            public int Group; // 0=Purple 1=CyanEarly 2=CyanLate 3=BlueEarly 4=BlueLate 5=GreenEarly 6=GreenLate
            public double P;  // 归一化完美度（仅 Perfect 有效）
        }

        private static readonly System.Collections.Generic.List<Entry> _entries = new System.Collections.Generic.List<Entry>();
        // seq → 该 seq 首次出现的列表位置（判定目标 seq 由按键时 floor 提供，会滞后；用"首次位置"截断可绕开滞后误差）
        private static readonly System.Collections.Generic.Dictionary<int, int> _seqFirst = new System.Collections.Generic.Dictionary<int, int>();

        public static void Add(int seq, double lambda, double timeMs, bool isPerfect, int group, double p)
        {
            Entry e;
            e.Seq = seq;
            e.Lambda = lambda;
            e.TimeMs = timeMs;
            e.IsPerfect = isPerfect;
            e.Group = group;
            e.P = p;
            // 记录 seq 首次出现位置（Truncate 用）；不做去重（滞后 seq 去重会误删正常判定）
            if (!_seqFirst.ContainsKey(seq)) _seqFirst[seq] = _entries.Count;
            _entries.Add(e);
        }

        /// <summary>回档：按"目标 seq 首次出现的判定位置"截断（存档点格判定及其后判定——含滞后记录——全部撤销，只保留存档点前的判定）。返回是否发生了截断。</summary>
        public static bool Truncate(int targetSeq)
        {
            int cut = _entries.Count;
            int pos;
            if (_seqFirst.TryGetValue(targetSeq, out pos))
                cut = pos; // 存档点格判定（首次出现）也属回档前的尝试，一并撤销（保留 pos 之前的判定）
            else
            {
                // 目标 seq 从未判定过：删最后一个 seq < targetSeq 的判定之后
                for (int i = 0; i < _entries.Count; i++)
                    if (_entries[i].Seq < targetSeq) cut = i + 1;
            }
            if (cut >= _entries.Count) return false;
            int removed = _entries.Count - cut;
            _entries.RemoveRange(cut, removed);
            RebuildAll();
            Logger.Log("[RainbowJudgement] Truncate target=" + targetSeq + " 删除=" + removed + " 剩余=" + _entries.Count);
            return true;
        }

        public static void Clear()
        {
            _entries.Clear();
            _seqFirst.Clear();
        }

        private static void RebuildAll()
        {
            try
            {
                _seqFirst.Clear();
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (!_seqFirst.ContainsKey(_entries[i].Seq)) _seqFirst[_entries[i].Seq] = i;
                }
                RainbowState.Reset();
                RainbowCounter.ResetCounts();
                for (int i = 0; i < _entries.Count; i++)
                {
                    Entry e = _entries[i];
                    RainbowState.Add(e.Lambda, e.TimeMs);
                    if (e.IsPerfect)
                        RainbowCounter.AddGroupRaw(e.Group, e.P);
                }
                RainbowCounter.Refresh();
                Logger.Log("[RainbowJudgement] Rebuild 判定数=" + RainbowState.Count
                    + " Purple=" + RainbowCounter.Purple
                    + " CyanE=" + RainbowCounter.CyanEarly + " CyanL=" + RainbowCounter.CyanLate
                    + " BlueE=" + RainbowCounter.BlueEarly + " BlueL=" + RainbowCounter.BlueLate
                    + " GreenE=" + RainbowCounter.GreenEarly + " GreenL=" + RainbowCounter.GreenLate
                    + " 历史=" + _entries.Count);
            }
            catch (Exception ex)
            {
                Logger.Warn("[RainbowJudgement] RebuildAll 异常: " + ex.Message);
            }
        }
    }
}
