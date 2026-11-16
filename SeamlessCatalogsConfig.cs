#if UNITY_EDITOR
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace SeamlessCatalogs
{
    public class SeamlessCatalogsConfig : ScriptableObject
    {
        [Serializable]
        public class CatalogConfig
        {
            [SerializeField] public string Name;
            [SerializeField] public bool Active;
            [NonSerialized] private bool _loaded;
            [NonSerialized] private bool _loading;
            [SerializeField] public string Path;

            [NonSerialized] IResourceLocator catalog;
            AsyncOperationHandle<IResourceLocator> theLoad;
            public void EditorUpdate()
            {
#if !MARROW_SDK
                if (!(File.Exists(Path) && Path.EndsWith(".json"))) 
                    Active = false;

                if (Active && !_loading && !_loaded) 
                {
                    _loading = true;

                    theLoad = Addressables.LoadContentCatalogAsync(Path);
                    theLoad.Completed += a =>
                    {
                        Debug.Log("Loaded Catalog " + Name);
                        _loading = false;
                        _loaded = true;
                        catalog = a.Result;


                        if (catalog is ResourceLocationMap rlm)
                        {
                            var newCat = ExternalCatalog.FromResourceLocationMap(rlm, Name);
                            newCat.CatalogPath = System.IO.Path.GetFullPath(Path);
                        }
                    };
                }
                if (!Active && _loaded)
                {
                    Addressables.RemoveResourceLocator(catalog);
                    _loaded = false;
                    if (ExternalCatalog.ActiveCatalogs.TryGetValue(catalog, out ExternalCatalog ec))
                        ec.Destroy();
                    Debug.Log("Unloaded Catalog " + Name);
                    
                }
#endif
            }
        }

        public List<CatalogConfig> Catalogs = new List<CatalogConfig>();


        public void Load()
        {
            string saveData = EditorPrefs.GetString("holadivinus.SavedConfig");

            if (!string.IsNullOrWhiteSpace(saveData))
                if (saveData.Contains(SPLIT))
                    foreach (var savedCat in saveData.Split(SPLIT2, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] sd = savedCat.Split(SPLIT, StringSplitOptions.None);
                        Catalogs.Add(new CatalogConfig
                        {
                            Name = sd[0],
                            Active = sd[1] == "True",
                            Path = sd[2]
                        });
                    }
                else EditorPrefs.SetString("holadivinus.SavedConfig", ""); 
        }
        private const string SPLIT = "-5n420-";
        private const string SPLIT2 = "-4g803-";
        private void OnDestroy() 
        {
            string s = "";
            foreach (var c in Catalogs)
            {
                if (string.IsNullOrEmpty(c.Name) || string.IsNullOrEmpty(c.Path))
                    continue;
                s += $"{c.Name}{SPLIT}{c.Active}{SPLIT}{c.Path}{SPLIT2}";
            }
            EditorPrefs.SetString("holadivinus.SavedConfig", s);
        }



        [HarmonyPatch]
        public static class p5
        {
            public static MethodBase TargetMethod() => AccessTools.Method("UnityEngine.AddressableAssets.AddressablesImpl:LoadContentCatalogAsync");

            public static void Postfix(string catalogPath, ref AsyncOperationHandle<IResourceLocator> __result)
            {
                __result.Completed += (cat) =>
                {
                    try
                    {
                        if (cat.Result is ResourceLocationMap rlm)
                            ExternalCatalog.FromResourceLocationMap(rlm, catalogPath);
                    }
                    catch (Exception e) 
                    {
                        Debug.LogException(e);
                    }
                };
            }
        }
        [HarmonyPatch]
        public static class p6
        {
            public static MethodBase TargetMethod() => AccessTools.Method("UnityEngine.AddressableAssets.AddressablesImpl:RemoveResourceLocator");

            public static void Postfix(IResourceLocator loc)
            {
                ExternalCatalog cat = null;
                if (loc == null) return;
                if (ExternalCatalog.ActiveCatalogs?.TryGetValue(loc, out cat) ?? false)
                    cat.Destroy();
            }
        }
        [HarmonyPatch]
        public static class p7
        {
            public static MethodBase TargetMethod() => AccessTools.Method("UnityEngine.AddressableAssets.AddressablesImpl:ClearResourceLocators");

            public static void Postfix() 
            {
                foreach (var cat in ExternalCatalog.ActiveCatalogs.Values.ToArray())
                    cat.Destroy();
            }
        }
    }
}
#endif