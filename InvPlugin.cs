using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Events;

using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API;
using System.Drawing;
using static CounterStrikeSharp.API.Core.Listeners;
using System.Collections.Immutable;
using CounterStrikeSharp.API.Modules.Memory;
using System.Reflection;
using System.Diagnostics.Contracts;
using CounterStrikeSharp.API.Modules.Utils;
using System.Runtime.InteropServices;

namespace InvisibilityPlugin;

public class InvisibilityPlugin : BasePlugin
{
    // private Dictionary<CCSPlayerPawn, Timer> playerVisibilityTimers = new Dictionary<CCSPlayerPawn, Timer>();
    private Dictionary<CCSPlayerPawn, float> playerVisibilityLevels = new Dictionary<CCSPlayerPawn, float>();
    // 0.0 = invisible, 1.0 = visible, in between = partially visible

    public override string ModuleName => "InvisibilityPlugin";
    public override string ModuleVersion => "1.1";
    public override string ModuleAuthor => "Mrec&me";
    private static float INVISIBILITY_GAIN_PER_SECOND = 0.75f; // fully invisible in 2 seconds
    private static float INVISIBILITY_GAIN = INVISIBILITY_GAIN_PER_SECOND * (1 / 64.0f);
    private static float INVISIBILITY_DELAY = 0.0f; // visible for 4 seconds, including fading into invisibility
    private static bool[] handlerToggles = new bool[4];
    private static bool printDebugMessages = false;
    private static bool showBar = true;

    private void Debug(string s)
    {
        if (!printDebugMessages) return;
        Server.PrintToChatAll(s);
    }
    public override void Load(bool hotReload)
    {
        RegisterListener<OnTick>(() =>
        {
            var itemsToRemove = playerVisibilityLevels.Where(f =>
            {
                if (f.Key == null ||
                    !f.Key.IsValid ||
                    f.Key.OriginalController == null ||
                    !f.Key.OriginalController.IsValid ||
                    f.Key.OriginalController.Value == null // mama mia
                )
                    return true;
                return false;
            }).ToArray();
            foreach (var item in itemsToRemove)
                playerVisibilityLevels.Remove(item.Key);

            foreach (var player in playerVisibilityLevels)
            {
                var newValue = player.Value < 1.0f ? player.Value + INVISIBILITY_GAIN : 1.0f;
                playerVisibilityLevels[player.Key] = newValue;

                player.Key.EntitySpottedState.SpottedByMask[0] = 0;
                player.Key.EntitySpottedState.SpottedByMask[1] = 0;
                player.Key.EntitySpottedState.Spotted = false;

                Utilities.SetStateChanged(player.Key, "EntitySpottedState_t", "m_bSpotted");
                if (newValue != player.Value)
                    SetPlayerVisibilityLevel(player.Key.OriginalController?.Value!, newValue);
                if (showBar)
                    UpdateVisibilityBar(player.Key, newValue);
            }

            if (Server.TickCount % 8 != 0)
                return;

            var fullColor = Color.FromArgb(255, 255, 255, 255);

            var ents = Utilities.GetAllEntities();
            foreach (var ent in ents)
            {
                if (ent.DesignerName.StartsWith("weapon_"))
                {
                    var weapon = new CBasePlayerWeapon(ent.Handle);
                    if (!weapon.IsValid) continue;
                    CCSWeaponBase _weapon = weapon.As<CCSWeaponBase>();

                    if (_weapon.Render.A != 255)
                    {
                        Debug("Found weapon entity and made it visible");
                        _weapon.Render = fullColor;
                        _weapon.ShadowStrength = 1;
                        Utilities.SetStateChanged(_weapon, "CBaseModelEntity", "m_clrRender");
                    }
                }
            }
        });

        // HookEntityOutput("*", "OnItemDrop", (CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay) =>
        // {
        //     Server.PrintToChatAll($"someone dropped ({name}, {activator}, {caller}, {delay})");

        //     return HookResult.Continue;
        // });

        RegisterEventHandler<EventPlayerSound>((@event, info) =>
        {
            if (handlerToggles[0] && @event.Userid != null)
                MakeTemporaryVisible(@event.Userid);
            Debug("sound trigger");
            return HookResult.Continue;
        });

        RegisterEventHandler<EventWeaponFire>((@event, info) =>
        {
            if (handlerToggles[1] && @event.Userid != null)
                MakeTemporaryVisible(@event.Userid);
            Debug("weapon fire trigger");
            return HookResult.Continue;
        });

        RegisterEventHandler<EventPlayerHurt>((@event, info) =>
        {
            if (handlerToggles[2] && @event.Userid != null)
                MakeTemporaryVisible(@event.Userid);
            Debug("hurt trigger");
            return HookResult.Continue;
        });

        RegisterEventHandler<EventWeaponReload>((@event, info) =>
        {
            if (handlerToggles[3] && @event.Userid != null)
                MakeTemporaryVisible(@event.Userid);
            Debug("reload trigger");
            return HookResult.Continue;
        });

        for (int i = 0; i < 3; i++)
            handlerToggles[i] = true;

        Console.WriteLine("InvisibilityPlugin loaded.");

        if (hotReload == true)
            Debug("Im here hi!!! just hotreloaded");
    }

