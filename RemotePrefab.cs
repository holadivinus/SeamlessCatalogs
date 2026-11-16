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
#if UNITY_EDITOR
        private void Awake()
        {
        }

        private void OnEnable()
        {
            if (PrefabUtility.IsPartOfPrefabInstance(this))
                LoadAsset();
        }

        bool isLoaded;
        bool midLoad;
        public async Task LoadAsset()
        {
            if (midLoad)
            {
                while (true)
                {
                    await Task.Delay(250);
                    if (!midLoad) return;
                }
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


            var remoteGobj = (GameObject)await cat.AllEntries.First(e => e.AssetType != null && e.Location.PrimaryKey == PrimaryKey).LoadAssetAsync();
            foreach (var baseRef in BaseRefs)
            {
                Component localComp = GetTargComp(gameObject, baseRef);
                Component remoteComp = GetTargComp(remoteGobj, baseRef);

                using var localSO = new SerializedObject(localComp);
                var localProp = localSO.FindProperty(baseRef.propPath);
                using var remoteSO = new SerializedObject(remoteComp);
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
                Component c = GetTargComp(gameObject, br);
                using var so = new SerializedObject(c);
                var prop = so.FindProperty(br.propPath);
                prop.objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
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
        public string propPath;
    }
}