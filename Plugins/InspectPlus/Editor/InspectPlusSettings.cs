using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace InspectPlusNamespace
{
	public class InspectPlusSettings : ScriptableObject
	{
		private const string SAVE_PATH = "Assets/Plugins/InspectPlus/Settings.asset";

		private static InspectPlusSettings m_instance;
		public static InspectPlusSettings Instance
		{
			get
			{
				if( !m_instance )
				{
					m_instance = AssetDatabase.LoadAssetAtPath<InspectPlusSettings>( SAVE_PATH );
					if( !m_instance )
					{
						Directory.CreateDirectory( Path.GetDirectoryName( SAVE_PATH ) );

						AssetDatabase.CreateAsset( CreateInstance<InspectPlusSettings>(), SAVE_PATH );
						AssetDatabase.SaveAssets();
						m_instance = AssetDatabase.LoadAssetAtPath<InspectPlusSettings>( SAVE_PATH );
					}
				}

				return m_instance;
			}
		}

		public List<Object> FavoriteAssets = new List<Object>();

		[Tooltip( "Height of the Favorites list" )]
		public float FavoritesHeight = 42f;
		[Tooltip( "Height of the History list" )]
		public float HistoryHeight = 42f;

		[Tooltip( "Refresh and repaint interval of the Inspector in Normal mode" )]
		public float NormalModeRefreshInterval = 0.5f;
		[Tooltip( "Refresh interval of the Inspector in Debug mode" )]
		public float DebugModeRefreshInterval = 0.5f;

		[Tooltip( "New windows should show Favorites list by default (if Favorites is not empty)" )]
		public bool ShowFavoritesByDefault = true;
		[Tooltip( "New windows should show History list by default (if History is not empty)" )]
		public bool ShowHistoryByDefault = true;
		[Tooltip( "While inspecting a folder, selecting files/folders inside the folder will update Unity's selection, as well" )]
		public bool SyncProjectWindowSelection = true;
		[Tooltip( "Selecting an object in Favorites or History will highlight the object in Hierarchy/Project" )]
		public bool AutomaticallyPingSelectedObject = true;
		[Tooltip( "Clearing the History via context menu will delete the currently inspected object's History entry, as well" )]
		public bool ClearingHistoryRemovesActiveObject = false;

		private void OnEnable()
		{
			for( int i = FavoriteAssets.Count - 1; i >= 0; i-- )
			{
				if( !FavoriteAssets[i] )
					FavoriteAssets.RemoveAt( i );
			}
		}
	}
}