using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace InspectPlusNamespace
{
	public class BasketWindow : EditorWindow, IHasCustomMenu
	{
		private class NameComparer<T> : IComparer<T> where T : BasketWindowEntry
		{
			public int Compare( T x, T y )
			{
				return EditorUtility.NaturalCompare( x.Name, y.Name );
			}
		}

		private class TypeComparer<T> : IComparer<T> where T : BasketWindowEntry
		{
			private readonly ObjectBrowserWindow.TypeComparer objectTypeComparer = new ObjectBrowserWindow.TypeComparer();

			public int Compare( T x, T y )
			{
				if( x.Target == null )
				{
					if( y.Target != null )
						return 1;
					else
						return EditorUtility.NaturalCompare( x.Name, y.Name );
				}
				else if( y.Target == null )
					return -1;

				return objectTypeComparer.Compare( x.Target, y.Target );
			}
		}

		private const string SAVE_FILE_EXTENSION = "basket";
		private const string SAVE_DIRECTORY = "UserSettings/BasketWindows";
		private const string ACTIVE_WINDOW_SAVE_FILE = SAVE_DIRECTORY + "/_ActiveWindow." + SAVE_FILE_EXTENSION;

#pragma warning disable 0649
		private BasketWindowDrawer treeView;
		[SerializeField]
		private BasketWindowState treeViewState = new BasketWindowState();
		private SearchField searchField;
#pragma warning restore 0649

		private bool shouldRepositionSelf;
		private bool isDataDirty;
		private bool isHierarchyWindowDirty, isProjectWindowDirty, isNewSceneOpened;
		private int titleObjectCount = 0;

		public static new BasketWindow Show( bool newInstance )
		{
			BasketWindow window = newInstance ? CreateInstance<BasketWindow>() : GetWindow<BasketWindow>();
			window.titleObjectCount = 0;
			window.titleContent = new GUIContent( "Basket (0)" );
			window.minSize = new Vector2( 200f, 100f );

			if( newInstance )
				window.shouldRepositionSelf = true;
			else if( window.treeViewState.Entries.Count == 0 && File.Exists( ACTIVE_WINDOW_SAVE_FILE ) )
				window.LoadData( ACTIVE_WINDOW_SAVE_FILE );

			window.Show();
			return window;
		}

		void IHasCustomMenu.AddItemsToMenu( GenericMenu menu )
		{
			if( treeView == null )
				return;

			if( treeViewState.Entries.Count > 0 )
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

			menu.AddItem( new GUIContent( "Synchronize Selection With Unity" ), treeViewState.SyncSelection, () => treeViewState.SyncSelection = !treeViewState.SyncSelection );

			if( treeViewState.Entries.Count > 1 )
			{
				menu.AddSeparator( "" );

				menu.AddItem( new GUIContent( "Sort By Name" ), false, () =>
				{
					treeViewState.Entries.Sort( new NameComparer<BasketWindowRootEntry>() );
					foreach( BasketWindowRootEntry entry in treeViewState.Entries )
					{
						if( entry.Children.Count > 0 )
							entry.Children.Sort( new NameComparer<BasketWindowChildEntry>() );
					}

					treeView.Reload();
				} );

				menu.AddItem( new GUIContent( "Sort By Type" ), false, () =>
				{
					treeViewState.Entries.Sort( new TypeComparer<BasketWindowRootEntry>() );
					foreach( BasketWindowRootEntry entry in treeViewState.Entries )
					{
						if( entry.Children.Count > 0 )
							entry.Children.Sort( new TypeComparer<BasketWindowChildEntry>() );
					}

					treeView.Reload();
				} );
			}
		}

		private void Awake()
		{
			treeViewState.SyncSelection = InspectPlusSettings.Instance.SyncBasketSelection;

			if( treeViewState.Entries.Count > 0 )
			{
				// This BasketWindow has persisted between Editor sessions, reload its data
				LoadData();
			}
		}

		private void OnEnable()
		{
			EditorSceneManager.sceneOpened += OnSceneOpened;
#if UNITY_2018_1_OR_NEWER
			EditorApplication.wantsToQuit += OnEditorQuitting;
#endif
		}

		private void OnDisable()
		{
			EditorSceneManager.sceneOpened -= OnSceneOpened;
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

		private void OnSceneOpened( Scene scene, OpenSceneMode mode )
		{
			isNewSceneOpened = isHierarchyWindowDirty = true;
		}

		private void OnHierarchyChange()
		{
			isHierarchyWindowDirty = true;
		}

		private void OnProjectChange()
		{
			isProjectWindowDirty = true;
		}

		private void InitializeTreeViewIfNecessary()
		{
			if( treeView == null )
				treeView = new BasketWindowDrawer( treeViewState );
		}

		public void AddToBasket( Object[] objects )
		{
			InitializeTreeViewIfNecessary();
			treeView.AddObjects( objects, treeViewState.Entries.Count );
		}

		private void SaveData()
		{
			if( isDataDirty )
			{
				Directory.CreateDirectory( SAVE_DIRECTORY );
				SaveData( ACTIVE_WINDOW_SAVE_FILE );

				isDataDirty = false;
			}
		}

		private void SaveData( string path )
		{
			File.WriteAllText( path, EditorJsonUtility.ToJson( treeViewState, false ) );
		}

		private void LoadData( string path )
		{
			EditorJsonUtility.FromJsonOverwrite( File.ReadAllText( path ), treeViewState );
			LoadData();
		}

		private void LoadData()
		{
			isHierarchyWindowDirty = isProjectWindowDirty = isNewSceneOpened = true;

			if( treeView != null )
				treeView.Reload();
		}

		private void OnGUI()
		{
			InitializeTreeViewIfNecessary();

			if( searchField == null )
			{
				searchField = new SearchField();
				searchField.downOrUpArrowKeyPressed += treeView.SetFocusAndEnsureSelectedItem;
			}

			bool isDirty = false;
			if( isNewSceneOpened )
			{
				isNewSceneOpened = false;
				foreach( BasketWindowRootEntry entry in treeViewState.Entries )
					isDirty |= entry.RefreshTargetsOfChildren();
			}

			if( isHierarchyWindowDirty )
			{
				isHierarchyWindowDirty = false;
				foreach( BasketWindowRootEntry entry in treeViewState.Entries )
				{
					if( entry.Target as SceneAsset )
						isDirty |= entry.RefreshNamesOfChildren();
				}
			}

			if( isProjectWindowDirty )
			{
				isProjectWindowDirty = false;
				foreach( BasketWindowRootEntry entry in treeViewState.Entries )
					isDirty |= entry.RefreshName();
			}

			isDataDirty |= isDirty;

			string searchTerm = treeViewState.SearchTerm;
			treeViewState.SearchTerm = searchField.OnToolbarGUI( searchTerm );
			isDirty |= treeViewState.SearchTerm != searchTerm;

			if( isDirty )
				treeView.Reload();

			treeView.OnGUI( GUILayoutUtility.GetRect( 0f, 100000f, 0f, 100000f ) );

			// This happens only when the mouse click is not captured by the TreeView. In this case, clear its selection
			if( Event.current.type == EventType.MouseDown && Event.current.button == 0 )
			{
				treeView.SetSelection( new int[0] );

				Event.current.Use();
				Repaint();
			}

			int entryCount = treeViewState.TotalEntryCount;
			if( titleObjectCount != entryCount )
			{
				titleObjectCount = entryCount;
				titleContent = new GUIContent( "Basket (" + titleObjectCount + ")" );
				isDataDirty = true;
			}

			if( shouldRepositionSelf )
			{
				shouldRepositionSelf = false;
				Vector2 _position = position.position + new Vector2( 50f, 50f );
				position = new Rect( _position, position.size );
			}
		}
	}

	public abstract class BasketWindowEntry
	{
#pragma warning disable 0649
		public Object Target;
		public string Name = "Null";
#pragma warning restore 0649
		public int InstanceID { get { return ( Target != null ) ? Target.GetInstanceID() : GetHashCode(); } }

		public BasketWindowEntry( Object target )
		{
			RefreshTarget( target );
		}

		public bool RefreshTarget( Object target )
		{
			if( target == null || target == Target )
				return RefreshName();

			Target = target;
			RefreshName();
			return true;
		}

		public bool RefreshName()
		{
			if( Target == null )
				return false;

			string prevName = Name;
			Name = Target.name;
			return Name != prevName;
		}
	}

	[Serializable]
	public class BasketWindowRootEntry : BasketWindowEntry
	{
		public List<BasketWindowChildEntry> Children = new List<BasketWindowChildEntry>();

		public BasketWindowRootEntry( Object target ) : base( target )
		{
		}

		public bool RefreshNamesOfChildren()
		{
			bool isDirty = false;
			foreach( BasketWindowChildEntry child in Children )
				isDirty |= child.RefreshName();

			return isDirty;
		}

		public bool RefreshTargetsOfChildren()
		{
#if UNITY_2019_2_OR_NEWER
			if( Children == null )
				return false;

			List<BasketWindowChildEntry> nullEntries = Children.FindAll( ( e ) => e.Target == null );
			if( nullEntries.Count == 0 )
				return false;

			bool isDirty = false;
			Object[] objects = new Object[nullEntries.Count];
			GlobalObjectId[] globalObjectIds = new GlobalObjectId[nullEntries.Count];
			for( int i = 0; i < nullEntries.Count; i++ )
				GlobalObjectId.TryParse( nullEntries[i].ID, out globalObjectIds[i] );

			GlobalObjectId.GlobalObjectIdentifiersToObjectsSlow( globalObjectIds, objects );
			for( int i = 0; i < objects.Length; i++ )
				isDirty |= nullEntries[i].RefreshTarget( objects[i] );

			return isDirty;
#else
			return false;
#endif
		}
	}

	[Serializable]
	public class BasketWindowChildEntry : BasketWindowEntry
	{
#if UNITY_2019_2_OR_NEWER // Correctly save scene objects using GlobalObjectId on Unity 2019.2+
		public string ID;
#endif

		public BasketWindowChildEntry( Object target ) : base( target )
		{
#if UNITY_2019_2_OR_NEWER
			ID = GlobalObjectId.GetGlobalObjectIdSlow( target ).ToString();
#endif
		}
	}

	[Serializable]
	public class BasketWindowState : TreeViewState
	{
#pragma warning disable 0649
		public List<BasketWindowRootEntry> Entries = new List<BasketWindowRootEntry>();
		public bool SyncSelection = true;
		public string SearchTerm; // Built-in search doesn't preserve row order, so we perform search manually
#pragma warning restore 0649

		public int TotalEntryCount
		{
			get
			{
				int result = Entries.Count;
				foreach( BasketWindowRootEntry entry in Entries )
					result += entry.Children.Count;

				return result;
			}
		}
	}

	public class BasketWindowTreeViewItem : TreeViewItem
	{
		public readonly BasketWindowEntry Entry;
		public BasketWindowRootEntry ParentEntry { get { return ( parent is BasketWindowTreeViewItem ) ? ( parent as BasketWindowTreeViewItem ).Entry as BasketWindowRootEntry : null; } }
		public int Index { get { return parent.children.IndexOf( this ); } }

		public BasketWindowTreeViewItem( BasketWindowEntry entry ) : base()
		{
			Entry = entry;
		}
	}

	public class BasketWindowDrawer : TreeView
	{
		private readonly new BasketWindowState state;
		private static readonly CompareInfo textComparer = new CultureInfo( "en-US" ).CompareInfo;

		public BasketWindowDrawer( BasketWindowState state ) : base( state )
		{
			this.state = state;
			Reload();
		}

		protected override TreeViewItem BuildRoot()
		{
			TreeViewItem root = new TreeViewItem() { id = -1, depth = -1, displayName = "Root" };
			foreach( BasketWindowRootEntry entry in state.Entries )
				CreateItemForEntryRecursive( entry, root );

			if( !root.hasChildren ) // If we don't create a dummy child, Unity throws an exception
				root.AddChild( new TreeViewItem() { id = -2, depth = 0, displayName = string.IsNullOrEmpty( state.SearchTerm ) ? "Basket is empty..." : "No matching results..." } );

			return root;
		}

		private void CreateItemForEntryRecursive( BasketWindowEntry entry, TreeViewItem parent )
		{
			if( string.IsNullOrEmpty( state.SearchTerm ) || textComparer.IndexOf( entry.Name, state.SearchTerm, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace ) >= 0 )
			{
				BasketWindowTreeViewItem item = new BasketWindowTreeViewItem( entry )
				{
					id = entry.InstanceID,
					depth = parent.depth + 1,
					displayName = entry.Name,
					icon = ( entry.Target != null ) ? AssetPreview.GetMiniThumbnail( entry.Target ) : null,
				};

				parent.AddChild( item );
				if( string.IsNullOrEmpty( state.SearchTerm ) )
					parent = item;
			}

			if( entry is BasketWindowRootEntry )
			{
				foreach( BasketWindowChildEntry childEntry in ( entry as BasketWindowRootEntry ).Children )
					CreateItemForEntryRecursive( childEntry, parent );
			}
		}

		protected override void SelectionChanged( IList<int> selectedIds )
		{
			if( !state.SyncSelection || selectedIds == null )
				return;

			int[] selectionArray = new int[selectedIds.Count];
			selectedIds.CopyTo( selectionArray, 0 );

			Selection.instanceIDs = selectionArray;
		}

		protected override void DoubleClickedItem( int id )
		{
			AssetDatabase.OpenAsset( id );
		}

		protected override void ContextClickedItem( int id )
		{
			ContextClicked();
		}

		protected override void ContextClicked()
		{
			if( state.Entries.Count > 0 && HasSelection() && HasFocus() )
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
			return state.Entries.Count > 0;
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

			DragAndDrop.objectReferences = draggedObjects.ToArray();
			DragAndDrop.SetGenericData( "BasketIDs", draggedItemIds );
			DragAndDrop.StartDrag( ( draggedItemIds.Count > 1 ) ? "<Multiple>" : FindEntryWithInstanceID( draggedItemIds[0] ).Name );
		}

		protected override DragAndDropVisualMode HandleDragAndDrop( DragAndDropArgs args )
		{
			if( args.dragAndDropPosition == DragAndDropPosition.UponItem )
				return DragAndDropVisualMode.None;
			if( hasSearch && args.dragAndDropPosition == DragAndDropPosition.BetweenItems )
				return DragAndDropVisualMode.None;

			if( args.performDrop )
			{
				AddObjects( DragAndDrop.objectReferences, DragAndDrop.GetGenericData( "BasketIDs" ) as IList<int>,
					( args.parentItem is BasketWindowTreeViewItem ) ? ( args.parentItem as BasketWindowTreeViewItem ).Entry as BasketWindowRootEntry : null,
					( args.dragAndDropPosition == DragAndDropPosition.OutsideItems ) ? state.Entries.Count : args.insertAtIndex );
			}

			return DragAndDropVisualMode.Copy;
		}

		protected override void CommandEventHandling()
		{
			if( state.Entries.Count > 0 && HasFocus() ) // There may be multiple SearchResultTreeViews. Execute the event only for the currently focused one
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

		public void AddObjects( Object[] objects, int insertIndex )
		{
			AddObjects( objects, null, null, insertIndex );
		}

		private void AddObjects( Object[] objects, IList<int> instanceIDs, BasketWindowRootEntry targetParentEntry, int insertIndex )
		{
			// If we're in search mode, exit search mode to make things easier
			if( !string.IsNullOrEmpty( state.SearchTerm ) )
			{
				state.SearchTerm = null;
				Reload();
			}

			if( instanceIDs == null )
				instanceIDs = Array.ConvertAll( objects, ( e ) => ( e != null ) ? e.GetInstanceID() : 0 );

			List<int> addedInstanceIDs = new List<int>();
			for( int i = instanceIDs.Count - 1; i >= 0; i-- )
			{
				if( !addedInstanceIDs.Contains( instanceIDs[i] ) && AddObject( instanceIDs[i], targetParentEntry, ref insertIndex ) != null )
					addedInstanceIDs.Add( instanceIDs[i] );
			}

			if( addedInstanceIDs.Count > 0 )
			{
				/// Filtering addedInstanceIDs with <see cref="TreeView.FindItem"/> is necessary in the following scenario to avoid an error in <see cref="TreeView.SetSelection"/>:
				/// 1) Object X from scene Y is added to the basket
				/// 2) Scene Y is closed
				/// 3) Object X is drag & dropped to change its sibling index
				Reload();
				SetSelection( addedInstanceIDs.FindAll( ( e ) => FindItem( e, rootItem ) != null ), TreeViewSelectionOptions.FireSelectionChanged | TreeViewSelectionOptions.RevealAndFrame );
			}
		}

		private BasketWindowEntry AddObject( int instanceID, BasketWindowRootEntry targetParentEntry, ref int insertIndex )
		{
			BasketWindowRootEntry parentEntry;
			BasketWindowEntry entry = FindEntryWithInstanceID( instanceID, out parentEntry );
			if( entry != null ) // If the object already exists in the BasketWindow
			{
				if( parentEntry != targetParentEntry ) // Don't allow changing the entry's parent
					return entry;
				else if( parentEntry != null )
					ReorderEntry( entry as BasketWindowChildEntry, parentEntry.Children, ref insertIndex );
				else
					ReorderEntry( entry as BasketWindowRootEntry, state.Entries, ref insertIndex );

				return entry;
			}

			Object obj = EditorUtility.InstanceIDToObject( instanceID );
			if( obj == null )
				return null;

			if( AssetDatabase.Contains( obj ) )
			{
				entry = new BasketWindowRootEntry( obj );
				state.Entries.Insert( ( targetParentEntry == null ) ? insertIndex : state.Entries.IndexOf( targetParentEntry ), entry as BasketWindowRootEntry );
			}
			else
			{
				string scenePath = AssetDatabase.GetAssetOrScenePath( obj );
				if( string.IsNullOrEmpty( scenePath ) )
				{
					Debug.LogWarning( "Object is neither asset nor scene object: " + obj, obj );
					return null;
				}

				// Make sure that scene objects' SceneAsset exists in the list
				SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>( scenePath );
				parentEntry = ( FindEntryWithInstanceID( sceneAsset.GetInstanceID() ) as BasketWindowRootEntry ) ?? AddObject( sceneAsset.GetInstanceID(), targetParentEntry, ref insertIndex ) as BasketWindowRootEntry;
				if( parentEntry == null )
					return null;

				entry = new BasketWindowChildEntry( obj );
				parentEntry.Children.Insert( ( parentEntry == targetParentEntry ) ? insertIndex : parentEntry.Children.Count, entry as BasketWindowChildEntry );
			}

			return entry;
		}

		private void RemoveObjects( IList<int> instanceIDs )
		{
			bool removedObjects = false;
			foreach( int instanceID in instanceIDs )
			{
				BasketWindowRootEntry parentEntry;
				BasketWindowEntry entry = FindEntryWithInstanceID( instanceID, out parentEntry );
				if( entry is BasketWindowRootEntry )
					removedObjects |= state.Entries.Remove( entry as BasketWindowRootEntry );
				else if( entry is BasketWindowChildEntry )
					removedObjects |= parentEntry.Children.Remove( entry as BasketWindowChildEntry );
			}

			if( removedObjects )
				Reload();
		}

		private void ReorderEntry<T>( T entry, List<T> siblings, ref int newIndex ) where T : BasketWindowEntry
		{
			int index = siblings.IndexOf( entry );
			if( index < newIndex )
				newIndex--;

			siblings.RemoveAt( index );
			siblings.Insert( newIndex, entry );
		}

		private BasketWindowEntry FindEntryWithInstanceID( int instanceID )
		{
			BasketWindowRootEntry parentEntry;
			return FindEntryWithInstanceID( instanceID, out parentEntry );
		}

		private BasketWindowEntry FindEntryWithInstanceID( int instanceID, out BasketWindowRootEntry parentEntry )
		{
			foreach( BasketWindowRootEntry entry in state.Entries )
			{
				if( entry.InstanceID == instanceID )
				{
					parentEntry = null;
					return entry;
				}

				foreach( BasketWindowChildEntry childEntry in entry.Children )
				{
					if( childEntry.InstanceID == instanceID )
					{
						parentEntry = entry;
						return childEntry;
					}
				}
			}

			/// Entry couldn't be found. Perhaps its <see cref="BasketWindowEntry.InstanceID"/> has changed because the object was destroyed or restored after the tree was last reloaded.
			BasketWindowTreeViewItem item = FindItem( instanceID, rootItem ) as BasketWindowTreeViewItem;
			if( item != null )
			{
				parentEntry = item.ParentEntry;
				return item.Entry;
			}

			parentEntry = null;
			return null;
		}
	}
}