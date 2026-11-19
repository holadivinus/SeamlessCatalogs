using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeamlessCatalogs
{
    [ExecuteAlways]
    public class RemotePrefab : MonoBehaviour
    {
        public List<PrefabRemoteRef> BaseRefs = new();
        public string PrimaryKey;
        public string CatalogName;
        public List<string> Dependancies;
        public List<Object> UsedClasses = new();

#if UNITY_EDITOR
        private void Awake()
        {
        }

        private void OnEnable()
        {
            isLoaded = false;
            midLoad = false;

            if (PrefabUtility.GetCorrespondingObjectFromSource(gameObject) != null && !AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(gameObject)).StartsWith("Assets/RemotePrefabs/"))
            {
                NullRefs();
                LoadAsset();
            }
        }

        bool isLoaded;
        bool midLoad;
        public int KnownLoadDelay;
        public async Task LoadAsset()
        {
            if (midLoad)
                while (true)
                {
                    await Task.Delay(250);
                    if (!midLoad)
                        return;
                }

            if (isLoaded) return;
            isLoaded = true;
            midLoad = true;

            ExternalCatalog cat;
            while (true)
            {
                cat = ExternalCatalog.ActiveCatalogs.Values.FirstOrDefault(c => c.Name == CatalogName);
                if (cat != null) break;
                await Task.Delay(250);
            }

            while (!SeamlessCatalogPlugin.ShouldLoadAssets) 
                await Task.Delay(250);

            CatalogEntry catE = cat.AllEntries.First(e => e.AssetType != null && e.Location.PrimaryKey == PrimaryKey);
            var remoteGobj = (GameObject)await catE.LoadAssetAsync(false, loadDelay: KnownLoadDelay);
            KnownLoadDelay = catE.onloadRequiredDelay;
            if (this == null) return;
            foreach (var baseRef in BaseRefs)
            {
                if (baseRef.SceneObj == null) continue;

                Component remoteComp = GetTargComp(remoteGobj, baseRef);

                using var localSO = new SerializedObject(baseRef.SceneObj);
                var localProp = localSO.FindProperty(baseRef.propPath);
                using var remoteSO = new SerializedObject(baseRef.GetFinalObject(remoteComp));
                var remoteProp = remoteSO.FindProperty(baseRef.propPath);

                localSO.Update();
                if (localProp.objectReferenceValue == null)
                {
                    localProp.objectReferenceValue = remoteProp.objectReferenceValue;
                    localSO.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            midLoad = false;
        }

        private Component GetTargComp(GameObject root, PrefabRemoteRef baseRef)
        {
            foreach (var chilIdx in baseRef.gobjPath)
                root = root.transform.GetChild(chilIdx).gameObject;
            return root.GetComponents<Component>()[baseRef.compIdx];
        }

        public void NullRefs()
        {
            isLoaded = false; midLoad = false;
            foreach (var br in BaseRefs)
            {
                using var so = new SerializedObject(br.SceneObj);
                var prop = so.FindProperty(br.propPath);
                if (prop.objectReferenceValue != null && string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(prop.objectReferenceValue)))
                {
                    prop.objectReferenceValue = null;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
#endif
    }

    [System.Serializable]
    public class PrefabRemoteRef
    {
        public UnityEngine.Object SceneObj;
        public string AssetName;
        public string AssetType;
        public int[] gobjPath;
        public int compIdx;
        public string propPath; // path from SceneObj -> ref
        public string[] fullPropPath; // path from component -> SceneObj -> ref

#if UNITY_EDITOR
        public UnityEngine.Object GetFinalObject(Component c)
        {
            if (fullPropPath == null) return c;

            UnityEngine.Object o = c;
            for (int i = 0; i < fullPropPath.Length-1; i++)
            {
                string path = fullPropPath[i];

                using var so = new SerializedObject(o);
                var prop = so.FindProperty(path);
                o = prop.objectReferenceValue;
            }
            return o;
        }
#endif 
    }
}