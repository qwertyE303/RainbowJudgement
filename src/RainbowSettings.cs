using UnityModManagerNet;

namespace RainbowJudgement
{
    /// <summary>UMM 设置（字段名即 Settings.xml 的键名，改名会丢玩家设置，请勿改动）</summary>
    public class RainbowSettings : UnityModManager.ModSettings
    {
        /// <summary>一级：彩虹判定总开关（关闭后完全恢复原版）</summary>
        public bool EnableRainbow = true;
        /// <summary>二级：结果页显示「平均判定」</summary>
        public bool ShowAverageJudgment = true;
        /// <summary>三级：平均判定里显示平均绝对偏差(ms)</summary>
        public bool ShowAverageTime = true;
        /// <summary>三级：平均判定里显示平均判定颜色</summary>
        public bool ShowAverageColor = true;
        /// <summary>二级：显示右下角彩虹计数器（7 个数字）</summary>
        public bool ShowRainbowCounter = false;
        /// <summary>三级：计数器字号</summary>
        public int CounterFontSize = 44;
        /// <summary>三级：计数器 X 位置</summary>
        public int CounterX = 0;
        /// <summary>三级：计数器 Y 位置</summary>
        public int CounterY = 220;
        /// <summary>三级：计数器数字间距（空格数）</summary>
        public int CounterSpacing = 1;
        /// <summary>调试日志</summary>
        public bool DebugLog = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
