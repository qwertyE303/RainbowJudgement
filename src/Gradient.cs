using System;

namespace RainbowJudgement
{
    /// <summary>
    /// 一次判定（或一次 tick 绘制）所处的几何基准，全部换算到**仪表盘的 60 刻度**上
    /// （60 = 原版 Counted 边界；游戏里 tick 的角度、<c>scrHitErrorMeter.CalculateTickColor</c>
    /// 内部比较用的边界，都是这个坐标系）。所有锚点位置、判定位移都从这里出发，
    /// 所以 tick / 判定文字 / 平均判定颜色天然共用同一套坐标，不会再出现"度数 vs 刻度"的错位。
    /// </summary>
    public struct GradientFrame
    {
        /// <summary>每 1 度误差对应多少刻度 = 60 / Counted边界角</summary>
        public double PosPerDeg;
        /// <summary>自定义档位边界(刻度) = max(角度 × AScale, 最小时间ms × BScale)</summary>
        public double AScale;
        public double BScale;
        /// <summary>原版 Perfect（PP）边界(刻度) —— 常驻锚点</summary>
        public double PpScaled;
        /// <summary>原版 Pure（稍快/稍晚）边界(刻度) —— 常驻锚点</summary>
        public double PureScaled;
        /// <summary>练习速度（currentSpeedTrial）：自定义档位的"最小限制时间"要和原版一样除以它</summary>
        public double PracticeScale;

        public bool Valid { get { return PosPerDeg > 0.0001; } }

        /// <summary>判定瞬间的基准（GetHitMargin / CalculateTickColor 的参数就是这一组）</summary>
        public static GradientFrame FromRuntime(double countedDeg, double marginScale, double bpmTimesSpeed, double conductorPitch)
        {
            GradientFrame f = default(GradientFrame);
            if (double.IsNaN(countedDeg) || countedDeg <= 0.0001) countedDeg = RainbowMath.CountedScaled;
            f.PosPerDeg = RainbowMath.CountedScaled / countedDeg;
            f.AScale = marginScale * f.PosPerDeg;
            f.BScale = RainbowMath.DegPerMs(bpmTimesSpeed, conductorPitch) * f.PosPerDeg;
            f.PpScaled = RainbowMath.PerfectBoundaryDeg(bpmTimesSpeed, conductorPitch, marginScale) * f.PosPerDeg;
            f.PureScaled = RainbowMath.PureBoundaryDeg(bpmTimesSpeed, conductorPitch, marginScale) * f.PosPerDeg;
            f.PracticeScale = RainbowMath.PracticeSpeed();
            return f;
        }

        /// <summary>账本里的历史判定：用当时存下来的换算系数重建（与判定瞬间逐位一致）</summary>
        public static GradientFrame FromRecord(HitRecord r)
        {
            GradientFrame f = default(GradientFrame);
            f.PosPerDeg = r.PosPerDeg;
            f.AScale = r.AScale;
            f.BScale = r.BScale;
            f.PpScaled = r.PpDeg * r.PosPerDeg;
            f.PureScaled = r.PureDeg * r.PosPerDeg;
            f.PracticeScale = r.PracticeScale > 0.0001 ? r.PracticeScale : 1.0;
            return f;
        }
    }

    /// <summary>
    /// 渐变锚点集（**全 Mod 唯一的颜色曲线**）：
    ///   0 刻度 → 380nm；
    ///   每个启用的自定义档位 → 380 + 320 × 角度/60（位置 = 该档的实际边界刻度）；
    ///   原版 Perfect 边界 → 540nm、Pure 边界 → 620nm、Counted 边界(60) → 700nm（恒存在，删不掉）；
    ///   60 之外 → 700nm（原版 TooEarly/TooLate 段）。
    /// 锚点之间**在波长空间线性插值**（与 v1.1.0 同一套规则），最后由 Spectrum 转 RGB。
    /// 位置重合时（例如默认的 30° 档正好压在 PP 边界上）**常驻锚点优先**。
    ///
    /// 性能：tick 颜色是在 DrawStraightTick / DrawCurvedTick 里逐帧逐刻度调用的，
    /// 因此锚点表带缓存（键 = 几何基准 + 档位定义版本），命中时每次只做一次线性查找，不分配。
    /// </summary>
    public static class AnchorSet
    {
        private const int MaxAnchors = 64;
        private const double Epsilon = 1e-6;

        private static readonly double[] _pos = new double[MaxAnchors];
        private static readonly double[] _lam = new double[MaxAnchors];
        private static readonly int[] _pri = new int[MaxAnchors]; // 0 = 常驻（优先），1 = 自定义档位
        private static int _count;

        private static double _cacheA, _cacheB, _cacheP, _cacheQ, _cacheT;
        private static int _cacheRev = -1;
        private static int _cacheDiff = -1;
        private static bool _cacheValid;

        /// <summary>波长(nm) → 该位置的颜色</summary>
        public static double WavelengthAt(double pos, GradientFrame frame)
        {
            Ensure(frame);
            return Lookup(Math.Abs(pos));
        }

        /// <summary>角度(度) → 该档位的锚点波长（自定义档位取色规则；与原锚点色计算模式一致）</summary>
        public static double WavelengthOfDeg(double deg)
        {
            if (deg < 0.0) deg = 0.0;
            else if (deg > RainbowMath.CountedScaled) deg = RainbowMath.CountedScaled;
            return Spectrum.MinWavelengthNm
                + (RainbowMath.RedWavelengthNm - Spectrum.MinWavelengthNm) * deg / RainbowMath.CountedScaled;
        }

