using System;

namespace RainbowJudgement
{
    /// <summary>
    /// 关卡内统计的聚合值：波长之和（平均判定颜色）+ 误差时间绝对值之和（平均绝对偏差）。
    /// 仅 Perfect/Auto 判定计入，由 RainbowProgress 追加/重放驱动，不存在第二套口径。
    /// </summary>
    public static class RainbowState
    {
        private static double _sumWavelength;
        private static double _sumAbsTimeMs;
        private static int _count;

        public static int Count { get { return _count; } }

        public static void Reset()
        {
            _sumWavelength = 0;
            _sumAbsTimeMs = 0;
            _count = 0;
        }

        public static void Add(double wavelengthNm, double timeMs)
        {
            _sumWavelength += wavelengthNm;
            _sumAbsTimeMs += Math.Abs(timeMs); // 平均绝对偏差：对每次判定时间误差的绝对值平均（权重1）
            _count++;
        }

        /// <summary>平均判定波长(nm)</summary>
        public static double AverageWavelength
        {
            get { return _count > 0 ? _sumWavelength / _count : 0.0; }
        }

        /// <summary>平均绝对偏差(ms)</summary>
        public static double AverageAbsTimeMs
        {
            get { return _count > 0 ? _sumAbsTimeMs / _count : 0.0; }
        }
    }
}
