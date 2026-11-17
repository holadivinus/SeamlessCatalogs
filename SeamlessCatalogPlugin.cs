#if UNITY_EDITOR
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace SeamlessCatalogs
{
    [InitializeOnLoad]
    public static class SeamlessCatalogPlugin
    {
        private static HashSet<UnityEngine.Object> s_initializedProjectBrowsers = new HashSet<UnityEngine.Object>();
        private static HashSet<ProjectBrowserManager> s_projectBrowserManagers = new HashSet<ProjectBrowserManager>();

        static SeamlessCatalogPlugin()
        {
            //new BuiltCatalog(@"B:\GitHub\CatalogBorrower\BuiltPallets\StandaloneWindows64\holadivinus.BorrowerTests\catalog_holadivinus.BorrowerTests.json");
            //return; 
            EditorSettings.enableTextureStreamingInEditMode = false;
            EditorSettings.enableTextureStreamingInPlayMode = false;

            // Unity Hookz!
            new Harmony("com.holadivinus.seamlesscatalogs").PatchAll();
            EditorApplication.update += EditorUpdate;

            // If a domain reload happens, we need to kill loaded bundles
            AssetBundle.UnloadAllAssetBundles(true);

            // Ensure remote catalogs load correctly
            Addressables.InitializeAsync().Completed += (e) => 
            {
                _addressablesLoaded = true;
                Addressables.InternalIdTransformFunc = loc =>
                {
                    string locStr = loc.ToString();
                    if (locStr.Length > 2 && locStr[1] == ':') return locStr;
                    if (loc.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider")
                    {
                        foreach (var cat in ExternalCatalog.ActiveCatalogs.Values)
                        {
                            if (!cat.AllResources.Contains(loc)) continue;

                            string catDir = new DirectoryInfo(cat.CatalogPath).Parent.FullName;
                            string[] locBits = loc.InternalId.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
                            locBits[0] = "";
                            return catDir + string.Join('\\', locBits);
                        }
                        return locStr;
                    }
                    return locStr;
                };
            };

            Config = ScriptableObject.CreateInstance<SeamlessCatalogsConfig>();
            CompilationPipeline.compilationStarted += (a) => UnityEngine.Object.DestroyImmediate(Config);
            Config.Load();


            // Hook global editor events
            FieldInfo info = typeof(EditorApplication).GetField("globalEventHandler", BindingFlags.Static | BindingFlags.NonPublic);
            EditorApplication.CallbackFunction value = (EditorApplication.CallbackFunction)info.GetValue(null);
            value += EditorEvent;
            info.SetValue(null, value);


            // modify the built bundles
            ContentPipeline.BuildCallbacks.PostPackingCallback += (a, b, c) =>
            {
                EditorApplication.delayCall += PostBuildProcessing.ProcessOutputs;
                return ReturnCode.Success;
            };


            
        }

        

        private static void StartRemotePrefabDrag(GameObject g, CatalogEntry e)
        {
            handlingDrag = true; 
            remoteDragPrefab = g;
            remoteDragEntry = e;
            DragAndDrop.AddDropHandler(dropy);
        }
        private static bool handlingDrag;
        private static CatalogEntry remoteDragEntry;
        private static GameObject remoteDragPrefab;
        private static GameObject localDragPrefabInstance;
        private static Collider[] curDragCols;
        private static void EndRemotePrefabDrag()
        {
            handlingDrag = false;
            remoteDragPrefab = null;
            remoteDragEntry = null;
            curDragCols = null;
            localDragPrefabInstance = null;
            DragAndDrop.RemoveDropHandler(dropy);
        }
        private static DragAndDropVisualMode dropy(UnityEngine.Object dropUpon, Vector3 worldPosition, Vector2 viewportPosition, Transform parentForDraggedObjects, bool perform)
        {
            // see if we have a local gunkfab
            string gunkfabPath = $"Assets/RemotePrefabs/{remoteDragEntry.Location.PrimaryKey}.prefab";
            if (!Directory.Exists("Assets/RemotePrefabs/")) Directory.CreateDirectory("Assets/RemotePrefabs/");
            string o = "";
            if (!File.Exists(gunkfabPath))
            {
                GameObject gunkFab = UnityEngine.Object.Instantiate(remoteDragPrefab);
                gunkFab.name = gunkFab.name.Replace("(Clone)", "");

                foreach (MonoScript ms in Resources.FindObjectsOfTypeAll<MonoScript>())
                    if (ms.GetClass() == null && string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(ms)))
                    {
                        AssetDatabase.RemoveObjectFromAsset(ms);
                        UnityEngine.Object.DestroyImmediate(ms);
                    }

                foreach (var t in gunkFab.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);


                // Record all Remote Assets this asset has (what can't be put in a .prefab
                // also null them
                RemotePrefab rp = gunkFab.AddComponent<RemotePrefab>();
                rp.PrimaryKey = remoteDragEntry.Location.PrimaryKey;
                rp.Dependancies = remoteDragEntry.Location.Dependencies.Select(d => d.InternalId).ToList();
                rp.CatalogName = remoteDragEntry.Catalog.Name;
                MonoScript[] allMonoScripts = Resources.FindObjectsOfTypeAll<MonoScript>();
                foreach (var c in gunkFab.GetComponentsInChildren<Component>(true)) 
                {
                    if (c == null) continue;
                    GameObject g = c.gameObject;
                    using var so1 = new SerializedObject(c);
                    var scripPrip = so1.FindProperty("m_Script");
                    if (scripPrip != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(scripPrip.objectReferenceValue)))
                    {
                        // gotta swap this to a real script
                        MonoScript fakeVersion = (MonoScript)scripPrip.objectReferenceValue;
                        MonoScript realVersion = allMonoScripts.FirstOrDefault(ms => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(ms)) && ms.name == fakeVersion.name);
                        so1.Update();
                        if (realVersion != null)
                        {
                            scripPrip.objectReferenceValue = realVersion;
                            so1.ApplyModifiedPropertiesWithoutUndo();
                        }
                        else
                        {
                            scripPrip.objectReferenceValue = null;
                            so1.ApplyModifiedPropertiesWithoutUndo();
                        }
                    }
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(g);
                }
                foreach (var c in gunkFab.GetComponentsInChildren<Component>(true))
                    {
                    if (c == rp) continue;
                    GameObject curG = c.gameObject;
                    using var so = new SerializedObject(c);
                    bool dirt = false;
                    var prop = so.GetIterator();
                    while (prop.NextVisible(true))
                    {
                        if (c == null) break;
                        if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                        if (prop.propertyPath == "m_Script") 
                            continue; 
                        if (prop.objectReferenceValue == null) continue;
                        if (prop.objectReferenceValue is Component comp && comp.gameObject.scene.IsValid()) continue;
                        if (prop.objectReferenceValue is GameObject g && g.scene.IsValid()) continue;

                        // get path to gobj
                        List<int> gPath = new();
                        Transform gP = c.transform;
                        while (gP != rp.transform)
                        {
                            gPath.Insert(0, gP.GetSiblingIndex());
                            gP = gP.parent;
                        }

                        

                        rp.BaseRefs.Add(new()
                        {
                            SceneObj = c,
                            AssetName = prop.objectReferenceValue.name,
                            AssetType = RemoteAssetTracker.GetSimpleType(prop.objectReferenceValue),
                            gobjPath = gPath.ToArray(),
                            compIdx = Array.IndexOf(c.gameObject.GetComponents<Component>(), c),
                            propPath = prop.propertyPath
                        });
                        if (prop.objectReferenceValue is ScriptableObject asSO) 
                        {
                            MonoScript realVersion = allMonoScripts.FirstOrDefault(ms => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(ms)) && ms.name == MonoScript.FromScriptableObject(asSO).name);
                            rp.UsedClasses.Add(realVersion);
                        }

                        so.Update();
                        prop.objectReferenceValue = null;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        dirt = true;

                        o += $"{c.gameObject.name}.{c.GetType().Name} -> {prop.propertyPath}\n";
                    }
                    if (c != null && dirt)
                        EditorUtility.SetDirty(c);
                    if (c == null)
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(curG);
                }
                rp.UsedClasses = rp.UsedClasses.Distinct().ToList();


                PrefabUtility.SaveAsPrefabAsset(gunkFab, gunkfabPath);

                UnityEngine.Object.DestroyImmediate(gunkFab);

                ((Action)(async () => 
                {
                    foreach (IResourceLocation loc in remoteDragEntry.Location.Dependencies)
                    {
                        IAssetBundleResource bun = await Addressables.LoadAssetAsync<IAssetBundleResource>(loc).Task;
                        AssetBundle b = bun.GetAssetBundle();
                        Debug.Log(loc.InternalId);
                        Debug.Log(loc.PrimaryKey);
                        foreach (var item in b.GetAllAssetNames())
                        {
                        Debug.Log(item);

                        }
                    }
                }))();
                

            }

            if (localDragPrefabInstance == null)
            {
                localDragPrefabInstance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(gunkfabPath));
                localDragPrefabInstance.name = remoteDragPrefab.name;

                foreach (var t in localDragPrefabInstance.GetComponentsInChildren<Transform>(true))
                    if (t.gameObject.GetComponent<RemoteGameObjectTag>() == null)
                        t.gameObject.AddComponent<RemoteGameObjectTag>();

                curDragCols = localDragPrefabInstance.GetComponentsInChildren<Collider>().Where(c => c.enabled).ToArray();
                foreach (var c in curDragCols)
                    c.enabled = false;
            }

            localDragPrefabInstance.transform.SetParent(parentForDraggedObjects, true);
            localDragPrefabInstance.transform.position = worldPosition;

            if (perform)
            {
                foreach (var c in curDragCols)
                    c.enabled = true;

                Selection.activeGameObject = localDragPrefabInstance;
            }

            return DragAndDropVisualMode.Move;
        }

        private static void EditorEvent()
        {
        }

        public static SeamlessCatalogsConfig Config;
        static bool _addressablesLoaded;

        private static void EditorUpdate()
        {
            RemoteAssetTracker.FindOrAddCurrent();

            // Handle the finding & setup of ProjectBrowsers
            UnityEngine.Object[] projectBrowsers = Resources.FindObjectsOfTypeAll(CatalogExtensions.typeof_ProjectBrowser);
            for (int i = 0; i < projectBrowsers.Length; i++)
                if (!s_initializedProjectBrowsers.Contains(projectBrowsers[i]))
                {
                    s_initializedProjectBrowsers.Add(projectBrowsers[i]);
                    s_projectBrowserManagers.Add(new ProjectBrowserManager(projectBrowsers[i]));
                }

            foreach (var item in s_projectBrowserManagers)
                item.EditorUpdate();

            if (_addressablesLoaded)
                foreach (var catConfig in Config.Catalogs)
                    catConfig.EditorUpdate();

            if (!handlingDrag && DragAndDrop.objectReferences.Length == 1)
                if (DragAndDrop.objectReferences[0] is GameObject go && ExternalCatalog.RemoteAssets.TryGetValue(go, out List<CatalogEntry> entries))
                    StartRemotePrefabDrag(go, entries[0]);


            if (handlingDrag && DragAndDrop.objectReferences.Length == 0)
                EndRemotePrefabDrag();
            
        }

        private static void RemoveMissingScripts(GameObject g)
        {
            var components = g.GetComponents<Component>();

            var r = 0;

            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] != null) continue;
                var s = g.name;
                var t = g.transform;

                var serializedObject = new SerializedObject(g);

                var prop = serializedObject.FindProperty("m_Component");

                prop.DeleteArrayElementAtIndex(i - r);
                r++;

                serializedObject.ApplyModifiedProperties();
            }

            foreach (Transform childT in g.transform)
            {
                RemoveMissingScripts(childT.gameObject);
            }
        }
    }
}

