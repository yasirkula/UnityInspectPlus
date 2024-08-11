using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace InspectPlusNamespace
{
	public delegate void ProjectWindowSelectionChangedDelegate( IList<int> newSelection );

	[System.Serializable]
	public class CustomProjectWindow
	{
#pragma warning disable 0649
		[SerializeField]
		private TreeViewState treeViewState;
		[SerializeField]
		private string rootDirectory;
#pragma warning restore 0649

		private CustomProjectWindowDrawer treeView;
		private SearchField searchField;
		private GUIContent createButtonContent;

		public ProjectWindowSelectionChangedDelegate OnSelectionChanged;

		public void Show( string directory )
		{
			if( treeView != null && rootDirectory == directory )
			{
				Refresh();
				return;
			}

			if( treeViewState == null || rootDirectory != directory )
				treeViewState = new TreeViewState();

			treeView = new CustomProjectWindowDrawer( treeViewState, directory )
			{
				OnSelectionChanged = ( newSelection ) =>
				{
					if( OnSelectionChanged != null )
						OnSelectionChanged( newSelection );
				}
			};

			searchField = new SearchField();
			searchField.downOrUpArrowKeyPressed += treeView.SetFocusAndEnsureSelectedItem;

			createButtonContent = new GUIContent( "Create" );
			rootDirectory = directory;
		}

		public CustomProjectWindowDrawer GetTreeView()
		{
			return treeView;
		}

		public void Refresh()
		{
			if( treeView != null )
				treeView.Reload();
		}

		public void OnGUI()
		{
			GUILayout.BeginHorizontal( EditorStyles.toolbar );
			Rect rect = GUILayoutUtility.GetRect( createButtonContent, EditorStyles.toolbarDropDown, GUILayout.ExpandWidth( false ) );
			if( EditorGUI.DropdownButton( rect, createButtonContent, FocusType.Passive, EditorStyles.toolbarDropDown ) )
			{
				GUIUtility.hotControl = 0;

				treeView.ChangeUnitySelection();
				EditorUtility.DisplayPopupMenu( rect, "Assets/Create", null );
			}

			GUILayout.Space( 8f );
			treeView.searchString = searchField.OnToolbarGUI( treeView.searchString );
			GUILayout.EndHorizontal();

			rect = GUILayoutUtility.GetRect( 0, 100000, 0, 100000 );
			treeView.OnGUI( rect );
		}
	}

	public class CustomProjectWindowDrawer : TreeView
	{
		private class CacheEntry
		{
			private Hash128 hash;

			public int[] ChildIDs;
			public string[] ChildNames;
			public Texture2D[] ChildThumbnails;

			public CacheEntry( string path )
			{
				Refresh( path );
			}

			public void Refresh( string path )
			{
				Hash128 hash = AssetDatabase.GetAssetDependencyHash( path );
				if( this.hash != hash )
				{
					this.hash = hash;

					Object[] childAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath( path );
					ChildIDs = new int[childAssets.Length];
					ChildNames = new string[childAssets.Length];
					ChildThumbnails = new Texture2D[childAssets.Length];

					for( int i = 0; i < childAssets.Length; i++ )
					{
						Object childAsset = childAssets[i];

						ChildIDs[i] = childAsset.GetInstanceID();
						ChildNames[i] = childAsset.name;
						ChildThumbnails[i] = AssetPreview.GetMiniThumbnail( childAsset );
					}
				}
			}
		}

		private readonly string rootDirectory;
		private readonly List<TreeViewItem> rows = new List<TreeViewItem>( 100 );
		private readonly Dictionary<int, CacheEntry> childAssetsCache = new Dictionary<int, CacheEntry>( 256 );

		private readonly MethodInfo instanceIDFromGUID;
		private readonly CompareInfo textComparer;
		private readonly CompareOptions textCompareOptions;

		private bool isSearching;

		public ProjectWindowSelectionChangedDelegate OnSelectionChanged;
		public bool SyncSelection;

		public CustomProjectWindowDrawer( TreeViewState state, string rootDirectory ) : base( state )
		{
			this.rootDirectory = rootDirectory;
			instanceIDFromGUID = typeof( AssetDatabase ).GetMethod( "GetInstanceIDFromGUID", BindingFlags.NonPublic | BindingFlags.Static );
			textComparer = new CultureInfo( "en-US" ).CompareInfo;
			textCompareOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

			Reload();
		}

		protected override TreeViewItem BuildRoot()
		{
			if( AssetDatabase.IsValidFolder( rootDirectory ) )
				return new TreeViewItem { id = GetInstanceIDFromPath( rootDirectory ), depth = -1 };

			return new TreeViewItem { id = -1, depth = -1 };
		}

		protected override IList<TreeViewItem> BuildRows( TreeViewItem root )
		{
			rows.Clear();
			isSearching = !string.IsNullOrEmpty( searchString );

			string[] entries;
			if( FolderHasEntries( rootDirectory, out entries ) )
			{
				AddChildrenRecursive( rootDirectory, 0, entries );

				if( isSearching )
					rows.Sort( ( x, y ) => EditorUtility.NaturalCompare( x.displayName, y.displayName ) );
			}

			SetupParentsAndChildrenFromDepths( root, rows );
			return rows;
		}

		private void AddChildrenRecursive( string directory, int depth, string[] entries )
		{
			for( int i = 0; i < entries.Length; i++ )
			{
				string entry = entries[i];
				if( string.IsNullOrEmpty( entry ) )
					continue;

				int instanceID = GetInstanceIDFromPath( entry );
				string displayName = Path.GetFileNameWithoutExtension( entry );
				TreeViewItem item = null;
				if( !isSearching || textComparer.IndexOf( displayName, searchString, textCompareOptions ) >= 0 )
				{
					item = new TreeViewItem( instanceID, !isSearching ? depth : 0, displayName ) { icon = AssetDatabase.GetCachedIcon( entry ) as Texture2D };
					rows.Add( item );
				}

				if( Directory.Exists( entry ) )
				{
					if( isSearching || IsExpanded( instanceID ) )
					{
						string[] entries2;
						if( FolderHasEntries( entry, out entries2 ) )
							AddChildrenRecursive( entry, depth + 1, entries2 );
					}
					else if( FolderHasEntries( entry ) )
						item.children = CreateChildListForCollapsedParent();
				}
				else
				{
					CacheEntry cacheEntry = GetCacheEntry( instanceID, entry );
					int[] childAssets = cacheEntry.ChildIDs;
					if( childAssets.Length > 0 )
					{
						if( isSearching || IsExpanded( instanceID ) )
						{
							string[] childNames = cacheEntry.ChildNames;
							Texture2D[] childThumbnails = cacheEntry.ChildThumbnails;

							if( !isSearching )
							{
								for( int j = 0; j < childAssets.Length; j++ )
									rows.Add( new TreeViewItem( childAssets[j], depth + 1, childNames[j] ) { icon = childThumbnails[j] } );
							}
							else
							{
								for( int j = 0; j < childAssets.Length; j++ )
								{
									if( textComparer.IndexOf( childNames[j], searchString, textCompareOptions ) >= 0 )
										rows.Add( new TreeViewItem( childAssets[j], 0, childNames[j] ) { icon = childThumbnails[j] } );
								}
							}
						}
						else
							item.children = CreateChildListForCollapsedParent();
					}
				}
			}
		}

		protected override IList<int> GetAncestors( int id )
		{
			List<int> ancestors = new List<int>();
			string path = AssetDatabase.GetAssetPath( id );
			if( string.IsNullOrEmpty( path ) )
				return ancestors;

			if( !AssetDatabase.IsMainAsset( id ) )
				ancestors.Add( GetInstanceIDFromPath( path ) );

			while( !string.IsNullOrEmpty( path ) )
			{
				path = Path.GetDirectoryName( path );
				if( !StringStartsWithFast( path, rootDirectory ) || !AssetDatabase.IsValidFolder( path ) )
					break;

				ancestors.Add( GetInstanceIDFromPath( path ) );
			}

			return ancestors;
		}

		protected override IList<int> GetDescendantsThatHaveChildren( int id )
		{
			string path = AssetDatabase.GetAssetPath( id );
			if( string.IsNullOrEmpty( path ) )
				return new List<int>( 0 );

			if( !StringStartsWithFast( path, rootDirectory ) )
			{
				if( StringStartsWithFast( rootDirectory, path ) )
				{
					path = rootDirectory;
					id = rootItem.id;
				}
				else
					return new List<int>( 0 );
			}

			string[] entries;
			if( !FolderHasEntries( path, out entries ) )
			{
				if( File.Exists( path ) && AssetDatabase.IsMainAsset( id ) )
				{
					if( GetCacheEntry( id, path ).ChildIDs.Length > 0 )
						return new List<int>( 1 ) { id };
				}

				return new List<int>( 0 );
			}

			Stack<string> pathsStack = new Stack<string>();
			Stack<string[]> entriesStack = new Stack<string[]>();

			pathsStack.Push( path );
			entriesStack.Push( entries );

			List<int> parents = new List<int>();
			while( pathsStack.Count > 0 )
			{
				string current = pathsStack.Pop();
				string[] currentEntries = entriesStack.Pop();
				parents.Add( GetInstanceIDFromPath( current ) );

				for( int i = 0; i < currentEntries.Length; i++ )
				{
					string currentEntry = currentEntries[i];

					if( string.IsNullOrEmpty( currentEntry ) )
						continue;

					if( FolderHasEntries( currentEntry, out entries ) )
					{
						pathsStack.Push( currentEntry );
						entriesStack.Push( entries );
					}
					else if( File.Exists( currentEntry ) )
					{
						int instanceID = GetInstanceIDFromPath( currentEntry );
						if( GetCacheEntry( instanceID, currentEntry ).ChildIDs.Length > 0 )
							parents.Add( instanceID );
					}
				}
			}

			return parents;
		}

		protected override bool CanBeParent( TreeViewItem item )
		{
			return AssetDatabase.IsValidFolder( AssetDatabase.GetAssetPath( item.id ) );
		}

		protected override void SelectionChanged( IList<int> selectedIds )
		{
			try
			{
				if( OnSelectionChanged != null )
					OnSelectionChanged( selectedIds );
			}
			catch( System.Exception e )
			{
				Debug.LogException( e );
			}

			if( !SyncSelection || selectedIds == null )
				return;

			int[] selectionArray = new int[selectedIds.Count];
			selectedIds.CopyTo( selectionArray, 0 );

			Selection.instanceIDs = selectionArray;
		}

		protected override bool CanRename( TreeViewItem item )
		{
			return true;
		}

		protected override void RenameEnded( RenameEndedArgs args )
		{
			if( args.acceptedRename )
				AssetDatabase.RenameAsset( AssetDatabase.GetAssetPath( args.itemID ), args.newName );
		}

		protected override void DoubleClickedItem( int id )
		{
			Object obj = EditorUtility.InstanceIDToObject( id );
			if( obj != null )
			{
				if( obj is DefaultAsset && AssetDatabase.IsValidFolder( AssetDatabase.GetAssetPath( obj ) ) )
					SetExpanded( id, true );
				else
					AssetDatabase.OpenAsset( obj );
			}
		}

		protected override void ContextClicked()
		{
			Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>( rootDirectory );
			EditorUtility.DisplayPopupMenu( new Rect( Event.current.mousePosition, new Vector2( 0f, 0f ) ), "Assets/", null );
			Event.current.Use();
		}

		protected override void ContextClickedItem( int id )
		{
			ChangeUnitySelection();
			EditorUtility.DisplayPopupMenu( new Rect( Event.current.mousePosition, new Vector2( 0f, 0f ) ), "Assets/", null );
			Event.current.Use();
		}

		protected override void CommandEventHandling()
		{
			Event e = Event.current;
			if( ( e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand ) && HasSelection() )
			{
				if( e.commandName == "Delete" || e.commandName == "SoftDelete" )
				{
					if( e.type == EventType.ExecuteCommand )
						DeleteAssets( GetSelection(), e.commandName == "SoftDelete" );

					e.Use();
					return;
				}
				else if( e.commandName == "Duplicate" )
				{
					if( e.type == EventType.ExecuteCommand )
						DuplicateAssets( GetSelection() );

					e.Use();
					return;
				}
			}

			base.CommandEventHandling();
		}

		protected override bool CanStartDrag( CanStartDragArgs args )
		{
			return true;
		}

		protected override void SetupDragAndDrop( SetupDragAndDropArgs args )
		{
			DragAndDrop.PrepareStartDrag();
			IList<int> sortedDraggedIDs = SortItemIDsInRowOrder( args.draggedItemIDs );

			List<Object> objList = new List<Object>( sortedDraggedIDs.Count );
			List<string> paths = new List<string>( sortedDraggedIDs.Count );
			for( int i = 0; i < sortedDraggedIDs.Count; i++ )
			{
				int instanceID = sortedDraggedIDs[i];

				Object obj = EditorUtility.InstanceIDToObject( instanceID );
				if( obj != null )
				{
					objList.Add( obj );

					string path = AssetDatabase.GetAssetPath( obj );
					if( !string.IsNullOrEmpty( path ) && paths.IndexOf( path ) < 0 )
						paths.Add( path );
				}
			}

			DragAndDrop.objectReferences = objList.ToArray();
			DragAndDrop.paths = paths.ToArray();
			DragAndDrop.StartDrag( objList.Count > 1 ? "<Multiple>" : objList[0].name );
		}

		protected override DragAndDropVisualMode HandleDragAndDrop( DragAndDropArgs args )
		{
			string parentFolder = null;
			switch( args.dragAndDropPosition )
			{
				case DragAndDropPosition.UponItem:
				case DragAndDropPosition.BetweenItems:
					if( args.parentItem != null && ( !hasSearch || args.dragAndDropPosition == DragAndDropPosition.UponItem ) )
						parentFolder = AssetDatabase.GetAssetPath( args.parentItem.id );

					break;
				case DragAndDropPosition.OutsideItems:
					parentFolder = rootDirectory;
					break;
			}

			if( hasSearch && ( args.parentItem == rootItem || parentFolder == rootDirectory ) )
				return DragAndDropVisualMode.None;

			if( string.IsNullOrEmpty( parentFolder ) || !AssetDatabase.IsValidFolder( parentFolder ) )
				return DragAndDropVisualMode.None;

			if( args.performDrop )
				MoveAssets( DragAndDrop.objectReferences, parentFolder );

			return DragAndDropVisualMode.Move;
		}

		private bool MoveAssets( IList<Object> assets, string parentFolder )
		{
			bool containsAsset = false;
			bool containsSceneObject = false;
			List<string> paths = new List<string>( assets.Count );
			List<bool> directoryStates = new List<bool>( assets.Count );
			for( int i = 0; i < assets.Count; i++ )
			{
				string path = AssetDatabase.GetAssetPath( assets[i] );

				// Can't make a folder a subdirectory of itself
				if( path == parentFolder )
					return false;

				if( string.IsNullOrEmpty( path ) )
					containsSceneObject = true;
				else if( paths.IndexOf( path ) < 0 )
				{
					paths.Add( path );
					directoryStates.Add( Directory.Exists( path ) );
					containsAsset = true;
				}
			}

			if( containsAsset && containsSceneObject )
				return false;

			if( containsSceneObject )
			{
				// Convert all scene objects to Transforms
				int invalidObjectCount = 0;
				for( int i = 0; i < assets.Count; i++ )
				{
					if( assets[i] is GameObject )
						assets[i] = ( (GameObject) assets[i] ).transform;
					else if( assets[i] is Component )
						assets[i] = ( (Component) assets[i] ).transform;
					else
					{
						assets[i] = null;
						invalidObjectCount++;
					}
				}

				if( invalidObjectCount == assets.Count )
					return false;

				// Remove child Transforms whose parents are also included in drag&drop
				for( int i = assets.Count - 1; i >= 0; i-- )
				{
					if( assets[i] == null )
						continue;

					Transform transform = (Transform) assets[i];
					for( int j = 0; j < assets.Count; j++ )
					{
						if( i == j || assets[j] == null )
							continue;

						if( transform.IsChildOf( (Transform) assets[j] ) )
						{
							assets[i] = null;
							break;
						}
					}
				}

				List<int> instanceIDs = new List<int>( assets.Count );
				AssetDatabase.StartAssetEditing();
				try
				{
					for( int i = assets.Count - 1; i >= 0; i-- )
					{
						if( assets[i] == null )
							continue;

						Transform transform = (Transform) assets[i];
						string path = AssetDatabase.GenerateUniqueAssetPath( Path.Combine( parentFolder, transform.name + ".prefab" ) );
#if UNITY_2018_3_OR_NEWER
						GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect( transform.gameObject, path, InteractionMode.UserAction );
#else
						GameObject prefab = PrefabUtility.CreatePrefab( path, transform.gameObject, ReplacePrefabOptions.ConnectToPrefab );
#endif
						if( prefab )
							instanceIDs.Add( prefab.GetInstanceID() );
					}
				}
				finally
				{
					AssetDatabase.StopAssetEditing();
					AssetDatabase.Refresh();
				}

				SetSelection( instanceIDs, TreeViewSelectionOptions.RevealAndFrame );
				return true;
			}

			// Remove descendant paths
			for( int i = paths.Count - 1; i >= 0; i-- )
			{
				string path = paths[i];
				for( int j = 0; j < paths.Count; j++ )
				{
					if( i == j || !directoryStates[j] )
						continue;

					if( StringStartsWithFast( path, paths[j] ) )
					{
						paths.RemoveAt( i );
						break;
					}
				}
			}

			if( paths.Count == 0 )
				return false;

			string[] entries = Directory.GetFileSystemEntries( parentFolder );
			for( int i = 0; i < entries.Length; i++ )
			{
				// Don't allow move if an asset is already located inside parentFolder
				if( paths.IndexOf( entries[i].Replace( '\\', '/' ) ) >= 0 )
					return false;

				entries[i] = Path.GetFileName( entries[i] );
			}

			// Check if there are files in parentFolder with conflicting names
			string[] newPaths = new string[paths.Count];
			for( int i = 0; i < paths.Count; i++ )
			{
				string filename = Path.GetFileName( paths[i] );
				for( int j = 0; j < entries.Length; j++ )
				{
					if( filename == entries[j] )
						return false;
				}

				newPaths[i] = Path.Combine( parentFolder, filename );
			}

			string error = null;
			AssetDatabase.StartAssetEditing();
			try
			{
				for( int i = 0; i < paths.Count; i++ )
				{
					error = AssetDatabase.MoveAsset( paths[i], newPaths[i] );
					if( !string.IsNullOrEmpty( error ) )
						break;
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
				AssetDatabase.Refresh();
			}

			if( !string.IsNullOrEmpty( error ) )
			{
				Debug.LogError( error );
				return false;
			}
			else
			{
				int[] instanceIDs = new int[newPaths.Length];
				for( int i = 0; i < newPaths.Length; i++ )
					instanceIDs[i] = GetInstanceIDFromPath( newPaths[i] );

				SetSelection( instanceIDs, TreeViewSelectionOptions.RevealAndFrame );
				return true;
			}
		}

		// Credit: https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/ProjectWindow/ProjectWindowUtil.cs
		private bool DeleteAssets( IList<int> instanceIDs, bool askIfSure )
		{
			if( instanceIDs.Count == 0 )
				return true;

			if( instanceIDs.IndexOf( GetInstanceIDFromPath( "Assets" ) ) >= 0 )
			{
				EditorUtility.DisplayDialog( "Cannot Delete", "Deleting the 'Assets' folder is not allowed", "Ok" );
				return false;
			}

			List<string> paths = GetPathsOfMainAssets( instanceIDs );
			if( paths.Count == 0 )
				return false;

			if( askIfSure )
			{
				int maxCount = 3;
				StringBuilder infotext = new StringBuilder();
				for( int i = 0; i < paths.Count && i < maxCount; ++i )
					infotext.AppendLine( "   " + paths[i] );

				if( paths.Count > maxCount )
					infotext.AppendLine( "   ..." );

				infotext.AppendLine( "You cannot undo this action." );

				if( !EditorUtility.DisplayDialog( paths.Count > 1 ? "Delete selected assets?" : "Delete selected asset?", infotext.ToString(), "Delete", "Cancel" ) )
					return false;
			}

			bool success = true;
			AssetDatabase.StartAssetEditing();
			try
			{
				for( int i = 0; i < paths.Count; i++ )
				{
					if( ( File.Exists( paths[i] ) || Directory.Exists( paths[i] ) ) && !AssetDatabase.MoveAssetToTrash( paths[i] ) )
						success = false;
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
				AssetDatabase.Refresh();
			}

			if( !success )
			{
				string message = "Some assets could not be deleted.\n" +
					"If you are using Version Control server, make sure you are connected to your VCS or \"Work Offline\" is enabled.\n" +
					"Otherwise, make sure nothing is keeping a hook on the deleted assets, like a loaded DLL for example.";

				EditorUtility.DisplayDialog( "Cannot Delete", message, "Ok" );
			}

			return success;
		}

		// Credit: https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/ProjectWindow/ProjectWindowUtil.cs
		private void DuplicateAssets( IList<int> instanceIDs )
		{
			AssetDatabase.Refresh();

			List<string> paths = GetPathsOfMainAssets( instanceIDs );
			if( paths.Count == 0 )
				return;

			List<string> copiedPaths = new List<string>( paths.Count );
			AssetDatabase.StartAssetEditing();
			try
			{
				for( int i = 0; i < paths.Count; i++ )
				{
					string newPath = AssetDatabase.GenerateUniqueAssetPath( paths[i] );
					if( !string.IsNullOrEmpty( newPath ) && AssetDatabase.CopyAsset( paths[i], newPath ) )
						copiedPaths.Add( newPath );
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
				AssetDatabase.Refresh();
			}

			int[] newInstanceIDs = new int[copiedPaths.Count];
			for( int i = 0; i < copiedPaths.Count; i++ )
				newInstanceIDs[i] = GetInstanceIDFromPath( copiedPaths[i] );

			SetSelection( newInstanceIDs, TreeViewSelectionOptions.RevealAndFrame );
		}

		public void ChangeUnitySelection()
		{
			IList<int> selection = GetSelection();
			if( selection.Count == 0 )
				Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>( rootDirectory );
			else
			{
				int[] selectionArray = new int[selection.Count];
				selection.CopyTo( selectionArray, 0 );

				Selection.instanceIDs = selectionArray;
			}
		}

		private List<string> GetPathsOfMainAssets( IList<int> instanceIDs )
		{
			List<string> result = new List<string>( instanceIDs.Count );
			for( int i = 0; i < instanceIDs.Count; i++ )
			{
				if( AssetDatabase.IsMainAsset( instanceIDs[i] ) )
					result.Add( AssetDatabase.GetAssetPath( instanceIDs[i] ) );
			}

			return result;
		}

		private bool FolderHasEntries( string path )
		{
			if( !AssetDatabase.IsValidFolder( path ) )
				return false;

			string[] entries = Directory.GetFileSystemEntries( path );
			for( int i = 0; i < entries.Length; i++ )
			{
				string entry = entries[i];

				if( !StringEndsWithFast( entry, ".meta" ) && !string.IsNullOrEmpty( AssetDatabase.AssetPathToGUID( entry ) ) )
					return true;
			}

			return false;
		}

		private bool FolderHasEntries( string path, out string[] entries )
		{
			if( !AssetDatabase.IsValidFolder( path ) )
			{
				entries = null;
				return false;
			}

			bool hasValidEntries = false;
			entries = Directory.GetFileSystemEntries( path );
			for( int i = 0, lastFileIndex = -1; i < entries.Length; i++ )
			{
				string entry = entries[i];

				if( !StringEndsWithFast( entry, ".meta" ) && !string.IsNullOrEmpty( AssetDatabase.AssetPathToGUID( entry ) ) )
					hasValidEntries = true;
				else
				{
					entries[i] = null;
					continue;
				}

				// Sort the entries to ensure that directories come first
				if( Directory.Exists( entry ) )
				{
					if( lastFileIndex >= 0 )
					{
						for( int j = i; j > lastFileIndex; j-- )
							entries[j] = entries[j - 1];

						entries[lastFileIndex] = entry;
						lastFileIndex++;
					}
				}
				else if( lastFileIndex < 0 )
					lastFileIndex = i;
			}

			return hasValidEntries;
		}

		private int GetInstanceIDFromPath( string path )
		{
			if( instanceIDFromGUID != null )
				return (int) instanceIDFromGUID.Invoke( null, new object[1] { AssetDatabase.AssetPathToGUID( path ) } );
			else
				return AssetDatabase.LoadMainAssetAtPath( path ).GetInstanceID();
		}

		private CacheEntry GetCacheEntry( int instanceID, string path )
		{
			CacheEntry cacheEntry;
			if( !childAssetsCache.TryGetValue( instanceID, out cacheEntry ) )
			{
				cacheEntry = new CacheEntry( path );
				childAssetsCache[instanceID] = cacheEntry;
			}
			else
				cacheEntry.Refresh( path );

			return cacheEntry;
		}

		private bool StringStartsWithFast( string str, string prefix )
		{
			int length1 = str.Length;
			int length2 = prefix.Length;
			int index1 = 0; int index2 = 0;
			while( index1 < length1 && index2 < length2 && str[index1] == prefix[index2] )
			{
				index1++;
				index2++;
			}

			return index2 == length2;
		}

		private bool StringEndsWithFast( string str, string suffix )
		{
			int index1 = str.Length - 1;
			int index2 = suffix.Length - 1;
			while( index1 >= 0 && index2 >= 0 && str[index1] == suffix[index2] )
			{
				index1--;
				index2--;
			}

			return index2 < 0;
		}
	}
}