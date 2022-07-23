using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InspectPlusNamespace
{
	public delegate void HierarchyWindowSelectionChangedDelegate( IList<int> newSelection );

	public class IsolatedHierarchy : ScriptableObject
	{
		public Transform rootTransform;

		public override bool Equals( object other ) { return this == ( other as Object ) || rootTransform == ( other as Object ); }
		public override int GetHashCode() { return rootTransform ? rootTransform.GetHashCode() : base.GetHashCode(); }
	}

	[Serializable]
	public class CustomHierarchyWindow
	{
#pragma warning disable 0649
		[SerializeField]
		private TreeViewState treeViewState;
		[SerializeField]
		private Transform rootTransform;
#pragma warning restore 0649

		private CustomHierarchyWindowDrawer treeView;
		private SearchField searchField;
		private GUIContent createButtonContent;

		public HierarchyWindowSelectionChangedDelegate OnSelectionChanged;

		public void Show( Transform transform )
		{
			if( treeView != null && rootTransform == transform )
			{
				Refresh();
				return;
			}

			if( treeViewState == null || rootTransform != transform )
				treeViewState = new TreeViewState();

			treeView = new CustomHierarchyWindowDrawer( treeViewState, transform )
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
			rootTransform = transform;
		}

		public CustomHierarchyWindowDrawer GetTreeView()
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
				treeView.ShowContextMenu( null, true );
			}

			GUILayout.Space( 8f );
			treeView.searchString = searchField.OnToolbarGUI( treeView.searchString );
			GUILayout.EndHorizontal();

			rect = GUILayoutUtility.GetRect( 0, 100000, 0, 100000 );
			treeView.OnGUI( rect );
		}
	}

	// Credit: https://docs.unity3d.com/Manual/TreeViewAPI.html (TreeViewExamples.zip)
	// Credit: https://github.com/Unity-Technologies/UnityCsReference/blob/2020.2/Editor/Mono/SceneHierarchy.cs
	public class CustomHierarchyWindowDrawer : TreeView
	{
		private class SelectionChangeApplier : IDisposable
		{
			private readonly CustomHierarchyWindowDrawer hierarchy;
			private readonly GameObject[] oldSelection;

			public SelectionChangeApplier( CustomHierarchyWindowDrawer hierarchy )
			{
				this.hierarchy = hierarchy;
				oldSelection = Selection.gameObjects;
			}

			public void Dispose()
			{
				// Check if Unity's selection has changed
				GameObject[] newSelection = Selection.gameObjects;
				if( newSelection.Length == oldSelection.Length )
				{
					int index;
					for( index = 0; index < newSelection.Length; index++ )
					{
						if( oldSelection[index] != newSelection[index] )
							break;
					}

					if( index == newSelection.Length )
						return;
				}

				hierarchy.Reload();
				hierarchy.SetSelection( Selection.instanceIDs, TreeViewSelectionOptions.RevealAndFrame );
			}
		}

		// Credit: https://github.com/Unity-Technologies/UnityCsReference/blob/33cbfe062d795667c39e16777230e790fcd4b28b/Editor/Mono/GUI/TreeView/GameObjectTreeViewGUI.cs#L26-L30
		private static class GameObjectStyles
		{
			public static readonly GUIStyle disabledLabel = new GUIStyle( "PR DisabledLabel" );
			public static readonly GUIStyle prefabLabel = new GUIStyle( "PR PrefabLabel" );
			public static readonly GUIStyle disabledPrefabLabel = new GUIStyle( "PR DisabledPrefabLabel" );
			public static readonly GUIStyle brokenPrefabLabel = new GUIStyle( "PR BrokenPrefabLabel" );
			public static readonly GUIStyle disabledBrokenPrefabLabel = new GUIStyle( "PR DisabledBrokenPrefabLabel" );

			static GameObjectStyles()
			{
				disabledLabel.padding.left = 0;
				disabledLabel.alignment = TextAnchor.MiddleLeft;
				prefabLabel.padding.left = 0;
				prefabLabel.alignment = TextAnchor.MiddleLeft;
				disabledPrefabLabel.padding.left = 0;
				disabledPrefabLabel.alignment = TextAnchor.MiddleLeft;
				brokenPrefabLabel.padding.left = 0;
				brokenPrefabLabel.alignment = TextAnchor.MiddleLeft;
				disabledBrokenPrefabLabel.padding.left = 0;
				disabledBrokenPrefabLabel.alignment = TextAnchor.MiddleLeft;
			}
		}

		private readonly int rootGameObjectID;
		private GameObject RootGameObject { get { return GetGameObjectFromInstanceID( rootGameObjectID ); } }
		private Transform RootTransform { get { return GetTransformFromInstanceID( rootGameObjectID ); } }

		private readonly List<TreeViewItem> rows = new List<TreeViewItem>( 100 );

		private readonly CompareInfo textComparer;
		private readonly CompareOptions textCompareOptions;

		private bool isSearching;

		public HierarchyWindowSelectionChangedDelegate OnSelectionChanged;
		public bool SyncSelection;

#if UNITY_2019_3_OR_NEWER
		private readonly MethodInfo selectedIconGetter;
#endif

		public CustomHierarchyWindowDrawer( TreeViewState state, Transform rootTransform ) : base( state )
		{
			rootGameObjectID = rootTransform.gameObject.GetInstanceID();
			textComparer = new CultureInfo( "en-US" ).CompareInfo;
			textCompareOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
#if UNITY_2019_3_OR_NEWER
			selectedIconGetter = typeof( EditorUtility ).GetMethod( "GetIconInActiveState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );
#endif

			Reload();
		}

		protected override TreeViewItem BuildRoot()
		{
			if( RootGameObject )
				return new TreeViewItem { id = rootGameObjectID, depth = -1 };

			return new TreeViewItem { id = -1, depth = -1 };
		}

		protected override IList<TreeViewItem> BuildRows( TreeViewItem root )
		{
			rows.Clear();
			isSearching = !string.IsNullOrEmpty( searchString );

			Transform rootTransform = RootTransform;
			if( rootTransform && rootTransform.childCount > 0 )
			{
				AddChildrenRecursive( rootTransform, 0 );

				if( isSearching )
					rows.Sort( ( x, y ) => EditorUtility.NaturalCompare( x.displayName, y.displayName ) );
			}

			SetupParentsAndChildrenFromDepths( root, rows );
			return rows;
		}

		private void AddChildrenRecursive( Transform parent, int depth )
		{
			for( int i = 0, childCount = parent.childCount; i < childCount; i++ )
			{
				Transform child = parent.GetChild( i );
				if( !child )
					continue;

				int instanceID = child.gameObject.GetInstanceID();
				string displayName = child.name;
				TreeViewItem item = null;
				if( !isSearching || textComparer.IndexOf( displayName, searchString, textCompareOptions ) >= 0 )
				{
					item = new TreeViewItem( instanceID, !isSearching ? depth : 0, displayName );
#if UNITY_2018_3_OR_NEWER
					item.icon = PrefabUtility.GetIconForGameObject( child.gameObject );
#endif
					rows.Add( item );
				}

				if( child.childCount > 0 )
				{
					if( isSearching || IsExpanded( instanceID ) )
						AddChildrenRecursive( child, depth + 1 );
					else
						item.children = CreateChildListForCollapsedParent();
				}
			}
		}

		protected override IList<int> GetAncestors( int id )
		{
			List<int> ancestors = new List<int>();
			Transform transform = GetTransformFromInstanceID( id );
			if( !transform )
				return ancestors;

			while( transform.parent )
			{
				transform = transform.parent;
				ancestors.Add( transform.gameObject.GetInstanceID() );
			}

			return ancestors;
		}

		protected override IList<int> GetDescendantsThatHaveChildren( int id )
		{
			Transform transform = GetTransformFromInstanceID( id );
			if( !transform )
				return new List<int>( 0 );

			Stack<Transform> stack = new Stack<Transform>();
			stack.Push( transform );

			List<int> parents = new List<int>();
			while( stack.Count > 0 )
			{
				Transform current = stack.Pop();
				parents.Add( current.gameObject.GetInstanceID() );
				for( int i = 0, childCount = current.childCount; i < childCount; i++ )
				{
					Transform child = current.GetChild( i );
					if( child )
						stack.Push( child );
				}
			}

			return parents;
		}

		protected override void RowGUI( RowGUIArgs args )
		{
			GameObject go = GetGameObjectFromInstanceID( args.item.id );
			if( !go )
			{
				base.RowGUI( args );
				return;
			}

			bool goActive = go.activeInHierarchy;
			GUIStyle style;
			if( goActive )
			{
#if UNITY_2018_3_OR_NEWER
				switch( PrefabUtility.GetPrefabInstanceStatus( go ) )
				{
					case PrefabInstanceStatus.MissingAsset: style = GameObjectStyles.brokenPrefabLabel; break;
					case PrefabInstanceStatus.Connected: style = GameObjectStyles.prefabLabel; break;
					default: style = DefaultStyles.foldoutLabel; break;
				}
#else
				switch( PrefabUtility.GetPrefabType( go ) )
				{
					case PrefabType.MissingPrefabInstance: style = GameObjectStyles.brokenPrefabLabel; break;
					case PrefabType.ModelPrefabInstance:
					case PrefabType.PrefabInstance: style = GameObjectStyles.prefabLabel; break;
					default: style = DefaultStyles.foldoutLabel; break;
				}
#endif
			}
			else
			{
#if UNITY_2018_3_OR_NEWER
				switch( PrefabUtility.GetPrefabInstanceStatus( go ) )
				{
					case PrefabInstanceStatus.MissingAsset: style = GameObjectStyles.disabledBrokenPrefabLabel; break;
					case PrefabInstanceStatus.Connected: style = GameObjectStyles.disabledPrefabLabel; break;
					default: style = GameObjectStyles.disabledLabel; break;
				}
#else
				switch( PrefabUtility.GetPrefabType( go ) )
				{
					case PrefabType.MissingPrefabInstance: style = GameObjectStyles.disabledBrokenPrefabLabel; break;
					case PrefabType.ModelPrefabInstance:
					case PrefabType.PrefabInstance: style = GameObjectStyles.disabledPrefabLabel; break;
					default: style = GameObjectStyles.disabledLabel; break;
				}
#endif
			}

			Rect rect = args.rowRect;
			rect.x += GetContentIndent( args.item );

#if UNITY_2018_3_OR_NEWER
			Texture2D icon = args.item.icon;
			if( icon )
			{
				Color iconTint = goActive ? Color.white : new Color( 1f, 1f, 1f, 0.5f );
				Rect iconRect = rect;
				iconRect.width = 16f;

#if UNITY_2019_3_OR_NEWER
				if( args.selected && args.focused && selectedIconGetter != null )
				{
					icon = selectedIconGetter.Invoke( null, new object[] { icon } ) as Texture2D;
					if( !icon )
						icon = args.item.icon;
				}
#endif

				GUI.DrawTexture( iconRect, icon, ScaleMode.ScaleToFit, true, 0f, iconTint, 0f, 0f );

				if( PrefabUtility.IsAddedGameObjectOverride( go ) )
					GUI.DrawTexture( iconRect, EditorGUIUtility.IconContent( "PrefabOverlayAdded Icon" ).image, ScaleMode.ScaleToFit, true, 0f, iconTint, 0f, 0f );

				rect.x += iconRect.width + 2f;
			}
#endif

			if( Event.current.type == EventType.Repaint )
				style.Draw( rect, args.label, false, false, args.selected, args.focused );
		}

		protected override void SelectionChanged( IList<int> selectedIds )
		{
			try
			{
				if( OnSelectionChanged != null )
					OnSelectionChanged( selectedIds );
			}
			catch( Exception e )
			{
				Debug.LogException( e );
			}

			if( SyncSelection && selectedIds != null )
				Selection.objects = GetSelectedGameObjects();
		}

		private GameObject[] GetSelectedGameObjects()
		{
			IList<int> selectedIds = GetSelection();
			if( selectedIds == null || selectedIds.Count == 0 )
				return new GameObject[0];

			selectedIds = SortItemIDsInRowOrder( selectedIds );

			Transform rootTransform = RootTransform;
			if( !rootTransform )
				return new GameObject[0];

			List<GameObject> gameObjects = new List<GameObject>( selectedIds.Count );
			for( int i = 0; i < selectedIds.Count; i++ )
			{
				Transform transform = GetTransformFromInstanceID( selectedIds[i] );
				if( transform && transform != rootTransform && transform.IsChildOf( rootTransform ) )
					gameObjects.Add( transform.gameObject );
			}

			return gameObjects.ToArray();
		}

		protected override bool CanRename( TreeViewItem item )
		{
			return true;
		}

		protected override void RenameEnded( RenameEndedArgs args )
		{
			if( args.acceptedRename && args.newName != args.originalName && args.newName.Trim().Length > 0 )
			{
				GameObject selection = (GameObject) EditorUtility.InstanceIDToObject( args.itemID );
				Undo.RegisterCompleteObjectUndo( selection, "Rename Transform" );
				selection.name = args.newName;
			}
		}

		protected override void DoubleClickedItem( int id )
		{
			Transform transform = GetTransformFromInstanceID( id );
			if( transform && SceneView.lastActiveSceneView )
			{
				Selection.activeTransform = transform;
				SceneView.lastActiveSceneView.FrameSelected();
			}
		}

		protected override void ContextClicked()
		{
			ShowContextMenu( GetSelectedGameObjects(), false );
		}

		protected override void ContextClickedItem( int id )
		{
			ShowContextMenu( GetSelectedGameObjects(), false );
		}

		public void ShowContextMenu( GameObject[] selection, bool openedViaCreateButton )
		{
			bool hasSelection = !openedViaCreateButton && selection != null && selection.Length > 0;

			if( selection == null || selection.Length == 0 )
				selection = new GameObject[1] { RootGameObject };

			GenericMenu menu = new GenericMenu();

			if( !openedViaCreateButton )
			{
				if( hasSelection )
					menu.AddItem( new GUIContent( "Copy" ), false, () => CopySelection( selection ) );
				else
					menu.AddDisabledItem( new GUIContent( "Copy" ) );

				menu.AddItem( new GUIContent( "Paste" ), false, () => PasteToSelection( selection ) );

				menu.AddSeparator( "" );

				if( hasSelection )
				{
					TreeViewItem selectedItem = FindItem( selection[0].GetInstanceID(), rootItem );
					if( selectedItem != null )
					{
						menu.AddItem( new GUIContent( "Rename" ), false, () => BeginRename( selectedItem ) );
						menu.AddItem( new GUIContent( "Duplicate" ), false, () => DuplicateSelection( selection ) );
						menu.AddItem( new GUIContent( "Delete" ), false, () => DeleteSelection( selection ) );

						menu.AddSeparator( "" );

#if UNITY_2018_3_OR_NEWER
						Object prefab = PrefabUtility.GetCorrespondingObjectFromSource( EditorUtility.InstanceIDToObject( selectedItem.id ) );
#else
						Object prefab = PrefabUtility.GetPrefabParent( EditorUtility.InstanceIDToObject( selectedItem.id ) );
#endif
						if( prefab )
						{
							menu.AddItem( new GUIContent( "Select Prefab" ), false, () =>
							{
								Selection.activeObject = prefab;
								EditorGUIUtility.PingObject( prefab.GetInstanceID() );
							} );

#if UNITY_2018_3_OR_NEWER
							for( int i = 0; i < selection.Length; i++ )
							{
								if( selection[i] && PrefabUtility.IsPartOfNonAssetPrefabInstance( selection[i] ) && PrefabUtility.IsOutermostPrefabInstanceRoot( selection[i] ) )
								{
									int _i = i;

									menu.AddItem( new GUIContent( "Prefab/Unpack" ), false, () =>
									{
										for( int j = _i; j < selection.Length; j++ )
										{
											GameObject go = selection[j];
											if( go && PrefabUtility.IsPartOfNonAssetPrefabInstance( go ) && PrefabUtility.IsOutermostPrefabInstanceRoot( go ) )
												PrefabUtility.UnpackPrefabInstance( go, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction );
										}
									} );

									menu.AddItem( new GUIContent( "Prefab/Unpack Completely" ), false, () =>
									{
										for( int j = _i; j < selection.Length; j++ )
										{
											GameObject go = selection[j];
											if( go && PrefabUtility.IsPartOfNonAssetPrefabInstance( go ) && PrefabUtility.IsOutermostPrefabInstanceRoot( go ) )
												PrefabUtility.UnpackPrefabInstance( go, PrefabUnpackMode.Completely, InteractionMode.UserAction );
										}
									} );

									break;
								}
							}
#endif

							menu.AddSeparator( "" );
						}
					}
				}
			}

#if UNITY_2020_2_OR_NEWER
			string menusLastItem = (string) typeof( GameObjectUtility ).GetMethod( "GetFirstItemPathAfterGameObjectCreationMenuItems", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static ).Invoke( null, null );
#else
			string menusLastItem = "GameObject/Center On Children";
#endif
			foreach( string path in Unsupported.GetSubmenus( "GameObject" ) )
			{
				if( path.Equals( menusLastItem, StringComparison.OrdinalIgnoreCase ) )
					break;
				else if( path.Equals( "GameObject/Create Empty Child", StringComparison.OrdinalIgnoreCase ) ) // "Create Empty" does the same thing
					continue;
				else if( path.Equals( "GameObject/Create Empty Parent", StringComparison.OrdinalIgnoreCase ) ) // Doesn't take context into account, it uses Unity's Selection
					continue;
				else if( path.IndexOf( "Collapse All", StringComparison.OrdinalIgnoreCase ) >= 0 ) // Collapse functions don't work in this window
					continue;

				// Don't include context for Wizards (...) to avoid opening multiple wizards at once
				Object[] tempContext = selection;
				if( path.EndsWith( "..." ) )
					tempContext = null;

				menu.AddItem( new GUIContent( path.Substring( 11 ) ), false, () => // Substring: remove "GameObject/" prefix
				{
					using( new SelectionChangeApplier( this ) )
					{
						if( tempContext != null )
							typeof( EditorApplication ).GetMethod( "ExecuteMenuItemWithTemporaryContext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static ).Invoke( null, new object[] { path, tempContext } );
						else
							EditorApplication.ExecuteMenuItem( path );
					}
				} );
			}

			menu.ShowAsContext();
			Event.current.Use();
		}

		protected override void CommandEventHandling()
		{
			Event e = Event.current;
			if( e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand )
			{
				GameObject[] selection = GetSelectedGameObjects();
				if( selection == null || selection.Length == 0 )
					return;

				if( e.commandName == "Delete" || e.commandName == "SoftDelete" )
				{
					if( e.type == EventType.ExecuteCommand )
						DeleteSelection( selection );

					e.Use();
					return;
				}
				else if( e.commandName == "Duplicate" )
				{
					if( e.type == EventType.ExecuteCommand )
						DuplicateSelection( selection );

					e.Use();
					return;
				}
				else if( e.commandName == "Copy" )
				{
					if( e.type == EventType.ExecuteCommand )
						CopySelection( selection );

					e.Use();
					return;
				}
				else if( e.commandName == "Paste" )
				{
					if( e.type == EventType.ExecuteCommand )
						PasteToSelection( selection );

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
			for( int i = 0; i < sortedDraggedIDs.Count; i++ )
			{
				int instanceID = sortedDraggedIDs[i];

				Object obj = EditorUtility.InstanceIDToObject( instanceID );
				if( obj != null )
					objList.Add( obj );
			}

			DragAndDrop.objectReferences = objList.ToArray();
			DragAndDrop.StartDrag( objList.Count > 1 ? "<Multiple>" : objList[0].name );
		}

		protected override DragAndDropVisualMode HandleDragAndDrop( DragAndDropArgs args )
		{
			Transform parent = null;
			int siblingIndex = 0;
			switch( args.dragAndDropPosition )
			{
				case DragAndDropPosition.UponItem:
					if( args.parentItem != null )
					{
						parent = GetTransformFromInstanceID( args.parentItem.id );
						if( parent )
							siblingIndex = parent.childCount;
					}

					break;
				case DragAndDropPosition.BetweenItems:
					if( args.parentItem != null && !hasSearch )
					{
						parent = GetTransformFromInstanceID( args.parentItem.id );
						if( parent )
							siblingIndex = Mathf.Min( args.insertAtIndex, parent.childCount );
					}

					break;
				case DragAndDropPosition.OutsideItems:
					parent = RootTransform;
					if( parent )
						siblingIndex = parent.childCount;

					break;
			}

			if( hasSearch && ( args.parentItem == rootItem || parent == RootTransform ) )
				return DragAndDropVisualMode.None;

			if( !parent )
				return DragAndDropVisualMode.None;

			Object[] draggedObjects = DragAndDrop.objectReferences;
			List<Transform> draggedTransforms = new List<Transform>( draggedObjects.Length );
			for( int i = 0; i < draggedObjects.Length; i++ )
			{
				GameObject draggedGameObject = draggedObjects[i] as GameObject;
				if( draggedGameObject )
				{
					// Don't let parent's parents become children of it
					if( parent.IsChildOf( draggedGameObject.transform ) )
						return DragAndDropVisualMode.None;

					draggedTransforms.Add( draggedGameObject.transform );
				}
			}

			if( draggedTransforms.Count == 0 )
				return DragAndDropVisualMode.None;

			// Remove all Transforms that are children of other Transforms in the list
			draggedTransforms.RemoveAll( ( transform ) =>
			{
				while( transform.parent )
				{
					transform = transform.parent;
					if( draggedTransforms.Contains( transform ) )
						return true;
				}

				return false;
			} );

			if( args.performDrop )
			{
				List<int> newSelection = new List<int>( draggedTransforms.Count );
				for( int i = 0; i < draggedTransforms.Count; i++, siblingIndex++ )
				{
					Undo.SetTransformParent( draggedTransforms[i], parent, "Object Parenting" );
					draggedTransforms[i].SetSiblingIndex( draggedTransforms[i].GetSiblingIndex() >= siblingIndex ? siblingIndex : ( siblingIndex - 1 ) );

					newSelection.Add( draggedTransforms[i].gameObject.GetInstanceID() );
				}

				Reload();
				SetSelection( newSelection, TreeViewSelectionOptions.FireSelectionChanged | TreeViewSelectionOptions.RevealAndFrame );
			}

			return DragAndDropVisualMode.Move;
		}

		private void CopySelection( GameObject[] selection )
		{
			if( selection != null && selection.Length > 0 )
			{
				Selection.objects = selection;
				Unsupported.CopyGameObjectsToPasteboard();
			}
		}

		private void PasteToSelection( GameObject[] selection )
		{
			GameObject tempObject = null;
			if( selection == null || selection.Length == 0 || ( selection.Length == 1 && selection[0] == RootGameObject ) )
			{
				// We want to paste inside the root object, so we have to select one of its children
				Transform rootTransform = RootTransform;
				if( rootTransform.childCount > 0 )
					selection = new GameObject[1] { rootTransform.GetChild( 0 ).gameObject };
				else
				{
					// Create a temporary child object
					tempObject = new GameObject( "TEMP" );
					selection = new GameObject[1] { tempObject };
				}
			}

			try
			{
				if( tempObject )
					tempObject.transform.SetParent( RootTransform, false );

				Selection.objects = selection;

				using( new SelectionChangeApplier( this ) )
					Unsupported.PasteGameObjectsFromPasteboard();
			}
			finally
			{
				if( tempObject )
					Object.DestroyImmediate( tempObject );
			}
		}

		private void DuplicateSelection( GameObject[] selection )
		{
			if( selection != null && selection.Length > 0 )
			{
				Selection.objects = selection;

				using( new SelectionChangeApplier( this ) )
					Unsupported.DuplicateGameObjectsUsingPasteboard();
			}
		}

		private void DeleteSelection( GameObject[] selection )
		{
			if( selection != null && selection.Length > 0 )
			{
				for( int i = 0; i < selection.Length; i++ )
				{
					if( selection[i] )
						Undo.DestroyObjectImmediate( selection[i] );
				}
			}
		}

		private GameObject GetGameObjectFromInstanceID( int instanceID )
		{
			return EditorUtility.InstanceIDToObject( instanceID ) as GameObject;
		}

		private Transform GetTransformFromInstanceID( int instanceID )
		{
			GameObject gameObject = EditorUtility.InstanceIDToObject( instanceID ) as GameObject;
			return gameObject ? gameObject.transform : null;
		}
	}
}