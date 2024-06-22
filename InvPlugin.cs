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

namespace InvisibilityPlugin;

public class InvisibilityPlugin : BasePlugin
{
    // private Dictionary<CCSPlayerPawn, Timer> playerVisibilityTimers = new Dictionary<CCSPlayerPawn, Timer>();
    private Dictionary<CCSPlayerPawn, float> playerVisibilityLevels = new Dictionary<CCSPlayerPawn, float>();
    // 0.0 = invisible, 1.0 = visible, in between = partially visible

    public override string ModuleName => "InvisibilityPlugin";
    public override string ModuleVersion => "1.0";
    public override string ModuleAuthor => "Mrec";

    private const int INVISIBILITY_CHECK_INTERVAL = 8;
    private const float INVISIBILITY_GAIN_PER_SECOND = 0.5f;
    private const float INVISIBILITY_GAIN = INVISIBILITY_GAIN_PER_SECOND / INVISIBILITY_CHECK_INTERVAL;

    private static int defaultC4GlowRange = -1;

    public override void Load(bool hotReload)
    {
        RegisterListener<OnTick>(() =>
        {
            if (Server.TickCount % INVISIBILITY_CHECK_INTERVAL != 0)
                return;
            foreach (var player in playerVisibilityLevels)
            {
                if (player.Key == null)
                    continue;
                if (player.Key.OriginalController.Value == null)
                    continue;

                var newValue = player.Value < 1.0f ? player.Value + INVISIBILITY_GAIN : 1.0f;
                playerVisibilityLevels[player.Key] = newValue;
                SetPlayerVisibilityLevel(player.Key.OriginalController.Value, newValue);
                UpdateVisibilityBar(player.Key, newValue);
            }
        });

        RegisterEventHandler<EventPlayerSound>((@event, info) =>
        {
            MakeTemporaryVisible(@event.Userid);
            return HookResult.Continue;
        });

        RegisterEventHandler<EventPlayerHurt>((@event, info) =>
        {
            MakeTemporaryVisible(@event.Userid);
            return HookResult.Continue;
        });

        RegisterEventHandler<EventWeaponFire>((@event, info) =>
        {
            MakeTemporaryVisible(@event.Userid);
            return HookResult.Continue;
        });

        Console.WriteLine("InvisibilityPlugin loaded.");

        if (hotReload == true)
        {
            Server.PrintToChatAll("Im here hi!!! just hotreloaded");
            defaultC4GlowRange = -1;
        }
    }

    public override void Unload(bool hotReload)
    {
        RemoveCommand("invs", (p, i) => { });
        Console.WriteLine("InvisibilityPlugin unloaded.");
    }

    [ConsoleCommand("invs", "toggle invisibility for a player.")]
    [CommandHelper(minArgs: 1, usage: "/invs <playername>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
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

        ToggleInvisibility(targetPlayer);
    }

    private void MakeTemporaryVisible(CCSPlayerController player)
    {
        if (player == null)
            return;
        if (!playerVisibilityLevels.ContainsKey(player.PlayerPawn.Value))
            return;
        playerVisibilityLevels[player.PlayerPawn.Value] = -1.0f; // will be visible for a few seconds or so
        SetPlayerVisibilityLevel(player, 0.0f);
    }

    private void ToggleInvisibility(CCSPlayerPawn player)
    {
        if (playerVisibilityLevels.ContainsKey(player))
        {
            MakeTemporaryVisible(player.OriginalController.Value); // make forever visible :3
            playerVisibilityLevels.Remove(player);
            Console.WriteLine($"Invisibility disabled for player {player.Controller.Value?.PlayerName}.");
        }
        else
        {
            playerVisibilityLevels[player] = 1.0f; // Fully invisible + added to visibility list
            Console.WriteLine($"Invisibility enabled for player {player.Controller.Value?.PlayerName}.");
        }
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

        if (playerPawnValue != null && playerPawnValue.IsValid)
        {
            playerPawnValue.Render = fadeColor;
            Utilities.SetStateChanged(playerPawnValue, "CBaseModelEntity", "m_clrRender");
        }

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
                    weapon.Render = fadeColor;
                    weapon.ShadowStrength = visibilityLevel;
                    Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_clrRender");

                    if (weapon.DesignerName == "weapon_c4")
                    {
                        // welcome to my world of failed bomb flickering hacks :3
                        // if (defaultC4GlowRange == -1)
                        // {
                        //     defaultC4GlowRange = weapon.Glow.GlowRange;
                        //     Server.PrintToChatAll($"C4 Glow values: {weapon.RenderMode}, {weapon.RenderFX}, {weapon.Glow.GlowColor}, {weapon.Glow.GlowColorOverride}, {weapon.Glow.GlowType}");
                        // }

                        // // weapon.Glow.GlowColorOverride = fadeColor;
                        // weapon.RenderMode = visibilityLevel <= 1.0f ? RenderMode_t.kRenderNone : RenderMode_t.kRenderNormal;
                        // weapon.RenderFX = visibilityLevel <= 1.0f ? RenderFx_t.kRenderFxNone : RenderFx_t.kRenderFxPulseFastWide;
                        // Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_nRenderMode");
                        // Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_nRenderFX");
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
            visibilityText += line <= visibility_level_in_lines ? "░" : "█";
        visibilityText += "]";

        // Display visibility level on the player's HUD ?
        player.OriginalController.Value.PrintToCenterHtml(visibilityText, 3);
    }

    private CCSPlayerPawn? GetPlayerByName(string playerName)
    {
        var controllers = Utilities.GetPlayers();

        foreach (CCSPlayerController ctrl in controllers)
        {
            var name = ctrl.PlayerName;

            if (name.Equals(playerName, StringComparison.OrdinalIgnoreCase))
                return ctrl.PlayerPawn.Value;
        }

        return null;
    }
}
