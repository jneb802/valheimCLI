using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace valheimCLI
{
    internal static class CharacterImportCommands
    {
        private static string Root => Path.Combine(Paths.ConfigPath, "valheimCLI");
        private static string Imports => Path.Combine(Root, "CharacterImports");
        private static string Backups => Path.Combine(Root, "CharacterBackups");

        public static void Register()
        {
            new Terminal.ConsoleCommand("character_replace",
                "Replace local inventory and skills from Enforcer YAML: character_replace <playerName> [accountID]. Requires devcommands.",
                args => Execute(args, false));
            new Terminal.ConsoleCommand("character_restore",
                "Restore a character_replace backup: character_restore <backupFile>. Requires devcommands.",
                args => Execute(args, true));
        }

        private static void Execute(Terminal.ConsoleEventArgs args, bool restore)
        {
            try
            {
                if (!args.Context.IsCheatsEnabled())
                    throw new InvalidOperationException("Enable devcommands in a local world first.");
                Player player = Player.m_localPlayer;
                if (player == null || player.IsDead() || player.IsTeleporting())
                    throw new InvalidOperationException("A living local player outside teleportation is required.");
                string[] parameters = ParseArguments(args.ArgsAll);
                if (parameters.Length < 1 || parameters.Length > (restore ? 1 : 2))
                    throw new InvalidOperationException(restore
                        ? "Usage: character_restore <backupFile>"
                        : "Usage: character_replace \"Player Name\" [accountID]");

                Snapshot next = restore ? ReadBackup(player, parameters[0]) :
                    ReadImport(player, parameters[0], parameters.Length == 2 ? parameters[1] : null);
                Snapshot previous = Capture(player);
                string backup = WriteBackup(player, previous);
                args.Context.AddString("Backup: " + backup);
                try
                {
                    Apply(player, next);
                    Game.instance.SavePlayerProfile(false);
                }
                catch (Exception applyError)
                {
                    try
                    {
                        Apply(player, previous);
                        Game.instance.SavePlayerProfile(false);
                    }
                    catch (Exception rollbackError)
                    {
                        throw new InvalidOperationException("Replacement and rollback failed. Restore backup " +
                            Path.GetFileName(backup) + ". " + rollbackError.Message, applyError);
                    }
                    throw new InvalidOperationException("Replacement failed; previous inventory and skills restored. " +
                        applyError.Message, applyError);
                }
                args.Context.AddString($"Replaced inventory ({next.Items.Count} items) and skills ({next.Skills.Count}). " +
                    "Character identity unchanged. Restore with: character_restore " + Path.GetFileName(backup));
            }
            catch (Exception error)
            {
                args.Context.AddString("ERROR: " + error.Message);
                valheimCLIPlugin.Log.LogWarning("Character import: " + error);
            }
        }

        internal static string[] ParseArguments(string input)
        {
            List<string> values = new List<string>();
            int end = 0;
            foreach (Match match in Regex.Matches(input, "\\G\\s*(?:\"([^\"\\r\\n]*)\"|([^\"\\s]+))(?=\\s|$)"))
            {
                values.Add(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
                end = match.Index + match.Length;
            }
            if (!string.IsNullOrWhiteSpace(input.Substring(end)))
                throw new InvalidDataException("Invalid arguments. Put names containing spaces in double quotes.");
            return values.ToArray();
        }

        private static void CheckSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "." || value == ".." ||
                value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains("/") || value.Contains("\\"))
                throw new InvalidDataException("Use a name or account ID, not a path.");
        }

        private static Snapshot ReadImport(Player player, string name, string? account)
        {
            CheckSegment(name);
            if (account != null) CheckSegment(account);
            Directory.CreateDirectory(Imports);
            // Only the root and one account directory are supported. mmcli can link config files.
            List<string> directories = new List<string> { Imports };
            directories.AddRange(Directory.GetDirectories(Imports));
            List<string> matches = directories
                .Where(path => account == null || string.Equals(Path.GetFileName(path), account, StringComparison.Ordinal))
                .SelectMany(path => Directory.GetFiles(path, "*.yaml"))
                .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0) throw new FileNotFoundException("No matching YAML file in " + Imports);
            if (matches.Count != 1)
                throw new InvalidDataException("Multiple matching characters. Specify accountID. Accounts: " +
                    string.Join(", ", matches.Select(path => Path.GetFileName(Path.GetDirectoryName(path)))));
            FileInfo file = new FileInfo(matches[0]);
            if (file.Length > 4 * 1024 * 1024) throw new InvalidDataException("Character file exceeds 4 MiB.");
            IDeserializer reader = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties().WithDuplicateKeyChecking().Build();
            ImportCharacter source = reader.Deserialize<ImportCharacter>(File.ReadAllText(file.FullName));
            if (source == null || source.PlayerItems == null || source.SkillLevels == null)
                throw new InvalidDataException("Character file must contain playerItems and skillLevels.");
            Snapshot result = new Snapshot();
            Inventory inventory = player.GetInventory();
            HashSet<Vector2i> positions = new HashSet<Vector2i>();
            foreach (ImportItem saved in source.PlayerItems)
            {
                if (saved == null || string.IsNullOrEmpty(saved.prefabName))
                    throw new InvalidDataException("Item prefab name is missing.");
                GameObject prefab = ObjectDB.instance.GetItemPrefab(saved.prefabName);
                ItemDrop? drop = prefab == null ? null : prefab.GetComponent<ItemDrop>();
                if (drop == null) throw new InvalidDataException("Missing item prefab: " + saved.prefabName);
                ItemDrop.ItemData item = drop.m_itemData.Clone();
                if (saved.m_gridpos == null || saved.m_gridpos.x < 0 || saved.m_gridpos.y < 0 ||
                    saved.m_gridpos.x >= inventory.GetWidth() || saved.m_gridpos.y >= inventory.GetHeight())
                    throw new InvalidDataException("Item position does not fit this inventory: " + saved.prefabName);
                Vector2i position = new Vector2i(saved.m_gridpos.x, saved.m_gridpos.y);
                if (!positions.Add(position)) throw new InvalidDataException("Duplicate item position.");
                if (saved.m_stack < 1 || saved.m_stack > item.m_shared.m_maxStackSize ||
                    saved.m_quality < 1 || saved.m_quality > item.m_shared.m_maxQuality ||
                    !Finite(saved.m_durability) || saved.m_durability < 0 || saved.m_variant < 0 ||
                    saved.m_worldlevel < 0)
                    throw new InvalidDataException("Invalid item values: " + saved.prefabName);
                item.m_dropPrefab = prefab;
                item.m_stack = saved.m_stack;
                item.m_quality = saved.m_quality;
                item.m_durability = saved.m_durability;
                item.m_variant = saved.m_variant;
                item.m_worldLevel = saved.m_worldlevel;
                item.m_crafterID = saved.m_crafterID;
                item.m_crafterName = saved.m_crafterName ?? "";
                item.m_customData = new Dictionary<string, string>(saved.m_customdata ?? new Dictionary<string, string>());
                item.m_equipped = saved.m_equipped;
                item.m_gridPos = position;
                result.Items.Add(item);
            }
            HashSet<Skills.SkillType> seenSkills = new HashSet<Skills.SkillType>();
            foreach (KeyValuePair<string, float> entry in source.SkillLevels)
            {
                if (!Enum.TryParse(entry.Key, out Skills.SkillType type) ||
                    !player.GetSkills().m_skills.Any(def => def.m_skill == type) ||
                    !Finite(entry.Value) || entry.Value < 0 || entry.Value > 100 || !seenSkills.Add(type))
                    throw new InvalidDataException("Unknown skill or invalid level: " + entry.Key);
                result.Skills.Add(new SkillState(type, entry.Value, 0));
            }
            return result;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static Snapshot Capture(Player player)
        {
            Snapshot snapshot = new Snapshot();
            snapshot.Items.AddRange(player.GetInventory().GetAllItems().Select(item => item.Clone()));
            snapshot.Skills.AddRange(player.GetSkills().GetSkillList()
                .Select(skill => new SkillState(skill.m_info.m_skill, skill.m_level, skill.m_accumulator)));
            return snapshot;
        }

        private static void Apply(Player player, Snapshot snapshot)
        {
            Inventory inventory = player.GetInventory();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToList())
                player.UnequipItem(item, false);
            inventory.RemoveAll();
            List<ItemDrop.ItemData> equipped = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData source in snapshot.Items)
            {
                ItemDrop.ItemData item = source.Clone();
                item.m_equipped = false;
                if (!inventory.AddItem(item, item.m_stack, item.m_gridPos.x, item.m_gridPos.y))
                    throw new InvalidOperationException("Could not insert " + source.m_dropPrefab.name);
                if (source.m_equipped) equipped.Add(inventory.GetItemAt(source.m_gridPos.x, source.m_gridPos.y));
            }
            foreach (ItemDrop.ItemData item in equipped)
                if (!player.EquipItem(item, false)) throw new InvalidOperationException("Could not equip " + item.m_dropPrefab.name);
            if (equipped.Any(item => !item.m_equipped))
                throw new InvalidOperationException("Imported equipment contains conflicting slots.");
            Skills skills = player.GetSkills();
            skills.Clear();
            foreach (SkillState state in snapshot.Skills)
            {
                Skills.Skill skill = skills.GetSkill(state.Type);
                skill.m_level = state.Level;
                skill.m_accumulator = state.Progress;
            }
        }

        private static string WriteBackup(Player player, Snapshot snapshot)
        {
            Directory.CreateDirectory(Backups);
            ZPackage inventory = new ZPackage();
            player.GetInventory().Save(inventory);
            ZPackage skills = new ZPackage();
            player.GetSkills().Save(skills);
            ZPackage data = new ZPackage();
            data.Write(1);
            data.Write(player.GetPlayerID());
            data.Write(player.GetInventory().GetWidth());
            data.Write(player.GetInventory().GetHeight());
            data.Write(snapshot.Items.Count);
            data.Write(inventory.GetArray());
            data.Write(skills.GetArray());
            string path = Path.Combine(Backups, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" +
                Guid.NewGuid().ToString("N") + ".backup");
            File.WriteAllBytes(path, data.GetArray());
            return path;
        }

        private static Snapshot ReadBackup(Player player, string name)
        {
            CheckSegment(name);
            string path = Path.Combine(Backups, name);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > 8 * 1024 * 1024)
                throw new InvalidDataException("Invalid backup file.");
            ZPackage data = new ZPackage(File.ReadAllBytes(path));
            if (data.ReadInt() != 1 || data.ReadLong() != player.GetPlayerID())
                throw new InvalidDataException("Backup version or character identity does not match.");
            int width = data.ReadInt();
            int height = data.ReadInt();
            int count = data.ReadInt();
            if (width != player.GetInventory().GetWidth() || height != player.GetInventory().GetHeight())
                throw new InvalidDataException("Backup inventory dimensions do not match.");
            Inventory inventory = new Inventory("Character backup", null, width, height);
            inventory.Load(new ZPackage(data.ReadByteArray()));
            if (inventory.GetAllItems().Count != count)
                throw new InvalidDataException("Backup contains unavailable items.");
            Snapshot result = new Snapshot();
            result.Items.AddRange(inventory.GetAllItems());
            ZPackage skills = new ZPackage(data.ReadByteArray());
            if (skills.ReadInt() != 2) throw new InvalidDataException("Unsupported skill backup version.");
            int skillCount = skills.ReadInt();
            if (skillCount < 0 || skillCount > 4096) throw new InvalidDataException("Invalid skill count.");
            for (int index = 0; index < skillCount; index++)
            {
                Skills.SkillType type = (Skills.SkillType)skills.ReadInt();
                float level = skills.ReadSingle();
                float progress = skills.ReadSingle();
                if (!player.GetSkills().m_skills.Any(def => def.m_skill == type) ||
                    !Finite(level) || level < 0 || level > 100 || !Finite(progress) || progress < 0)
                    throw new InvalidDataException("Backup skill is unavailable or invalid.");
                result.Skills.Add(new SkillState(type, level, progress));
            }
            return result;
        }

        private sealed class Snapshot
        {
            public List<ItemDrop.ItemData> Items { get; } = new List<ItemDrop.ItemData>();
            public List<SkillState> Skills { get; } = new List<SkillState>();
        }

        private sealed class SkillState
        {
            public readonly Skills.SkillType Type;
            public readonly float Level;
            public readonly float Progress;
            public SkillState(Skills.SkillType type, float level, float progress)
            { Type = type; Level = level; Progress = progress; }
        }

        public sealed class ImportCharacter
        {
            public List<ImportItem>? PlayerItems { get; set; }
            public Dictionary<string, float>? SkillLevels { get; set; }
        }

        public sealed class GridPosition
        {
            public int x { get; set; }
            public int y { get; set; }
        }

        public sealed class ImportItem
        {
            public string prefabName { get; set; } = "";
            public int m_stack { get; set; }
            public float m_durability { get; set; }
            public int m_quality { get; set; } = 1;
            public int m_variant { get; set; }
            public int m_worldlevel { get; set; }
            public long m_crafterID { get; set; }
            public string? m_crafterName { get; set; }
            public Dictionary<string, string>? m_customdata { get; set; }
            public bool m_equipped { get; set; }
            // Enforcer omits the default Vector2i value, which means slot (0, 0).
            public GridPosition? m_gridpos { get; set; } = new GridPosition();
        }
    }
}
