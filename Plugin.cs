using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SteamShelf;
using SteamShelf.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
        
        Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        // patching
        harmony.PatchAll(typeof(FetchPatch));
        harmony.PatchAll(typeof(MenuPatch));
    }
    
    // Re-enabling the Fetch method
    [HarmonyPatch]
    class FetchPatch
    {
        // can't access it directly (class is internal and method is private)
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("SteamShelf.SteamGameCache");
            if (type == null) Logger.LogError("Can't access SteamGameCache class!");
            
            var method = AccessTools.Method(type, "TryLoadBoxArtAsync");
            if (method == null) Logger.LogError("Can't access TryLoadBoxArtAsync method!");
            
            return method;
        }

        // Needed for Awaitable to complete / else you get an error on second method invocation
        private static async Awaitable CompletedAsync()
        {
            await Awaitable.BackgroundThreadAsync();
        }
        
        [HarmonyPrefix]
        static bool Prefix(SteamGameData data, ref Awaitable __result, object __instance)
        {
            string path = Traverse.Create(__instance).Method("FullGamePath", data.AppId, "boxart.jpg").GetValue() as string; //check if there's a local file already
            if (File.Exists(path)) // it means an image was saved. Either cover was pulled at previous load, or user set custom image
            {
                return true; 
            }
            
            var fetcher = AccessTools.StaticFieldRefAccess<object>(typeof(SteamLibrarySystem), "fetcher");
            if(fetcher == null)
            {
                Debug.LogError("Couldn't access fetcher!");
                __result = CompletedAsync();
                return true; // fallback if fetcher fails for any reason
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
            /* then we save the data as custom image. I think that's what the wizard does by default (don't know I never used it).
               so means it should still load pictures when you disable the mod (if you had the game when you used it)
               and it should support workshop if you share your room. */
            SteamLibrarySystem.ApplyUserBoxArt(data);
            return false; // no need to call original method
        }
    }
    
    // Reset button
    [HarmonyPatch(typeof(Menu_NoArt))]
    class MenuPatch
    {
        private static GameObject _btnObj;
        private static Button resetButton;
        private static SteamGameData _data;
        private static Menu_NoArt __instance;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var method = AccessTools.Method(typeof(Menu_NoArt), "Start");
            if(method == null) Logger.LogError("Can't access Start method in Menu_NoArt");
            return method;
        }

        [HarmonyPostfix]
        static void Postfix(Menu_NoArt __instance)
        {
            // we clone the "Apply" button, and we change its transform / event listener
            // too lazy to try to figure out something with assetbundles
            MenuPatch.__instance = __instance;
            GameObject oBtnObj = (AccessTools.Field(typeof(Menu_NoArt), "applyButton").GetValue(__instance) as Button)?.gameObject;
            if(!oBtnObj)
            {
                Logger.LogError("Can't get Button data");
                return;
            }
            _btnObj = Instantiate(oBtnObj, oBtnObj.transform.parent);
            _btnObj.name = "ResetButton";
            _btnObj.transform.position = new Vector3(_btnObj.transform.position.x - 250f, _btnObj.transform.position.y, _btnObj.transform.position.z);
            
            resetButton = _btnObj.GetComponent<Button>();
            resetButton.onClick.RemoveAllListeners();
            resetButton.interactable = true;
            resetButton.onClick.AddListener(ClearFetchArt);
            resetButton.GetComponentInChildren<TMP_Text>().SetText("Reset Art");
        }

        private static void ClearFetchArt()
        {
            _data = (SteamGameData)AccessTools.Field(typeof(Menu_NoArt), "currentGame").GetValue(__instance);
            Logger.LogInfo("Trying to clear art...");
            // first we clear...
            var sgc = AccessTools.StaticFieldRefAccess<object>(typeof(SteamLibrarySystem), "ownedCache");
            string path = Traverse.Create(sgc).Method("FullGamePath", _data.AppId, "boxart.jpg").GetValue() as string;
            if (File.Exists(path))
            {
                if (path == null)
                {
                    Logger.LogError("Path is null!");
                }
                else
                {
                    File.Delete(path);
                    Logger.LogInfo($"Cleared user image for {_data.Name} ({_data.AppId})");   
                }
            }
            // then we fetch,
            var fetcher = AccessTools.StaticFieldRefAccess<object>(typeof(SteamLibrarySystem), "fetcher");
            if(fetcher == null)
            {
                Debug.LogError("Couldn't access fetcher!");
                return;
            }
            var fetchMethod =
                AccessTools.Method(AccessTools.TypeByName("SteamShelf.SteamStoreFetcher"), "FetchBoxArtAsync");
            if (fetchMethod == null)
            {
                Debug.LogError("Couldn't access FetchBoxArtAsync method!");
                return;
            }
            var method = MethodInvoker.GetHandler(fetchMethod, true);
            method.Invoke(fetcher, _data);
            SteamLibrarySystem.ApplyUserBoxArt(_data);
            // LET'S CLEARFETCH!
        }
    }
}