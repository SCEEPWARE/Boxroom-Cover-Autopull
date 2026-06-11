using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SteamShelf;
using UnityEngine;

namespace AutoPull;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("BOXROOM.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} is loaded!");
        
        Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll(typeof(FetchPatch));
    }

    [HarmonyPatch]
    class FetchPatch
    {
        // can't access it directly (class is internal)
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("SteamShelf.SteamGameCache");
            if (type == null) Logger.LogError("Can't access SteamGameCache class!");
            
            var method = AccessTools.Method(type, "TryLoadBoxArtAsync");
            if (method == null) Logger.LogError("Can't access TryLoadBoxArtAsync method!");
            else Logger.LogInfo("Patched TryLoadBoxArtAsync successfully!");
            
            return method;
        }

        // Needed for Awaitable to complete / else you get an error on second method invocation
        private static async Awaitable CompletedAsync()
        {
            await Awaitable.MainThreadAsync();
        }
        
        [HarmonyPrefix]
        static bool Prefix(SteamGameData data, ref Awaitable __result)
        {
            var fetcher = AccessTools.StaticFieldRefAccess<object>(typeof(SteamLibrarySystem), "fetcher");
            if(fetcher == null)
            {
                Debug.LogError("Couldn't access fetcher!");
                __result = CompletedAsync();
                return true;
            }
            
            var fetchMethod =
                AccessTools.Method(AccessTools.TypeByName("SteamShelf.SteamStoreFetcher"), "FetchBoxArtAsync");
            if (fetchMethod == null)
            {
                Debug.LogError("Couldn't access FetchBoxArtAsync method!");
                __result = CompletedAsync();
                return true;
            }

            var method = MethodInvoker.GetHandler(fetchMethod, true);
            method.Invoke(fetcher, data);
            __result = CompletedAsync();
            return false;
        }
    }
}