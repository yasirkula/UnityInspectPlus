using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace InspectPlusNamespace
{
	public class BasketWindow : EditorWindow, IHasCustomMenu
	{
#pragma warning disable 0649
		private BasketWindowDrawer treeView;
		[SerializeField]
		private BasketWindowState treeViewState = new BasketWindowState();
		private SearchField searchField;
#pragma warning restore 0649

		private bool shouldRepositionSelf;
		private int titleObjectCount = -1;

		public static new void Show( bool newInstance )
		{
			BasketWindow window = newInstance ? CreateInstance<BasketWindow>() : GetWindow<BasketWindow>();
			window.titleContent = new GUIContent( "Basket (0)" );
			window.minSize = new Vector2( 200f, 100f );

			if( newInstance )
				window.shouldRepositionSelf = true;

			window.Show();
		}

		void IHasCustomMenu.AddItemsToMenu( GenericMenu menu )
		{
			menu.AddItem( new GUIContent( "Synchronize Selection With Unity" ), treeViewState.syncSelection, () => treeViewState.syncSelection = !treeViewState.syncSelection );

			if( treeView != null && treeViewState.objects.Count > 0 )
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
		}

		private void OnEnable()
		{
			treeViewState.objects.RemoveAll( ( obj ) => !obj );
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