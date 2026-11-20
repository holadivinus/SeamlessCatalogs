#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class RATSaveNotice : AssetModificationProcessor
{
    static string[] OnWillSaveAssets(string[] paths)
    {
        foreach (var item in paths)
        {
            AssetRAT ar = AssetDatabase.LoadAssetAtPath<AssetRAT>(item) as AssetRAT;
            if (ar != null) 
            {
                ar.OnBeforeSave();
            }
        }
        return paths;
    }
}

#endif