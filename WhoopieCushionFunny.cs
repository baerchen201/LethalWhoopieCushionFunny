using System;
using System.Collections;
using BepInEx;
using BepInEx.Logging;
using CSync.Extensions;
using CSync.Lib;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace WhoopieCushionFunny;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("com.sigurd.csync", "5.0.0")]
public class WhoopieCushionFunny : BaseUnityPlugin
{
    public static WhoopieCushionFunny Instance { get; private set; } = null!;
    internal static new ManualLogSource Logger { get; private set; } = null!;
    internal static Harmony? Harmony { get; set; }

    private SyncedEntry<bool> enable = null!;
    public bool Enable => enable.Value;
    private SyncedEntry<float> delay = null!;
    public float? Delay => delay.Value > 0f ? delay.Value : null;
    private SyncedEntry<ModAction> action = null!;
    public ModAction Action => action.Value;
    private SyncedEntry<ModAction2> action2 = null!;
    public ModAction2 Action2 => action2.Value;
    private SyncedEntry<ModPosition> position = null!;
    public ModPosition Position => position.Value;

    public enum ModAction
    {
        None,
        PlaySound,
        Explode,
        Vanish,
        PlaySoundAndExplode,
        PlaySoundAndVanish,
        PlaySoundAndExplodeAndVanish,
        ExplodeAndVanish,
    }

    public enum ModAction2
    {
        None = ModAction.None,
        PlaySound = ModAction.PlaySound,
        Explode = ModAction.Explode,
        PlaySoundAndExplode = ModAction.PlaySoundAndExplode,
    }

    public enum ModPosition
    {
        FollowItem,
        FollowTrigger,
        TriggerPosition,
    }

    private void Awake()
    {
        Logger = base.Logger;
        Instance = this;

        enable = Config.BindSyncedEntry("General", "Enable", false, "Enables or disables the mod");
        delay = Config.BindSyncedEntry(
            "Settings",
            "Delay",
            0f,
            "Delay in seconds until the specified action happens"
        );
        action = Config.BindSyncedEntry(
            "Settings",
            "Action",
            ModAction.PlaySound,
            "What action to perform after the specified delay"
        );
        action2 = Config.BindSyncedEntry(
            "Settings",
            "ActionNoDelay",
            ModAction2.None,
            "What action to perform immediately, ignoring the specified delay (This is ignored if there is no delay)"
        );
        position = Config.BindSyncedEntry(
            "Settings",
            "Position",
            ModPosition.TriggerPosition,
            "Where the action should be performed after the delay"
        );

        Harmony ??= new Harmony(MyPluginInfo.PLUGIN_GUID);
        Logger.LogDebug("Patching...");
        Harmony.PatchAll();
        Logger.LogDebug("Finished patching!");

        Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
    }

    [HarmonyPatch(typeof(WhoopieCushionItem), nameof(WhoopieCushionItem.ActivatePhysicsTrigger))]
    internal class TriggerPatch
    {
        // ReSharper disable once UnusedMember.Local
        private static bool Prefix(ref WhoopieCushionItem __instance, ref Collider other)
        {
            if (!other.TryGetComponent<PlayerControllerB>(out var player))
                return true;
            if (!Instance.Enable || player.isPlayerDead)
                return true;
            if (Instance.Delay == null)
                DoActionNow(
                    __instance,
                    __instance.transform.position,
                    other,
                    Instance.Action,
                    Instance.Position
                );
            else
            {
                if (Instance.Action2 != ModAction2.None)
                    DoActionNow(
                        __instance,
                        __instance.transform.position,
                        other,
                        (ModAction)Instance.Action2,
                        ModPosition.TriggerPosition
                    );
                DoActionAfter(
                    Instance.Delay.Value,
                    __instance,
                    __instance.transform.position,
                    other,
                    Instance.Action,
                    Instance.Position
                );
            }
            return false;
        }
    }

