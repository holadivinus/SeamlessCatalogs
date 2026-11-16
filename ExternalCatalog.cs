#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.Video;

namespace SeamlessCatalogs
{
    public class ExternalCatalog
    {
        public static Dictionary<IResourceLocator, ExternalCatalog> ActiveCatalogs = new();
        public static Dictionary<int, CatalogEntry> UI2Entry = new();
        public static Dictionary<UnityEngine.Object, List<CatalogEntry>> RemoteAssets = new();
        public static HashSet<string> EveryCatalogPathEver = new();
        public HashSet<IResourceLocation> AllResources = new();
        public HashSet<CatalogEntry> AllEntries = new();
        public HashSet<AssetBundle> LoadedAssetBundles = new();
        //public Dictionary<AssetBundle, string> Bundle2CAB = new();

        public string CatalogPath;
        IResourceLocator Target;
        public static ExternalCatalog FromResourceLocationMap(ResourceLocationMap resourceLocationMap, string jsonPath)
        {
            string name = Path.GetFileNameWithoutExtension(jsonPath).Replace("catalog_", "");

            var o = new ExternalCatalog(name, resourceLocationMap);
            o.AllResources = resourceLocationMap.Locations.SelectMany(kvp => kvp.Value).ToHashSet();
            o.CatalogPath = jsonPath;
            EveryCatalogPathEver.Add(jsonPath);

            // Create Entries based off of Locations
            List<IResourceLocation> goodAssets = new();
            foreach (KeyValuePair<object, IList<IResourceLocation>> info in resourceLocationMap.Locations)
            {
                IResourceLocation assetLocation = info.Value.FirstOrDefault();
                if (assetLocation == null) continue;
                if (assetLocation.ResourceType == typeof(IAssetBundleResource)) // i dont think asset bundles are used for much in-editor
                    continue;

                string path = assetLocation.InternalId;
                if (!path.Contains('/')) continue;

                

                CatalogEntry assetEntry = o.Root;
                string[] pathBits = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < pathBits.Length; i++)
                    assetEntry = FindOrCreateChild(assetEntry, pathBits[i], i == pathBits.Length - 1 ? assetLocation.ResourceType : null);
                
                assetEntry.Location = assetLocation;
                goodAssets.Add(assetLocation);
            }

            void FillFilter(CatalogEntry filtRoot, Type filtTo, IResourceLocation[] assets)
            {
                foreach (var item in assets)
                    FindOrCreateChild(filtRoot, Path.GetFileName(item.InternalId), filtTo).Location = item;
            }
             
            // filtered stuff
            if (o.Root.Children.Count > 0)
            {
                var filtRoot = FindOrCreateChild(o.Root, "BLP Filters");
                filtRoot.UI.icon = (Texture2D)EditorGUIUtility.IconContent("Search Icon").image;
                
                void AddFilt<T>()
                {
                    IResourceLocation[] assets = goodAssets.Where(ga => ga.ResourceType == typeof(T)).ToArray();
                    if (assets.Length == 0) return;
                    if (typeof(T) == typeof(GameObject))
                        assets = assets.OrderBy(l => l.InternalId.EndsWith(".prefab") ? -1 : 1).ToArray();

                    CatalogEntry filt = FindOrCreateChild(filtRoot, typeof(T).Name+"s");
                    filt.UI.icon = AssetPreview.GetMiniTypeThumbnail(typeof(T));
                    FillFilter(filt, typeof(T), assets);
                }
                AddFilt<Material>();
                AddFilt<Texture>();
                AddFilt<Shader>();
                AddFilt<GameObject>();
                AddFilt<TMP_FontAsset>();

                if (filtRoot.Children.Count == 0)
                {
                    o.Root.Children.Remove(filtRoot);
                    filtRoot.Destroy();
                }



            }


            foreach (var pbm in ProjectBrowserManager.instances)
                pbm.RepaintFolders();

            return o;
        }
        

        public ExternalCatalog(string name, IResourceLocator target)
        {
            Name = name;
            ActiveCatalogs.Add(target, this);
            Target = target;

            Root = new CatalogEntry();
            Root.Catalog = this;
            Root.name = name;
            Root.EnsureNoUnloading();
        }

