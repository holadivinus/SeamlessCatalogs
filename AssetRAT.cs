using SeamlessCatalogs;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using System;







#if UNITY_EDITOR
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.Build.Content;
using UnityEditor.Build.Pipeline;
using UnityEditor;
#endif


class AssetRAT : ScriptableObject
#if UNITY_EDITOR
    ,ISerializationCallbackReceiver
#endif
{
    [SerializeField] List<RemoteAssetTracker.RemoteLoadRecord> Records = new();
    [SerializeField] List<RemoteAssetTracker.RemoteLoadRecord> PrefabModRecords = new(); // lazy
    [SerializeField] string AssetPath;
    [SerializeField] List<RemotePrefab> Prefabs = new();
    [SerializeField] bool IsPrefabsTracker;
    [SerializeField] List<UnityEngine.Object> localUsedObjects = new();

#if UNITY_EDITOR
    [MenuItem("CONTEXT/Object/Add Remote-Ref Support", false)]
    private static void ExecuteAction(MenuCommand command)
    {
        string assetPath = AssetDatabase.GetAssetPath(command.context);
        var existingRat = AssetDatabase.LoadAssetAtPath<AssetRAT>(assetPath);
        if (existingRat != null) return;

        var newRat = CreateInstance<AssetRAT>();
        AssetDatabase.AddObjectToAsset(newRat, assetPath);
        newRat.InitialSetup(command.context);
    }

    // Custom Verifier
    [MenuItem("CONTEXT/Object/Add Remote-Ref Support", true)]
    private static bool VerifyAction(MenuCommand command)
    {
        string assetPath = AssetDatabase.GetAssetPath(command.context);
        if (string.IsNullOrWhiteSpace(assetPath)) return false;
        var existingRat = AssetDatabase.LoadAssetAtPath<AssetRAT>(assetPath);
        return existingRat == null;
    }
    [InitializeOnLoadMethod]
    private static void OnDomainReload()
    {
        EditorApplication.delayCall += () =>
        {
            foreach (var r in Resources.FindObjectsOfTypeAll<AssetRAT>())
                if (r != null)
                {
                    r.loading = false;
                    r.loaded = false;
                    r.notNull = false;
                    r.LoadAssets();
                }
        };
    }
    public void OnAfterDeserialize()
    {
        LoadAssets();
    }

    public void AddDep(IBuildParameters buildParams, IDependencyData dependancyDatas)
    {
        if (!ObjectIdentifier.TryGetObjectIdentifier(this, out ObjectIdentifier id))
        {
            Debug.LogError("Couldn't get my own id?");
            return;
        }

        for (int i = 0; i < dependancyDatas.SceneInfo.Values.Count; i++)
        {
            SceneDependencyInfo si = dependancyDatas.SceneInfo.Values.ElementAt(i);
            bool dirt = false;
            foreach (var item in si.referencedObjects)
            {
                if (string.Compare(AssetDatabase.GUIDToAssetPath(item.guid), AssetPath) == 0)
                {
                    si.RSF("m_ReferencedObjects", si.referencedObjects.Append(id).ToArray());
                    dirt = true;
                }
            }
            if (dirt)
            {
                dependancyDatas.SceneInfo[dependancyDatas.SceneInfo.Keys.ElementAt(i)] = si;
                Debug.Log("injected dep!");
            }
        }
        
        return ;
    }

    bool loading = false;
    [SerializeField] bool notNull;
    bool loaded = false;
    private async void LoadAssets()
    {
        if (loading)
        {
            while (loading)
                await Task.Delay(250);
            return;
        }
        loading = true;
        loaded = false;


        // wait for all cats
        foreach (var lr in Records)
            while (!ExternalCatalog.ActiveCatalogs.Values.Any(c => c.Name == lr.CatalogName))
                await Task.Delay(250);
        if (!loading) return;
        await Task.WhenAll(Records.Select(lr => Load(lr)));
        loading = false;
        loaded = true;
        notNull = true;
    }
    private async Task Load(RemoteAssetTracker.RemoteLoadRecord lro)
    {
        RemoteAssetTracker.RemoteLoadRecord lr = lro;
        using var so = new SerializedObject(MyAsset);
        var prop = so.FindProperty(lr.PropPath);
        if (prop == null) return;
        if (prop.objectReferenceValue != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(prop.objectReferenceValue))) return;

        var cat = ExternalCatalog.ActiveCatalogs.Values.First(c => c.Name == lr.CatalogName);
        var c = cat.AllEntries.First(e => e.Location != null && e.Location.PrimaryKey == lr.PrimaryKey);
        var obj = await c.LoadAssetAsync();
        if (!loading) return;
        so.Update();
        prop = so.FindProperty(lr.PropPath);
        if (prop == null) return;
        prop.objectReferenceValue = obj;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public void OnBeforeSerialize() { }
    public void OnBeforeSave()
    {
        AssetPath = AssetDatabase.GetAssetPath(MyAsset);
        loading = false;
        CollectRecords();
        List<RemoteAssetTracker.RemoteLoadRecord> doLater = new();
        using var so = new SerializedObject(MyAsset);
        foreach (var lr in Records.ToArray())
        {
            var prop = so.FindProperty(lr.PropPath);
            if (prop == null) continue;
            if (prop.objectReferenceValue == null && loaded && notNull) 
            {
                Records.Remove(lr);
                continue;
            }
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(prop.objectReferenceValue))) continue;

            so.Update();
            prop.objectReferenceValue = null;
        }
        var it = so.GetIterator();
        while (it.Next(true))
        {
            if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
            if (it.objectReferenceValue == null) continue;
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(it.objectReferenceValue))) 
                it.objectReferenceValue = null;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        notNull = false;
        EditorApplication.delayCall += () => LoadAssets();
    }

    void CollectRecords()
    {
        if (!EditorUtility.IsDirty(MyAsset)) return;
        {
            using var so = new SerializedObject(MyAsset);
            var prop = so.GetIterator();
            localUsedObjects.Clear();
            while (prop.Next(true))
            {
                if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                var obj = prop.objectReferenceValue;
                if (obj == null) continue;
                if (!string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(obj)))
                {
                    localUsedObjects.Add(obj);
                    Records = Records.Where(r => r.PropPath != prop.propertyPath).ToList();
                    continue;
                }
                if (!ExternalCatalog.RemoteAssets.TryGetValue(obj, out List<CatalogEntry> entries)) continue;
                var lr = Records.FirstOrDefault(lr => lr.PropPath == prop.propertyPath);
                if (lr == null)
                {
                    lr = new RemoteAssetTracker.RemoteLoadRecord(MyAsset, prop.propertyPath);
                    Records.Add(lr);
                }
                lr.SetEntry(entries.First());
            }
        }
        if (MyAsset is Material m && Records.Any(r => r.PropPath == "m_Shader"))
        {
            // AUUGUHHHHGAAAA
            // mats with remote shaders are screwed
            MaterialTexPropList = new();
            MaterialIntPropList = new();
            MaterialFloatPropList = new();
            MaterialColorPropList = new();
            IsMaterial = true;

            using var so = new SerializedObject(m);
            var texArrSize = so.FindProperty("m_SavedProperties.m_TexEnvs.Array.size").intValue;
            for (int i = 0; i < texArrSize; i++)
            {
                MaterialTexProp p = (new()
                {
                    name = so.FindProperty($"m_SavedProperties.m_TexEnvs.Array.data[{i}].first").stringValue,
                    value = (Texture)so.FindProperty($"m_SavedProperties.m_TexEnvs.Array.data[{i}].second.m_Texture").objectReferenceValue,
                    scale = so.FindProperty($"m_SavedProperties.m_TexEnvs.Array.data[{i}].second.m_Scale").vector2Value,
                    offset = so.FindProperty($"m_SavedProperties.m_TexEnvs.Array.data[{i}].second.m_Offset").vector2Value,
                });
                MaterialTexPropList.Add(p);
                if (string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(p.value)))
                    p.value = null;
            }

            var intArrSize = so.FindProperty("m_SavedProperties.m_Ints.Array.size").intValue;
            for (int i = 0; i < intArrSize; i++)
            {
                MaterialIntProp p = (new()
                {
                    name = so.FindProperty($"m_SavedProperties.m_Ints.Array.data[{i}].first").stringValue,
                    value = so.FindProperty($"m_SavedProperties.m_Ints.Array.data[{i}].second").intValue,
                });
                MaterialIntPropList.Add(p);
            }

            var floatArrSize = so.FindProperty("m_SavedProperties.m_Floats.Array.size").intValue;
            for (int i = 0; i < floatArrSize; i++)
            {
                MaterialFloatProp p = (new()
                {
                    name = so.FindProperty($"m_SavedProperties.m_Floats.Array.data[{i}].first").stringValue,
                    value = so.FindProperty($"m_SavedProperties.m_Floats.Array.data[{i}].second").floatValue,
                });
                MaterialFloatPropList.Add(p);
            }

            var colorArrSize = so.FindProperty("m_SavedProperties.m_Colors.Array.size").intValue;
            for (int i = 0; i < colorArrSize; i++)
            {
                MaterialColorProp p = (new()
                {
                    name = so.FindProperty($"m_SavedProperties.m_Colors.Array.data[{i}].first").stringValue,
                    value = so.FindProperty($"m_SavedProperties.m_Colors.Array.data[{i}].second").colorValue,
                });
                MaterialColorPropList.Add(p);
            }
        }
        else IsMaterial = false;
    }
    

    void InitialSetup(UnityEngine.Object myAsset)
    {
        name = RemoteAssetTracker.REMOTE_STR;
        MyAsset = myAsset;
    }
#endif




    [SerializeField] UnityEngine.Object MyAsset;
    [SerializeField] bool IsMaterial;
    [SerializeField] List<MaterialTexProp> MaterialTexPropList;
    [SerializeField] List<MaterialIntProp> MaterialIntPropList;
    [SerializeField] List<MaterialFloatProp> MaterialFloatPropList;
    [SerializeField] List<MaterialColorProp> MaterialColorPropList;
    [Serializable]
    public class MaterialTexProp
    {
        public string name;
        public Texture value;
        public Vector2 scale;
        public Vector2 offset;
    }
    [Serializable]
    public class MaterialIntProp
    {
        public string name;
        public int value;
    }
    [Serializable]
    public class MaterialFloatProp
    {
        public string name;
        public float value;
    }
    [Serializable]
    public class MaterialColorProp
    {
        public string name;
        public Color value;
    }
}