internal static class CatalogExtensions 
{
    private static Type[] _ta = new Type[3];
    public static Type typeof_ProjectBrowser => _ta[0] ??= Type.GetType("UnityEditor.ProjectBrowser, UnityEditor");
    public static Type typeof_AssetsTreeViewDataSource_RootTreeItem => _ta[1] ??= Type.GetType("UnityEditor.AssetsTreeViewDataSource+RootTreeItem, UnityEditor.CoreModule");
    public static Type typeof_ProjectBrowserColumnOneTreeViewDataSource => _ta[2] ??= Type.GetType("UnityEditor.ProjectBrowserColumnOneTreeViewDataSource, UnityEditor.CoreModule");


    public static Texture2D FolderIcon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
    public static Texture2D UnknownIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.holadivinus.seamlesscatalogs/UnknownIcon.png");

    private static Dictionary<(Type, string), PropertyInfo> _propInfos = new();
    public static object RGP(this object o, string memberName)
    {
        if (o == null) throw new ArgumentNullException(nameof(o));
        if (_propInfos.TryGetValue((o.GetType(), memberName), out PropertyInfo pi))
            return pi.GetValue(o);
        else
        {
            var propInfo = o.GetType().GetProperty(memberName, (BindingFlags)60);
            if (propInfo == null) throw new Exception($"Type {o.GetType().Name} does not contain Property " + memberName);
            _propInfos[(o.GetType(), memberName)] = propInfo;
            return propInfo.GetValue(o);
        }
    }
    public static void RSP(this object o, string memberName, object value)
    {
        if (o == null) throw new ArgumentNullException(nameof(o));
        if (_propInfos.TryGetValue((o.GetType(), memberName), out PropertyInfo pi))
            pi.SetValue(o, value);
        else
        {
            var propInfo = o.GetType().GetProperty(memberName, (BindingFlags)60);
            if (propInfo == null) throw new Exception($"Type {o.GetType().Name} does not contain Property " + memberName);
            _propInfos[(o.GetType(), memberName)] = propInfo;
            propInfo.SetValue(o, value);
        }
    }
    private static Dictionary<(Type, string), FieldInfo> _fieldInfos = new();
    public static object RGF(this object o, string memberName)
    {
        if (o == null) throw new ArgumentNullException(nameof(o));
        if (_fieldInfos.TryGetValue((o.GetType(), memberName), out FieldInfo fi))
            return fi.GetValue(o);
        else
        {
            var fieldInfo = o.GetType().GetField(memberName, (BindingFlags)60);
            if (fieldInfo == null) throw new Exception($"Type {o.GetType().Name} does not contain Field " + memberName);
            _fieldInfos[(o.GetType(), memberName)] = fieldInfo;
            return fieldInfo.GetValue(o);
        }
    }
    public static T RGF<T>(this object o, string memberName) => (T)RGF(o, memberName);
    public static void RSF(this object o, string memberName, object value)
    {
        if (o == null) throw new ArgumentNullException(nameof(o));
        if (_fieldInfos.TryGetValue((o.GetType(), memberName), out FieldInfo fi))
            fi.SetValue(o, value);
        else
        {
            var fieldInfo = o.GetType().GetField(memberName, (BindingFlags)60);
            if (fieldInfo == null) throw new Exception($"Type {o.GetType().Name} does not contain Field " + memberName);
            _fieldInfos[(o.GetType(), memberName)] = fieldInfo;
            fieldInfo.SetValue(o, value);
        }
    }

