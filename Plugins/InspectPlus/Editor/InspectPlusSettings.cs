using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace InspectPlusNamespace
{
	public class InspectPlusSettings : ScriptableObject
	{
		private const string INITIAL_SAVE_PATH = "Assets/Plugins/InspectPlus/InspectPlusSettings.asset";

		private static InspectPlusSettings m_instance;
		public static InspectPlusSettings Instance
		{
			get
			{
				if( !m_instance )
				{
					string[] instances = AssetDatabase.FindAssets( "t:InspectPlusSettings" );
					if( instances != null && instances.Length > 0 )
						m_instance = AssetDatabase.LoadAssetAtPath<InspectPlusSettings>( AssetDatabase.GUIDToAssetPath( instances[0] ) );

					if( !m_instance )
					{
						Directory.CreateDirectory( Path.GetDirectoryName( INITIAL_SAVE_PATH ) );

						AssetDatabase.CreateAsset( CreateInstance<InspectPlusSettings>(), INITIAL_SAVE_PATH );
						AssetDatabase.SaveAssets();
						m_instance = AssetDatabase.LoadAssetAtPath<InspectPlusSettings>( INITIAL_SAVE_PATH );

						Debug.Log( "Created Inspect+ settings file at " + INITIAL_SAVE_PATH + ". You can move this file around freely.", m_instance );
					}
				}

				return m_instance;
			}
		}

		public List<Object> FavoriteAssets = new List<Object>();

		[Space]
		[Tooltip( "Determines whether Favorites and History lists should be drawn horizontally or vertically" )]
		public bool CompactFavoritesAndHistoryLists = true;

		[Tooltip( "New windows should show Favorites list by default (if Favorites is not empty)" )]
		public bool ShowFavoritesByDefault = true;
		[Tooltip( "New windows should show History list by default (if History is not empty)" )]
		public bool ShowHistoryByDefault = true;

		[Space]
		[Tooltip( "If enabled, a new Unity tab will be created for objects when 'Open In New Tab' is clicked. Otherwise, these objects will be added to the active Inspect+ window's history list" )]
		public bool OpenNewTabsAsUnityTabs = true;

		[Space]
		[Tooltip( "Height of the Favorites list" )]
		public float FavoritesHeight = 42f;
		[Tooltip( "Height of the History list" )]
		public float HistoryHeight = 42f;
		[Tooltip( "Height of the compact Favorites and History lists" )]
		public float CompactListHeight = 28f;

		[Space]
		[HideInInspector]
		public ObjectBrowserWindow.SortType FavoritesSortType = ObjectBrowserWindow.SortType.Name;
		[HideInInspector]
		public ObjectBrowserWindow.SortType HistorySortType = ObjectBrowserWindow.SortType.None;

		[Space]
		[Tooltip( "Refresh and repaint interval of the Inspector in Normal mode" )]
		public float NormalModeRefreshInterval = 0.5f;
		[Tooltip( "Refresh interval of the Inspector in Debug mode" )]
		public float DebugModeRefreshInterval = 0.5f;

		[Space]
		[Tooltip( "When an asset or scene object's path is calculated, its path relative to the Object that the copy operation was performed on will also be calculated. During a paste operation, this path will be used first for smart paste operations (e.g. imagine objects A and B having children named C. When a variable on A is copied and that variable points to A.C, after pasting that variable to B, the value will be resolved to B.C instead of A.C)" )]
		public bool SmartCopyPaste = false;

		[Space]
		[Tooltip( "While inspecting a folder, selecting files/folders inside that folder will update Unity's selection, as well" )]
		public bool SyncProjectWindowSelection = true;
		[Tooltip( "While inspecting an object's Isolated Hierarchy, selecting child objects inside that hierarchy will update Unity's selection, as well" )]
		public bool SyncIsolatedHierarchyWindowSelection = true;
		[Tooltip( "Selecting objects in Basket window will update Unity's selection, as well" )]
		public bool SyncBasketSelection = true;

		[Space]
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