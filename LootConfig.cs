// Reference: UnityEngine
using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("LootConfig", "Oxide-Russia.ru/Misstake", "1.3.2")]
    internal class LootConfig : HurtworldPlugin
    {
        private PluginConfig _pluginConfig;
        private readonly FieldInfo testField = typeof(RuntimeHurtDB).GetField("_objectDatabase", BindingFlags.NonPublic | BindingFlags.Instance);

        public class PluginConfig
        {
            public float GlobalStackSizeMultiplier { get; set; } = 1;
            public int Version { get; set; }
            public GroupAndItemConfig LootConfig { get; set; } = new GroupAndItemConfig();
        }

        public class GroupAndItemConfig
        {
            public string GUID;
            public bool Force;
            public float ProbabilityCache;
            public float SiblingProbability;
            public float FailsPerSuccess;
            public string Note;
            public bool RollAll;
            public bool RollWithoutReplacement;
            public int RollCount;
            public float LevelMin;
            public float LevelMax;
            public string Name;
            public int MinDrop;
            public int MaxDrop;
            public float Mutliplier;
            public int ItemID;
            public List<GroupAndItemConfig> Children;
        }

        private new void LoadDefaultConfig() { }

        private new void LoadConfig()
        {
            try
            {
                Config.Settings = new JsonSerializerSettings()
                {
                    Formatting = Formatting.Indented,
                    DefaultValueHandling = DefaultValueHandling.Ignore,
                    NullValueHandling = NullValueHandling.Ignore
                };
                _pluginConfig = Config.ReadObject<PluginConfig>();
            }
            catch (Exception ex)
            {
                Puts("Config load failed: {0}{1}{2}", ex.Message, Environment.NewLine, ex.StackTrace);
            }
        }

        private void OnServerInitialized()
        {
            AddCovalenceCommand("reloot", "CmdReloot");
            NextTick(InitWhenLoaded);
        }

        HashSet<string> modifiedGuids = new HashSet<string>();

        void InitWhenLoaded()
        {
            try
            {
                LoadConfig();
                CheckConfig();
                ChangeRatesOnFly();
            }
            catch (Exception ex)
            {
                PrintError(ex.Message);
            }

        }

        private void CmdReloot(IPlayer player, string command, string[] args)
        {
            LoadConfig();
            CheckConfig();
            ChangeRatesOnFly();
        }

        private void CheckConfig()
        {
            if (_pluginConfig.Version == GameManager.PROTOCOL_VERSION)
                return;

            PrintError("Detected a update to version: {gameversion}.".Replace("{gameversion}", GameManager.PROTOCOL_VERSION.ToString()));
            try
            {
                CreateDefaultConfig();
            }
            catch(Exception ex) {

                Puts(ex.Message);
            }
        }

        private void CreateDefaultConfig()
        {
            if (_pluginConfig != null)
            {
                PrintWarning("Creating a new config file for " + this.Title);
                Core.Interface.Oxide.DataFileSystem.WriteObject("lootconfig/" + _pluginConfig.Version, _pluginConfig);
            }
            Config.Clear();

            var config = CollectTreeLoot();
            _pluginConfig = new PluginConfig()
            {
                Version = GameManager.PROTOCOL_VERSION,
                LootConfig = config
            };

            try
            {
                Config.Settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    DefaultValueHandling = DefaultValueHandling.Ignore,
                    NullValueHandling = NullValueHandling.Ignore
                };
                Config.WriteObject(_pluginConfig);
            }
            catch (Exception e)
            {
                PrintError("Failed to save config file: {0}{1}{2}", e.Message, Environment.NewLine, e.StackTrace);
            }
        }



        #region ReadConfigValues
        private GroupAndItemConfig CollectTreeLoot()
        {
            var config = new GroupAndItemConfig();
            foreach (var lt in Singleton<RuntimeHurtDB>.Instance.GetOrderedAssetsAssignableToType<LootTree>())
            {
                try
                {
                    if (lt.name.ToLower().Contains("testnode") || lt.name.ToLower().Contains("recipe") ||
                        lt.name.ToLower().Contains("cost") || lt.name.ToLower().Contains("spawn") ||
                        lt.name.ToLower().Contains("builder"))
                    {
                        PrintWarning($"{lt.name} is not part of the lootconfig in game.");
                        continue;
                    }

                    var gc = new GroupAndItemConfig()
                    {
                        Note = lt.name
                    };
                    CollectTreeLootConfig(lt.Root, ref gc);
                    if (config.Children == null) config.Children = new List<GroupAndItemConfig>();
                    config.Children.Add(gc);
                }
                catch(Exception e)
                {
                    Puts(e.ToString());
                }
            }
            return config;
        }

        private void CollectTreeLootConfig(LootTreeNodeRollGroup root, ref GroupAndItemConfig lootconfig)
        {
            var gc = new GroupAndItemConfig()
            {
                GUID = root.Guid,
                Note = root.Note,
                RollAll = root.RollAll,
                RollWithoutReplacement = root.RollWithoutReplacement,
                RollCount = root.RollCount,
                ProbabilityCache = root.ProbabilityCache,
                SiblingProbability = root.SiblingProbability,
                FailsPerSuccess = root.FailsPerSuccess
            };
            foreach (var child in root.Children)
                SubCollectTreeLootConfig(child, ref gc);
            if (lootconfig.Children == null) lootconfig.Children = new List<GroupAndItemConfig>();
            lootconfig.Children.Add(gc);
        }

        private void SubCollectTreeLootConfig(LootTreeNodeBase child, ref GroupAndItemConfig gconfig)
        {
            var ltnrg = child as LootTreeNodeRollGroup;
            if (ltnrg != null)
            {
                var gc = new GroupAndItemConfig()
                {
                    GUID = ltnrg.Guid,
                    Note = ltnrg.Note,
                    RollAll = ltnrg.RollAll,
                    RollWithoutReplacement = ltnrg.RollWithoutReplacement,
                    RollCount = ltnrg.RollCount
                    //test
                };
                foreach (var ch in ltnrg.Children)
                    SubCollectTreeLootConfig(ch, ref gc);
                if (gconfig.Children == null) gconfig.Children = new List<GroupAndItemConfig>();
                gconfig.Children.Add(gc);
                return;
            }

            var ltnst = child as LootTreeNodeSubtree;
            if (ltnst != null)
            {
                var gc = new GroupAndItemConfig()
                {
                    GUID = ltnst.Guid,
                    ProbabilityCache = ltnst.ProbabilityCache,
                    SiblingProbability = ltnst.SiblingProbability,
                    FailsPerSuccess = ltnst.FailsPerSuccess
                };
                SubCollectTreeLootConfig(ltnst.LootTree.Root, ref gc);
                if (gconfig.Children == null) gconfig.Children = new List<GroupAndItemConfig>();
                gconfig.Children.Add(gc);
                return;
            }

            var ltnitemgenerator = child as LootTreeNodeItemGeneratorContainer;
            if (ltnitemgenerator != null)
            {
                var gc = new GroupAndItemConfig()
                {
                    GUID = ltnitemgenerator.Guid,
                    ProbabilityCache = ltnitemgenerator.ProbabilityCache,
                    SiblingProbability = ltnitemgenerator.SiblingProbability,
                    FailsPerSuccess = ltnitemgenerator.FailsPerSuccess,
                };
                SubCollectTreeLootConfig(ltnitemgenerator.ChildTree, ref gc);
                if (gconfig.Children == null) gconfig.Children = new List<GroupAndItemConfig>();
                gconfig.Children.Add(gc);
                return;
            }

            var ltsfn = child as LootTreeSourceFilterNode;
            if (ltsfn != null)
            {
                if (ltsfn.LootTree != null)
                {
                    var gc = new GroupAndItemConfig()
                    {
                        GUID = ltsfn.Guid,
                        ProbabilityCache = ltsfn.ProbabilityCache,
                        SiblingProbability = ltsfn.SiblingProbability,
                        FailsPerSuccess = ltsfn.FailsPerSuccess,
                        LevelMin = ltsfn.LevelMin,
                        LevelMax = ltsfn.LevelMax,
                    };
                    SubCollectTreeLootConfig(ltsfn.LootTree, ref gc);
                    if (gconfig.Children == null) gconfig.Children = new List<GroupAndItemConfig>();
                    gconfig.Children.Add(gc);
                }
                return;
            }

            var ltniga = child as LootTreeNodeItemGeneratorAdvanced;
            if (ltniga != null)
            {
                var p = ltniga.StackSize as MutatorGeneratorFloatFromReferenceCurve;
                if (p != null)
                {
                    if (gconfig.Children == null) gconfig.Children = new List<GroupAndItemConfig>();
                    gconfig.Children.Add(new GroupAndItemConfig()
                    {
                        GUID = ltniga.Guid,
                        Name = ltniga.LootResult?.name,
                        Mutliplier = p.Mutliplier
                    });
                }
                return;
            }

            var itemgenerator = child as LootTreeNodeItemGenerator;
            if (itemgenerator != null)
            {
                if (gconfig.Children == null) gconfig.Children = new List<GroupAndItemConfig>();
                gconfig.Children.Add(new GroupAndItemConfig()
                {
                    GUID = itemgenerator.Guid,
                    Name = itemgenerator.LootResult?.name,
                    MinDrop = itemgenerator.MinStack,
                    MaxDrop = itemgenerator.MaxStack,
                    FailsPerSuccess = itemgenerator.FailsPerSuccess,
                    ItemID = itemgenerator.LootResult?.GeneratorId ?? 0,
                    SiblingProbability = itemgenerator.SiblingProbability
                });
                return;
            }
        }

        #endregion

        #region SetConfigValues

        private static int depth = 0;
        private void ChangeRatesOnFly()
        {
            var lootConfig = _pluginConfig.LootConfig;
            var ltree = Singleton<RuntimeHurtDB>.Instance.GetOrderedAssetsAssignableToType<LootTree>();
            foreach (var ch in lootConfig.Children)
            {
                foreach (var lt in ltree)
                {
                    if (ch.Note == lt.name)
                    {
                        lt.Root.RollAll = ch.RollAll;
                        lt.Root.RollCount = ch.RollCount;
                        lt.Root.RollWithoutReplacement = ch.RollWithoutReplacement;
                        lt.Root.ProbabilityCache = ch.ProbabilityCache;
                        lt.Root.SiblingProbability = ch.SiblingProbability;
                        lt.Root.FailsPerSuccess = ch.FailsPerSuccess;

                        SetLootTreeConfig(ch, lt);

                        break;
                    }
                }
            }
            Config.WriteObject(_pluginConfig);
        }

        private bool SetLootTreeConfig(GroupAndItemConfig config, LootTree root)
        {
            depth++;

            bool modified = false;
            foreach (var ch in config.Children)
            {
                depth++;
                modified = SubSetLootTreeConfig(ch, root.Root);
                depth--;

            }
            depth--;
            return false;
        }

        private bool SubSetLootTreeConfig(GroupAndItemConfig config, LootTreeNodeBase root)
        {
            var rollGroup = root as LootTreeNodeRollGroup;
            if (rollGroup != null)
            {
                if (config.GUID != rollGroup.Guid)
                    return false;

                rollGroup.RollAll = config.RollAll;
                rollGroup.RollWithoutReplacement = config.RollWithoutReplacement;
                rollGroup.RollCount = config.RollCount;

                bool modified = false;
                if (config.Children == null)
                    return modified;
                foreach (var ch in config.Children)
                {
                    foreach (var child in rollGroup.Children)
                    {
                        depth++;
                        bool m = SubSetLootTreeConfig(ch, child);
                        depth--;
                    }
                }
                return modified;
            }


            var subtree = root as LootTreeNodeSubtree;
            if (subtree != null)
            {
                if (config.GUID != subtree.Guid)
                    return false;

                subtree.ProbabilityCache = config.ProbabilityCache;
                subtree.SiblingProbability = config.SiblingProbability;
                subtree.FailsPerSuccess = config.FailsPerSuccess;

                bool modified = false;
                if (config.Children == null)
                    return modified;
                foreach (var ch in config.Children)
                {
                    depth++;
                    bool m = SubSetLootTreeConfig(ch, subtree.LootTree.Root);
                    depth--;
                }

                return modified;
            }

            var itemGeneratorAdvanced = root as LootTreeNodeItemGeneratorAdvanced;
            if (itemGeneratorAdvanced != null)
            {
                if (config.GUID != itemGeneratorAdvanced.Guid)
                    return false;
                var p = itemGeneratorAdvanced.StackSize as MutatorGeneratorFloatFromReferenceCurve;
                if (p != null)
                {
                    var value = config.Mutliplier * (config.Force ? 1 : _pluginConfig.GlobalStackSizeMultiplier);
                    p.Mutliplier = value;
                }
                return true;
            }

            var itemGenerator = root as LootTreeNodeItemGenerator;
            if (itemGenerator != null)
            {
                if (config.GUID != itemGenerator.Guid)
                    return false;
                var value1 = Mathf.CeilToInt((float)config.MinDrop * (config.Force ? 1 : _pluginConfig.GlobalStackSizeMultiplier));
                var value2 = Mathf.CeilToInt((float)config.MaxDrop * (config.Force ? 1 : _pluginConfig.GlobalStackSizeMultiplier));
                itemGenerator.MinStack = value1;
                itemGenerator.MaxStack = value2;
                itemGenerator.FailsPerSuccess = config.FailsPerSuccess;
                itemGenerator.SiblingProbability = config.SiblingProbability;
                itemGenerator.ProbabilityCache = config.ProbabilityCache;
                itemGenerator.SiblingProbability = config.SiblingProbability;
                if (GlobalItemManager.Instance.ItemGenerators.ContainsKey(config.ItemID))
                {
                    itemGenerator.LootResult = GlobalItemManager.Instance.ItemGenerators[config.ItemID];
                    config.Name = itemGenerator.LootResult?.ToString();
                }

                else if(config.ItemID != 0)
                    Puts($"itemID \"{config.ItemID}\" doesn't exist for {config.Name} with GUID {config.GUID}.");

                return true;
            }
            return false;
        }
        #endregion
    }
}
