using Rocket.API;
using Rocket.Core.Plugins;
using Rocket.Core.Utils;
using Rocket.Unturned;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Framework.Utilities;
using SDG.NetTransport;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MinemadeCrafting
{
    public class MinemadeCraftingPlugin : RocketPlugin<MinemadeCraftingConfig>
    {
        public static MinemadeCraftingPlugin Instance { get; private set; }

        // Keyed by player SteamID
        private readonly Dictionary<ulong, List<RecipeEntry>> _playerRecipeList    = new();
        private readonly Dictionary<ulong, string>           _playerSearchText     = new();
        private readonly Dictionary<ulong, CraftingSession>  _playerCraftingStatus = new();
        private readonly Dictionary<ulong, Dictionary<ushort, int>> _playerItemsCache = new();

        // barricadeId -> recipes, built once on load
        private readonly Dictionary<ushort, List<RecipeEntry>> _recipeIndex = new();

        EffectAsset? effectAsset = null;

        protected override void Load()
        {
            Instance = this;
            BuildRecipeIndex();

            U.Events.OnPlayerDisconnected += OnPlayerDisconnected;
            UnturnedPlayerEvents.OnPlayerUpdateGesture += OnPlayerGesture;
            EffectManager.onEffectButtonClicked += OnEffectButtonClicked;
            EffectManager.onEffectTextCommitted  += OnEffectTextCommitted;

            effectAsset = Assets.find(EAssetType.EFFECT, Configuration.Instance.UEFFECT_ID) as EffectAsset;
            if (effectAsset == null && !SDG.Unturned.Level.isLoaded) SDG.Unturned.Level.onLevelLoaded += AfterLoad;

            Logger.Log($"[MinemadeCrafting] Loaded — {_recipeIndex.Count} barricade(s), " +
                       $"{_recipeIndex.Values.Sum(r => r.Count)} total recipes.");
        }

        private void AfterLoad(int level)
        {
            effectAsset = Assets.find(EAssetType.EFFECT, Configuration.Instance.UEFFECT_ID) as EffectAsset;
            if (effectAsset == null) Logger.Log("Unable to find effectAsset!");
        }

        protected override void Unload()
        {
            U.Events.OnPlayerDisconnected -= OnPlayerDisconnected;
            UnturnedPlayerEvents.OnPlayerUpdateGesture -= OnPlayerGesture;
            EffectManager.onEffectButtonClicked -= OnEffectButtonClicked;
            EffectManager.onEffectTextCommitted  -= OnEffectTextCommitted;
            SDG.Unturned.Level.onLevelLoaded -= AfterLoad;

            // Cancel all pending craft timers
            foreach (var session in _playerCraftingStatus.Values)
                session.Timer?.Dispose();

            _playerCraftingStatus.Clear();
            Instance = null;
        }

        public override Rocket.API.Collections.TranslationList DefaultTranslations => new Rocket.API.Collections.TranslationList
        {
            {"invalid_id", "Invalid recipe ID." },
            { "already_crafting", "You are already crafting an item." },
            { "no_recipes", "No recipes available. Punch a crafting station first." },
            { "recipe_not_found", "Recipe ID not found." },
            { "missing_requireditems", "You do not have the required items to craft this." },
            { "crafting", "Crafting {0}. Time remaining: {1}s." },
            { "crafting_complete", "Crafting of {0} complete!" },
        };

        // ─── Index ────────────────────────────────────────────────────────────

        private void BuildRecipeIndex()
        {
            _recipeIndex.Clear();
            foreach (var bc in Configuration.Instance.BarricadeRecipes)
            {
                if (!_recipeIndex.ContainsKey(bc.BarricadeId))
                    _recipeIndex[bc.BarricadeId] = bc.Recipes;
            }
            Logger.Log($"[MinemadeCrafting] Recipe index built: {_recipeIndex.Count} barricades.");
        }

        // ─── Events ───────────────────────────────────────────────────────────

        private void OnPlayerGesture(UnturnedPlayer player, UnturnedPlayerEvents.PlayerGesture gesture)
        {
            if (gesture != UnturnedPlayerEvents.PlayerGesture.PunchLeft &&
                gesture != UnturnedPlayerEvents.PlayerGesture.PunchRight)
                return;

            // Raycast to find barricade
            if (!Physics.Raycast(
                    player.Player.look.aim.position, player.Player.look.aim.forward,
                    out RaycastHit hit, Configuration.Instance.RAY_DIST, RayMasks.BARRICADE))
                return; // ~0.05ms

            var barricadeDrop = BarricadeManager.FindBarricadeByRootTransform(hit.transform); // Touches Unity component. Unknown ms
            if (barricadeDrop == null) return;

            ushort barricadeId = barricadeDrop.asset.id;
            if (!_recipeIndex.TryGetValue(barricadeId, out var recipes)) return; // ~50ns

            ulong steamId = player.CSteamID.m_SteamID;
            var transport = player.Player.channel.GetOwnerTransportConnection();
            _playerRecipeList[steamId]  = recipes;
            _playerSearchText[steamId]  = string.Empty;

            Task.Run(() =>
            {
                var itemsMap = BuildItemsMap(player); // READS player.Inventory.items. Inventory is completely 'virtual'. Should not use Unity directly.
                _playerItemsCache[steamId] = itemsMap;

                TaskDispatcher.QueueOnMainThread(() => DisplayRecipes(player, recipes, itemsMap));
                string avatarUrl = player.SteamProfile.AvatarFull.AbsoluteUri; // lazy-load
                TaskDispatcher.QueueOnMainThread(() => SetImage(transport, "Canvas/BACKGROUND/LeftSide/PlayerInfoBG/Mask2/PlayerAvatar", avatarUrl));
            });
        }

        private void OnEffectTextCommitted(Player nativePlayer, string buttonName, string text)
        {
            if (buttonName != "CMM_SEARCH_IF") return;

            var player  = UnturnedPlayer.FromPlayer(nativePlayer);
            ulong steamId = player.CSteamID.m_SteamID;

            string search = text.Trim().ToLower();
            _playerSearchText[steamId] = search;

            if (!_playerRecipeList.TryGetValue(steamId, out var allRecipes)) return;

            var filtered = FilterRecipes(allRecipes, search);
            var itemsMap = BuildItemsMap(player);
            _playerItemsCache[steamId] = itemsMap;

            DisplayRecipes(player, filtered, itemsMap);
        }

        private void OnEffectButtonClicked(Player nativePlayer, string buttonName)
        {
            var player  = UnturnedPlayer.FromPlayer(nativePlayer);
            ulong steamId = player.CSteamID.m_SteamID;

            if (buttonName == "SAR_CraftingLLogOut")
            {
                EffectManager.askEffectClearByID(Instance.Configuration.Instance.UEFFECT_ID, player.Player.channel.GetOwnerTransportConnection());
                player.Player.disablePluginWidgetFlag(EPluginWidgetFlags.Modal);
                player.Player.enablePluginWidgetFlag(EPluginWidgetFlags.ShowCenterDot);
                player.Player.disablePluginWidgetFlag(EPluginWidgetFlags.NoBlur);
                return;
            }

            if (!buttonName.StartsWith("SAR_ButtonCraft_")) return;

            if (!int.TryParse(buttonName.Replace("SAR_ButtonCraft_", ""), out int recipeIndex))
            {
                UnturnedChat.Say(player, Translate("invalid_id"), Color.red);
                return;
            }

            if (_playerCraftingStatus.ContainsKey(steamId))
            {
                UnturnedChat.Say(player, Translate("already_crafting"), Color.red);
                return;
            }

            if (!_playerRecipeList.TryGetValue(steamId, out var allRecipes))
            {
                UnturnedChat.Say(player, Translate("no_recipes"), Color.yellow);
                return;
            }

            if (!_playerSearchText.TryGetValue(steamId, out string search)) search = string.Empty;
            var filtered    = FilterRecipes(allRecipes, search);

            if (recipeIndex < 0 || recipeIndex >= filtered.Count)
            {
                UnturnedChat.Say(player, Translate("recipe_not_found"), Color.red);
                return;
            }

            var recipe = filtered[recipeIndex];

            if (!player.HasPermission(recipe.Permission))
            {
                UnturnedChat.Say(player, recipe.NoPermission, Color.red);
                return;
            }

            var itemsMap = BuildItemsMap(player);
            if (!HasRequiredItems(itemsMap, recipe.RequiredItems))
            {
                UnturnedChat.Say(player, Translate("missing_requireditems"), Color.red);
                return;
            }

            RemoveRequiredItems(player, recipe.RequiredItems);
            StartCrafting(player, recipe);
        }

        private void OnPlayerDisconnected(UnturnedPlayer player)
        {
            ulong steamId = player.CSteamID.m_SteamID;
            if (_playerCraftingStatus.TryGetValue(steamId, out var session))
                session.Timer?.Dispose();

            _playerCraftingStatus.Remove(steamId);
            _playerRecipeList.Remove(steamId);
            _playerSearchText.Remove(steamId);
            _playerItemsCache.Remove(steamId);
        }

        // ─── Crafting ─────────────────────────────────────────────────────────

        private void StartCrafting(UnturnedPlayer player, RecipeEntry recipe)
        {
            ulong steamId = player.CSteamID.m_SteamID;
            int remaining = recipe.CraftTime;

            UnturnedChat.Say(player, Translate("crafting", recipe.Name, remaining), Color.green);

            var session = new CraftingSession { Recipe = recipe, RemainingSeconds = remaining };
            _playerCraftingStatus[steamId] = session;

            session.Timer = new Timer(_ => CraftingTick(steamId), null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private void CraftingTick(ulong steamId)
        {
            if (!_playerCraftingStatus.TryGetValue(steamId, out var session)) return;

            session.RemainingSeconds--;

            if (session.RemainingSeconds > 0) return;

            // Done
            session.Timer?.Dispose();
            _playerCraftingStatus.Remove(steamId);

            TaskDispatcher.QueueOnMainThread(() =>
            {
                var player = UnturnedPlayer.FromCSteamID(new CSteamID(steamId));
                if (player == null) return;

                GiveRewards(player, session.Recipe.RewardCommands);
                UnturnedChat.Say(player, Translate("crafting_complete", session.Recipe.Name), Color.green);
            });
        }

        private static void GiveRewards(UnturnedPlayer player, List<RewardCommand> commands)
        {
            foreach (var cmd in commands)
            {
                string text = cmd.Command.Replace("{id}", player.SteamProfile.SteamID);
                Rocket.Core.R.Commands.Execute(cmd.IsServerCommand ? null : player, text);
            }
        }

        // ─── UI ───────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, string> RarityColors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["common"]    = "#FFFFFF",
            ["uncommon"]  = "#1F871F",
            ["rare"]      = "#4B64FA",
            ["epic"]      = "#964BFA",
            ["legendary"] = "#C832FA",
            ["mythical"]  = "#FA3219",
        };

        private static void DisplayRecipes(UnturnedPlayer player, List<RecipeEntry> recipes,
                                           Dictionary<ushort, int> itemsMap)
        {
            var transport = player.Player.channel.GetOwnerTransportConnection();

            EffectManager.SendUIEffect(Instance.effectAsset, Instance.Configuration.Instance.EFFECT_ID, transport, true);

            SetText(transport, $"Canvas/BACKGROUND/LeftSide/PlayerInfoBG/PlayerName", player.DisplayName);

            player.Player.enablePluginWidgetFlag(EPluginWidgetFlags.Modal);
            player.Player.disablePluginWidgetFlag(EPluginWidgetFlags.ShowCenterDot);
            player.Player.enablePluginWidgetFlag(EPluginWidgetFlags.NoBlur);

            // Hide all rows first
            for (int i = 0; i < Instance.Configuration.Instance.MAX_UI_ROWS; i++)
                SetVisible(transport, $"Canvas/BACKGROUND/RightSide/Scroll View/Viewport/Content/CraftObject_{i}", false);

            int counter = 0;
            foreach (var recipe in recipes)
            {
                if (counter >= Instance.Configuration.Instance.MAX_UI_ROWS) break;

                string rowPath = $"Canvas/BACKGROUND/RightSide/Scroll View/Viewport/Content/CraftObject_{counter}";
                SetVisible(transport, rowPath, true);

                SetImage(transport, $"{rowPath}/MaskForIcon_{counter}/Icon_{counter}", recipe.Image);

                bool hasItems   = HasRequiredItems(itemsMap, recipe.RequiredItems);
                string color    = hasItems ? "white" : "red";
                SetText(transport, $"{rowPath}/ItemName_{counter}",
                        $"<color={color}>{recipe.Name}</color>");

                // Build info string
                var sb = new System.Text.StringBuilder();
                sb.Append($"Time {recipe.CraftTime} seconds.\n\nRecipe:\n");

                foreach (var req in recipe.RequiredItems)
                {
                    var asset = Assets.find(EAssetType.ITEM, req.ItemId) as ItemAsset;
                    if (asset == null) continue;

                    string rarity = asset.rarity.ToString().ToLower();
                    string rColor = RarityColors.TryGetValue(rarity, out var c) ? c : "#FFFFFF";
                    sb.Append($"{req.Amount}x <color={rColor}>{asset.itemName}</color>\n");
                }

                SetText(transport,
                        $"{rowPath}/Scroll View_{counter}/Viewport_{counter}/Content_{counter}/Text_{counter}",
                        sb.ToString());

                counter++;
            }
        }

        private static void SetText(ITransportConnection t, string path, string text)
            => EffectManager.sendUIEffectText(Instance.Configuration.Instance.EFFECT_ID, t, true, path, text);

        private static void SetImage(ITransportConnection t, string path, string url)
            => EffectManager.sendUIEffectImageURL(Instance.Configuration.Instance.EFFECT_ID, t, true, path, url);

        private static void SetVisible(ITransportConnection t, string path, bool visible)
            => EffectManager.sendUIEffectVisibility(Instance.Configuration.Instance.EFFECT_ID, t, true, path, visible);

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static Dictionary<ushort, int> BuildItemsMap(UnturnedPlayer player)
        {
            var map = new Dictionary<ushort, int>();
            var items = player.Inventory.items;

            for (byte page = 0; page < PlayerInventory.PAGES - 2; page++)
            {
                var storage = items[page];
                if (storage == null) continue;
                foreach (var jar in storage.items)
                {
                    ushort id = jar.item.id;
                    map.TryGetValue(id, out int qty);
                    map[id] = qty + jar.item.amount;  // amount is stack size for stackable items
                }
            }
            return map;
        }

        private static bool HasRequiredItems(Dictionary<ushort, int> itemsMap,
                                             List<RequiredItem> required)
        {
            // We need per-slot counting to handle stacks correctly
            // For simplicity and parity with original script (which counts individual item instances),
            // use the pre-built map
            var counts = new Dictionary<ushort, int>(itemsMap);
            foreach (var req in required)
            {
                if (!req.NeedToBeRemoved)
                {
                    if (!counts.ContainsKey(req.ItemId)) return false;
                    continue;
                }
                if (!counts.TryGetValue(req.ItemId, out int have) || have < req.Amount)
                    return false;
                counts[req.ItemId] = have - req.Amount;
            }
            return true;
        }

        private static void RemoveRequiredItems(UnturnedPlayer player, List<RequiredItem> required)
        {
            foreach (var req in required)
            {
                if (!req.NeedToBeRemoved) continue;

                int toRemove = req.Amount;
                var inv = player.Inventory;

                for (byte page = 0; page < PlayerInventory.PAGES - 2 && toRemove > 0; page++)
                {
                    var storage = inv.items[page];
                    if (storage == null) continue;

                    for (int i = storage.items.Count - 1; i >= 0 && toRemove > 0; i--)
                    {
                        var jar = storage.items[i];
                        if (jar.item.id != req.ItemId) continue;

                        if (jar.item.amount <= toRemove)
                        {
                            toRemove -= jar.item.amount;
                            inv.removeItem(page, (byte)i);
                        }
                        else
                        {
                            jar.item.amount -= (byte)toRemove;
                            inv.sendUpdateAmount(page, jar.x, jar.y, jar.item.amount);
                            toRemove = 0;
                        }
                    }
                }
            }
        }

        private static List<RecipeEntry> FilterRecipes(List<RecipeEntry> all, string search)
        {
            if (string.IsNullOrEmpty(search)) return all;
            return all.Where(r => r.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
    }

    // ─── Supporting types ─────────────────────────────────────────────────────

    public class CraftingSession
    {
        public RecipeEntry Recipe          { get; set; }
        public int         RemainingSeconds { get; set; }
        public Timer       Timer            { get; set; }
    }
}