        /// <summary>诊断用：把当前锚点表打印成一行（每次关卡第一条判定时写一次日志）</summary>
        public static string Describe(GradientFrame frame)
        {
            Ensure(frame);
            System.Text.StringBuilder sb = new System.Text.StringBuilder(160);
            sb.Append("[AnchorTable]");
            for (int i = 0; i < _count; i++)
            {
                sb.Append(' ');
                sb.Append(_pos[i].ToString("F2"));
                sb.Append("→");
                sb.Append(_lam[i].ToString("F0"));
                if (_pri[i] == 0) sb.Append("*"); // * = 常驻锚点
            }
            return sb.ToString();
        }

        // ---------------- 建表 / 缓存 ----------------

        private static void Ensure(GradientFrame frame)
        {
            int rev = CustomJudge.LayoutRevision;
            // 难度必须进缓存键：高倍速下三种难度的 AScale/BScale/PpScaled/PureScaled 可能**完全相同**
            // （VE/VL 的时间项被 60° 角度项压住），只比那几个量的话换难度不会重建锚点表。
            int diff = CustomJudge.CurrentDifficulty();
            if (_cacheValid && _cacheRev == rev && _cacheDiff == diff
                && _cacheA == frame.AScale && _cacheB == frame.BScale
                && _cacheP == frame.PpScaled && _cacheQ == frame.PureScaled
                && _cacheT == frame.PracticeScale)
                return;

            _cacheA = frame.AScale;
            _cacheB = frame.BScale;
            _cacheP = frame.PpScaled;
            _cacheQ = frame.PureScaled;
            _cacheT = frame.PracticeScale;
            _cacheRev = rev;
            _cacheDiff = diff;
            _cacheValid = true;

            _count = 0;
            Push(0.0, Spectrum.MinWavelengthNm, 0);

            if (CustomJudge.Enabled)
            {
                int difficulty = CustomJudge.CurrentDifficulty();
                int n = CustomJudge.EnabledCount;
                for (int i = 0; i < n; i++)
                {
                    CustomTierDef tier = CustomJudge.EnabledAt(i);
                    if (tier == null) continue;
                    // 边界按**当前关卡难度**那一行算 → 自定义锚点也吃难度（严格/标准/宽松可以不同）
                    double pos = CustomJudge.BoundaryScaled(tier, frame, difficulty);
                    // 60 刻度之外画不到（仪表盘满刻度 = Counted 边界），不参与取色
                    if (pos <= 0.0 || pos >= RainbowMath.CountedScaled) continue;
                    Push(pos, WavelengthOfDeg(CustomJudge.AnchorDeg(tier, difficulty)), 1);
                }
            }

            Push(frame.PpScaled, WavelengthOfDeg(RainbowMath.PerfectNominalDeg), 0);   // 540nm
            Push(frame.PureScaled, WavelengthOfDeg(RainbowMath.PureNominalDeg), 0);    // 620nm
            Push(RainbowMath.CountedScaled, RainbowMath.RedWavelengthNm, 0);

            SortAndDedup();
        }

        private static void Push(double pos, double lambda, int priority)
        {
            if (_count >= MaxAnchors) return;
            _pos[_count] = pos;
            _lam[_count] = lambda;
            _pri[_count] = priority;
            _count++;
        }

        /// <summary>按位置升序（同位时常驻优先），并把重合的锚点合并成一个</summary>
        private static void SortAndDedup()
        {
            for (int i = 1; i < _count; i++)
            {
                double p = _pos[i], l = _lam[i];
                int pr = _pri[i];
                int j = i - 1;
                while (j >= 0 && (_pos[j] > p || (_pos[j] == p && _pri[j] > pr)))
                {
                    _pos[j + 1] = _pos[j];
                    _lam[j + 1] = _lam[j];
                    _pri[j + 1] = _pri[j];
                    j--;
                }
                _pos[j + 1] = p;
                _lam[j + 1] = l;
                _pri[j + 1] = pr;
            }

            int kept = 0;
            for (int i = 0; i < _count; i++)
            {
                if (kept > 0 && Math.Abs(_pos[i] - _pos[kept - 1]) <= Epsilon) continue;
                _pos[kept] = _pos[i];
                _lam[kept] = _lam[i];
                _pri[kept] = _pri[i];
                kept++;
            }
            _count = kept;
        }

        /// <summary>在缓存的锚点表里线性查找（pos 取绝对值后的刻度）</summary>
        private static double Lookup(double pos)
        {
            if (_count == 0) return Spectrum.MinWavelengthNm;
            if (pos <= _pos[0]) return _lam[0];
            if (pos >= _pos[_count - 1]) return _lam[_count - 1];

            for (int i = 0; i < _count - 1; i++)
            {
                if (pos > _pos[i + 1]) continue;
                double span = _pos[i + 1] - _pos[i];
                if (span <= Epsilon) return _lam[i + 1];
                double t = (pos - _pos[i]) / span;
                return _lam[i] + (_lam[i + 1] - _lam[i]) * t;
            }
            return _lam[_count - 1];
        }
    }
}
