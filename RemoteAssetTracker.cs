using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.ResourceManagement.ResourceProviders;


#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace SeamlessCatalogs
{
    [ExecuteAlways]
    public class RemoteAssetTracker : MonoBehaviour
    {
        [SerializeField] List<RemoteLoadRecord> Records = new();
        [SerializeField] List<RemotePrefabModificationLoadRecord> PrefabModRecords = new();
        [SerializeField] string AssetPath;
        [SerializeField] List<RemotePrefab> Prefabs = new();

#if UNITY_EDITOR
        public const string REMOTE_STR = "REMOTE_ASSET_TRACKER[9739799856]";
        public static RemoteAssetTracker FindOrAddCurrent()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                RemoteAssetTracker o = prefabStage.prefabContentsRoot.transform.Find(REMOTE_STR)?.gameObject.GetComponent<RemoteAssetTracker>();
                if (o != null) return o;
                o = new GameObject(REMOTE_STR, typeof(RemoteAssetTracker)).GetComponent<RemoteAssetTracker>();
                o.transform.SetParent(prefabStage.prefabContentsRoot.transform);
                o.AssetPath = prefabStage.assetPath;
                return o;
            }

            Scene activeScene = EditorSceneManager.GetActiveScene();
            var rootGobjs = activeScene.GetRootGameObjects();
            foreach (var gobj in rootGobjs)
                if (gobj.name == REMOTE_STR)
                    return gobj.GetComponent<RemoteAssetTracker>();

            // Gotta make one for this scene
            RemoteAssetTracker o2 = new GameObject(REMOTE_STR, typeof(RemoteAssetTracker)).GetComponent<RemoteAssetTracker>();
            o2.AssetPath = activeScene.path;
            return o2;
        }

        private void Awake()
        {
            HideFlags h = HideFlags.None;
            this.hideFlags = h;
            this.gameObject.hideFlags = h;
            this.transform.hideFlags = h;
        }



        bool inPrefab;
        bool isPrefabInstance;
        private void OnEnable()
        {
            inPrefab = PrefabUtility.IsPartOfPrefabInstance(gameObject) || PrefabStageUtility.GetPrefabStage(gameObject) != null;
            isPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(gameObject);


            if (!inPrefab)
            {
                EditorSceneManager.sceneSaving += editorSceneManager_sceneSaving;
                EditorSceneManager.sceneSaved += editorSceneManager_sceneSaved;
            }
            else
            {
                if (!isPrefabInstance)
                {
                    PrefabStage.prefabSaving += prefabStage_prefabSaving;
                    PrefabStage.prefabSaved += prefabStage_prefabSaved;
                }
            }

            NullOutRecords(); // when a domain reload occurs, we can't figure out what obj is local or remote ...
            FillInRecords();
        }

        private async void prefabStage_prefabSaved(GameObject obj)
        {
            if (loading) return;
            foreach (var rp in obj.GetComponentsInChildren<RemotePrefab>(true))
                if (rp.gameObject != obj)
                    await rp.LoadAsset();

            FillInRecords();
        }

        private void prefabStage_prefabSaving(GameObject obj)
        {
            if (loading) return;
            RecordRemoteAssets(new[] { obj });
            List<GameObject> chils = new();
            for (int i = 0; i < obj.transform.childCount; i++)
                chils.Add(obj.transform.GetChild(i).gameObject);
            if (chils.Count > 0)
                RecordRemotePrefabModifications(chils);
            NullOutRecords();

            Prefabs.Clear();
            foreach (var rp in obj.GetComponentsInChildren<RemotePrefab>(true))
                if (rp.gameObject != obj)
                {
                    Prefabs.Add(rp);
                    rp.NullRefs();
                }
        }
        private void editorSceneManager_sceneSaving(Scene scene, string path)
        {
            if (loading) return;
            GameObject[] rootGobjs = scene.GetRootGameObjects().Where(rg => PrefabStageUtility.GetPrefabStage(rg) == null).ToArray();
            RecordRemoteAssets(rootGobjs);
            RecordRemotePrefabModifications(rootGobjs);
            NullOutRecords();

            Prefabs.Clear();
            foreach (var gobj in rootGobjs)
            {
                foreach (var rp in gobj.GetComponentsInChildren<RemotePrefab>(true))
                {
                    Prefabs.Add(rp);
                    rp.NullRefs();
                }
                foreach (var rat in gobj.GetComponentsInChildren<RemoteAssetTracker>(true))
                    if (rat != this) rat.NullOutRecords();
            }
        }
        public async void editorSceneManager_sceneSaved(Scene scene)
        {
            if (loading) return;
            GameObject[] rootGobjs = scene.GetRootGameObjects().Where(rg => PrefabStageUtility.GetPrefabStage(rg) == null).ToArray();

            foreach (var gobj in rootGobjs)
                foreach (var rat in gobj.GetComponentsInChildren<RemoteAssetTracker>(true))
                    if (rat != this) rat.FillInRecords();

            FillInRecords();
        }

        private void RecordRemoteAssets(IEnumerable<GameObject> gobjs)
        {
            if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || loading) return;

            List<UnityEngine.Object> dirtComps = new List<UnityEngine.Object>();
            foreach (GameObject rootGobj in gobjs)
                foreach (var c in rootGobj.GetComponentsInChildren<Component>(true))
                    if (EditorUtility.IsDirty(c))
                        dirtComps.Add(c);

            if (!inPrefab)
            {
                dirtComps.Add(typeof(RenderSettings).GetMethod("GetRenderSettings", (System.Reflection.BindingFlags)60).Invoke(null, null) as UnityEngine.Object);
            }

            foreach (var dc in dirtComps)
            {
                if (dc == null || dc is RemoteAssetTracker) continue;

                if (PrefabUtility.IsPartOfPrefabInstance(dc))
                    continue;

                using var so = new SerializedObject(dc);
                var prop = so.GetIterator();
                while (prop.NextVisible(true))
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (prop.objectReferenceValue == null) continue;
                    if (prop.name == "m_Script") continue;
                    if (!ExternalCatalog.RemoteAssets.TryGetValue(prop.objectReferenceValue, out List<CatalogEntry> entries))
                        continue;

                    CatalogEntry entry = entries[0];

                    RemoteLoadRecord record = Records.FirstOrDefault(r => r.Comp == dc && r.PropPath == prop.propertyPath);
                    if (record != null)
                        record.SetEntry(entry);
                    else
                    {
                        record = new RemoteLoadRecord(dc, prop.propertyPath);
                        record.SetEntry(entry);
                        Records.Add(record);
                    }

                }
            }

            // clear out old / bad records
            foreach (var r in Records.ToArray())
            {
                if (r.Comp == null)
                {
                    Records.Remove(r); continue;
                }
                using var so = new SerializedObject(r.Comp);
                var prop = so.FindProperty(r.PropPath);

                if (prop == null || prop.objectReferenceValue == null)
                {
                    Records.Remove(r); continue;
                }
                if (!ExternalCatalog.RemoteAssets.TryGetValue(prop.objectReferenceValue, out List<CatalogEntry> entries))
                {
                    Records.Remove(r); continue;
                }
            }

            EditorUtility.SetDirty(this);
        }

        [ContextMenu("nully")]
        private void NullOutRecords()
        {
            foreach (var r in Records)
            {
                if (r.Comp == null) continue;

                using var so = new SerializedObject(r.Comp);
                var prop = so.FindProperty(r.PropPath);
                if (prop == null) continue;
                prop.objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(r.Comp);
            }
            if (!isPrefabInstance)
                foreach (var r in PrefabModRecords)
                {
                    if (r.Comp == null) continue;
                    var mods = PrefabUtility.GetPropertyModifications(r.PrefabRoot);
                    var mod = mods.First(m => m.target == r.SrcComp && m.propertyPath == r.PropPath);
                    mod.objectReference = null;
                    PrefabUtility.SetPropertyModifications(r.PrefabRoot, mods);
                    EditorUtility.SetDirty(r.Comp);
                }
            else
                foreach (var r in PrefabModRecords)
                {
                    if (r.Comp == null) continue;
                    using var so = new SerializedObject(r.Comp);
                    var prop = so.FindProperty(r.PropPath);
                    if (prop == null) continue;
                    prop.objectReferenceValue = null;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(r.Comp);
                }
        }
        bool loading;
        public async void FillInRecords()
        {
            if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling) return;

            loading = true;

            // wait for all cats
            foreach (var r in Records)
            {
                ExternalCatalog cat;
                while (true)
                {
                    cat = ExternalCatalog.ActiveCatalogs.Values.FirstOrDefault(cat => cat.Name == r.CatalogName);
                    if (cat != null) break;
                    await Task.Delay(250);
                }
            }
            // also prefab's cats
            foreach (var p in Prefabs)
            {
                if (p == null) continue;
                ExternalCatalog cat;
                while (true)
                {
                    cat = ExternalCatalog.ActiveCatalogs.Values.FirstOrDefault(cat => cat.Name == p.CatalogName);
                    if (cat != null) break;
                    await Task.Delay(250);
                }
            }
            while (!SeamlessCatalogPlugin.ShouldLoadAssets)
                await Task.Delay(250);

            List<Task> assetLoads = Prefabs.Select(p => p.LoadAsset()).ToList();
            foreach (var r in Records)
            {
                RemoteLoadRecord curRec = r;
                ExternalCatalog cat;
                cat = ExternalCatalog.ActiveCatalogs.Values.FirstOrDefault(cat => cat.Name == r.CatalogName);
                CatalogEntry entry = cat.AllEntries.First(e => e.Location != null && e.Location.PrimaryKey == r.PrimaryKey);
                assetLoads.Add(((Func<Task>)(async () =>
                {
                    UnityEngine.Object obj = await entry.LoadAssetAsync();

                    using var so = new SerializedObject(r.Comp);
                    var prop = so.FindProperty(r.PropPath);
                    if (prop == null) return;

                    prop.objectReferenceValue = obj;
                    so.ApplyModifiedPropertiesWithoutUndo();
                })).Invoke());
            }
            foreach (var r in PrefabModRecords)
            {
                ExternalCatalog cat = ExternalCatalog.ActiveCatalogs.Values.FirstOrDefault(cat => cat.Name == r.CatalogName);
                CatalogEntry entry = cat.AllEntries.First(e => e.Location != null && e.Location.PrimaryKey == r.PrimaryKey);
                assetLoads.Add(entry.LoadAssetAsync());
            }

            await Task.WhenAll(assetLoads);

            foreach (var r in PrefabModRecords)
            {
                if (r.Comp == null) continue;

                ExternalCatalog cat = ExternalCatalog.ActiveCatalogs.Values.FirstOrDefault(cat => cat.Name == r.CatalogName);
                CatalogEntry entry = cat.AllEntries.First(e => e.Location != null && e.Location.PrimaryKey == r.PrimaryKey);
                UnityEngine.Object obj = entry.LoadedAsset;


                using var so = new SerializedObject(r.Comp);
                var prop = so.FindProperty(r.PropPath);

                prop.objectReferenceValue = obj;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            loading = false;
        }
        private void RecordRemotePrefabModifications(IEnumerable<GameObject> gobjs)
        {
            if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || loading) return;
            PrefabModRecords.Clear();
            foreach (var rootGobj in gobjs)
            {
                foreach (var t in rootGobj.GetComponentsInChildren<Transform>(true))
                {
                    if (t == transform) continue;
                    if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;

                    var mods = PrefabUtility.GetPropertyModifications(t.gameObject);
                    foreach (PropertyModification mod in mods)
                    {
                        if (mod.objectReference == null) continue;

                        if (ExternalCatalog.RemoteAssets.TryGetValue(mod.objectReference, out var entries))
                        {
                            CatalogEntry entry = entries[0];

                            // gotta find local version of target
                            Component srcComp = (Component)mod.target;
                            int targIdx = Array.IndexOf(srcComp.gameObject.GetComponents<Component>(), srcComp);
                            List<int> path = new();
                            Transform srcT = srcComp.transform;
                            while (true)
                            {
                                path.Insert(0, srcT.GetSiblingIndex());
                                srcT = srcT.parent;

                                if (srcT.parent == null) break;
                            }
                            Transform realT = t;
                            foreach (var idx in path)
                                realT = realT.GetChild(idx);

                            var modRecord = new RemotePrefabModificationLoadRecord(t.gameObject, srcComp, realT.gameObject.GetComponents<Component>()[targIdx], mod.propertyPath);
                            modRecord.SetEntry(entry);
                            PrefabModRecords.Add(modRecord);
                        }
                    }
                }
            }
            EditorUtility.SetDirty(this);
        }

        private void OnDisable()
        {
            if (!inPrefab)
            {
                EditorSceneManager.sceneSaving -= editorSceneManager_sceneSaving;
                EditorSceneManager.sceneSaved -= editorSceneManager_sceneSaved;
            }
            else
            {
                if (!isPrefabInstance)
                {
                    PrefabStage.prefabSaving -= prefabStage_prefabSaving;
                    PrefabStage.prefabSaved -= prefabStage_prefabSaved;
                }
            }
        }

        public static string GetSimpleType(UnityEngine.Object obj)
        {
            if (typeof(MonoBehaviour).IsAssignableFrom(obj.GetType()))
                return nameof(MonoBehaviour);
            if (typeof(ScriptableObject).IsAssignableFrom(obj.GetType()))
                return nameof(MonoBehaviour);
            return obj.GetType().Name;
        }
