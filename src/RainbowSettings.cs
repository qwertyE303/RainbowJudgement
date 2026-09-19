using System.Collections.Generic;
using UnityModManagerNet;

namespace RainbowJudgement
{
    /// <summary>UMM 设置（字段名即 Settings.xml 的键名，改名会丢玩家设置，请勿随意改动）</summary>
    public class RainbowSettings : UnityModManager.ModSettings
    {
        /// <summary>一级：彩虹判定总开关（关闭后完全恢复原版）</summary>
        public bool EnableRainbow = true;
        /// <summary>设置界面语言：0 = 未初始化（按游戏语言预选），1 中文 / 2 English / 3 日本語 / 4 한국어（见 Lang）</summary>
        public int GuiLanguage = 0;

        // ---------------- 二级：显示判定详情 ----------------

        /// <summary>二级：显示判定详情（结尾页 + 关卡内实时）</summary>
        public bool ShowJudgeDetails = true;
        /// <summary>三级：结尾页显示平均绝对时间偏差(ms)</summary>
        public bool ShowAverageTime = true;
        /// <summary>三级：结尾页显示平均判定颜色</summary>
        public bool ShowAverageColor = true;
        /// <summary>三级：结尾页显示平均绝对角度偏差(°)</summary>
        public bool ShowAverageAngle = true;

        // ---------------- 三级：关卡内实时详情（三项各自开关 / 字号 / 位置 / 对齐 / 文本） ----------------
        // 对齐：0 = 左（左端固定、向右生长），1 = 居中，2 = 右（右端固定、向左生长）
        // 文本：显示内容的前缀，**原样输出、不跟随语言**（默认在首次启动时按当时的语言写入"名称＋："）

        public bool ShowLiveColor = false;
        public int LiveColorFontSize = 44;
        public int LiveColorX = 0;
        public int LiveColorY = 200;
        public int LiveColorAlign = 1;
        public string LiveColorText = "";

        public bool ShowLiveTime = false;
        public int LiveTimeFontSize = 44;
        public int LiveTimeX = 0;
        public int LiveTimeY = 150;
        public int LiveTimeAlign = 1;
        public string LiveTimeText = "";

        public bool ShowLiveAngle = false;
        public int LiveAngleFontSize = 44;
        public int LiveAngleX = 0;
        public int LiveAngleY = 100;
        public int LiveAngleAlign = 1;
        public string LiveAngleText = "";

        /// <summary>实时文本前缀是否已按语言写入过（区分"首次运行"与"玩家清空"）</summary>
        public bool LiveTextInitialized = false;

        // ---------------- 实验性：自定义判定 ----------------

        /// <summary>实验功能总开关：关闭 = 不产生自定义锚点、不显示计数（统计照常结算）</summary>
        public bool EnableCustomJudge = true;
        /// <summary>显示判定计数（关卡内 2n−1 个数字）</summary>
        public bool ShowCustomCount = false;
        public int CustomCountFontSize = 44;
        public int CustomCountX = 0;
        public int CustomCountY = 260;
        /// <summary>计数数字间距（空格数）</summary>
        public int CustomCountSpacing = 1;
        /// <summary>结尾页显示计数详情（整行）</summary>
        public bool ShowCustomCountInResults = true;
        /// <summary>出厂四条是否已写入（用于区分"首次运行"与"玩家把条目删光"）</summary>
        public bool CustomTiersInitialized = false;
        /// <summary>自定义判定条目（出厂 = 10/15/20/30° 四条，见 CustomJudge.DefaultTiers）</summary>
        public List<CustomTierDef> CustomTiers = new List<CustomTierDef>();

        /// <summary>调试日志</summary>
        public bool DebugLog = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        /// <summary>
        /// 首次运行（设置里还没有这些字段）时，按**当前界面语言**把实时显示的前缀文本写一次：
        /// 写完之后它就是玩家自己的字符串，永远不跟随语言变化（玩家可以随便改，清空也可以）。
        /// 调用时机刻意放在"真正要用到文本"的地方（设置页/关卡内显示），确保此时游戏语言已经就绪。
        /// </summary>
        public void EnsureLiveTexts()
        {
            if (LiveTextInitialized) return;
            LiveColorText = Lang.T("liveColor") + "：";
            LiveTimeText = Lang.T("avgTime") + "：";
            LiveAngleText = Lang.T("avgAngle") + "：";
            LiveTextInitialized = true;
        }
    }
}
