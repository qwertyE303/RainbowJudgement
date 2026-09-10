using System;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 可见光谱波长 &lt;-&gt; RGB 转换工具（**全局唯一的颜色映射**：tick 颜色 / 判定文字颜色 / 结果页平均判定色块 /
    /// 档位锚点色 / X^n 颜色 / 仪表盘纹理，全部走这里，所以改这一处 = 全部同步）。
    /// 亮度补偿（v1.0.3）：380nm 紫端 factor=0.45 → 700nm 纯红 factor=1.0，用一条全局光滑的幂曲线；
    /// 另外蓝紫段做一次光滑"变浅"（向白混合），让紫端从"死黑"变成"浅亮的紫罗兰"。
    /// 因为三个通道乘的是同一个 factor、加的是同一个 k，色相/饱和度比例变化是光滑且可导的。
    /// </summary>
    public static class Spectrum
    {
        public const double MinWavelengthNm = 380.0;   // 可见光谱最短波长（紫端）
        public const double MaxWavelengthNm = 780.0;   // 可见光谱最长波长（红）

        /// <summary>渐变跨度：380nm(紫端) → 700nm(纯红) 的映射区间</summary>
        private const double GradientSpanNm = 320.0;

        /// <summary>紫端(380nm)亮度下限。0.30 = 旧版（深紫看不清）；0.45 = 蓝紫提亮档；可调到 0.50~0.60 更醒目</summary>
        private const double VioletFloor = 0.45;
        /// <summary>幂曲线指数。越大 → 提亮越集中在蓝紫端、中段越少动（0.55 = 均衡；0.85 = 只提蓝紫，红黄基本不动）</summary>
        private const double BrightnessGamma = 0.85;
        /// <summary>蓝紫"变浅"强度（向白混合，让紫端更"浅亮"而不是"死黑"）。
        /// 权重从 380nm 的 1 用 smoothstep 衰减到 540nm 的 0 —— 所以 540nm 之后（绿/黄/橙/红）一个字节都不动。</summary>
        private const double VioletDesat = 0.15;

        /// <summary>
        /// 波长(nm) -&gt; RGB。**全 Mod 唯一的颜色映射**（tick / 判定文字 / 平均判定色块 / 档位锚点色 /
        /// X^n 颜色 / 仪表盘纹理，全部经此函数）。色相走 Dan Bruton 分段线性近似；
        /// 亮度走全局光滑幂曲线；蓝紫段额外做一次光滑"变浅"。
        /// 三个步骤都是三元组上的连续映射，所以整条色带连续可导（无台阶、无跳变）。
        /// </summary>
        public static Color32 WavelengthToRgb(double lambdaNm)
        {
            lambdaNm = Math.Max(MinWavelengthNm, Math.Min(MaxWavelengthNm, lambdaNm));
            double r = 0, g = 0, b = 0;
            if (lambdaNm >= 380 && lambdaNm < 440) { r = (440 - lambdaNm) / 60.0; g = 0; b = 1; }
            else if (lambdaNm < 490) { r = 0; g = (lambdaNm - 440) / 50.0; b = 1; }
            else if (lambdaNm < 510) { r = 0; g = 1; b = (510 - lambdaNm) / 20.0; }
            else if (lambdaNm < 580) { r = (lambdaNm - 510) / 70.0; g = 1; b = 0; }
            else if (lambdaNm < 645) { r = 1; g = (645 - lambdaNm) / 65.0; b = 0; }
            else { r = 1; g = 0; b = 0; }

            // ① 亮度：factor(λ) = VioletFloor + (1-VioletFloor) × u^γ，u = (λ-380)/320
            //    · 端点精确：λ=380 → 0.45（紫端提亮 1.5 倍）；λ=700 → 1.0（红端=纯红 255,0,0）
            //    · 单调、C∞ 光滑，且在 645nm 处天然连续（旧版那条 0.76375 分段接缝因此消失）
            //    · 三通道同乘一个 factor → 色相与饱和度比例不变，只是整体亮了
            double u = (lambdaNm - MinWavelengthNm) / GradientSpanNm;
            if (u < 0.0) u = 0.0; else if (u > 1.0) u = 1.0;
            double factor = VioletFloor + (1.0 - VioletFloor) * Math.Pow(u, BrightnessGamma);
            r *= factor; g *= factor; b *= factor;

            // ② 蓝紫"变浅"：向白混合。权重 w(λ) = smoothstep(1 - u/0.5)，380nm 处 1 → 540nm 处 0
            //    变浅同时把亮度抬高一大截（中心 0.128 → 0.259），且 540nm 之后完全不受影响
            double v = 1.0 - u / 0.5;
            if (v < 0.0) v = 0.0; else if (v > 1.0) v = 1.0;
            double k = VioletDesat * (v * v * (3.0 - 2.0 * v));
            r += (1.0 - r) * k;
            g += (1.0 - g) * k;
            b += (1.0 - b) * k;

            return new Color32(
                (byte)Math.Round(255.0 * r),
                (byte)Math.Round(255.0 * g),
                (byte)Math.Round(255.0 * b),
                255);
        }

        public static string ToHex(Color32 c)
        {
            return string.Format("{0:X2}{1:X2}{2:X2}", c.r, c.g, c.b);
        }

        /// <summary>解析 "RRGGBB"（可带 #），失败返回 fallback</summary>
        public static Color32 ParseHex(string hex, Color32 fallback)
        {
            try
            {
                if (!string.IsNullOrEmpty(hex))
                {
                    if (hex[0] == '#') hex = hex.Substring(1);
                    if (hex.Length == 6)
                    {
                        byte r = (byte)Convert.ToInt32(hex.Substring(0, 2), 16);
                        byte g = (byte)Convert.ToInt32(hex.Substring(2, 2), 16);
                        byte b = (byte)Convert.ToInt32(hex.Substring(4, 2), 16);
                        return new Color32(r, g, b, 255);
                    }
                }
            }
            catch { }
            return fallback;
        }
    }
}
