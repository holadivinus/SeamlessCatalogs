#if UNITY_EDITOR
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Newtonsoft.Json;
using SeamlessCatalogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static BuiltCatalog;

public static class PostBuildProcessing
{
    private static List<string> SearchForFiles(string dir, string extension, List<string> o = null)
    {
        o ??= new();
        foreach (var f in Directory.EnumerateFiles(dir))
            if (Path.GetExtension(f) == extension) o.Add(f);

        foreach (var d in Directory.EnumerateDirectories(dir))
            SearchForFiles(d, extension, o);

        return o;
    }
    private static string CurBuildDir => $"BuiltPallets/{EditorUserBuildSettings.activeBuildTarget}/";

    public static void ProcessOutputs()
    {
        foreach (var palletCat in SearchForFiles(CurBuildDir, ".json").Where(f => Path.GetFileName(f).StartsWith("catalog_")))
        {
            try // just since EditorUtility.RequestScriptReload() really needs to fire
            {
                ProcessCat(palletCat);
            }
            catch (Exception e) 
            {
                UnityEngine.Debug.LogException(e);
            }
        }
        EditorApplication.delayCall += () => EditorUtility.RequestScriptReload();
    }

    private static AssetsManager CurManager;
    static void ProcessCat(string catPath) 
    {
        var localBundles = SearchForFiles(new FileInfo(catPath).Directory.FullName, ".bundle");

        CurManager = new AssetsManager();
        CurManager.LoadClassPackage(Path.GetFullPath("Packages/com.holadivinus.seamlesscatalogs/uncompressed.tpk"));

        Dictionary<string, List<string>> localAsset2DependenciesIDs = new();
        Dictionary<string, string> DependencyID2CatalogPath = new();
        Dictionary<AssetsFileInstance, int> subBundleIdxs = new();
        foreach (var localBundle in localBundles)
        {
            BundleItem.ResetCache();
            
            // load local bundle into ram
            var ms = new MemoryStream();
            using (var fileStream = new FileStream(localBundle, FileMode.Open))
            {
                fileStream.CopyTo(ms);
            }
            ms.Position = 0;
            BundleFileInstance loadedLocalBundle = CurManager.LoadBundleFile(ms, localBundle, true);

            // Load each sub-bundle
            List<AssetsFileInstance> subBundles = new();
            for (int i = 0; i < loadedLocalBundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
            {
                var subBundle = CurManager.LoadAssetsFileFromBundle(loadedLocalBundle, i, false);
                if (subBundle == null) continue;
                subBundles.Add(subBundle);
                subBundleIdxs[subBundle] = i;
                CurManager.LoadClassDatabaseFromPackage(subBundle.file.Metadata.UnityVersion);
            }
            
            // find first preloadTable
            BundleItem preloadTable = null;
            foreach (var ba in subBundles)
            {
                var pt = ba.file.GetAssetsOfType(AssetClassID.PreloadData).FirstOrDefault();
                if (pt == null) continue;
                preloadTable = new BundleItem(ba, pt, CurManager.GetBaseField(ba, pt));
                break;
            }
            //if (preloadTable == null) continue; // currently we'll just support scenes (& prefabs ?)

            // get list of RemoteAssetTrackers
            BundleItem[] trackers = subBundles.SelectMany(ba => ba.file.GetAssetsOfType(AssetClassID.GameObject)
                                                                       .Where(go => CurManager.GetBaseField(ba, go)["m_Name"].AsString == RemoteAssetTracker.REMOTE_STR)
                                                                       .Select(go => new BundleItem(ba, go, CurManager.GetExtAsset(ba, CurManager.GetBaseField(ba, go)["m_Component"]["Array"][1]["component"]).baseField))
                                                         ).ToArray();

            string CatName2Path(string name) => ExternalCatalog.EveryCatalogPathEver.FirstOrDefault(cat => cat.EndsWith($"catalog_{name}.json"));

            // get a list of all the Remote Assets this bundle uses
            List<RemoteAsset> neededAssets = new();
            List<RemoteApplication> pendingChanges1 = new(); // changes  that make remote prefabs act as if native
            List<RemoteApplication> pendingChanges2 = new(); // changes for all other stuffs
            HashSet<string> localAssetPaths = new(); // asset paths noted in this subbundle by RemoteAssetTrackers
            foreach (var trackComp in trackers)
            {

                localAssetPaths.Add(trackComp.Prop["AssetPath"].AsString);

                // Get RemotePrefab's RemoteAssets
                for (int i = 0; i < trackComp.Prop["Prefabs.Array"].AsArray.size; i++)
                {
                    BundleItem curPrefab = BundleItem.GetItem(CurManager.GetExtAsset(trackComp.Bundle, trackComp.Prop["Prefabs.Array"][i]));

                    // determine the RemoteAssets this prefab demands

                    // get this prefab's bundlepaths
                    List<string> dependancyPaths = new();
                    List<string> dependancyIDs = new();
                    string prefabCatPath = CatName2Path(curPrefab.Prop["CatalogName"].AsString);
                    for (int j = 0; j < curPrefab.Prop["Dependancies.Array"].AsArray.size; j++)
                    {
                        string depID = curPrefab.Prop["Dependancies.Array"][j].AsString;
                        dependancyIDs.Add(depID);
                        string remotePrefabBundlePath = Path.Combine(Directory.GetParent(prefabCatPath).FullName, curPrefab.Prop["Dependancies.Array"][j].AsString.Split(":\\")[1]);
                        dependancyPaths.Add(remotePrefabBundlePath);

                        DependencyID2CatalogPath[depID] = prefabCatPath;
                    }

                    // compose a full list of remote assets from BaseRefs
                    for (int j = 0; j < curPrefab.Prop["BaseRefs.Array"].AsArray.size; j++)
                    {
                        string assetName = curPrefab.Prop["BaseRefs.Array"][j]["AssetName"].AsString;
                        string assetType = curPrefab.Prop["BaseRefs.Array"][j]["AssetType"].AsString;
                        var targAsset = BundleItem.GetItem(CurManager.GetExtAsset(curPrefab.Bundle, curPrefab.Prop["BaseRefs.Array"][j]["SceneObj"]));

                        RemoteAsset existingRemote = neededAssets.FirstOrDefault(r => dependancyPaths.Contains(r.BundlePath) && r.assetName == assetName && r.assetType == assetType);
                        if (existingRemote == null)
                        {
                            existingRemote = new RemoteAsset(dependancyPaths, assetName, assetType, dependancyIDs);
                            neededAssets.Add(existingRemote);
                        }

                        pendingChanges1.Add(new RemoteApplication()
                        {
                            Targ = targAsset,
                            Asset = existingRemote,
                            PropPath = curPrefab.Prop["BaseRefs.Array"][j]["propPath"].AsString
                        });
                    }
                }

                // Get RemoteAssetTracker's noted RemoteAssets
                for (int m = 0; m < 2; m++)
                {
                    string which = m == 0 ? "Records" : "PrefabModRecords";
                    for (int i = 0; i < trackComp.Prop[which+".Array"].AsArray.size; i++)
                    {
                        AssetTypeValueField curRec = trackComp.Prop[which+".Array"][i];

                        string remoteCatPath = CatName2Path(curRec["CatalogName"].AsString);
                        string remoteBundlePath = Path.Combine(Directory.GetParent(remoteCatPath).FullName, curRec["PrimaryBundle"].AsString);
                        string assetName = curRec["AssetName"].AsString;
                        string assetType = curRec["AssetType"].AsString;

                        RemoteAsset remoteAsset = neededAssets.FirstOrDefault(ra => ra.BundlePath == remoteBundlePath && ra.assetName == assetName && ra.assetType == assetType);
                        if (remoteAsset == null)
                        {
                            List<string> dependancies = new();
                            for (int j = 0; j < curRec["Dependancies.Array"].AsArray.size; j++)
                                dependancies.Add(curRec["Dependancies.Array"][j].AsString);

                            foreach (var d in dependancies)
                                DependencyID2CatalogPath[d] = remoteCatPath;

                            neededAssets.Add(remoteAsset = new(remoteBundlePath, assetName, assetType, dependancies));
                        }

                        pendingChanges2.Add(new() 
                        {
                            Targ = BundleItem.GetItem(CurManager.GetExtAsset(trackComp.Bundle, curRec["Comp"])),
                            Asset = remoteAsset,
                            PropPath = curRec["PropPath"].AsString,
                        });
                    }
                }
            }

            foreach (var p in localAssetPaths)
                localAsset2DependenciesIDs[p] = neededAssets.SelectMany(na => na.DependantBundles).ToList();

            // Register these RemoteAssets in each sub-bundle's dependancies array
            foreach (AssetsFileInstance ba in subBundles)
                foreach (RemoteAsset ra in neededAssets)
                    ra.RegisterAsDependancy(ba);

            // Add these RemoteAssets to the PreloadTable
            if (preloadTable != null)
            {
                var preloadArr = preloadTable.Prop["m_Assets.Array"];
                foreach (RemoteAsset ra in neededAssets)
                {
                    if (ra.Failure) continue;

                    var newArrItem = ValueBuilder.DefaultValueFieldFromArrayTemplate(preloadArr);
                    newArrItem["m_FileID"].AsInt = ra.FileIDs[preloadTable.Bundle];
                    newArrItem["m_PathID"].AsLong = ra.PathID;
                    preloadArr.Children.Add(newArrItem);
                }
                preloadTable.Save();
            }

            // all sub-bundles ref assets & the preload table is full!
            // now we just gotta enforce the records
            foreach (var pendingChanges in new[] { pendingChanges1, pendingChanges2 })
                foreach (var change in pendingChanges)
                {
                    if (change.Asset.Failure) continue;

                    BundleItem targProp = change.Targ.ToProp(change.PropPath, true);
                    targProp.Prop["m_FileID"].AsInt = change.Asset.FileIDs[targProp.Bundle];
                    targProp.Prop["m_PathID"].AsLong = change.Asset.PathID; 
                    change.Targ.Save();
                }
            

            foreach (var ba in subBundles)
                loadedLocalBundle.file.BlockAndDirInfo.DirectoryInfos[subBundleIdxs[ba]].SetNewData(ba.file);

            // overwrite bundle!
            using (AssetsFileWriter bundleWriter = new AssetsFileWriter(localBundle))
            {
                loadedLocalBundle.file.Write(bundleWriter);
            }

            CurManager.UnloadBundleFile(loadedLocalBundle);
        }

        CurManager.UnloadAll(true);
        CurManager = null;


        // Huzzah!!! All the bundles are modded!
        // now we just need to inject Addressable dependancies into the catalog,
        // and disable crc checking
        var builtCat = new BuiltCatalog(catPath);

        Dictionary<string, BuiltCatalog> dependantCats = new(DependencyID2CatalogPath.Values.Distinct().Select(p => new KeyValuePair<string, BuiltCatalog>(p, new BuiltCatalog(p))));

        foreach (KeyValuePair<string, List<string>> v in localAsset2DependenciesIDs)
        {
            foreach (string remoteID in v.Value)
                builtCat.AddDependancies(v.Key, remoteID, dependantCats[DependencyID2CatalogPath[remoteID]]);
        }

        builtCat.SaveToFile();



        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(@"Packages/com.holadivinus.seamlesscatalogs/CRCPatcher~/Example.exe"),
            Arguments = $" patchcrc \"{Path.GetFullPath(catPath)}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true, 
            RedirectStandardError = true, 
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit(); 
    }
    private class BundleItem
    {
        public BundleItem(AssetsFileInstance b, AssetFileInfo o, AssetTypeValueField i)
        {
            Bundle = b; Prop = i; Object = o;
        }
        private BundleItem(AssetExternal ext)
        {
            Bundle = ext.file; Prop = ext.baseField; Object = ext.info;
        }
        static Dictionary<(AssetsFileInstance, long), BundleItem> _items = new();
        public static BundleItem GetItem(AssetExternal e)
        {
            var key = (e.file, e.info.PathId);
            if (!_items.TryGetValue(key, out var item)) _items[key] = item = new BundleItem(e);
            return item;
        }
        public static void ResetCache() => _items.Clear();
        public AssetsFileInstance Bundle;
        public AssetFileInfo Object;
        public AssetTypeValueField Prop;

        public void Save() => Object.SetNewData(Prop);

        public BundleItem ToProp(string propStr, bool containsArrays = false)
        {
            if (!containsArrays) return new BundleItem(Bundle, Object, Prop[propStr]);

            string[] pathBits = propStr.Split("Array.data", System.StringSplitOptions.RemoveEmptyEntries);
            if (pathBits.Length == 1) return new BundleItem(Bundle, Object, Prop[propStr]);

            AssetTypeValueField curProp = Prop[pathBits[0] + "Array"];
            for (int i = 1; i < pathBits.Length; i++)
            {
                string curPathBit = pathBits[i];
                string postArrPath = pathBits[i].Split(']')[1];

                int idx = int.Parse(pathBits[i].Split(']')[0].Substring(1));
                curProp = curProp[idx];

                if (i != pathBits.Length - 1)
                    postArrPath += "Array";

                if (!string.IsNullOrWhiteSpace(postArrPath))
                    curProp = curProp[postArrPath];
            }
            return new BundleItem(Bundle, Object, curProp);
        }
    }
    private class RemoteAsset 
    {
        public RemoteAsset(string bunPath, string assetName, string assetType, List<string> depBuns) : this(new List<string>() { bunPath }, assetName, assetType, depBuns) { }
        public RemoteAsset(List<string> bunPathz, string assetName, string assetType, List<string> depBuns)
        {
            this.assetName = assetName;
            this.assetType = assetType;
            DependantBundles = depBuns;

            foreach (var bundlePath in bunPathz)
            {
                BundlePath = bundlePath;

                // we need to find the PathID & CAB
                var remoteBundle = CurManager.LoadBundleFile(bundlePath);
                int numSubBuns = remoteBundle.file.BlockAndDirInfo.DirectoryInfos.Count;

                for (int i = 0; i < numSubBuns; i++)
                {
                    var subBundle = CurManager.LoadAssetsFileFromBundle(remoteBundle, i, false);
                    if (subBundle == null) continue;

                    foreach (AssetFileInfo subAsset in subBundle.file.AssetInfos)
                    {
                        if (((AssetClassID)subAsset.TypeId).ToString() != assetType) continue;

                        var @base = CurManager.GetBaseField(subBundle, subAsset);
                        var nameField = @base.Children.FirstOrDefault(c => c.FieldName == "m_Name");
                        if (nameField != null && nameField.AsString == assetName)
                        {
                            CAB = subBundle.name;
                            PathID = subAsset.PathId;
                            break;
                        }
                    }
                    if (!Failure) break;
                }
                CurManager.UnloadBundleFile(remoteBundle);

                if (!Failure) break;
            }

            //if (Failure) Debug.LogException(new Exception("Failed to find cab for " + assetName + " in " + JsonConvert.SerializeObject(bunPathz.ToArray())));

        }
        

        public string assetName;
        public string assetType;
        public List<string> DependantBundles = new(); // remote bundle internalid's this needs
        public long PathID;
        private string CAB;
        public bool Failure => string.IsNullOrWhiteSpace(CAB);
        public string BundlePath;
        public Dictionary<AssetsFileInstance, int> FileIDs = new();

        public void RegisterAsDependancy(AssetsFileInstance fi)
        {
            if (string.IsNullOrEmpty(CAB)) return;

            if (fi.file.Metadata.Externals.Any(f => f.PathName == $"archive:/{CAB}/{CAB}")) return;
            fi.file.Metadata.Externals.Add(new()
            {
                VirtualAssetPathName = string.Empty,
                Guid = new GUID128(),
                Type = AssetsFileExternalType.Normal,
                PathName = $"archive:/{CAB}/{CAB}",
                OriginalPathName = $"archive:/{CAB}/{CAB}"
            });
            FileIDs[fi] = fi.file.Metadata.Externals.Count;
        }
    }

    class RemoteApplication
    {
        public BundleItem Targ;
        public string PropPath;
        public RemoteAsset Asset;
    }
}
#endif