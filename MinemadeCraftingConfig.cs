using Rocket.API;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace MinemadeCrafting
{
    /// <summary>
    /// Root Rocketmod config — serialised to
    ///   Rocket/Plugins/MinemadeCrafting/MinemadeCrafting.configuration.xml
    /// At first run this file is created empty; paste your barricade blocks in.
    /// The large recipe data lives in a separate JSON file for readability —
    /// see RecipesJson.cs for the loader.
    /// </summary>
    [XmlRoot("MinemadeCraftingConfig")]
    public class MinemadeCraftingConfig : IRocketPluginConfiguration
    {
        public int MAX_UI_ROWS;
        public short EFFECT_ID;
        public ushort UEFFECT_ID;
        public float RAY_DIST;
        /// <summary>All barricade-recipe groups.</summary>
        [XmlArray("BarricadeRecipes")]
        [XmlArrayItem("BarricadeConfig")]
        public List<BarricadeConfig> BarricadeRecipes { get; set; } = new();

        public void LoadDefaults()
        {
            MAX_UI_ROWS = 150;
            EFFECT_ID = 17681;
            UEFFECT_ID = 17681;
            RAY_DIST = 5f;
            BarricadeRecipes = new List<BarricadeConfig>
            {
                // Minimal example so the config is not completely empty on first run.
                new BarricadeConfig
                {
                    BarricadeId = 1916,
                    Recipes = new List<RecipeEntry>
                    {
                        new RecipeEntry
                        {
                            Id         = 1,
                            Name       = "Example Recipe",
                            Image      = "https://example.com/icon.png",
                            CraftTime  = 5,
                            Permission = "Default.craft.permission",
                            NoPermission = "You don't have permission!",
                            RequiredItems = new List<RequiredItem>
                            {
                                new RequiredItem { ItemId = 1, Amount = 1, NeedToBeRemoved = true }
                            },
                            RewardCommands = new List<RewardCommand>
                            {
                                new RewardCommand { Command = "i 1 1", IsServerCommand = false }
                            }
                        }
                    }
                }
            };
        }
    }

    // ─── Data models ──────────────────────────────────────────────────────────

    public class BarricadeConfig
    {
        [XmlAttribute("barricadeId")]
        public ushort BarricadeId { get; set; }

        [XmlArray("Recipes")]
        [XmlArrayItem("Recipe")]
        public List<RecipeEntry> Recipes { get; set; } = new();
    }

    public class RecipeEntry
    {
        [XmlAttribute("id")]
        public int Id { get; set; }

        public string Name        { get; set; } = string.Empty;
        public string Image       { get; set; } = string.Empty;
        public int    CraftTime   { get; set; } = 1;
        public string Permission  { get; set; } = "Default.craft.permission";
        public string NoPermission { get; set; } = "You don't have permission to craft this!";

        [XmlArray("RequiredItems")]
        [XmlArrayItem("Item")]
        public List<RequiredItem> RequiredItems { get; set; } = new();

        [XmlArray("RewardCommands")]
        [XmlArrayItem("Cmd")]
        public List<RewardCommand> RewardCommands { get; set; } = new();
    }

    public class RequiredItem
    {
        [XmlAttribute("itemId")]
        public ushort ItemId { get; set; }

        [XmlAttribute("amount")]
        public int Amount { get; set; } = 1;

        [XmlAttribute("needToBeRemoved")]
        public bool NeedToBeRemoved { get; set; } = true;
    }

    public class RewardCommand
    {
        [XmlAttribute("isServer")]
        public bool IsServerCommand { get; set; }

        [XmlText]
        public string Command { get; set; } = string.Empty;
    }
}
