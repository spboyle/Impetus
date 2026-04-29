using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;

namespace Impetus
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class ImpetusPlugin : BaseUnityPlugin
    {
        internal const string ModName = "Impetus";
        internal const string ModVersion = "1.1.0";
        internal const string Author = "CodeWarrior";
        private const string ModGUID = Author + "." + ModName;
        private static string ConfigFileName = ModGUID + ".cfg";
        internal static string ConnectionError = "";
        private readonly Harmony HarmonyInstance = new(ModGUID);
        public static readonly ManualLogSource ImpetusLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

        public static ConfigEntry<float> AccuracyMultiplier = null!;
        public static ConfigEntry<float> VelocityMultiplier = null!;
        public static ConfigEntry<float> SpearLaunchAngle = null!;
        public static ConfigEntry<float> SpearGravity = null!;

        private static readonly ConfigSync ConfigSync = new(ModGUID)
        { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
        public enum Toggle
        {
            On = 1,
            Off = 0
        }

        public void Awake()
        {
            bool saveOnSet = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;

            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On,
                "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);

            AccuracyMultiplier = config(
                "Spear",
                "Accuracy Multiplier", 0f,
                new ConfigDescription(
                    "Spear accuracy multiplier. Bigger number means aim is worse (more random). Zero means projectile goes exactly where you aim.\nRecommended: 0",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes{ShowRangeAsPercent = false}
                    )
                );
            VelocityMultiplier = config("Spear", "Velocity Multiplier", 2f, new ConfigDescription("Spear velocity multiplier.\nRecommended: 2", new AcceptableValueRange<float>(1f, 5f)));
            SpearGravity = config("Spear", "Gravity", 7f, new ConfigDescription("Change the spear's gravity. Vanilla is 5.\nRecommended: 7", new AcceptableValueRange<float>(1f, 30f)));
            SpearLaunchAngle = config("Spear", "Launch Angle", -1.5f, new ConfigDescription("The vertical angle at which the spear leaves after being thrown. Vanilla is 0.\nRecommended: -1.5", new AcceptableValueRange<float>(-5f, 0f)));

            Config.Save();
            if (saveOnSet)
            {
                Config.SaveOnConfigSet = saveOnSet;
            }

            Assembly assembly = Assembly.GetExecutingAssembly();
            HarmonyInstance.PatchAll(assembly);
        }

        #region ConfigOptions

        private static ConfigEntry<Toggle> _serverConfigLocked = null!;

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
            bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription =
                new(
                    description.Description +
                    (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
                    description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description,
            bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            [UsedImplicitly] public int? Order = null!;
            [UsedImplicitly] public bool? Browsable = null!;
            [UsedImplicitly] public string? Category = null!;
            [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer = null!;
            [UsedImplicitly] public bool? ShowRangeAsPercent = null!;
        }

        #endregion
    }

    [HarmonyPatch(typeof(Attack), "FireProjectileBurst")]
    public static class Attack_FireProjectileBurst_Patch
    {
        public static bool Prefix(ref Attack __instance)
        {
            if (!__instance.m_character.IsPlayer())
            {
                return true;
            }

            if (__instance is not { m_attackAnimation: "spear_throw", m_attackType: Attack.AttackType.Projectile })
            {
                return true;
            }

            ImpetusPlugin.ImpetusLogger.LogInfo($"Accuracy before: {__instance.m_projectileAccuracyMin} - {__instance.m_projectileAccuracy}");
            __instance.m_projectileVel *= ImpetusPlugin.VelocityMultiplier.Value;
            __instance.m_projectileVelMin *= ImpetusPlugin.VelocityMultiplier.Value;
            __instance.m_projectileAccuracy *= ImpetusPlugin.AccuracyMultiplier.Value;
            __instance.m_projectileAccuracyMin *= ImpetusPlugin.AccuracyMultiplier.Value;
            __instance.m_launchAngle = ImpetusPlugin.SpearLaunchAngle.Value;
            ImpetusPlugin.ImpetusLogger.LogInfo($"Accuracy after: {__instance.m_projectileAccuracyMin} - {__instance.m_projectileAccuracy}");

            return true;
        }
    }

    [HarmonyPatch(typeof(Projectile), "Setup")]
    public static class Projectile_Setup_Patch
    {
        public static bool Prefix(ref Projectile __instance, Character owner, Vector3 velocity, float hitNoise,
            HitData hitData, ItemDrop.ItemData item)
        {
            if (__instance == null)
            {
                return true;
            }

            if (!owner.IsPlayer())
            {
                return true;
            }

            if (item.m_shared.m_attack is not { m_attackAnimation: "spear_throw", m_attackType: Attack.AttackType.Projectile })
            {
                return true;
            }
            __instance.m_gravity = ImpetusPlugin.SpearGravity.Value;
            return true;
        }
    }
}