        public string Name;
        public CatalogEntry Root;


        public void Destroy()
        {
            Root.Destroy();
            ActiveCatalogs.Remove(Target);

            foreach (var pbm in ProjectBrowserManager.instances)
                pbm.RepaintFolders();

            foreach (var b in LoadedAssetBundles)
                if (b != null)
                    b.Unload(false);
        }


        static CatalogEntry FindOrCreateChild(CatalogEntry parent, string chilName, Type chilType = null)
        {
            CatalogEntry foundEntry = parent.Children.FirstOrDefault(c => (c.name == chilName) && c.AssetType == chilType);
            if (foundEntry != null) return foundEntry;

            CatalogEntry newEntry = CatalogEntry.NewEntry(chilName, chilType, parent.Catalog);
            parent.Children.Add(newEntry);
            newEntry.Parent = parent;
            return newEntry;
        }

        static Type FindFileType(string path)
        {
            switch (Path.GetExtension(path))
            {
                case ".apng":
                case ".avif":
                case ".bmp":
                case ".cur":
                case ".dds":
                case ".exr":
                case ".gif":
                case ".hdr":
                case ".ico":
                case ".jfif":
                case ".jpeg":
                case ".jpg":
                case ".pict":
                case ".pjp":
                case ".pjpeg":
                case ".png":
                case ".psb":
                case ".psd":
                case ".svg":
                case ".tga":
                case ".tif":
                case ".tiff":
                case ".webp":
                    return typeof(Texture2D);
                case ".3dm":
                case ".3ds":
                case ".3dxml":
                case ".asm":
                case ".catpart":
                case ".catproduct":
                case ".cgr":
                case ".dae":
                case ".dwg":
                case ".dxf":
                case ".fbx":
                case ".gds":
                case ".glb":
                case ".gltf":
                case ".iam":
                case ".ifc":
                case ".iges":
                case ".igs":
                case ".ipt":
                case ".jt":
                case ".neu":
                case ".nwc":
                case ".nwd":
                case ".obj":
                case ".par":
                case ".plmxml":
                case ".prc":
                case ".prt":
                case ".psm":
                case ".pwd":
                case ".pxz":
                case ".rfa":
                case ".rvm":
                case ".rvt":
                case ".sab":
                case ".sat":
                case ".sldasm":
                case ".sldprt":
                case ".step":
                case ".stepx":
                case ".stepz":
                case ".stl":
                case ".stp":
                case ".stpx":
                case ".stpxz":
                case ".stpz":
                case ".u3d":
                case ".usd":
                case ".usda":
                case ".usdc":
                case ".usdz":
                case ".vda":
                case ".vrml":
                case ".wrl":
                case ".xas":
                case ".xpr":
                case ".blend":
                    return typeof(GameObject);
                case ".avi":
                case ".flv":
                case ".h264":
                case ".h265":
                case ".m4v":
                case ".mkv":
                case ".mp4":
                case ".webm":
                case ".mov":
                    return typeof(VideoClip);
                case ".wav":
                case ".aif":
                case ".aiff":
                case ".mp3":
                case ".aac":
                case ".ogg":
                case ".xm":
                case ".mod":
                case ".it":
                    return typeof(AudioClip);
                case ".shader":
                case ".shadergraph":
                    return typeof(Shader);
                case ".shadervariants":
                    return typeof(ShaderVariantCollection);
                case ".json":
                case ".xml":
                case ".txt":
                case ".csv":
                case ".yaml":
                case ".cs":
                    return typeof(TextAsset);
                case ".material":
                case ".mat":
                    return typeof(Material);
                case ".unity":
                    return typeof(SceneAsset);
                case ".physicsMaterial":
                    return typeof(PhysicMaterial);
                case ".asmdef":
                    return typeof(AssemblyDefinitionAsset);
                case ".ttf":
                case ".otf":
                case ".woff":
                case ".woff2":
                case ".eot":
                case ".fon":
                case ".fnt":
                case ".pfb":
                case ".pfm":
                case ".sfd":
                    return typeof(Font);
                default:
                    return null;
            }
        }
    }
}
#endif