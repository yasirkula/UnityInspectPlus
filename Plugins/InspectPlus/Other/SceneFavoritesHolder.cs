#if UNITY_EDITOR
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using UnityEngine;

namespace InspectPlusNamespace
{
	/// <summary>
	/// No longer used because it's saved in scene and therefore isn't user-specific. Use "Window/Inspect+/Basket" instead.
	/// </summary>
	[ExecuteInEditMode]
	public class SceneFavoritesHolder : MonoBehaviour
	{
#if UNITY_EDITOR
		private void Awake()
		{
			EditorApplication.delayCall += () =>
			{
				if( this != null && !Application.isPlaying )
				{
					Scene scene = gameObject.scene;
					DestroyImmediate( gameObject );
					EditorSceneManager.MarkSceneDirty( scene );
					Debug.Log( "(Inspect+) Removed deprecated SceneFavoritesHolder GameObject from scene: " + scene.name );
				}
			};
		}
#endif
	}
}