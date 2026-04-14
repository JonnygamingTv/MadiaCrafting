# MinemadeCrafting — Rocketmod Plugin

Native .NET 4.8 replacement for the uScript2 MINEMADE_CRAFTING script.  
Eliminates the +300–700 ms / 10s lag caused by uScript2's runtime interpreter.

---

## Files

| File | Purpose |
|---|---|
| `MinemadeCraftingPlugin.cs` | Main plugin logic |
| `MinemadeCraftingConfig.cs` | Config & data models |
| `MinemadeCrafting.csproj` | Build project (VS / Rider / `dotnet build`) |
| `convert_uscript_to_xml.py` | One-time tool: converts your `.uscript` config to XML |

---

## Quick Setup

### 1 — Generate your config (one time)

```bash
python3 convert_uscript_to_xml.py MINEMADE_CRAFTING.uscript
```

This produces `MinemadeCrafting.configuration.xml` with all 19 barricades and 513 recipes.

### 2 — Build the plugin

Edit `MinemadeCrafting.csproj` to point `$(UnturnedPath)` at your Unturned install, then:

```bash
# pass your Unturned path as a property
dotnet build -p:UnturnedPath="C:\Program Files (x86)\Steam\steamapps\common\Unturned"
```

Or open the `.csproj` in Visual Studio / Rider and adjust the reference hints.

### 3 — Deploy

Copy these two files to your server:

```
Rocket/Plugins/MinemadeCrafting/MinemadeCrafting.dll
Rocket/Plugins/MinemadeCrafting/MinemadeCrafting.configuration.xml
```

Restart the server (or `/rocket reload`).

---

## How it works

| uScript2 event | Rocketmod equivalent |
|---|---|
| `onPlayerGestured PUNCH_LEFT/RIGHT` | `UnturnedPlayerEvents.OnPlayerUpdateGesture` |
| `onEffectButtonClicked` | `EffectManager.onEffectButtonClicked` |
| `onEffectTextCommitted` | `EffectManager.onEffectTextCommitted` |
| `onPlayerQuit` | `U.Events.OnPlayerDisconnected` |
| `wait.seconds(1, ...)` countdown | `System.Threading.Timer` (off main thread, pumped back via `Update()`) |
| `player.sudo(cmd)` | `Commander.execute(player.CSteamID, cmd)` |
| `server.execute(cmd)` | `Commander.execute(CSteamID.Nil, cmd)` |
| `EffectManagerExtended.setVisibility` | `EffectManager.sendUIEffectVisibility` |
| `EffectManagerExtended.setText` | `EffectManager.sendUIEffectText` |
| `EffectManagerExtended.setImage` | `EffectManager.sendUIEffectImageURL` |

The recipe index is built **once at load** into a `Dictionary<ushort, List<RecipeEntry>>`,
so barricade lookups are O(1) instead of O(n) linear scans on every punch.

---

## Adding / editing recipes

Open `MinemadeCrafting.configuration.xml` and add `<BarricadeConfig>` / `<Recipe>` blocks.
No recompile needed — just reload the plugin.

Example recipe block:

```xml
<Recipe id="1">
  <Name>Water Bottle</Name>
  <Image>https://static.unturnedhub.com/200.Bottled_Water_14.png</Image>
  <CraftTime>2</CraftTime>
  <Permission>Default.craft.permission</Permission>
  <NoPermission>You don't have permission to craft this!</NoPermission>
  <RequiredItems>
    <Item itemId="54043" amount="1" needToBeRemoved="true"/>
    <Item itemId="1228"  amount="1" needToBeRemoved="false"/>
  </RequiredItems>
  <RewardCommands>
    <Cmd isServer="false">i 14</Cmd>
  </RewardCommands>
</Recipe>
```

---

## Permissions

The plugin reads permissions directly from `Rocket.permissions.xml`.  
Each recipe's `<Permission>` field must match a node there, e.g.:

```xml
<Permission Cooldown="0">Default.craft.permission</Permission>
<Permission Cooldown="0">Gunsmith.craft.permission</Permission>
<Permission Cooldown="0">Provisioner.craft.permission</Permission>
<Permission Cooldown="0">Carmanufacturing.craft.permission</Permission>
<Permission Cooldown="0">Medical.craft.permission</Permission>
```

---

## Why the lag happened

uScript2 parses and interprets your `.uscript` file at runtime.  
With 513 recipes and a 500 KB script, every event triggered a fresh interpretation pass.  
This plugin compiles to native IL — there is no interpreter overhead at runtime.
