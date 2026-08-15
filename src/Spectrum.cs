using System;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 可见光谱波长 <-> RGB 转换工具。
    /// 亮度修正使 380nm=深紫(77,0,77)、700nm=纯红(255,0,0)，
    /// 且 645nm 处分段亮度因子连续（避免 R 值跳变）。
    /// </summary>
    public static class Spectrum
    {
        public const double MinWavelengthNm = 380.0;   // 可见光谱最短波长（深紫）
        public const double MaxWavelengthNm = 780.0;   // 可见光谱最长波长（红）

        /// <summary>
        /// 波长(nm) -> RGB。Dan Bruton 分段线性近似 + 亮度修正：
        /// 380nm=深紫；645nm 处亮度因子 0.76375 连续过渡到 700nm 的 1.0（纯红）。
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
            // 亮度因子：在 645nm 处连续（0.76375），700nm 起为 1.0（纯红 255,0,0）
            double factor;
            if (lambdaNm <= 645.0)
                factor = 0.3 + 0.7 * (lambdaNm - 380) / 400.0;
            else if (lambdaNm <= 700.0)
                factor = 0.76375 + 0.23625 * (lambdaNm - 645.0) / 55.0;
            else
                factor = 1.0;
            return new Color32(
                (byte)Math.Round(255.0 * r * factor),
                (byte)Math.Round(255.0 * g * factor),
                (byte)Math.Round(255.0 * b * factor),
                255);
        }

        public static string ToHex(Color32 c)
        {
            return string.Format("{0:X2}{1:X2}{2:X2}", c.r, c.g, c.b);
        }
    }
}

