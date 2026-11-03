using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using HarmonyLib;

namespace ResourceStoryMod
{
    public sealed class MillsageMod : IMod
    {
        public const string Id = "local.millsage.story";
        private ModManifest manifest;
        private GameObject host;
        private Core runtime;
        private ReleaseDriver driver;
        private readonly Harmony frontend=new Harmony("local.millsage.story.frontend");
        public void Init(ModManifest value) { manifest=value; }
        public void OnEnable()
        {
            if(host!=null)return;
            if(manifest==null || manifest.ID!=Id || !ModLoader.IsModEnabled(Id))
                throw new InvalidOperationException("米尔赛奇只能通过游戏内MOD菜单启用。");
            try {
                // Native Init/OnEnable runs in the menu. Harmony's first patch
                // can run a game's type initializer, so defer gameplay hooks
                // until the real inventory scene exists.
                ReleaseChinese.Install(frontend);
                host=new GameObject("Millsage.NativeRuntime");
                UnityEngine.Object.DontDestroyOnLoad(host);
                driver=host.AddComponent<ReleaseDriver>();
                SceneManager.sceneLoaded+=SceneLoaded;
                UnityEngine.Debug.Log("[米尔赛奇] 正式版原生模组已启用；保留游戏语言设置。");
            } catch { OnDisable();throw; }
        }
        private void SceneLoaded(Scene scene,LoadSceneMode mode)
        {
            if(scene.name!="InventoryScene")return;
            try {
                if(runtime==null){runtime=new Core();runtime.OnInitializeMelon();driver.Runtime=runtime;}
                foreach(var directory in Resources.FindObjectsOfTypeAll<ModItemDirectory>())runtime.Register(directory);
            } catch(Exception error) { UnityEngine.Debug.LogError("[米尔赛奇] 店铺接线失败："+error);OnDisable(); }
        }
        public void OnDisable()
        {
            try { runtime?.OnApplicationQuit();runtime?.OnDeinitializeMelon(); }
            finally {
                SceneManager.sceneLoaded-=SceneLoaded;
                frontend.UnpatchAll(frontend.Id);
                if(runtime!=null)runtime.HarmonyInstance.UnpatchAll(runtime.HarmonyInstance.Id);
                if(host!=null)UnityEngine.Object.Destroy(host);
                host=null;runtime=null;Core.Instance=null;
            }
        }
    }
    public sealed class ReleaseDriver : MonoBehaviour
    {
        internal Core Runtime;
        private void Update() { ReleaseChinese.RefreshWhenReady();if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="InventoryScene"&&PlayerStore.HasInstance)Runtime?.OnUpdate(); }
        private void LateUpdate() => Runtime?.OnLateUpdate();
        private void OnGUI() => Runtime?.OnGUI();
    }
}
