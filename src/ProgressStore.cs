using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RainbowJudgement
{
    /// <summary>
    /// 续关持久化：游戏存档（<c>User\data.sav</c>）里只有 HitMargin 枚举（没有角度/时间），
    /// 所以"每格判定数据"必须由本 Mod 自己存一份。
    ///
    /// 存储方式刻意与官方对齐：
    ///   · 位置：<c>&lt;游戏目录&gt;\User\rainbow_judgement.sav</c>（与 data.sav / custom_data.sav 同目录）
    ///   · 格式：JSON，且用游戏自己的 <c>GDMiniJSON.Json</c>（PlayerPrefsJson 写 .sav 用的同一个序列化器）
    ///   · 备份：写入前把上一份留成 <c>.sav.old</c>（与官方一致）
    ///   · 时机：跟游戏的 savedProgress 同步写入/读取（SaveCheckpointProgress / LoadCheckpointProgress）
    ///   · 校验：读档用「关卡名 + 游戏判定列表指纹」，避免把上一次尝试的数据套到这次上
    /// 若 User 目录不可写，自动回退到 Mod 目录下的同名文件。
    /// </summary>
    public static class ProgressStore
    {
        private const string FileName = "rainbow_judgement.sav";
        private const int FormatVersion = 1;

        /// <summary>每格判定的字段顺序（写进文件头，方便人工查看）。
        /// 注意：第 2 列 "isPerfect" 是历史列名，v1.0.2 起写的是「是否落在原版完美窗口内」(InPure)——
        /// 保持列序号不变，老存档仍可读（老档里该列为"游戏判定==Perfect/Auto"，语义最接近，一并按 InPure 处理）；
        /// 老档中完美窗口外的那些条目会少计一次 F~G，再存一次档即精确。</summary>
        private static readonly string[] Fields = { "hasData", "isPerfect", "isAuto", "tier", "lambda", "timeMs", "p" };

        // ---------------- 路径 ----------------

        /// <summary>首选：与官方存档同目录（&lt;游戏目录&gt;\User）</summary>
        private static string PrimaryPath
        {
            get
            {
                try
                {
                    string gameRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
                    if (!string.IsNullOrEmpty(gameRoot))
                    {
                        string user = Path.Combine(gameRoot, "User");
                        if (!Directory.Exists(user)) Directory.CreateDirectory(user);
                        return Path.Combine(user, FileName);
                    }
                }
                catch { }
                return null;
            }
        }

        /// <summary>回退：Mod 目录</summary>
        private static string FallbackPath
        {
            get { return Path.Combine(ModPaths.BaseDir, FileName); }
        }

        private static List<string> CandidatePaths()
        {
            List<string> paths = new List<string>(2);
            string primary = PrimaryPath;
            if (!string.IsNullOrEmpty(primary)) paths.Add(primary);
            paths.Add(FallbackPath);
            return paths;
        }

        // ---------------- 写 ----------------

        public static void Save()
        {
            try
            {
                int gameCount = GameState.MarginCount;
                if (gameCount != RainbowProgress.Count)
                {
                    // 与游戏条数不一致（例如刚开关过 Mod）→ 不写，避免把错位数据落盘
                    Logger.Warn("[RainbowProgress] 保存跳过：条数不一致 我们=" + RainbowProgress.Count + " 游戏=" + gameCount);
                    return;
                }

                List<HitRecord> hits = RainbowProgress.Snapshot();
                string json = Serialize(hits);

                foreach (string path in CandidatePaths())
                {
                    try
                    {
                        string folder = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);

                        // 与官方一致：写入前把上一份留成 .old
                        if (File.Exists(path))
                        {
                            try { File.Copy(path, path + ".old", true); }
                            catch { }
                        }
                        File.WriteAllText(path, json, new UTF8Encoding(false));
                        Logger.Log("[RainbowProgress] 已保存进度：" + hits.Count + " 条 → " + path);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("[RainbowProgress] 写入失败 " + path + "：" + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[RainbowProgress] 保存进度失败: " + ex.Message);
            }
        }

        private static string Serialize(List<HitRecord> hits)
        {
            List<object> rows = new List<object>(hits.Count);
            for (int i = 0; i < hits.Count; i++)
            {
                HitRecord r = hits[i];
                List<object> row = new List<object>(Fields.Length);
                row.Add(r.HasData ? 1 : 0);
                row.Add(r.InPure ? 1 : 0);   // 列名仍为 isPerfect（保持落盘格式/列序不变）
                row.Add(r.IsAuto ? 1 : 0);
                row.Add(r.Tier);
                row.Add(r.Lambda);
                row.Add(r.TimeMs);
                row.Add(r.P);
                rows.Add(row);
            }

            Dictionary<string, object> root = new Dictionary<string, object>();
            root["version"] = FormatVersion;
            root["level"] = GameState.LevelName;
            root["fingerprint"] = Fingerprint();
            root["checkpointNum"] = GameState.CheckpointNum;
            root["count"] = hits.Count;
            root["fields"] = new List<object>(Fields);
            root["hits"] = rows;

            return GDMiniJSON.Json.Serialize(root);
        }

        // ---------------- 读 ----------------

        /// <summary>续关：把自有数据与游戏刚恢复的 hitMargins 对齐（前缀 + 占位补齐）</summary>
        public static void LoadAndAlign()
        {
            try
            {
                int gameCount = GameState.MarginCount;
                string gameFingerprint = Fingerprint();
                string level = GameState.LevelName;

                string savedLevel, savedFingerprint;
                List<HitRecord> loaded = Read(out savedLevel, out savedFingerprint);

                if (loaded == null)
                {
                    RainbowProgress.ReplaceAll(null, gameCount);
                    Logger.Warn("[RainbowProgress] 续关：无本 Mod 的进度文件（游戏 " + gameCount + " 条）→ 占位补齐");
                    return;
                }
                if (savedLevel != level)
                {
                    RainbowProgress.ReplaceAll(null, gameCount);
                    Logger.Warn("[RainbowProgress] 续关：进度文件关卡不匹配（" + savedLevel + "/" + level + "）→ 占位补齐");
                    return;
                }
                if (savedFingerprint != gameFingerprint)
                {
                    // 文件与游戏存档不同步（例如上次没走退出菜单就关了游戏）→ 仍按前缀对齐：
                    // 存档点之前的判定顺序完全一致，而且随后 RevertToLastCheckpoint 会把存档点之后的部分丢掉，
                    // 所以这样能保住绝大部分数据，比整份丢弃（计数全 0）好得多。
                    RainbowProgress.ReplaceAll(loaded, gameCount);
                    Logger.Warn("[RainbowProgress] 续关：指纹不一致（文件 " + savedFingerprint + " / 游戏 " + gameFingerprint
                        + "）→ 按前缀对齐 " + Math.Min(loaded.Count, gameCount) + " 条");
                    return;
                }
                if (loaded.Count != gameCount)
                    Logger.Warn("[RainbowProgress] 续关：条数不一致 文件=" + loaded.Count + " 游戏=" + gameCount + " → 取前 "
                        + Math.Min(loaded.Count, gameCount) + " 条");
                else
                    Logger.Log("[RainbowProgress] 续关：已恢复 " + loaded.Count + " 条判定数据");

                RainbowProgress.ReplaceAll(loaded, gameCount);
            }
            catch (Exception ex)
            {
                Logger.Warn("[RainbowProgress] 读档失败: " + ex.Message);
            }
        }

        private static List<HitRecord> Read(out string level, out string fingerprint)
        {
            level = "";
            fingerprint = "";

            List<string> paths = CandidatePaths();
            for (int i = 0; i < paths.Count; i++)
            {
                if (!File.Exists(paths[i])) continue;
                List<HitRecord> list = ReadJson(paths[i], out level, out fingerprint);
                if (list != null) return list;
            }
            return null;
        }

        private static List<HitRecord> ReadJson(string path, out string level, out string fingerprint)
        {
            level = "";
            fingerprint = "";
            try
            {
                Dictionary<string, object> root = GDMiniJSON.Json.Deserialize(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                if (root == null) return null;
                if (ToInt(Get(root, "version")) != FormatVersion) return null;

                level = ToString(Get(root, "level"));
                fingerprint = ToString(Get(root, "fingerprint"));

                List<object> rows = Get(root, "hits") as List<object>;
                if (rows == null) return null;

                List<HitRecord> list = new List<HitRecord>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    List<object> row = rows[i] as List<object>;
                    if (row == null || row.Count < Fields.Length) continue;

                    HitRecord r = default(HitRecord);
                    r.HasData = ToInt(row[0]) != 0;
                    r.InPure = ToInt(row[1]) != 0;   // 列名 isPerfect，v1.0.2 起语义 = InPure
                    r.IsPerfect = r.InPure;
                    r.IsAuto = ToInt(row[2]) != 0;
                    r.Tier = ToInt(row[3]);
                    r.Lambda = ToDouble(row[4]);
                    r.TimeMs = ToDouble(row[5]);
                    r.P = ToDouble(row[6]);
                    list.Add(r);
                }
                return list;
            }
            catch (Exception ex)
            {
                Logger.Warn("[RainbowProgress] 读取 " + path + " 失败: " + ex.Message);
                return null;
            }
        }

        // ---------------- 删除 ----------------

        public static void DeleteFile()
        {
            bool deleted = false;
            List<string> paths = CandidatePaths();
            for (int i = 0; i < paths.Count; i++)
            {
                deleted |= DeleteIfExists(paths[i]);
                deleted |= DeleteIfExists(paths[i] + ".old");
            }
            if (deleted) Logger.Log("[RainbowProgress] 已删除进度文件");
        }

        private static bool DeleteIfExists(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch { return false; }
        }

        // ---------------- 小工具 ----------------

        private static object Get(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict.TryGetValue(key, out value) ? value : null;
        }

        private static int ToInt(object value)
        {
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static double ToDouble(object value)
        {
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { return 0.0; }
        }

        private static string ToString(object value)
        {
            return value as string ?? "";
        }

        /// <summary>游戏判定列表的指纹（内容 + 存档点长度）</summary>
        private static string Fingerprint()
        {
            scrMarginTracker tracker = GameState.PlayerTracker;
            if (tracker == null) return "0";
            unchecked
            {
                ulong hash = 1469598103934665603UL; // FNV-1a 64
                List<HitMargin> margins = tracker.hitMargins;
                if (margins != null)
                {
                    for (int i = 0; i < margins.Count; i++) { hash ^= (ulong)(int)margins[i]; hash *= 1099511628211UL; }
                }
                hash ^= (ulong)(uint)tracker.lastHitMarginsSize;
                hash *= 1099511628211UL;
                return hash.ToString("X16", CultureInfo.InvariantCulture);
            }
        }
    }
}
