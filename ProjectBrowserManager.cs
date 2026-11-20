#if UNITY_EDITOR
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeamlessCatalogs
{
    public class ProjectBrowserManager
    {
        public static List<ProjectBrowserManager> instances = new();
        public static List<int> FakeTreeViews = new() { RootFolderID };

        public EditorWindow ProjectBrowser;
        public ProjectBrowserManager(UnityEngine.Object projectBrowser)
        {
            ProjectBrowser = (EditorWindow)projectBrowser;


            CatalogTree = (TreeViewItem)Activator.CreateInstance(CatalogExtensions.typeof_AssetsTreeViewDataSource_RootTreeItem, RootFolderID, 0, null, "External Catalogs");
            CatalogTree.icon = CatalogExtensions.FolderIcon;

            instances.Add(this);

            FakeBreadCrumbBar = new VisualElement();
            ProjectBrowser.rootVisualElement.Add(FakeBreadCrumbBar);
            FakeBreadCrumbBar.style.position = Position.Absolute;
            FakeBreadCrumbBar.style.backgroundColor = new Color(64 / 255f, 64 / 255f, 64 / 255f, 1);
            FakeBreadCrumbBar.style.borderBottomColor = new Color(40 / 255f, 40 / 255f, 40 / 255f, 1);
            FakeBreadCrumbBar.style.borderBottomWidth = 1;
            FakeBreadCrumbBar.style.paddingLeft = 3;
            FakeBreadCrumbBar.style.paddingTop = 2;
            FakeBreadCrumbBar.style.flexDirection = FlexDirection.Row;

            FakeFileNameBar = new VisualElement();
            ProjectBrowser.rootVisualElement.Add(FakeFileNameBar);
            FakeFileNameBar.style.position = Position.Absolute;
            FakeFileNameBar.style.backgroundColor = new Color(64f / 255f, 64f / 255f, 64f / 255f, 1   );
            FakeFileNameBar.style.flexDirection = FlexDirection.Row;
            FakeFileNameBar.style.borderTopColor = new Color(40f / 255f, 40f / 255f, 40f / 255f,1);
            FakeFileNameBar.style.borderTopWidth = 1;

            FakeFileNameBarIcon = new Image();
            ProjectBrowser.rootVisualElement.Add(FakeFileNameBarIcon);
            FakeFileNameBarIcon.style.position = Position.Absolute;
            FakeFileNameBarIcon.scaleMode = ScaleMode.ScaleToFit;

            FileNameText = new Label("helllo");
            ProjectBrowser.rootVisualElement.Add(FileNameText);
            FileNameText.style.position = Position.Absolute;
            FileNameText.style.fontSize = 12;
            FileNameText.style.color = new Color(201f / 255f, 201f / 255f, 201f / 255f,1);
            FileNameText.style.overflow = Overflow.Hidden;

            FakeSearchBar = new TextField ();
            ProjectBrowser.rootVisualElement.Add(FakeSearchBar);
            FakeSearchBar.style.position = Position.Absolute;
            FakeSearchBar.style.flexDirection = FlexDirection.Row;
            FakeSearchBar.RegisterValueChangedCallback(v =>
            {
                RepaintFolders();
            });
            FakeSearchBar.Q(className: TextField.inputUssClassName).style.paddingLeft = 13;

            FakeSearchBarIcon = new Image();
            ProjectBrowser.rootVisualElement.Add(FakeSearchBarIcon);
            FakeSearchBarIcon.style.position = Position.Absolute;
            FakeSearchBarIcon.scaleMode = ScaleMode.ScaleToFit;
            FakeSearchBarIcon.image = EditorGUIUtility.IconContent("search_icon").image as Texture2D;

            var config = ScriptableObject.CreateInstance<SeamlessCatalogsConfig>();
            CatalogTree.id = RootFolderID;
        }

        public const int RootFolderID = -34989;
        TreeViewItem CatalogTree;
        TreeViewItem RootTree;
        public object TreeDataSource;
        public int[] ViewingFolders;
        public object ListArea;
        public VisualElement FakeBreadCrumbBar;
        public VisualElement FakeFileNameBar;
        public TextField FakeSearchBar;
        public Image FakeSearchBarIcon;
        public Image FakeFileNameBarIcon;
        public Label FileNameText;
        public static CatalogEntry LatestSel;
        public Dictionary<UnityEngine.Object, CatalogEntry> AvailableSels = new();

        public void EditorUpdate()
        {
            if (TreeDataSource == null)
            {
                TreeDataSource ??= ProjectBrowser.RGF("m_FolderTree")?.RGP("data");
                if (TreeDataSource != null)
                    OnFolderListRecomposed();
            }


            if (ListArea == null)
                ListArea = ProjectBrowser.RGP("ListArea");

            Rect r = (Rect)ProjectBrowser.RGF("m_ListHeaderRect");
            FakeBreadCrumbBar.style.left = r.x+1;
            FakeBreadCrumbBar.style.top = r.y;
            FakeBreadCrumbBar.style.width = r.width-1;
            FakeBreadCrumbBar.style.height = r.height;

            FakeSearchBar.style.left = (r.x + r.width) - 435;
            FakeSearchBar.style.top = r.y-20;
            FakeSearchBar.style.width = 319;
            FakeSearchBar.style.height = 16;

            
            FakeSearchBarIcon.style.left = ((r.x + r.width) - 435) + 6;
            FakeSearchBarIcon.style.top = (r.y - 20)+5f;
            FakeSearchBarIcon.style.width = 8;
            FakeSearchBarIcon.style.height = 8;

            Rect r2 = (Rect)ProjectBrowser.RGF("m_BottomBarRect");
            FakeFileNameBar.style.left = r2.x + 1;
            FakeFileNameBar.style.top = r2.y;
            FakeFileNameBar.style.width = r2.width - 100;
            FakeFileNameBar.style.height = r2.height;

            FakeFileNameBarIcon.style.left = r2.x + 1;
            FakeFileNameBarIcon.style.top = r2.y+3;
            FakeFileNameBarIcon.style.width = r2.height;
            FakeFileNameBarIcon.style.height = r2.height - 5;

            FileNameText.style.left = r2.x + 1 + r2.height;
            FileNameText.style.top = r2.y+3;
            FileNameText.style.width = (r2.width - 100) - r2.height;
            FileNameText.style.height = r2.height;

            void SetShow(VisualElement v, bool show)
            {
                v.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
                v.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

            }
            if (ViewingFolders != null && ViewingFolders.Length > 0 && FakeTreeViews.Contains(ViewingFolders[0]))
            {
                SetShow(FakeSearchBar, true);
                SetShow(FakeSearchBarIcon, true);
                
                FakeBreadCrumbBar.style.visibility = Visibility.Visible;
                FakeFileNameBar.style.visibility = Visibility.Visible;

                FakeFileNameBar.style.display = DisplayStyle.Flex;
                FakeBreadCrumbBar.style.display = DisplayStyle.Flex;

                if (Selection.activeObject != null && ExternalCatalog.RemoteAssets.TryGetValue(Selection.activeObject, out List<CatalogEntry> cs))
                    LatestSel = cs.First();
                else LatestSel = null;
                
                if (LatestSel != null)
                {
                    FileNameText.style.visibility = Visibility.Visible;
                    FakeFileNameBarIcon.style.visibility = Visibility.Visible;
                    FileNameText.style.display = DisplayStyle.Flex;
                    FakeFileNameBarIcon.style.display = DisplayStyle.Flex;

                    FileNameText.text = LatestSel.Path;
                    FakeFileNameBarIcon.image = LatestSel.UI.icon;
                }
                else
                {
                    FileNameText.style.visibility = Visibility.Hidden;
                    FakeFileNameBarIcon.style.visibility = Visibility.Hidden;

                    FakeFileNameBarIcon.style.display = DisplayStyle.None;
                    FileNameText.style.display = DisplayStyle.None;
                }
            }
            else
            {
                FakeBreadCrumbBar.style.visibility = Visibility.Hidden;
                FakeFileNameBar.style.visibility = Visibility.Hidden;
                FileNameText.style.visibility = Visibility.Hidden;
                FakeFileNameBarIcon.style.visibility = Visibility.Hidden;

                FakeBreadCrumbBar.style.display = DisplayStyle.None;
                FakeFileNameBar.style.display = DisplayStyle.None;
                FileNameText.style.display = DisplayStyle.None;
                FakeFileNameBarIcon.style.display = DisplayStyle.None;

                SetShow(FakeSearchBarIcon, false);
                SetShow(FakeSearchBar, false);
            }
        }

        public void RepaintFolders()
        {
            ProjectBrowser.RGF("m_FolderTree")?.RRM("ReloadData");
        }

        // Functions triggered by Patches
        public void OnFolderListRecomposed()
        {
            if (CatalogTree == null) return;

            // Inject "External Catalogs" folder, & descendants
            RootTree = (TreeViewItem)TreeDataSource.RGP("root");
            RootTree.AddChild(CatalogTree);

            List<int> expandedIDs = (List<int>)TreeDataSource.RGP("expandedIDs"); // open folders
            IList rowsList = (IList)TreeDataSource.RGF("m_Rows"); // the list of TreeViewItems to draw
            rowsList.Add(CatalogTree);

            // TreeViewItems work in a super strange way
            // and can't be used as a big ol database
            // instead we gotta pull them in when needed from a different database (so ExternalCatalog.cs)

            // Custom stuff all collapsed?
            if (!expandedIDs.Contains(RootFolderID))
            {
                CatalogTree.children = ExternalCatalog.ActiveCatalogs.Count == 0 ? NoChildren : MinimizedChildren;
                return;
            }

            // not collapsed
            CatalogTree.children = new List<TreeViewItem>();
            foreach (var catalog in ExternalCatalog.ActiveCatalogs.Values)
            {
                // gotta make ui for descendants
                void DrawChildrenRecursive(TreeViewItem p, List<CatalogEntry> chils)
                {
                    if (p == null) return;
                    foreach (var c in chils)
                    {
                        p.AddChild(c.UI);
                        rowsList.Add(c.UI);
                        c.UI.depth = p.depth + 1;

                        if ((c.Children?.Count ?? 0) == 0)
                        {
                            c.UI.children = NoChildren;
                            continue;
                        }
                        if (expandedIDs.Contains(c.UI.id))
                            DrawChildrenRecursive(c.UI, c.Children);
                        else c.UI.children = MinimizedChildren;
                    }
                }
                DrawChildrenRecursive(CatalogTree, new List<CatalogEntry>() { catalog.Root });
            }
            OnOpenFolderIsCustom();
        }
        private Dictionary<int, CatalogEntry> fakeID2Entry = new();
        private Dictionary<CatalogEntry, object> entry2BigUI = new();
        private Dictionary<int, CatalogEntry> treeID2Entry = new();
        public void OnOpenFolderIsCustom()
        {
            if (ViewingFolders == null || ViewingFolders.Length == 0) return;
            if (!FakeTreeViews.Contains(ViewingFolders[0])) return;

            // clear objects shown
            ListArea.RRM("ShowObjectsInList", new int[] { });

            // add fake objects 
            var frT = Type.GetType("UnityEditor.FilteredHierarchy, UnityEditor.CoreModule").Inner("FilterResult");
            List<object> visibleEntries = new();

            AvailableSels.Clear();

            void ShowObject(string name, CatalogEntry entry)
            {
                if (!string.IsNullOrWhiteSpace(FakeSearchBar.value))
                {
                    if (!name.ToLower().Contains(FakeSearchBar.value.ToLower()))
                        return;
                }

                Texture2D icon = entry.UI.icon;

                object newObj = Activator.CreateInstance(frT);
                newObj.RSF("name", name);
                // Bruh UnityEditor.FilteredHierarchy+FilterResult.instanceID lags a shit ton if it's set to a real object
                int fakeID = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                newObj.RSF("instanceID", fakeID);//eskpdijf ? instanceID : 0);
                newObj.RSF("m_Icon", icon);

                fakeID2Entry[fakeID] = entry;
                entry2BigUI[entry] = newObj;
                treeID2Entry[entry.UI.id] = entry;

                if (entry.LoadedAsset != null)
                    newObj.RSF("instanceID", entry.LoadedAsset.GetInstanceID());

                visibleEntries.Add(newObj);
            }

            FakeBreadCrumbBar.Clear();
            void AddCrumbFolder(string name, TreeViewItem ui, bool bold)
            {
                var txt = new Label(name);
                FakeBreadCrumbBar.Add(txt);
                txt.style.fontSize = 13;
                txt.style.color = new Color(188 / 255f, 188 / 255f, 188 / 255f);
                txt.style.unityFontStyleAndWeight = !bold ? FontStyle.Normal : FontStyle.Bold;
                TreeViewItem ui2 = ui;
                txt.RegisterCallback<MouseUpEvent>(mue =>
                {
                    if (mue.button == 0)
                    {
                        FocusFolder(ui2);
                    }
                });
            }

            void AddCrumbArrow()
            {
                var bt = new Button();
                FakeBreadCrumbBar.Add(bt);
                bt.text = ">";
                bt.style.color = new Color(138f / 255f, 138f / 255f, 138f / 255f);
                bt.style.fontSize = 16;
                bt.style.borderLeftWidth = 0;
                bt.style.borderRightWidth = 0;
                bt.style.borderTopWidth = 0;
                bt.style.borderBottomWidth = 0;
                bt.style.backgroundColor = Color.clear;
                bt.transform.scale = new Vector3(.8f, 1.2f) * .9f;
                bt.style.marginLeft = 3;
                bt.style.marginTop = -2;
                bt.style.maxWidth = 8;
                bt.style.unityFontStyleAndWeight = FontStyle.Bold;
            }

            if (ExternalCatalog.UI2Entry.TryGetValue(ViewingFolders[0], out CatalogEntry entry) && entry != null)
            {
                if (entry.AssetType != null)
                {
                    //Selection.activeObject = entry.LoadedAsset == null ? entry : entry.LoadedAsset;
                    ProjectBrowserManager.LatestSel = entry;
                    entry = entry.Parent;
                }

                foreach (var c in entry.Children)
                {
                    ShowObject(c.name, c);
                    //AvailableSels[c.LoadedAsset == null ? c:c.LoadedAsset] = c;
                }


                AddCrumbFolder("External Catalogs", CatalogTree, false);
                AddCrumbArrow();

                List<CatalogEntry> gloobies = new() { entry }; 
                while (gloobies[gloobies.Count-1].Parent != null)
                    gloobies.Add(gloobies[gloobies.Count - 1].Parent);
                for (int i = gloobies.Count - 1; i >= 0; i--)
                {
                    AddCrumbFolder(gloobies[i].name, gloobies[i].UI, i == 0);
                    AddCrumbArrow();
                }
            }
            else if (ViewingFolders[0] == RootFolderID)
            {
#if !MARROW_SDK
                Selection.activeObject = SeamlessCatalogPlugin.Config;
#endif
                foreach (var cat in ExternalCatalog.ActiveCatalogs.Values)
                    ShowObject(cat.Name, cat.Root);
                AddCrumbFolder("External Catalogs", CatalogTree, true);
                AddCrumbArrow();
            }

            Array shownObjects = Array.CreateInstance(frT, visibleEntries.Count);
            for (int i = 0; i < visibleEntries.Count; i++)
                shownObjects.SetValue(visibleEntries[i], i);

            ListArea.RGF("m_LocalAssets").RGF("m_FilteredHierarchy").RSF("m_VisibleItems", shownObjects);
        }

        public void FocusFolder(TreeViewItem c)
        {
            ProjectBrowser.RGF("m_FolderTree").RRM("SelectionByKey", c);
        }
        public void OnOpenAsset(CatalogEntry obj)
        {
            if (obj.AssetType == null) // open folder!
                FocusFolder(obj.UI);
        }
        private static List<TreeViewItem> MinimizedChildren = new() { null };
        private static List<TreeViewItem> NoChildren = new();

        // THE Patches
        [HarmonyPatch]
        public static class FetchData_Patch
        {
            public static MethodBase TargetMethod() => AccessTools.Method(CatalogExtensions.typeof_ProjectBrowserColumnOneTreeViewDataSource, "FetchData");

            public static void Postfix(object __instance)
            {
                foreach (var i in ProjectBrowserManager.instances)
                    if (i.TreeDataSource == __instance)
                    {
                        i.OnFolderListRecomposed();
                        return;
                    }
            }
        }

        [HarmonyPatch]
        public static class FolderTreeSelectionCallback_Patch
        {
            public static MethodBase TargetMethod() => AccessTools.Method(CatalogExtensions.typeof_ProjectBrowser, "FolderTreeSelectionCallback");

            public static bool Prefix(object __instance, int[] selectedTreeViewInstanceIDs)
            {
                __instance.ToManager().ViewingFolders = selectedTreeViewInstanceIDs;

                if (selectedTreeViewInstanceIDs.Length == 1 && FakeTreeViews.Contains(selectedTreeViewInstanceIDs[0]))
                {
                    __instance.RRM("SetAsLastInteractedProjectBrowser");
                    __instance.RGF("m_SearchFilter").RSP("folders", new string[] { });
                    __instance.ToManager().OnOpenFolderIsCustom();
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch]
        public static class p3
        {
            public static MethodBase TargetMethod() => AccessTools.Method(CatalogExtensions.typeof_ProjectBrowser, "ListAreaItemSelectedCallback");

            public static void Prefix(object __instance, bool doubleClicked)
            {
                var man = __instance.ToManagerOrNull();
                if (man == null) return;
                int[] selecteds = (int[])man.ListArea.RRM("GetSelection");
                if (selecteds == null || selecteds.Length == 0) return;

                CatalogEntry ce;
                if (!man.fakeID2Entry.TryGetValue(selecteds[0], out ce))
                    return;

                if (doubleClicked)
                {
                    man.OnOpenAsset(ce);
                }else
                {
                    if (ce.AssetType != null)
                        ce.LoadAssetAsync(true);
                }
            }
        }
        [HarmonyPatch]
        public static class p4
        {
            public static MethodBase TargetMethod() => AccessTools.Method(CatalogExtensions.typeof_ProjectBrowser, "ListAreaKeyboardCallback");

            public static bool Prefix(object __instance)
            {
                var man = __instance.ToManagerOrNull();
                if (man?.ListArea == null) return true;
                int[] selecteds = (int[])man.ListArea.RRM("GetSelection");
                if (selecteds == null || selecteds.Length == 0) return true;

                CatalogEntry ce;
                if (man.fakeID2Entry.TryGetValue(selecteds[0], out ce))
                {
                    if (Event.current.type == EventType.KeyDown)
                    {
                        if (Event.current.keyCode == KeyCode.Return)
                            man.OnOpenAsset(ce);

                        if (Event.current.keyCode == KeyCode.DownArrow || Event.current.keyCode == KeyCode.UpArrow || Event.current.keyCode == KeyCode.LeftArrow || Event.current.keyCode == KeyCode.RightArrow || Event.current.keyCode == KeyCode.Backspace)
                            return true;

                        Event.current.Use();
                    }
                    if (Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Backspace && man.ViewingFolders.Length > 0)
                    {
                        CatalogEntry parentEntry = man.fakeID2Entry.Values.FirstOrDefault(cUI => cUI.UI.id == man.ViewingFolders[0]);
                        if (parentEntry?.Parent != null)
                        {
                            man.FocusFolder(parentEntry.Parent.UI);
                        } else
                        {
                            man.FocusFolder(man.CatalogTree);
                        }
                        Event.current.Use();
                    }
                    return false;
                }
                return true;
            }
        }
    }
    public static class ProjectBrowserManagerExtensions
    {
        public static ProjectBrowserManager ToManager(this object o) => ProjectBrowserManager.instances.First(i => ReferenceEquals(o, i.ProjectBrowser));
        public static ProjectBrowserManager ToManagerOrNull(this object o) => ProjectBrowserManager.instances.FirstOrDefault(i => ReferenceEquals(o, i.ProjectBrowser));
    }

}
#endif