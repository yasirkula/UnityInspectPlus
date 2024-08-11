using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace InspectPlusNamespace
{
	public class InspectPlusSettings : ScriptableObject
	{
		private const string SAVE_PATH = "UserSettings/InspectPlusSettings.asset";

		private static InspectPlusSettings m_instance;
		public static InspectPlusSettings Instance
		{
			get
			{
				if( m_instance == null )
				{
					if( File.Exists( SAVE_PATH ) )
						m_instance = InternalEditorUtility.LoadSerializedFileAndForget( SAVE_PATH )[0] as InspectPlusSettings;
					else
						m_instance = CreateInstance<InspectPlusSettings>();

					m_instance.name = typeof( InspectPlusSettings ).Name;
					m_instance.hideFlags = HideFlags.DontSave;
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

		private double autoSaveTime;

		/// <summary>
		/// Without this constructor, <see cref="m_instance"/> is reset after domain reload (causing duplicate <see cref="InspectPlusSettings"/> instances in memory).
		/// </summary>
		protected InspectPlusSettings()
		{
			m_instance = this;
		}

		protected void OnEnable()
		{
			for( int i = FavoriteAssets.Count - 1; i >= 0; i-- )
			{
				if( !FavoriteAssets[i] )
					FavoriteAssets.RemoveAt( i );
			}

			/// After domain reload, <see cref="OnValidate"/> is invoked just before <see cref="OnEnable"/>. Don't save settings after every domain reload, so reset <see cref="autoSaveTime"/> here.
			autoSaveTime = 0;
			EditorApplication.update -= OnEditorUpdate;

			// If a settings asset in Assets folder is selected (they were saved in Assets on older versions of Inspect+), delete it
			string path = AssetDatabase.GetAssetPath( this );
			if( !string.IsNullOrEmpty( path ) && path.StartsWith( "Assets/" ) )
			{
				Debug.LogWarning( "<b>Inspect+ settings are now loaded from \"" + SAVE_PATH + "\". Deleting obsolete asset: \"" + AssetDatabase.GetAssetPath( this ) + "\"</b>" );
				AssetDatabase.DeleteAsset( path );
				m_instance = null;
			}
		}

		/// <summary>
		/// Since this asset is no longer serialized in Assets folder (<see cref="SAVE_PATH"/>), it isn't auto-saved by AssetDatabase. So we need to save it manually when a change is made.
		/// A timer is used to avoid excessive auto-saving while a value is rapidly changing (e.g. changing a float variable by dragging its name).
		/// </summary>
		protected void OnValidate()
		{
			if( autoSaveTime == 0 )
				EditorApplication.update += OnEditorUpdate;

			autoSaveTime = EditorApplication.timeSinceStartup + 2;
		}

		private void OnEditorUpdate()
		{
			if( EditorApplication.timeSinceStartup >= autoSaveTime )
			{
				EditorApplication.update -= OnEditorUpdate;
				Save();
			}
		}

		public void Save()
		{
			autoSaveTime = 0;
			Directory.CreateDirectory( Path.GetDirectoryName( SAVE_PATH ) );
			InternalEditorUtility.SaveToSerializedFileAndForget( new[] { this }, SAVE_PATH, true );
		}
	}
}