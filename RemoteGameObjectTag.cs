#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[ExecuteAlways]
public class RemoteGameObjectTag : MonoBehaviour
{
    private void OnEnable()
    { 
        this.hideFlags |= HideFlags.DontSaveInBuild;

        //if (!PrefabUtility.IsPartOfAnyPrefab(gameObject))
        //    DestroyImmediate(gameObject);
    }

    private void Update()
    {
        //if (!PrefabUtility.IsPartOfAnyPrefab(gameObject))
        //    DestroyImmediate(gameObject);
    } 
}
#endif