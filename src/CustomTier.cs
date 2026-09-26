using System;
using System.Collections.Generic;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 自定义判定的一行（一个难度下的三个参数）。
    /// 边界角 = max(判定角度 × marginScale, max(最小限制时间, 判定时间 / 练习速度) → 角度)，
    /// 与原版 <c>scrMisc.GetAdjustedAngleBoundaryInDeg</c> 完全同构：
    ///   · 角度项与时间项都随倍速（bpm × 砖块速度 × pitch）变化；
    ///   · **最小限制时间不随倍速变**，它就是时间项的下限；
    ///   · `判定时间 / 练习速度` 是原版的时间基准（PP=20ms、EP/LP=30ms、VE/VL=40/65/91ms），
    ///     练习速度 < 1 时窗口变宽；高倍速下它会被最小限制时间顶住 —— 这正是原版
    ///     严格/标准/宽松在高倍速下"合流"的成因（那个难度差异只写在 VE/VL 的 40/65/91 里）。
    /// 空输入（角度/判定时间/最小限制时间 全为 0）= 该行未填写。
    /// </summary>
    [Serializable]
    public class TierRow
    {
        /// <summary>判定角度下限（度）</summary>
        public double Deg;
        /// <summary>判定时间（ms）：原版的 20 / 30 / 40|65|91</summary>
        public double JudgeTimeMs;
        /// <summary>最小限制时间（ms）：时间项的下限，不随倍速变</summary>
        public double MinTimeMs;

        public TierRow() { }

        public TierRow(double deg, double judgeTimeMs, double minTimeMs)
        {
            Deg = deg;
            JudgeTimeMs = judgeTimeMs;
            MinTimeMs = minTimeMs;
        }

        public TierRow Copy()
        {
            return new TierRow(Deg, JudgeTimeMs, MinTimeMs);
        }

        /// <summary>三个参数都为 0 或负数 = 未填写（New 出来的空白行）</summary>
        public bool IsBlank
        {
            get { return Deg <= 0.0 && JudgeTimeMs <= 0.0 && MinTimeMs <= 0.0; }
        }
    }

    /// <summary>难度行下标（与游戏 <c>Difficulty</c> 的数值一致：0=宽松 1=标准 2=严格）</summary>
    public static class TierDifficulty
    {
        public const int Lenient = 0;
        public const int Normal = 1;
        public const int Strict = 2;
        public const int Count = 3;

        public static string LabelKey(int index)
        {
            return index == Lenient ? "diffLenient" : index == Normal ? "diffNormal" : "diffStrict";
        }
    }

    /// <summary>
    /// 一条自定义判定（UMM 设置序列化对象；字段名 = Settings.xml 的键名，改名会丢玩家设置）。
    /// **每个难度一行**（严格 / 标准 / 宽松），三行的 判定角度 / 判定时间 / 最小限制时间 完全独立；
    /// 颜色（波长 / RGB）三档共享，只作用在计数数字上。
    /// 旧版本（只有单份 Deg/MinTimeMs）读进来时由 <see cref="NormalizeLegacy"/> 铺到三行。
    /// </summary>
    [Serializable]
    public class CustomTierDef
    {
        /// <summary>是否启用（禁用 = 不参与计数、不产生锚点、不显示数字）</summary>
        public bool Enabled = true;

        // ---------------- 新格式：每个难度一行（严格 / 标准 / 宽松） ----------------

        /// <summary>严格难度行</summary>
        public TierRow Strict = new TierRow();
        /// <summary>标准难度行</summary>
        public TierRow Normal = new TierRow();
        /// <summary>宽松难度行</summary>
        public TierRow Lenient = new TierRow();

        // ---------------- 老格式（v1.2.x）：升级后仅作兼容读取，不再写入 ----------------

        /// <summary>【老字段】判定角度（度）：升级后铺到三行，保留只为读旧设置</summary>
        public double Deg;
        /// <summary>【老字段】最小限制时间（ms）：升级后铺到三行</summary>
        public double MinTimeMs;

        // ---------------- 颜色（三档共享） ----------------

        /// <summary>是否使用自定义颜色（关闭 = 用该档"慢速下仪表盘上对应的颜色"）</summary>
        public bool UseCustomColor = true;
        /// <summary>颜色模式：false = 波长映射（nm），true = RGB（#RRGGBB）。二者互斥</summary>
        public bool RgbMode = false;
        /// <summary>波长模式的颜色值（nm）</summary>
        public double WavelengthNm = 400.0;
        /// <summary>RGB 模式的颜色值，玩家必须自带 '#'，否则无效（回退到锚点色）</summary>
        public string RgbHex = "#5FFF4E";

        /// <summary>取某个难度的行（越界回退到严格行），永不返回 null</summary>
        public TierRow Row(int difficulty)
        {
            TierRow row = difficulty == TierDifficulty.Lenient ? Lenient
                : difficulty == TierDifficulty.Normal ? Normal : Strict;
            if (row != null) return row;
            row = new TierRow();
            if (difficulty == TierDifficulty.Lenient) Lenient = row;
            else if (difficulty == TierDifficulty.Normal) Normal = row;
            else Strict = row;
            return row;
        }

        /// <summary>三个难度行是否全部为空（New 出来的空白档位 → 不参与分档 / 不产生锚点）</summary>
        public bool IsBlank
        {
            get
            {
                return (Strict == null || Strict.IsBlank)
                    && (Normal == null || Normal.IsBlank)
                    && (Lenient == null || Lenient.IsBlank);
            }
        }

        /// <summary>
        /// 旧设置升级：三行都是空白 + 老字段有值 → 用老字段铺满三行（等价于"三档同值"，
        /// 保持 v1.2.x 的手感不变）。对已经是新格式的条目无副作用。
        /// </summary>
        public void NormalizeLegacy()
        {
            if (Strict == null) Strict = new TierRow();
            if (Normal == null) Normal = new TierRow();
            if (Lenient == null) Lenient = new TierRow();
            if (!Strict.IsBlank || !Normal.IsBlank || !Lenient.IsBlank) return;
            if (Deg <= 0.0 && MinTimeMs <= 0.0) return;

            // 老格式只有 角度 + 最小限制时间：判定时间留 0 → 时间项 = 最小限制时间，与老行为逐位一致
            Strict = new TierRow(Deg, 0.0, MinTimeMs);
            Normal = Strict.Copy();
            Lenient = Strict.Copy();
        }
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

        /// <summary>规范顺序 = 启用档位按**严格难度**那一行的 (判定角度, 判定时间, 最小限制时间) 升序。
        /// 标准 / 宽松两行不参与排序（档位链只有一条，分档时再按当前难度取该行的值算边界）。</summary>
        private static int Compare(CustomTierDef a, CustomTierDef b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            TierRow ra = a.Row(TierDifficulty.Strict);
            TierRow rb = b.Row(TierDifficulty.Strict);
            if (ra.Deg < rb.Deg) return -1;
            if (ra.Deg > rb.Deg) return 1;
            if (ra.JudgeTimeMs < rb.JudgeTimeMs) return -1;
            if (ra.JudgeTimeMs > rb.JudgeTimeMs) return 1;
            if (ra.MinTimeMs < rb.MinTimeMs) return -1;
            if (ra.MinTimeMs > rb.MinTimeMs) return 1;
            return 0;
        }

        // ---------------- 边界 / 分档 ----------------

        /// <summary>该档边界（60 刻度）= max(判定角度 × 每度刻度, 有效时间ms × 每毫秒刻度)，
        /// 其中 **有效时间 = max(最小限制时间, 判定时间 / 练习速度)** —— 与原版逐位同构：
        ///   · 判定时间**除以练习速度**（练习速度越低窗口越宽，这就是原版 `t / currentSpeedTrial`）；
        ///   · 最小限制时间是**除法之后**的下限、不随倍速变（对应原版那个无条件的 25ms）。
        /// 这里**刻意不做任何钳制**：钳 `practice` 会同时把"低速放宽"和"玩家填的小下限"一起掐死，
        /// 而这两件事本来就分别由"除以 practice"和"下限比较"各管一件。
        /// 难度三行由 <paramref name="difficulty"/> 选，所以**锚点与分档都吃关卡难度**。</summary>
        public static double BoundaryScaled(CustomTierDef tier, GradientFrame frame, int difficulty)
        {
            if (tier == null) return 0.0;
            TierRow row = tier.Row(difficulty);

            // 只兜"读不到 / 0"：练习速度不会是负数，但真拿到 0 时不能让除法把 NaN 漏进判定
            double practice = frame.PracticeScale > 0.0001 ? frame.PracticeScale : 1.0;

            double scaled = row.JudgeTimeMs / practice;            // 判定时间 / 练习速度
            if (scaled < row.MinTimeMs) scaled = row.MinTimeMs;    // 最小限制时间 = 下限（不随倍速变）

            double timeTerm = scaled * frame.BScale;
            double angleTerm = row.Deg * frame.AScale;
            return angleTerm > timeTerm ? angleTerm : timeTerm;
        }

        /// <summary>分档：返回规范顺序索引（0 最严格），越界返回 -1。
        /// 边界按 <paramref name="difficulty"/> 那一行算。</summary>
        public static int Assign(double absScaled, GradientFrame frame, int difficulty)
        {
            EnsureOrder();
            for (int i = 0; i < _orderCount; i++)
            {
                CustomTierDef tier = EnabledAt(i);
                if (tier == null) continue;
                if (absScaled <= BoundaryScaled(tier, frame, difficulty)) return i;
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

        /// <summary>该档"慢速下仪表盘上对应的颜色"（= 该档锚点色，与 AnchorSet 同一公式）。
        /// 颜色三档共享，但锚点位置/取色按**当前难度那一行**的角度走（缺省严格）。</summary>
        public static Color32 AnchorColor(CustomTierDef tier)
        {
            return Spectrum.WavelengthToRgb(AnchorSet.WavelengthOfDeg(AnchorDeg(tier, CurrentDifficulty())));
        }

        public static Color32 AnchorColor(CustomTierDef tier, int difficulty)
        {
            return Spectrum.WavelengthToRgb(AnchorSet.WavelengthOfDeg(AnchorDeg(tier, difficulty)));
        }

        /// <summary>该档在某个难度下的锚点角度（= 该行填的判定角度）</summary>
        public static double AnchorDeg(CustomTierDef tier, int difficulty)
        {
            return tier == null ? 0.0 : tier.Row(difficulty).Deg;
        }

        /// <summary>当前关卡难度（读游戏全局量；读不到按严格）</summary>
        public static int CurrentDifficulty()
        {
            try
            {
                int value = (int)GCS.difficulty;
                return (value >= 0 && value < TierDifficulty.Count) ? value : TierDifficulty.Strict;
            }
            catch { return TierDifficulty.Strict; }
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

        /// <summary>出厂四条（三档同值）：
        ///   10 / 7.5 / 7.5 @400nm、15 / 12.5 / 12.5 @440nm、20 / 16.67 / 16.67 @480nm
        ///   —— 判定时间 = 最小限制时间，等价于 v1.2.x 的老行为（1.0 倍速下逐位一致）；
        ///   30 / 20 / 25 @原版PP绿 —— 与原版完美判定（PP）完全同构，三档难度也都一致。
        /// 波长档的 RGB 栏同步写成"该波长对应的 RGB"（方便玩家切到 RGB 模式时接着改）；
        /// 原版 PP 绿不对应任何波长 → 波长栏**留空**（值为 NaN，GUI 显示空框）。</summary>
        public static List<CustomTierDef> DefaultTiers()
        {
            List<CustomTierDef> list = new List<CustomTierDef>(4);
            list.Add(Make(true, 10.0, 7.5, 7.5, true, false, 400.0, HexOfWavelength(400.0)));
            list.Add(Make(true, 15.0, 12.5, 12.5, true, false, 440.0, HexOfWavelength(440.0)));
            list.Add(Make(true, 20.0, 16.67, 16.67, true, false, 480.0, HexOfWavelength(480.0)));
            list.Add(Make(true, 30.0, 20.0, 25.0, true, true, double.NaN, "#5FFF4E"));
            return list;
        }

        /// <summary>波长 → "#RRGGBB"（经全局颜色映射，与 tick / 数字取色同一套）</summary>
        private static string HexOfWavelength(double nm)
        {
            return "#" + Spectrum.ToHex(Spectrum.WavelengthToRgb(nm));
        }

        /// <summary>造一条"三档同值"的档位</summary>
        private static CustomTierDef Make(bool enabled, double deg, double judgeMs, double minMs,
            bool customColor, bool rgb, double nm, string hex)
        {
            CustomTierDef t = new CustomTierDef();
            t.Enabled = enabled;
            t.Strict = new TierRow(deg, judgeMs, minMs);
            t.Normal = t.Strict.Copy();
            t.Lenient = t.Strict.Copy();
            t.UseCustomColor = customColor;
            t.RgbMode = rgb;
            t.WavelengthNm = nm;
            t.RgbHex = hex;
            return t;
        }

        /// <summary>首次运行 / 设置里还没有该字段：写入出厂四条；老设置则把单份 Deg/MinTimeMs 铺到三行</summary>
        public static void EnsureDefaults()
        {
            try
            {
                if (Main.Settings == null) return;
                if (Main.Settings.CustomTiersInitialized)
                {
                    if (Main.Settings.CustomTiers == null) Main.Settings.CustomTiers = new List<CustomTierDef>();
                    NormalizeAll();
                    return;
                }
                Main.Settings.CustomTiers = DefaultTiers();
                Main.Settings.CustomTiersInitialized = true;
                InvalidateLayout();
                Logger.Banner("[CustomJudge] 已写入出厂四条自定义判定（10/15/20 判定时间=最小限制时间 + 30/20/25 原版PP，三档同值）");
            }
            catch (Exception ex) { Logger.Warn("[CustomJudge] 初始化默认档位失败: " + ex.Message); }
        }

        /// <summary>把列表里所有老格式条目升级成"三行同值"（幂等：新格式条目不受影响）</summary>
        public static void NormalizeAll()
        {
            List<CustomTierDef> list = Tiers;
            if (list == null) return;
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                CustomTierDef t = list[i];
                if (t == null) continue;
                bool blankBefore = t.IsBlank;
                t.NormalizeLegacy();
                if (blankBefore != t.IsBlank) changed = true;
            }
            if (changed) InvalidateLayout();
        }

        /// <summary>New：新增一档，**三个难度行都是空白**（等玩家自己填；空白档位不参与分档/锚点/计数）</summary>
        public static void AddTier()
        {
            List<CustomTierDef> list = Tiers;
            if (list == null) return;
            if (list.Count >= MaxTiers) return;

            CustomTierDef t = new CustomTierDef();
            t.Enabled = true;
            t.Strict = new TierRow();
            t.Normal = new TierRow();
            t.Lenient = new TierRow();
            t.UseCustomColor = false;               // 关闭自定义颜色 → 用仪表盘锚点色
            t.RgbMode = false;
            t.WavelengthNm = AnchorSet.WavelengthOfDeg(0.0);
            t.RgbHex = "#5FFF4E";
            list.Add(t);
            InvalidateLayout();
        }

        /// <summary>Sort：把全部条目按**严格难度那一行**的 (判定角度, 判定时间, 最小限制时间) 升序重排
        /// （最严格在最前）。标准 / 宽松两行不参与排序。</summary>
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
    /// 特例：被游戏**强制记成 PP** 的判定（中旋 midspin / autoplay，见 <see cref="HitRecord.ForcePP"/>）
    /// 不看几何量、直接补进最严一档 —— 与游戏结果页的 PP 总数对齐。
    /// </summary>
    public static class CustomCounter
    {
        public static readonly int[] Early = new int[CustomJudge.MaxTiers];
        public static readonly int[] Late = new int[CustomJudge.MaxTiers];
        /// <summary>比最宽档还宽、**按几何量**没落进任何档的判定数（X^n 取色要用：非 0 表示"没有哪一档能包含全部判定"）。
        /// 被游戏强制记成 PP 的判定不算在内 —— 它们按游戏口径补进了最严一档（见 <see cref="ForcedPP"/>）。</summary>
        public static int OutOfRange { get; private set; }
        /// <summary>被游戏**强制记成 PP**（中旋 midspin / 旧式 autoplay）而补进最严一档的判定数。
        /// 已经计入 Early[0] / Late[0]，这里只是单独留个可读的账（供日志对账，页面上不显示）。
        /// 注意它**不是** OutOfRange 的一部分：这些判定按游戏口径算进了最严档，没有被丢弃。</summary>
        public static int ForcedPP { get; private set; }

        public static void ResetCounts()
        {
            for (int i = 0; i < CustomJudge.MaxTiers; i++) { Early[i] = 0; Late[i] = 0; }
            OutOfRange = 0;
            ForcedPP = 0;
        }

        /// <summary>按当前档位定义给一次判定分档并累加（越界不计入任何档，只记 OutOfRange）。
        /// 边界取 <paramref name="difficulty"/> 那一行（= 该判定发生时的关卡难度）。
        /// <paramref name="isForcedPP"/> = 游戏把这个判定强制记成了 PP（角度误差其实在完美窗口之外）：
        /// 此时**不看几何量**，直接补进**最严那一档**（规范顺序索引 0，早/晚按 delta 符号分），
        /// 与游戏的记账口径一致 —— 这样"自定义判定各档之和 == 结果页 PP 总数"才成立。</summary>
        public static void Count(double absScaled, bool isEarly, GradientFrame frame, int difficulty, bool isForcedPP)
        {
            if (isForcedPP)
            {
                // 一条"填过内容"的启用档位都没有 → 没有最严那一档可补，只能记越界（照旧不显示）
                if (CustomJudge.EnabledCount <= 0) { OutOfRange++; return; }
                ForcedPP++;
                if (isEarly) Early[0]++;
                else Late[0]++;
                return;
            }

            int index = CustomJudge.Assign(absScaled, frame, difficulty);
            if (index < 0)
            {
                OutOfRange++;
                return;
            }
            if (index >= CustomJudge.MaxTiers) return;
            if (isEarly) Early[index]++;
            else Late[index]++;
        }

        public static void Count(double absScaled, bool isEarly, GradientFrame frame, bool isForcedPP = false)
        {
            Count(absScaled, isEarly, frame, TierDifficulty.Strict, isForcedPP);
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