#endif


        [Serializable]
        class RemoteLoadRecord
        {
            public RemoteLoadRecord(UnityEngine.Object c, string propPath)
            {
                Comp = c;
                PropPath = propPath;
            }
#if UNITY_EDITOR
            public void SetEntry(CatalogEntry e)
            {
                CatalogName = e.Catalog.Name;
                PrimaryKey = e.Location.PrimaryKey;

                AssetName = e.LoadedAsset.name;
                AssetType = GetSimpleType(e.LoadedAsset);
                PrimaryBundle = e.Location.Dependencies[0].InternalId.Split(":\\")[1];
                Dependancies = e.Location.Dependencies.Select(d => d.InternalId).ToArray();

                if (e.LoadedAsset is MonoBehaviour mb)
                    MonoScript = UnityEditor.MonoScript.FromMonoBehaviour(mb);
                if (e.LoadedAsset is ScriptableObject so)
                    MonoScript = UnityEditor.MonoScript.FromScriptableObject(so);
                if (MonoScript != null && string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(MonoScript)))
                    MonoScript = Resources.FindObjectsOfTypeAll<MonoScript>().FirstOrDefault(ms => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(ms)) && ms.name == MonoScript.name);
            }
#endif

            public UnityEngine.Object Comp;
            public string PropPath;

            // these change
            public string CatalogName;
            public string AssetName;
            public string AssetType;
            public string PrimaryKey;
            public string PrimaryBundle;
            public string[] Dependancies;
            public UnityEngine.Object MonoScript;
        }

        [Serializable]
        class RemotePrefabModificationLoadRecord
        {
            public RemotePrefabModificationLoadRecord(GameObject g, Component srcComp, Component c, string propPath)
            {
                PrefabRoot = g;
                Comp = c; SrcComp = srcComp;
                PropPath = propPath;
            }
#if UNITY_EDITOR
            public void SetEntry(CatalogEntry e)
            {
                CatalogName = e.Catalog.Name;
                PrimaryKey = e.Location.PrimaryKey;

                AssetName = e.LoadedAsset.name;
                AssetType = GetSimpleType(e.LoadedAsset);
                PrimaryBundle = e.Location.Dependencies[0].InternalId.Split(":\\")[1];
                Dependancies = e.Location.Dependencies.Select(d => d.InternalId).ToArray();

                if (e.LoadedAsset is MonoBehaviour mb)
                    MonoScript = UnityEditor.MonoScript.FromMonoBehaviour(mb);
                if (e.LoadedAsset is ScriptableObject so)
                    MonoScript = UnityEditor.MonoScript.FromScriptableObject(so);
                if (MonoScript != null && string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(MonoScript)))
                    MonoScript = Resources.FindObjectsOfTypeAll<MonoScript>().FirstOrDefault(ms => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(ms)) && ms.name == MonoScript.name);
            }
#endif

            public GameObject PrefabRoot;
            public Component Comp;
            public Component SrcComp;
            public string PropPath;

            // these change
            public string CatalogName;
            public string AssetName;
            public string AssetType;
            public string PrimaryKey;
            public string PrimaryBundle;
            public string[] Dependancies;
            public UnityEngine.Object MonoScript;
        }
    }
}