    private static Dictionary<(Type, string), MethodInfo> _methodInfos = new();
    public static object RRM(this object o, string memberName, params object[] arguments)
    {
        if (o == null) throw new ArgumentNullException(nameof(o));
        if (_methodInfos.TryGetValue((o.GetType(), memberName), out MethodInfo mi))
            return mi.Invoke(o, arguments);
        else
        {
            var methodInfo = AccessTools.FirstMethod(o.GetType(), m => m.Name == memberName);
            if (methodInfo == null) throw new Exception($"Type {o.GetType().Name} does not contain Method " + memberName);
            _methodInfos[(o.GetType(), memberName)] = methodInfo;
            return methodInfo.Invoke(o, arguments);
        }
    }
    public static object RRM<T>(this object o, string memberName, params object[] arguments)
    {
        if (o == null) throw new ArgumentNullException(nameof(o));
        if (_methodInfos.TryGetValue((o.GetType(), memberName), out MethodInfo mi))
            return mi.Invoke(o, arguments);
        else
        {
            var methodInfo = AccessTools.Method(o.GetType(), memberName, new Type[] { typeof(T) });
            if (methodInfo == null) throw new Exception($"Type {o.GetType().Name} does not contain Method " + memberName);
            _methodInfos[(o.GetType(), memberName)] = methodInfo;
            return methodInfo.Invoke(o, arguments);
        }
    }
    public static object RRM<T, T2>(this object o, string memberName, params object[] arguments)
    {
        if (o == null) throw new ArgumentNullException(nameof(o));
        if (_methodInfos.TryGetValue((o.GetType(), memberName), out MethodInfo mi))
            return mi.Invoke(o, arguments);
        else
        {
            var methodInfo = AccessTools.Method(o.GetType(), memberName, new Type[] { typeof(T), typeof(T2) });
            if (methodInfo == null) throw new Exception($"Type {o.GetType().Name} does not contain Method " + memberName);
            _methodInfos[(o.GetType(), memberName)] = methodInfo;
            return methodInfo.Invoke(o, arguments);
        }
    }

    public static List<IResourceLocation> FindAllDependancies(this IResourceLocation loc, List<IResourceLocation> deps = null)
    {
        deps ??= new();
        deps.Add(loc);
        if (loc.Dependencies != null)
            foreach (var child in loc.Dependencies)
                FindAllDependancies(child, deps);
        return deps;
    }
}
#endif