using System;
using System.Collections.Generic;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 一条自定义判定（UMM 设置序列化对象；字段名 = Settings.xml 的键名，改名会丢玩家设置）。
    /// 边界角 = max(判定角度 × marginScale, 最小限制时间 → 角度)，与原版判定同构。
    /// </summary>
    [Serializable]
    public class CustomTierDef
    {
        /// <summary>是否启用（禁用 = 不参与计数、不产生锚点、不显示数字）</summary>
        public bool Enabled = true;
        /// <summary>判定角度下限（度）；空输入按 0 处理</summary>
        public double Deg = 10.0;
        /// <summary>最小限制时间（ms）；空输入按 0 处理</summary>
        public double MinTimeMs = 7.5;
        /// <summary>是否使用自定义颜色（关闭 = 用该档"慢速下仪表盘上对应的颜色"）</summary>
        public bool UseCustomColor = true;
        /// <summary>颜色模式：false = 波长映射（nm），true = RGB（#RRGGBB）。二者互斥</summary>
        public bool RgbMode = false;
        /// <summary>波长模式的颜色值（nm）</summary>
        public double WavelengthNm = 400.0;
        /// <summary>RGB 模式的颜色值，玩家必须自带 '#'，否则无效（回退到锚点色）</summary>
        public string RgbHex = "#5FFF4E";
    }

    /// <summary>
    /// 自定义判定的档位定义与分档规则（纯逻辑，不碰 UI / 不碰账本）：
    ///   · 启用档位按 (角度, 最小时间) 升序成链 —— 索引 0 = 最严格那一档；
    ///   · 一条判定落入"第一个边界 ≥ |该判定位移刻度| 的档"，越界（比最宽档还宽）不计入；
    ///   · 索引 0 早/晚合并成 1 个数字，其余各出 early / late 两个；
    ///   · 自定义颜色**只影响计数数字**，锚点/tick/判定文字/平均色一律走波长（见 AnchorSet）。
    /// 任何档位定义变化都必须调用 <see cref="InvalidateLayout"/>，否则锚点表与排序缓存不会刷新。
    /// </summary>
    public static class CustomJudge
    {
        /// <summary>档位条数上限（数组 / UI / 计数显示按此预分配）</summary>
        public const int MaxTiers = 16;

        /// <summary>档位定义版本号：变化 → 锚点表与排序缓存失效</summary>
        public static int LayoutRevision { get; private set; }

        private static readonly int[] _order = new int[MaxTiers];
        private static int _orderRev = -1;
        private static int _orderCount;

        public static void InvalidateLayout()
        {
            LayoutRevision++;
            _orderRev = -1;
            _orderCount = 0;
        }

        // ---------------- 定义存取 ----------------

        public static List<CustomTierDef> Tiers
        {
            get
            {
                try { return Main.Settings != null ? Main.Settings.CustomTiers : null; }
                catch { return null; }
            }
        }

        /// <summary>实验功能总开关（关闭 = 不产生自定义锚点、不显示计数，但仍照常结算）</summary>
        public static bool Enabled
        {
            get
            {
                try { return Main.Settings != null && Main.Settings.EnableCustomJudge; }
                catch { return false; }
            }
        }

        /// <summary>启用档位条数（按规范顺序）</summary>
        public static int EnabledCount
        {
            get { EnsureOrder(); return _orderCount; }
        }

        /// <summary>规范顺序下的第 i 个启用档位（i = 0 最严格）</summary>
        public static CustomTierDef EnabledAt(int i)
        {
            EnsureOrder();
            if (i < 0 || i >= _orderCount) return null;
            List<CustomTierDef> list = Tiers;
            int index = _order[i];
            if (list == null || index < 0 || index >= list.Count) return null;
            return list[index];
        }

        /// <summary>规范顺序 = 启用档位按 (角度, 最小时间) 升序</summary>
        private static void EnsureOrder()
        {
            if (_orderRev == LayoutRevision) return;
            _orderRev = LayoutRevision;
            _orderCount = 0;

            List<CustomTierDef> list = Tiers;
            if (list == null || list.Count == 0) return;

            int[] temp = new int[list.Count];
            int n = 0;
            for (int i = 0; i < list.Count && n < MaxTiers; i++)
            {
                CustomTierDef t = list[i];
                if (t != null && t.Enabled) temp[n++] = i;
            }
            for (int i = 1; i < n; i++)
            {
                int key = temp[i];
                CustomTierDef kt = list[key];
                int j = i - 1;
                while (j >= 0 && Compare(list[temp[j]], kt) > 0)
                {
                    temp[j + 1] = temp[j];
                    j--;
                }
                temp[j + 1] = key;
            }
            for (int i = 0; i < n; i++) _order[i] = temp[i]; // _order 容量 = MaxTiers
            _orderCount = n;
        }

        private static int Compare(CustomTierDef a, CustomTierDef b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            if (a.Deg < b.Deg) return -1;
            if (a.Deg > b.Deg) return 1;
            if (a.MinTimeMs < b.MinTimeMs) return -1;
            if (a.MinTimeMs > b.MinTimeMs) return 1;
            return 0;
        }

        // ---------------- 边界 / 分档 ----------------

        /// <summary>该档边界（60 刻度）= max(角度 × 每度刻度, 最小时间ms / 练习速度 × 每毫秒刻度)。
        /// 练习速度只**放宽**不**紧缩**：速度 ≤ 100% 时时间除以它（窗口变宽），> 100% 时保持玩家填的值
        /// —— 自定义的"最小限制时间"本身就是下限。</summary>
        public static double BoundaryScaled(CustomTierDef tier, GradientFrame frame)
        {
            if (tier == null) return 0.0;
            double practice = frame.PracticeScale > 0.0001 ? frame.PracticeScale : 1.0;
            if (practice > 1.0) practice = 1.0;
            double timeTerm = tier.MinTimeMs / practice * frame.BScale;
            return Math.Max(tier.Deg * frame.AScale, timeTerm);
        }

        /// <summary>分档：返回规范顺序索引（0 最严格），越界返回 -1</summary>
        public static int Assign(double absScaled, GradientFrame frame)
        {
            EnsureOrder();
            for (int i = 0; i < _orderCount; i++)
            {
                CustomTierDef tier = EnabledAt(i);
                if (tier == null) continue;
                if (absScaled <= BoundaryScaled(tier, frame)) return i;
            }
            return -1;
        }

        // ---------------- 计数数字的颜色（自定义颜色只作用在这里） ----------------

        /// <summary>该档计数字符的颜色：自定义颜色（波长 / RGB）优先，否则用"慢速下仪表盘上对应的颜色"。
        /// 波长模式走 <see cref="Spectrum.InputWavelengthToRgb"/>：可见区外会变暗，0nm / 空值 = 纯黑。</summary>
        public static Color32 DigitColor(CustomTierDef tier)
        {
            if (tier == null) return Spectrum.WavelengthToRgb(Spectrum.MinWavelengthNm);
            if (tier.UseCustomColor)
            {
                if (tier.RgbMode) return Spectrum.ParseHex(tier.RgbHex, AnchorColor(tier));
                return Spectrum.InputWavelengthToRgb(tier.WavelengthNm);
            }
            return AnchorColor(tier);
        }

        public static string DigitHex(CustomTierDef tier)
        {
            return Spectrum.ToHex(DigitColor(tier));
        }

        /// <summary>该档"慢速下仪表盘上对应的颜色"（= 该档锚点色，与 AnchorSet 同一公式）</summary>
        public static Color32 AnchorColor(CustomTierDef tier)
        {
            return Spectrum.WavelengthToRgb(AnchorSet.WavelengthOfDeg(tier == null ? 0.0 : tier.Deg));
        }

        /// <summary>RGB 输入是否有效（必须玩家自己带 '#'，6 位十六进制）</summary>
        public static bool IsValidHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return false;
            for (int i = 1; i < 7; i++)
            {
                char c = hex[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }

        // ---------------- 默认值 / 编辑操作 ----------------

        /// <summary>出厂四条 = 原来的计数系统（400 / 440 / 480nm + 原版 PP 绿）。
        /// 波长档的 RGB 栏同步写成"该波长对应的 RGB"（方便玩家切到 RGB 模式时接着改）；
        /// 原版 PP 绿不对应任何波长 → 波长栏**留空**（值为 NaN，GUI 显示空框）。</summary>
        public static List<CustomTierDef> DefaultTiers()
        {
            List<CustomTierDef> list = new List<CustomTierDef>(4);
            list.Add(Make(true, 10.0, 7.5, true, false, 400.0, HexOfWavelength(400.0)));
            list.Add(Make(true, 15.0, 12.5, true, false, 440.0, HexOfWavelength(440.0)));
            list.Add(Make(true, 20.0, 16.67, true, false, 480.0, HexOfWavelength(480.0)));
            list.Add(Make(true, 30.0, 25.0, true, true, double.NaN, "#5FFF4E"));
            return list;
        }

        /// <summary>波长 → "#RRGGBB"（经全局颜色映射，与 tick / 数字取色同一套）</summary>
        private static string HexOfWavelength(double nm)
        {
            return "#" + Spectrum.ToHex(Spectrum.WavelengthToRgb(nm));
        }

        private static CustomTierDef Make(bool enabled, double deg, double ms, bool customColor, bool rgb, double nm, string hex)
        {
            CustomTierDef t = new CustomTierDef();
            t.Enabled = enabled;
            t.Deg = deg;
            t.MinTimeMs = ms;
            t.UseCustomColor = customColor;
            t.RgbMode = rgb;
            t.WavelengthNm = nm;
            t.RgbHex = hex;
            return t;
        }

        /// <summary>首次运行 / 设置里还没有该字段：写入出厂四条</summary>
        public static void EnsureDefaults()
        {
            try
            {
                if (Main.Settings == null) return;
                if (Main.Settings.CustomTiersInitialized)
                {
                    if (Main.Settings.CustomTiers == null) Main.Settings.CustomTiers = new List<CustomTierDef>();
                    return;
                }
                Main.Settings.CustomTiers = DefaultTiers();
                Main.Settings.CustomTiersInitialized = true;
                InvalidateLayout();
                Logger.Banner("[CustomJudge] 已写入出厂四条自定义判定（400/440/480nm + 原版PP绿）");
            }
            catch (Exception ex) { Logger.Warn("[CustomJudge] 初始化默认档位失败: " + ex.Message); }
        }

        /// <summary>New：在最后一条之外再加一档（角度 +10°，最小时间 +5ms，颜色跟随锚点色）</summary>
        public static void AddTier()
        {
            List<CustomTierDef> list = Tiers;
            if (list == null) return;
            if (list.Count >= MaxTiers) return;

            double deg = 10.0, ms = 7.5;
            CustomTierDef last = null;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] != null) { last = list[i]; break; }
            if (last != null)
            {
                deg = last.Deg + 10.0;
                ms = last.MinTimeMs + 5.0;
            }
            CustomTierDef t = new CustomTierDef();
            t.Enabled = true;
            t.Deg = deg;
            t.MinTimeMs = ms;
            t.UseCustomColor = false;               // 关闭自定义颜色 → 用仪表盘锚点色
            t.RgbMode = false;
            t.WavelengthNm = AnchorSet.WavelengthOfDeg(deg);
            t.RgbHex = "#5FFF4E";
            list.Add(t);
            InvalidateLayout();
        }

        /// <summary>Sort：把全部条目按 (角度, 最小时间) 升序重排（最严格在最前）</summary>
        public static void SortTiers()
        {
            List<CustomTierDef> list = Tiers;
            if (list == null || list.Count < 2) return;
            for (int i = 1; i < list.Count; i++)
            {
                CustomTierDef key = list[i];
                int j = i - 1;
                while (j >= 0 && Compare(list[j], key) > 0)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = key;
            }
            InvalidateLayout();
        }

        /// <summary>Reset：把已有条目恢复成出厂四条（多出来的条目直接删除）</summary>
        public static void ResetTiers()
        {
            try
            {
                if (Main.Settings == null) return;
                Main.Settings.CustomTiers = DefaultTiers();
                Main.Settings.CustomTiersInitialized = true;
                InvalidateLayout();
            }
            catch (Exception ex) { Logger.Warn("[CustomJudge] 重置档位失败: " + ex.Message); }
        }

        public static void RemoveAt(int index)
        {
            List<CustomTierDef> list = Tiers;
            if (list == null || index < 0 || index >= list.Count) return;
            list.RemoveAt(index);
            InvalidateLayout();
        }
    }

    /// <summary>
    /// 自定义判定的计数（纯数据，按规范顺序索引）：索引 0 = 最严格档（早/晚合并），其余早/晚分开。
    /// 由 RainbowProgress 在追加与全量重放时驱动，所以它永远等于"当前档位定义下账本的结论"。
    /// </summary>
    public static class CustomCounter
    {
        public static readonly int[] Early = new int[CustomJudge.MaxTiers];
        public static readonly int[] Late = new int[CustomJudge.MaxTiers];
        /// <summary>比最宽档还宽、没有落进任何档的判定数（X^n 取色要用：非 0 表示"没有哪一档能包含全部判定"）</summary>
        public static int OutOfRange { get; private set; }

        public static void ResetCounts()
        {
            for (int i = 0; i < CustomJudge.MaxTiers; i++) { Early[i] = 0; Late[i] = 0; }
            OutOfRange = 0;
        }

        /// <summary>按当前档位定义给一次判定分档并累加（越界不计入任何档，只记 OutOfRange）</summary>
        public static void Count(double absScaled, bool isEarly, GradientFrame frame)
        {
            int index = CustomJudge.Assign(absScaled, frame);
            if (index < 0)
            {
                OutOfRange++;
                return;
            }
            if (index >= CustomJudge.MaxTiers) return;
            if (isEarly) Early[index]++;
            else Late[index]++;
        }

        /// <summary>第 i 档显示的数字数（0 档早/晚合并成 1 个）</summary>
        public static int Total(int index)
        {
            if (index < 0 || index >= CustomJudge.MaxTiers) return 0;
            return Early[index] + Late[index];
        }

        /// <summary>
        /// 规范顺序里"有计数的最宽档"下标（X^n 取色用）= 含全部判定的那一档：
        /// 判定按链式分档，所以最差的一条落在哪一档，就是"所有判定都在这一档里"的那一档。
        /// 返回 -1 表示没有可用档位（功能关闭 / 一条计数都没有），调用方回退到原版 4 档取色。
        /// </summary>
        public static int WidestUsedIndex()
        {
            if (!CustomJudge.Enabled) return -1;
            int n = CustomJudge.EnabledCount;
            for (int i = n - 1; i >= 0; i--)
            {
                if (i >= CustomJudge.MaxTiers) continue;
                if (Early[i] + Late[i] > 0) return i;
            }
            return -1;
        }
    }
}
