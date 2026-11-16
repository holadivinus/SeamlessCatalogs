#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace SeamlessCatalogs
{
    /// <summary>
    /// Corresponds to a Remote Asset, loaded or not.
    /// Is created on ExternalCatalog load, Destroyed on unload.
    /// Children automatically render as ui
    /// </summary>
    public class CatalogEntry
    {
        public string name;

        public static CatalogEntry NewEntry(string Name, Type type, ExternalCatalog catalog, List<CatalogEntry> children = null)
        {
            var o = new CatalogEntry();
            o.Catalog = catalog;
            o.name = Name;
            o.AssetType = type;
            o._c = children;
            
            catalog.AllEntries.Add(o);
            
            o.EnsureNoUnloading();

            if (children != null)
                foreach (var c in children)
                    c.Parent = o;
            
            return o;
        }

        /// <summary>
        /// Hacky method to make sure scene-changes doesn't destroy this entry
        /// sucks
        /// replace this pls
        /// </summary>
        public void EnsureNoUnloading()
        {
        }

        /// <summary>
        /// The Catalog that contains this Entry's Asset
        /// </summary>
        [NonSerialized] public ExternalCatalog Catalog;
        /// <summary>
        /// The Type of this Entry's Asset
        /// </summary>
        public Type AssetType;

        /// <summary>
        /// The Project-Browser UI Item for this Entry
        /// </summary>
        public TreeViewItem UI
        {
            get
            {
                if (this == null) return null;

                if (_ui == null)
                {
                    _ui = new TreeViewItem()
                    {
                        id = new System.Random().Next(),
                        displayName = name,
                        icon = AssetType == null ? CatalogExtensions.FolderIcon : AssetPreview.GetMiniTypeThumbnail(AssetType) ?? CatalogExtensions.UnknownIcon,
                    };
                    ProjectBrowserManager.FakeTreeViews.Add(_ui.id);
                    ExternalCatalog.UI2Entry.Add(_ui.id, this);
                }
                return _ui;
            }
        }
        private TreeViewItem _ui;


        [NonSerialized] public CatalogEntry Parent;
        public List<CatalogEntry> Children => _c ??= new();
        private List<CatalogEntry> _c;

        /// <summary>
        /// Path of Entry (NOT REMOTE ASSET PATH)
        /// </summary>
        public string Path
        {
            get
            {
                string p = name;
                CatalogEntry e = Parent;
                while (e != null)
                {
                    p = e.name + "/" + p;
                    e = e.Parent;
                }
                return p;
            }
        }

        public void AddChild(CatalogEntry e)
        {
            if (e.Parent != null) e.Parent.Children.Remove(e);
            Children.Add(e);
            e.Parent = this;
        }

        bool destroyed = false;
        public void Destroy()
        {
            if (destroyed) return;
            destroyed = true;

            if (LoadedAsset != null)
                ExternalCatalog.RemoteAssets.Remove(LoadedAsset);

            if (Catalog != null)
                Catalog.AllEntries.Remove(this);

            foreach (var c in Children)
                c?.Destroy();
        }

        [NonSerialized] public IResourceLocation Location;
        [NonSerialized] public UnityEngine.Object LoadedAsset;
        //[NonSerialized] public AssetBundle[] LoadedBundles;

        private static CatalogEntry mostRecentSelLoad;
        public async Task<UnityEngine.Object> LoadAssetAsync(bool refreshPBMs = false)       
        {
            if (Location == null) return null;


            if (LoadedAsset != null)
                return LoadedAsset;
            else
            {
                if (refreshPBMs) mostRecentSelLoad = this;
                LoadedAsset = await Addressables.LoadAssetAsync<UnityEngine.Object>(Location).Task;

                if (ExternalCatalog.RemoteAssets.TryGetValue(LoadedAsset, out List<CatalogEntry> assetEntries))
                    assetEntries.Add(this);
                else ExternalCatalog.RemoteAssets[LoadedAsset] = new() { this };

                var loadedLocs = Location.FindAllDependancies();
                loadedLocs.Add(Location);

                var bundlesLoaded = loadedLocs.Where(l => l.ResourceType == typeof(IAssetBundleResource));
                var othersLoaded = loadedLocs.Where(l => l.ResourceType != typeof(IAssetBundleResource));


                var loadedBuns = Resources.FindObjectsOfTypeAll<AssetBundle>();
                //List<AssetBundle> lodBuns = new();
                foreach (var item in bundlesLoaded)
                {
                    AssetBundleRequestOptions options = (AssetBundleRequestOptions)item.Data;
                    var loadedBun = loadedBuns.FirstOrDefault(b => b.name == options.BundleName + ".bundle");
                    Catalog.LoadedAssetBundles.Add(loadedBun);
                    //lodBuns.Add(loadedBun);


                    //var realPath = System.IO.Path.Combine(Directory.GetParent(Catalog.CatalogPath).FullName, item.InternalId.Split(":\\")[1]);
                    //string bunRead = File.ReadAllText(realPath);
                    //Catalog.Bundle2CAB[loadedBun] = bunRead.Substring(bunRead.IndexOf("CAB-"), 36);
                }
                //LoadedBundles = lodBuns.ToArray();


                if (refreshPBMs && mostRecentSelLoad == this)
                    Selection.activeObject = LoadedAsset;

                if (refreshPBMs)
                    foreach (var pbm in ProjectBrowserManager.instances)
                        pbm.RepaintFolders();

                return LoadedAsset;
            }
        }
    }
}
#endif