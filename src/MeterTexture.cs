using System;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 仪表盘纹理（**运行期只加载用户图片，兜底用游戏原版**）：
    ///   · 找到 assets\straight_meter.png / curved_meter.png（或 Mod 根目录）→ 用用户的图
    ///   · 找不到 / 加载失败 → 返回 null，由 MeterVisualPatch 保持游戏原版 sprite 不动（等于没装这个 Mod 的仪表盘）
    /// **本文件不含任何程序化绘图代码**：代码生成只存在于调试工具
    /// `_analysis\meterpreview\Program.cs`（那是独立工具，不参与游戏编译与运行），
    /// 它的几何常量与颜色公式必须与用户图片保持一致，供出图/对比用。
    /// 目标图片规格：400x200、RGBA、彩色带在视觉坐标 y=94..105（横条）/ 半径 145..157、角度 ±64°（弧形），
    /// 两侧与 61°~64° 为映射下的红端色；细节以用户资产为准。
    /// 生成结果缓存（每张只加载一次）；**不主动销毁**，避免还原时 sprite 悬空。
    /// </summary>
    public static class MeterTexture
    {
        private static Sprite _straight;
        private static bool _straightTried;
        private static Sprite _curved;
        private static bool _curvedTried;

        /// <summary>用户图片：横条。找不到返回 null（调用方应保持游戏原版）</summary>
        public static Sprite Straight()
        {
            if (!_straightTried)
            {
                _straightTried = true;
                _straight = LoadCustom("straight_meter.png");
            }
            return _straight;
        }

        /// <summary>用户图片：弧形。找不到返回 null（调用方应保持游戏原版）</summary>
        public static Sprite Curved()
        {
            if (!_curvedTried)
            {
                _curvedTried = true;
                _curved = LoadCustom("curved_meter.png");
            }
            return _curved;
        }

        // ---------------- 用户自定义图片 ----------------

        /// <summary>加载用户 PNG（400x200，RGBA）。查找方式见 ModPaths：基准目录=实际加载的 DLL 目录，目录内 assets\ 优先。</summary>
        private static Sprite LoadCustom(string fileName)
        {
            Texture2D texture = null;
            try
            {
                string path = ModPaths.FindFile(ModPaths.Aliases(fileName));
                if (path == null)
                {
                    // 不再程序化生成 —— 交给游戏原版仪表盘
                    Logger.Log("[MeterVisualPatch] 未找到自定义图片 " + fileName + "（基准目录：" + ModPaths.BaseDir + "）→ 使用游戏原版仪表盘");
                    return null;
                }
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                if (texture.LoadImage(bytes))
                {
                    Logger.Log("[MeterVisualPatch] 已加载自定义仪表盘图片: " + path);
                    return MakeSprite(texture);
                }
                UnityEngine.Object.Destroy(texture);
                Logger.Log("[MeterVisualPatch] 图片解码失败 " + path + " → 使用游戏原版仪表盘");
                return null;
            }
            catch (Exception ex)
            {
                if (texture != null) { try { UnityEngine.Object.Destroy(texture); } catch { } }
                Logger.Log("[MeterVisualPatch] 加载自定义图片失败（" + ex.Message + "）→ 使用游戏原版仪表盘");
                return null;
            }
        }

        private static Sprite MakeSprite(Texture2D texture)
        {
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
