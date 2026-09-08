namespace RainbowJudgement
{
    /// <summary>
    /// 最近一次判定的瞬时数据（判定文字颜色 / auto 检测用）。
    /// 由 JudgeHooks.GetMarginHook 在每次判定时写入，VisualHooks 读取；
    /// 只描述"当前这一次判定"，不参与累计统计。
    /// </summary>
    public static class LastJudge
    {
        /// <summary>本次判定是否 auto 触发（官方 autoplay / 自动砖块）——auto 强制按完美中心统计</summary>
        public static bool AutoActive;
        /// <summary>原始角度误差(度，auto 强制 0)，与游戏 tick 刻度同源</summary>
        public static double RawDeg;
        /// <summary>游戏同款缩放刻度(0~60，含 marginScale)</summary>
        public static double ScaledPos;
        /// <summary>判定瞬间 Counted 边界角(含 marginScale)</summary>
        public static double CountedDeg;
        /// <summary>判定瞬间 bpm×speed</summary>
        public static double BpmTimesSpeed;
        /// <summary>判定瞬间 pitch</summary>
        public static double Pitch = 1.0;
        /// <summary>判定瞬间 marginScale</summary>
        public static double MarginScale = 1.0;

        public static void Clear()
        {
            AutoActive = false;
            RawDeg = 0.0;
            ScaledPos = 0.0;
            CountedDeg = 0.0;
            BpmTimesSpeed = 0.0;
            Pitch = 1.0;
            MarginScale = 1.0;
        }
    }
}
