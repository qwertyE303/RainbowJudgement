namespace RainbowJudgement
{
    /// <summary>
    /// 最近一次判定的瞬时数据（判定文字颜色 / 判定文字定位用）。
    /// 由 JudgeHooks.GetMarginHook 在每次判定时写入，VisualHooks 读取；
    /// 只描述"当前这一次判定"，不参与累计统计。
    /// </summary>
    public static class LastJudge
    {
        /// <summary>本次判定是否 auto 触发（官方 autoplay / 自动砖块）——auto 强制按完美中心统计</summary>
        public static bool AutoActive;
        /// <summary>本次判定的位移（60 刻度绝对值），与游戏 tick 刻度同源</summary>
        public static double ScaledPos;
        /// <summary>本次判定的几何基准（tick / 判定文字 / 平均色三者共用同一套锚点坐标）</summary>
        public static GradientFrame Frame;

        public static void Clear()
        {
            AutoActive = false;
            ScaledPos = 0.0;
            Frame = default(GradientFrame);
        }
    }
}
