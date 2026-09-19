using System;

namespace RainbowJudgement
{
    /// <summary>
    /// 关卡内统计的聚合值：波长之和（平均判定颜色）+ 时间误差绝对值之和（平均绝对时间偏差）
    /// + 角度误差绝对值之和（平均绝对角度偏差）。
    /// 口径 = **所有有判定数据的判定**（含完美窗口外的 EP/LP/VE/VL/Too；auto 判定按 0 计入），
    /// 由 RainbowProgress 追加/全量重放驱动，本类不持有第二套口径。
    /// </summary>
    public static class RainbowState
    {
        private static double _sumWavelength;
        private static double _sumAbsTimeMs;
        private static double _sumAbsDeg;
        private static int _count;

        public static int Count { get { return _count; } }

        public static void Reset()
        {
            _sumWavelength = 0.0;
            _sumAbsTimeMs = 0.0;
            _sumAbsDeg = 0.0;
            _count = 0;
        }

        public static void Add(double wavelengthNm, double timeMs, double absDeg)
        {
            _sumWavelength += wavelengthNm;
            _sumAbsTimeMs += Math.Abs(timeMs);
            _sumAbsDeg += Math.Abs(absDeg);
            _count++;
        }

        /// <summary>平均判定波长(nm) → 平均判定颜色</summary>
        public static double AverageWavelength
        {
            get { return _count > 0 ? _sumWavelength / _count : 0.0; }
        }

        /// <summary>平均绝对时间偏差(ms)</summary>
        public static double AverageAbsTimeMs
        {
            get { return _count > 0 ? _sumAbsTimeMs / _count : 0.0; }
        }

        /// <summary>平均绝对角度偏差(度)</summary>
        public static double AverageAbsDeg
        {
            get { return _count > 0 ? _sumAbsDeg / _count : 0.0; }
        }
    }
}
