using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace InspectPlusNamespace
{
	// To support serializing scene objects and for performance reasons, BasketWindow uses two different save formats:
	// 1) BasketWindowState.objects: used to save state during domain reloads (it's fast but it doesn't support serializing scene objects between Unity sessions)
	// 2) BasketWindowSaveData: used to save state between Unity sessions and while saving/loading to/from a file (it's slow but it supports serializing scene objects between Unity sessions)
	public class BasketWindow : EditorWindow, IHasCustomMenu
	{
		private const string SAVE_FILE_EXTENSION = "basket";
		private const string SAVE_DIRECTORY = "Library/BasketWindows";
		private const string ACTIVE_WINDOW_SAVE_FILE = SAVE_DIRECTORY + "/_ActiveWindow." + SAVE_FILE_EXTENSION;

#pragma warning disable 0649
		private BasketWindowDrawer treeView;
#if !UNITY_2018_1_OR_NEWER
		[SerializeField] // This data is saved between Editor sessions instead of savedData on Unity versions that don't support EditorApplication.wantsToQuit
#endif
		private BasketWindowState treeViewState = new BasketWindowState();
		private SearchField searchField;

#if UNITY_2018_1_OR_NEWER
		[SerializeField] // This data is saved between Editor sessions inside EditorApplication.wantsToQuit on Unity 2018.1+
#endif
		private BasketWindowSaveData savedData;
#pragma warning restore 0649

		private bool shouldRepositionSelf;
		private bool isDirtyActiveWindow;
		private int titleObjectCount = 0;

		public static new void Show( bool newInstance )
		{
			BasketWindow window = newInstance ? CreateInstance<BasketWindow>() : GetWindow<BasketWindow>();
			window.titleObjectCount = 0;
			window.titleContent = new GUIContent( "Basket (0)" );
			window.minSize = new Vector2( 200f, 100f );

			if( newInstance )
				window.shouldRepositionSelf = true;
			else if( window.treeViewState.objects.Count == 0 && File.Exists( ACTIVE_WINDOW_SAVE_FILE ) )
				window.LoadData( ACTIVE_WINDOW_SAVE_FILE );

			window.Show();
		}

		void IHasCustomMenu.AddItemsToMenu( GenericMenu menu )
		{
			if( treeView == null )
				return;

			if( treeViewState.objects.Count > 0 )
			{
				menu.AddItem( new GUIContent( "Save..." ), false, () =>
				{
					Directory.CreateDirectory( SAVE_DIRECTORY );

					string savePath = EditorUtility.SaveFilePanel( "Save As", SAVE_DIRECTORY, "", SAVE_FILE_EXTENSION );
					if( !string.IsNullOrEmpty( savePath ) )
						SaveData( savePath );
				} );
			}
			else
				menu.AddDisabledItem( new GUIContent( "Save..." ) );

			menu.AddItem( new GUIContent( "Load..." ), false, () =>
			{
				Directory.CreateDirectory( SAVE_DIRECTORY );

				string loadPath = EditorUtility.OpenFilePanel( "Load", SAVE_DIRECTORY, SAVE_FILE_EXTENSION );
				if( !string.IsNullOrEmpty( loadPath ) )
					LoadData( loadPath );
			} );

			menu.AddSeparator( "" );

			menu.AddItem( new GUIContent( "Synchronize Selection With Unity" ), treeViewState.syncSelection, () => treeViewState.syncSelection = !treeViewState.syncSelection );

			if( treeViewState.objects.Count > 1 )
			{
				menu.AddSeparator( "" );

				menu.AddItem( new GUIContent( "Sort By Name" ), false, () =>
				{
					treeViewState.objects.Sort( new ObjectBrowserWindow.NameComparer() );
					treeView.Reload();
				} );

				menu.AddItem( new GUIContent( "Sort By Type" ), false, () =>
				{
					treeViewState.objects.Sort( new ObjectBrowserWindow.TypeComparer() );
					treeView.Reload();
				} );
			}
		}

		private void Awake()
		{
			treeViewState.syncSelection = InspectPlusSettings.Instance.SyncBasketSelection;

			if( savedData != null && savedData.IsValid )
			{
				// This BasketWindow has persisted between Editor sessions, reload its data
				LoadData();
			}
		}

		private void OnEnable()
		{
			treeViewState.objects.RemoveAll( ( obj ) => !obj );

#if UNITY_2018_1_OR_NEWER
			EditorApplication.wantsToQuit -= OnEditorQuitting;
			EditorApplication.wantsToQuit += OnEditorQuitting;
#endif
		}

		private void OnDisable()
		{
#if UNITY_2018_1_OR_NEWER
			EditorApplication.wantsToQuit -= OnEditorQuitting;
#endif
		}

#if UNITY_2018_1_OR_NEWER
		private bool OnEditorQuitting()
		{
			// Calling SaveData inside OnDestroy doesn't seem to save the changes to savedData between Unity sessions
			// on at least Unity 2019.4.26f1 (EditorWindow is possibly serialized before OnDestroy is invoked). Thus,
			// we're saving the data in EditorApplication.wantsToQuit instead
			SaveData();
			return true;
		}
#endif

		private void OnDestroy()
		{
			SaveData();
		}

		private void SaveData()
		{
			if( isDirtyActiveWindow )
			{
				Directory.CreateDirectory( SAVE_DIRECTORY );
				SaveData( ACTIVE_WINDOW_SAVE_FILE );

				isDirtyActiveWindow = false;
			}
		}

		private void SaveData( string path )
		{
			savedData = new BasketWindowSaveData();
			savedData.Serialize( treeViewState.objects );
			File.WriteAllText( path, EditorJsonUtility.ToJson( savedData, true ) );
		}

		private void LoadData( string path )
		{
			savedData = new BasketWindowSaveData();
			EditorJsonUtility.FromJsonOverwrite( File.ReadAllText( path ), savedData );

			LoadData();
		}

		private void LoadData()
		{
			treeViewState.objects = savedData.Deserialize();
			treeViewState.objects.RemoveAll( ( obj ) => !obj );

			if( treeView != null )
				treeView.Reload();
		}

		private void OnGUI()
		{
			if( treeView == null )
				treeView = new BasketWindowDrawer( treeViewState );

			if( searchField == null )
			{
				searchField = new SearchField();
				searchField.downOrUpArrowKeyPressed += treeView.SetFocusAndEnsureSelectedItem;
			}

			string searchTerm = treeViewState.searchTerm;
			treeViewState.searchTerm = searchField.OnToolbarGUI( searchTerm );
			if( treeViewState.searchTerm != searchTerm )
				treeView.Reload();

			treeView.OnGUI( GUILayoutUtility.GetRect( 0f, 100000f, 0f, 100000f ) );

			// This happens only when the mouse click is not captured by the TreeView. In this case, clear its selection
			if( Event.current.type == EventType.MouseDown && Event.current.button == 0 )
			{
				treeView.SetSelection( new int[0] );

				Event.current.Use();
				Repaint();
			}

			if( titleObjectCount != treeViewState.objects.Count )
			{
				titleObjectCount = treeViewState.objects.Count;
				titleContent = new GUIContent( "Basket (" + titleObjectCount + ")" );
				isDirtyActiveWindow = true;
			}

			if( shouldRepositionSelf )
			{
				shouldRepositionSelf = false;
				Vector2 _position = position.position + new Vector2( 50f, 50f );
				position = new Rect( _position, position.size );
			}
		}
	}

	[System.Serializable]
	public class BasketWindowSaveData
	{
#if UNITY_2019_2_OR_NEWER // Correctly save scene objects using GlobalObjectId on Unity 2019.2+
		[SerializeField]
		private string[] globalObjectIds;
#else
		[SerializeField]
		private List<Object> objects;
#endif

#if UNITY_2019_2_OR_NEWER
		public bool IsValid { get { return globalObjectIds != null && globalObjectIds.Length > 0; } }
#else
		public bool IsValid { get { return objects != null && objects.Count > 0; } }
#endif

		public void Serialize( List<Object> objects )
		{
#if UNITY_2019_2_OR_NEWER
			Object[] _objects = objects.ToArray();
			globalObjectIds = new string[_objects.Length];
			GlobalObjectId[] _globalObjectIds = new GlobalObjectId[_objects.Length];
			GlobalObjectId.GetGlobalObjectIdsSlow( _objects, _globalObjectIds );

			for( int i = 0; i < _globalObjectIds.Length; i++ )
				globalObjectIds[i] = _globalObjectIds[i].ToString();
#else
			this.objects = objects;
#endif
		}

		public List<Object> Deserialize()
		{
#if UNITY_2019_2_OR_NEWER
			if( globalObjectIds == null )
				return new List<Object>();

			Object[] result = new Object[globalObjectIds.Length];
			GlobalObjectId[] _globalObjectIds = new GlobalObjectId[globalObjectIds.Length];
			for( int i = 0; i < globalObjectIds.Length; i++ )
				GlobalObjectId.TryParse( globalObjectIds[i], out _globalObjectIds[i] );

			GlobalObjectId.GlobalObjectIdentifiersToObjectsSlow( _globalObjectIds, result );
			return new List<Object>( result );
#else
			return objects ?? new List<Object>();
#endif
		}
	}

	[System.Serializable]
	public class BasketWindowState : TreeViewState
	{
#pragma warning disable 0649
		public List<Object> objects = new List<Object>();
		public bool syncSelection = true;
		public string searchTerm; // Built-in search doesn't preserve row order, so we perform search manually
#pragma warning restore 0649
	}

	public class BasketWindowDrawer : TreeView
	{
		private readonly new BasketWindowState state;

		public BasketWindowDrawer( BasketWindowState state ) : base( state )
		{
			this.state = state;
			Reload();
		}

		protected override TreeViewItem BuildRoot()
		{
			TreeViewItem root = new TreeViewItem() { id = 0, depth = -1, displayName = "Root" };

			CompareInfo textComparer = new CultureInfo( "en-US" ).CompareInfo;
			CompareOptions textCompareOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
			bool isSearching = !string.IsNullOrEmpty( state.searchTerm );

			List<Object> objects = state.objects;
			for( int i = 0; i < objects.Count; i++ )
			{
				if( objects[i] && ( !isSearching || textComparer.IndexOf( objects[i].name, state.searchTerm, textCompareOptions ) >= 0 ) )
					root.AddChild( new TreeViewItem() { id = objects[i].GetInstanceID(), depth = 0, displayName = objects[i].name, icon = AssetPreview.GetMiniThumbnail( objects[i] ) } );
			}

			if( !root.hasChildren ) // If we don't create a dummy child, Unity throws an exception
				root.AddChild( new TreeViewItem() { id = 1, depth = 0, displayName = ( isSearching ? "No matching results..." : "Basket is empty..." ) } );

			return root;
		}

		protected override void SelectionChanged( IList<int> selectedIds )
		{
			if( !state.syncSelection || selectedIds == null )
				return;

			int[] selectionArray = new int[selectedIds.Count];
			selectedIds.CopyTo( selectionArray, 0 );

			Selection.instanceIDs = selectionArray;
		}

		protected override void ContextClickedItem( int id )
		{
			ContextClicked();
		}

		protected override void ContextClicked()
		{
			if( state.objects.Count > 0 && HasSelection() && HasFocus() )
			{
				GenericMenu contextMenu = new GenericMenu();
				contextMenu.AddItem( new GUIContent( "Remove" ), false, () => RemoveObjects( GetSelection() ) );
				contextMenu.ShowAsContext();

				if( Event.current != null && Event.current.type == EventType.ContextClick )
					Event.current.Use(); // It's safer to eat the event and if we don't, the context menu is sometimes displayed with a delay
			}
		}

		protected override bool CanStartDrag( CanStartDragArgs args )
		{
			return state.objects.Count > 0;
		}

		protected override void SetupDragAndDrop( SetupDragAndDropArgs args )
		{
			IList<int> draggedItemIds = SortItemIDsInRowOrder( args.draggedItemIDs );
			if( draggedItemIds.Count == 0 )
				return;

			List<Object> draggedObjects = new List<Object>( draggedItemIds.Count );
			for( int i = 0; i < draggedItemIds.Count; i++ )
			{
				Object obj = EditorUtility.InstanceIDToObject( draggedItemIds[i] );
				if( obj )
					draggedObjects.Add( obj );
			}

			if( draggedObjects.Count > 0 )
			{
				DragAndDrop.objectReferences = draggedObjects.ToArray();
				DragAndDrop.StartDrag( draggedObjects.Count > 1 ? "<Multiple>" : draggedObjects[0].name );
			}
		}

		protected override DragAndDropVisualMode HandleDragAndDrop( DragAndDropArgs args )
		{
			if( args.dragAndDropPosition == DragAndDropPosition.UponItem )
				return DragAndDropVisualMode.None;
			if( hasSearch && args.dragAndDropPosition == DragAndDropPosition.BetweenItems )
				return DragAndDropVisualMode.None;

			if( args.performDrop )
			{
				List<Object> objects = state.objects;
				Object[] draggedObjects = DragAndDrop.objectReferences;
				List<int> draggedInstanceIDs = new List<int>( draggedObjects.Length );
				int insertIndex = ( args.dragAndDropPosition == DragAndDropPosition.OutsideItems ) ? objects.Count : args.insertAtIndex;
				for( int i = 0; i < draggedObjects.Length; i++ )
				{
					if( !draggedObjects[i] )
						continue;

					objects.Insert( insertIndex + draggedInstanceIDs.Count, draggedObjects[i] );
					draggedInstanceIDs.Add( draggedObjects[i].GetInstanceID() );
				}

				int addedObjectCount = draggedInstanceIDs.Count;
				if( addedObjectCount > 0 )
				{
					// Remove duplicates
					for( int i = objects.Count - 1; i >= 0; i-- )
					{
						if( ( i < insertIndex || i >= insertIndex + addedObjectCount ) && System.Array.IndexOf( draggedObjects, objects[i] ) >= 0 )
							objects.RemoveAt( i );
					}

					SetSelection( draggedInstanceIDs, TreeViewSelectionOptions.FireSelectionChanged );
					Reload();
				}
			}

			return DragAndDropVisualMode.Copy;
		}

		protected override void CommandEventHandling()
		{
			if( state.objects.Count > 0 && HasFocus() ) // There may be multiple SearchResultTreeViews. Execute the event only for the currently focused one
			{
				Event ev = Event.current;
				if( ev.type == EventType.ValidateCommand || ev.type == EventType.ExecuteCommand )
				{
					if( ev.commandName == "Delete" || ev.commandName == "SoftDelete" )
					{
						if( ev.type == EventType.ExecuteCommand )
							RemoveObjects( GetSelection() );

						ev.Use();
						return;
					}
				}
			}

			base.CommandEventHandling();
		}

		private void RemoveObjects( IList<int> instanceIDs )
		{
			bool removedObjects = false;
			foreach( int instanceID in instanceIDs )
			{
				Object obj = EditorUtility.InstanceIDToObject( instanceID );
				if( obj && state.objects.Remove( obj ) )
					removedObjects = true;
			}

			if( removedObjects )
				Reload();
		}
	}
}