#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using UnityEngine;

namespace InspectPlusNamespace
{
	[ExecuteInEditMode]
	public class SceneFavoritesHolder : MonoBehaviour
	{
#if UNITY_EDITOR
		private const string GAMEOBJECT_NAME = "InspectPlusFavorites";

		private static readonly Dictionary<Scene, SceneFavoritesHolder> lookupTable = new Dictionary<Scene, SceneFavoritesHolder>( 8 );
		public static readonly List<SceneFavoritesHolder> Instances = new List<SceneFavoritesHolder>( 8 );

		public List<Object> FavoriteObjects = new List<Object>();

		private void OnEnable()
		{
			Instances.Add( this );
			lookupTable[gameObject.scene] = this;

			for( int i = FavoriteObjects.Count - 1; i >= 0; i-- )
			{
				if( !FavoriteObjects[i] )
					FavoriteObjects.RemoveAt( i );
			}
		}

		private void OnDisable()
		{
			Instances.Remove( this );
		}

		public static SceneFavoritesHolder GetInstance( Scene scene )
		{
			SceneFavoritesHolder result;
			if( !lookupTable.TryGetValue( scene, out result ) )
			{
				GameObject[] objects = scene.GetRootGameObjects();
				for( int i = 0; i < objects.Length; i++ )
				{
					if( objects[i].CompareTag( "EditorOnly" ) && objects[i].name == GAMEOBJECT_NAME )
					{
						result = objects[i].GetComponent<SceneFavoritesHolder>();
						break;
					}
				}

				if( result == null )
				{
					result = new GameObject( GAMEOBJECT_NAME ).AddComponent<SceneFavoritesHolder>();
					result.gameObject.tag = "EditorOnly";
					result.gameObject.hideFlags = HideFlags.HideInHierarchy;
					SceneManager.MoveGameObjectToScene( result.gameObject, scene );
					result.SetSceneDirty();
				}
			}

			return result;
		}

		public void SetSceneDirty()
		{
			if( !EditorApplication.isPlaying )
				EditorSceneManager.MarkSceneDirty( gameObject.scene );
		}
#endif
	}
}