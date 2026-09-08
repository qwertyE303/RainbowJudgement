using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 统一的资源定位（参考 XPerfect 的 EnsureSpritesLoaded）：
    /// 基准目录优先取"实际加载的 DLL 所在目录"（Assembly.GetExecutingAssembly().Location），
    /// 因此玩家的 Mods 文件夹叫 Mods / UMMMods / 其他任何名字都能找到；
    /// 再依次回退到 UMM 报告的路径、游戏根目录下的常见布局。
    /// 每个基准目录内先找 assets\ 子目录，再找该目录本身。
    /// </summary>
    public static class ModPaths
    {
        private static string _baseDir;

        /// <summary>首选基准目录（Mod 目录：图片等资源的回退位置）</summary>
        public static string BaseDir
        {
            get
            {
                if (_baseDir != null) return _baseDir;
                List<string> cands = BaseCandidates();
                _baseDir = cands.Count > 0 ? cands[0] : ".";
                return _baseDir;
            }
        }

        /// <summary>按候选顺序查找文件：基准目录×（assets\ → 根）×（文件名别名）；找不到返回 null</summary>
        public static string FindFile(params string[] names)
        {
            if (names == null || names.Length == 0) return null;
            foreach (string baseDir in BaseCandidates())
            {
                foreach (string sub in SubDirs)
                {
                    string dir = string.IsNullOrEmpty(sub) ? baseDir : Path.Combine(baseDir, sub);
                    foreach (string name in names)
                    {
                        try
                        {
                            string p = Path.Combine(dir, name);
                            if (File.Exists(p)) return p;
                        }
                        catch { }
                    }
                }
            }
            return null;
        }

        /// <summary>列出所有候选基准目录（去重、只保留存在的）</summary>
        public static List<string> BaseCandidates()
        {
            List<string> list = new List<string>();
            AddCandidate(list, AssemblyDir());
            AddCandidate(list, Main.ModPath);
            try
            {
                string gameRoot = Path.GetDirectoryName(Application.dataPath); // …\A Dance of Fire and Ice
                if (!string.IsNullOrEmpty(gameRoot))
                {
                    AddCandidate(list, Path.Combine(Path.Combine(gameRoot, "UMMMods"), "RainbowJudgement"));
                    AddCandidate(list, Path.Combine(Path.Combine(gameRoot, "Mods"), "RainbowJudgement"));
                }
            }
            catch { }
            AddCandidate(list, Directory.GetCurrentDirectory());
            return list;
        }

        private static readonly string[] SubDirs = new string[] { "assets", "" };

        private static void AddCandidate(List<string> list, string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return;
                string full = Path.GetFullPath(dir);
                if (!Directory.Exists(full)) return;
                foreach (string s in list)
                    if (string.Equals(s, full, StringComparison.OrdinalIgnoreCase)) return;
                list.Add(full);
            }
            catch { }
        }

        /// <summary>实际加载的 DLL 所在目录（XPerfect 同款做法）</summary>
        private static string AssemblyDir()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc)) return Path.GetDirectoryName(loc);
            }
            catch { }
            try
            {
                string loc = typeof(Main).Assembly.Location;
                if (!string.IsNullOrEmpty(loc)) return Path.GetDirectoryName(loc);
            }
            catch { }
            return null;
        }

        /// <summary>文件名别名：原名 + 去掉下划线的变体（Windows 下大小写不敏感，无需再列）</summary>
        public static string[] Aliases(string fileName)
        {
            try
            {
                string stem = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                string noUnderscore = stem.Replace("_", "");
                if (noUnderscore == stem) return new string[] { fileName };
                return new string[] { fileName, noUnderscore + ext };
            }
            catch { return new string[] { fileName }; }
        }
    }
}