    public override void Unload(bool hotReload)
    {
        Console.WriteLine("InvisibilityPlugin unloaded.");
    }

    [ConsoleCommand("invs_help", "help message")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnHelp(CCSPlayerController? player, CommandInfo info)
    {
        player.PrintToConsole("invs_help - shows this message");
        player.PrintToConsole("invs_delay <delay in ms> - sets how long player will be visible after shooting or making sounds");
        player.PrintToConsole("invs_regain <delay in ms> - controls how long it will take to regain invisibility in milliseconds");
        player.PrintToConsole("invs <playername> - toggles invisibility for a specific player (dos not have to be an exact copy of the players name)");
        player.PrintToConsole("invs_types <triggerOnSound> <triggerOnWeaponFire> <triggerOnPlayerHurt> <triggerOnReload> - toggles various invisibility handlers");
        player.PrintToConsole("invs_debug - triggers plugin to vomit in chat more information than anyone ever needed");
        player.PrintToConsole("invs_bar - toggle invisibility bar (for now, globally)");
    }

    [ConsoleCommand("invs_bar", "im insane")]
    [CommandHelper(minArgs: 0, usage: "granyola bar", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnInvsBar(CCSPlayerController? player, CommandInfo info)
    {
        showBar = !showBar;
        if (showBar)
            player.PrintToConsole("Bar enabled");
        else
            player.PrintToConsole("Bad disabled");
    }

    [ConsoleCommand("invs_debug", "hi")]
    [CommandHelper(minArgs: 0, usage: "call and look", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnInvsDebug(CCSPlayerController? player, CommandInfo info)
    {
        printDebugMessages = !printDebugMessages;
        if (printDebugMessages)
            Server.PrintToChatAll("good luck with this bug!");
        else
            Server.PrintToChatAll("bye bye");
    }

    [ConsoleCommand("invs_types", "switches various invisibility handlers")]
    [CommandHelper(minArgs: 4, usage: "<triggerOnSound> <triggerOnWeaponFire> <triggerOnPlayerHurt> <triggerOnReload>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnInvsTypes(CCSPlayerController? player, CommandInfo info)
    {
        string[] handlerNames = ["triggerOnSound", "triggerOnWeaponFire", "triggerOnPlayerHurt", "triggerOnReload"];
        for (int i = 0; i < handlerNames.Length; i++)
        {
            int imput_num = -1;
            try
            {
                imput_num = int.Parse(info.GetArg(i + 1));
            }
            catch (System.Exception)
            {
                player.PrintToConsole($"expected 1 of 0 at argument {i}, got {info.GetArg(i + 1)}, leaving value unchanged");
                continue;
            }

            handlerToggles[i] = imput_num == 1 ? true : imput_num == 0 ? false : handlerToggles[i];
            if (imput_num != 0 && imput_num != 1)
                player.PrintToConsole($"expected 1 of 0 at argument {i}, got {info.GetArg(i + 1)}, leaving value unchanged");
            else
                player.PrintToConsole($"{handlerNames[i]} is now {handlerToggles[i]}");
        }
    }

    [ConsoleCommand("invs_delay", "sets how long player will be visible after shooting or making sounds")]
    [CommandHelper(minArgs: 1, usage: "<delay in ms>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnInvsDelay(CCSPlayerController? player, CommandInfo info)
    {
        INVISIBILITY_DELAY = -(int.Parse(info.GetArg(1)) / 1000.0f) / INVISIBILITY_GAIN_PER_SECOND;
    }

    [ConsoleCommand("invs_regain", "controls how long it will take to regain invisibility in milliseconds")]
    [CommandHelper(minArgs: 1, usage: "<delay in ms>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnInvsRegain(CCSPlayerController? player, CommandInfo info)
    {
        INVISIBILITY_GAIN_PER_SECOND = 1.0f / (int.Parse(info.GetArg(1)) / 1000.0f);
        INVISIBILITY_GAIN = INVISIBILITY_GAIN_PER_SECOND * (1 / 64.0f);
    }

    [ConsoleCommand("invs", "toggle invisibility for a player.")]
    [CommandHelper(minArgs: 1, usage: "<playername>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnInvsCommand(CCSPlayerController? player, CommandInfo info)
    {
        string targetPlayerName = info.ArgByIndex(1);
        var targetPlayer = GetPlayerByName(targetPlayerName);

        if (player == null)
            Console.WriteLine("Player is null, but doesn matter");

        if (targetPlayer == null)
        {
            var message = $"Player '{targetPlayerName}' not found.";
            if (player != null)
                player.PrintToChat(message);
            else
                Console.WriteLine(message);
            return;
        }

        string status = ToggleInvisibility(targetPlayer) ? "enabled" : "disabled";
        if (player != null)
            player.PrintToChat($"Invisibility {status} for player '{targetPlayerName}'.");
    }

    private void MakeTemporaryVisible(CCSPlayerController player)
    {
        if (player == null)
            return;
        if (!playerVisibilityLevels.ContainsKey(player.PlayerPawn.Value))
            return;
        playerVisibilityLevels[player.PlayerPawn.Value] = INVISIBILITY_DELAY; // will be visible for a few seconds or so
        SetPlayerVisibilityLevel(player, 0.0f);
    }

    private bool ToggleInvisibility(CCSPlayerPawn player)
    {
        if (playerVisibilityLevels.ContainsKey(player))
        {
            MakeTemporaryVisible(player.OriginalController.Value); // make forever visible :3
            playerVisibilityLevels.Remove(player);
            return false;
        }
        playerVisibilityLevels[player] = 1.0f; // Fully invisible + added to visibility list
        SetPlayerVisibilityLevel(player.OriginalController.Value, 1.0f);

        return true;
    }

    public static void SetPlayerVisibilityLevel(CCSPlayerController player, float visibilityLevel)
    {
        var playerPawnValue = player.PlayerPawn.Value;
        if (playerPawnValue == null || !playerPawnValue.IsValid)
        {
            Console.WriteLine("Player pawn is not valid.");
            return;
        }

        int alpha = (int)((1.0f - visibilityLevel) * 255);
        alpha = alpha > 255 ? 255 : alpha < 0 ? 0 : alpha; // >:3
        var fadeColor = Color.FromArgb(alpha, 255, 255, 255);
        var blankColor = Color.FromArgb(0, 255, 255, 255);
        var fullColor = Color.FromArgb(255, 255, 255, 255);

        if (playerPawnValue != null && playerPawnValue.IsValid)
        {
            playerPawnValue.Render = fadeColor;
            Utilities.SetStateChanged(playerPawnValue, "CBaseModelEntity", "m_clrRender");
            Utilities.SetStateChanged(playerPawnValue, "CCSPlayer_ViewModelServices", "m_hViewModel");
        }

        // useless
        // var wearables = playerPawnValue.MyWearables;
        // if (wearables != null)
        // {
        //     Server.PrintToConsole($"There ar some wearables ({wearables.Size})");
        //     var gloves = wearables[0];
        //     if (gloves != null)
        //         if (gloves.Value != null)
        //         {
        //             gloves.Value.Render = fadeColor;
        //             gloves.Value.ShadowStrength = visibilityLevel;
        //             Server.PrintToConsole("got thoese gloves");
        //         }
        //     for (int i = 0; i < wearables.Size; i++)
        //     {
        //         if (wearables[i].Value != null)
        //         {
        //             // var index = wearables[i].Value.Index;
        //             // var entity = Utilities.GetEntityFromIndex<CCSWeaponBase>((int)index);
        //             // entity.Render = fadeColor;
        //             // entity.ShadowStrength = visibilityLevel;
        //             wearables[i].Value.Render = fadeColor;
        //             wearables[i].Value.ShadowStrength = visibilityLevel;
        //             Server.PrintToChatAll($"{wearables[i].Value.DesignerName}");
        //         }
        //     }
        // }
        // foreach (var wearable in wearables)
        // {
        //     if (wearable.Value != null)
        //     {
        //         wearable.Value.Render = fadeColor;
        //         wearable.Value.ShadowStrength = visibilityLevel;
        //         Server.PrintToChatAll($"{wearable.Value.DesignerName}");
        //     }
        // }
        // Utilities.SetStateChanged(playerPawnValue, "CBaseModelEntity", "m_hMyWearables");
        // Utilities.SetStateChanged(playerPawnValue, "CBaseCombatCharacter", "m_hMyWearables");

        var weaponServices = playerPawnValue.WeaponServices;
        if (weaponServices != null)
        {
            var activeWeapon = weaponServices.ActiveWeapon.Value;
            if (activeWeapon != null && activeWeapon.IsValid)
            {
                activeWeapon.Render = fadeColor;
                activeWeapon.ShadowStrength = visibilityLevel;
                Utilities.SetStateChanged(activeWeapon, "CBaseModelEntity", "m_clrRender");
            }
        }

        var myWeapons = playerPawnValue.WeaponServices.MyWeapons;
        if (myWeapons != null)
            foreach (var gun in myWeapons)
            {
                var weapon = gun.Value;
                if (weapon != null)
                {
                    if (alpha <= 25)
                    {
                        weapon.Render = fullColor;
                        weapon.ShadowStrength = 0;
                    }
                    weapon.Render = blankColor;
                    weapon.ShadowStrength = 1;
                    Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_clrRender");

                    if (weapon.DesignerName == "weapon_c4")
                    {
                        // Server.PrintToChatAll($"C4 Glow values: {weapon.RenderMode}, {weapon.RenderFX}, {weapon.Glow.GlowColor}, {weapon.Glow.GlowColorOverride}, {weapon.Glow.GlowType}");

                        // if (weapon.CRenderComponent != null)
                        // {
                        //     weapon.CRenderComponent.EnableRendering = false;
                        //     Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_bEnableRendering");
                        //     Server.PrintToChatAll(":3");
                        // }
                        // var toggle = Schema.GetRef<int>(weapon.Handle, "CC4", "m_blinktoggle");
                        // toggle = 0;

                        //Utilities.SetStateChanged(weapon, "CC4", "m_blinktoggle");
                    }
                }
            }
    }


    private void UpdateVisibilityBar(CCSPlayerPawn player, float visibilityLevel)
    {
        if (visibilityLevel >= 1.0f)
            return;

        const int total_lines = 16;
        int visibility_level_in_lines = (int)(visibilityLevel * total_lines);

        string visibilityText = "[";
        for (int line = 0; line < total_lines; line++)
            visibilityText += line <= visibility_level_in_lines ? "█" : "░";
        visibilityText += "]";

        player.OriginalController.Value.PrintToCenterHtml(visibilityText, 2);
    }

    private CCSPlayerPawn? GetPlayerByName(string playerName)
    {
        var controllers = Utilities.GetPlayers();

        foreach (CCSPlayerController ctrl in controllers)
        {
            var name = ctrl.PlayerName;

            if (name.Equals(playerName, StringComparison.OrdinalIgnoreCase))
                return ctrl.PlayerPawn.Value;
            if (name.Contains(playerName, StringComparison.OrdinalIgnoreCase))
                return ctrl.PlayerPawn.Value;
        }

        return null;
    }
}