    public static void DoActionNow(
        WhoopieCushionItem whoopieCushion,
        Vector3 origin,
        Collider other,
        ModAction action,
        ModPosition position
    )
    {
        switch (action)
        {
            case ModAction.None:
                return;
            case ModAction.Vanish
            or ModAction.PlaySoundAndVanish
            or ModAction.ExplodeAndVanish
            or ModAction.PlaySoundAndExplodeAndVanish when position is ModPosition.FollowItem:
                position = ModPosition.TriggerPosition;
                break;
        }

        var worldPosition = position switch
        {
            ModPosition.FollowItem => whoopieCushion.transform.position,
            ModPosition.FollowTrigger => other.transform.position,
            _ => origin,
        };

        if (
            action
            is ModAction.PlaySound
                or ModAction.PlaySoundAndVanish
                or ModAction.PlaySoundAndExplode
                or ModAction.PlaySoundAndExplodeAndVanish
        )
        {
            if (
                Vector3.Distance(
                    whoopieCushion.lastPositionAtFart,
                    whoopieCushion.transform.position
                ) > 2f
            )
            {
                whoopieCushion.timesPlayingInOneSpot = 0;
            }

            whoopieCushion.timesPlayingInOneSpot++;
            whoopieCushion.lastPositionAtFart = whoopieCushion.transform.position;

            GameObject audioSourceObject = new GameObject();
            var audioSource = audioSourceObject.AddComponent<AudioSource>();
            whoopieCushion.whoopieCushionAudio.CloneOnto(ref audioSource);
            audioSourceObject.transform.position = worldPosition;
            audioSourceObject.transform.parent = position switch
            {
                ModPosition.FollowItem => whoopieCushion.transform,
                ModPosition.FollowTrigger => other.transform,
                _ => audioSourceObject.transform.parent,
            };

            RoundManager.PlayRandomClip(
                audioSource,
                whoopieCushion.fartAudios,
                randomize: true,
                1f,
                -1
            );
            RoundManager.Instance.PlayAudibleNoise(
                worldPosition,
                8f,
                0.8f,
                whoopieCushion.timesPlayingInOneSpot,
                whoopieCushion.isInShipRoom && StartOfRound.Instance.hangarDoorsClosed,
                101158
            );
            audioSourceObject
                .AddComponent<_MonoBehaviour>()
                .StartCoroutine(_CleanUpAfterPlayingAudio(audioSource, audioSourceObject));
        }

        if (
            action
            is ModAction.PlaySoundAndExplode
                or ModAction.PlaySoundAndExplodeAndVanish
                or ModAction.Explode
                or ModAction.ExplodeAndVanish
        )
            Landmine.SpawnExplosion(worldPosition, true, 5.7f, 6f);

        if (
            action
            is ModAction.Vanish
                or ModAction.PlaySoundAndVanish
                or ModAction.ExplodeAndVanish
                or ModAction.PlaySoundAndExplodeAndVanish
        )
            Destroy(whoopieCushion.gameObject);
    }

    public static void DoActionAfter(
        float delay,
        WhoopieCushionItem whoopieCushion,
        Vector3 origin,
        Collider other,
        ModAction action,
        ModPosition position
    )
    {
        if (action == ModAction.None)
            return;
        switch (position)
        {
            case ModPosition.FollowItem:
                whoopieCushion.StartCoroutine(
                    _DoActionAfter(delay, whoopieCushion, origin, other, action, position)
                );
                break;
            case ModPosition.FollowTrigger
            or ModPosition.TriggerPosition:
            default:
                var coroutineObject = new GameObject { transform = { parent = other.transform } };
                coroutineObject
                    .AddComponent<_MonoBehaviour>()
                    .StartCoroutine(
                        _DoActionAfter(
                            delay,
                            whoopieCushion,
                            origin,
                            other,
                            action,
                            position,
                            coroutineObject
                        )
                    );
                break;
        }
    }

    internal static IEnumerator _DoActionAfter(
        float delay,
        WhoopieCushionItem whoopieCushion,
        Vector3 origin,
        Collider other,
        ModAction action,
        ModPosition position,
        GameObject? cleanUp = null
    )
    {
        yield return new WaitForSeconds(delay);
        try
        {
            DoActionNow(whoopieCushion, origin, other, action, position);
        }
        catch (Exception e)
        {
            Logger.LogError(e);
        }
        if (cleanUp)
            Destroy(cleanUp);
    }

    internal static IEnumerator _CleanUpAfterPlayingAudio(
        AudioSource audioSource,
        GameObject cleanUp
    )
    {
        yield return new WaitUntil(() => !audioSource.isPlaying);
        Destroy(cleanUp);
    }

    [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.AllowPlayerDeath))]
    internal class DoubleDeathFix
    {
        // ReSharper disable once UnusedMember.Local
        private static bool Prefix(ref PlayerControllerB __instance, ref bool __result)
        {
            if (__instance.isPlayerDead || !__instance.isPlayerControlled)
                return __result = false;
            return true;
        }
    }
}

internal static class AudioSourceExtensions
{
    internal static void CloneOnto(this AudioSource source, ref AudioSource other)
    {
        other.bypassEffects = source.bypassEffects;
        other.bypassListenerEffects = source.bypassListenerEffects;
        other.bypassReverbZones = source.bypassReverbZones;
        other.clip = source.clip;
        other.dopplerLevel = source.dopplerLevel;
        other.ignoreListenerPause = source.ignoreListenerPause;
        other.ignoreListenerVolume = source.ignoreListenerVolume;
        other.loop = source.loop;
        other.maxDistance = source.maxDistance;
        other.minDistance = source.minDistance;
        other.mute = source.mute;
        other.outputAudioMixerGroup = source.outputAudioMixerGroup;
        other.panStereo = source.panStereo;
        other.pitch = source.pitch;
        other.playOnAwake = source.playOnAwake;
        other.priority = source.priority;
        other.reverbZoneMix = source.reverbZoneMix;
        other.rolloffMode = source.rolloffMode;
        other.spatialBlend = source.spatialBlend;
        other.spatialize = source.spatialize;
        other.spatializePostEffects = source.spatializePostEffects;
        other.spread = source.spread;
        other.time = source.time;
        other.timeSamples = source.timeSamples;
        other.velocityUpdateMode = source.velocityUpdateMode;
        other.volume = source.volume;
    }
}

internal class _MonoBehaviour : MonoBehaviour { }
