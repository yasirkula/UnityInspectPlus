//#define APPLY_HORIZONTAL_PADDING

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
#if UNITY_2017_1_OR_NEWER
using UnityEditor.Experimental.AssetImporters;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace InspectPlusNamespace
{
	public class InspectPlusWindow : EditorWindow, IHasCustomMenu
	{
		private class DummyLogHandler : ILogHandler
		{
			public void LogException( Exception exception, Object context ) { }
			public void LogFormat( LogType logType, Object context, string format, params object[] args ) { }
		}

		private enum ButtonState { Normal = -1, LeftClicked = 0, RightClicked = 1, MiddleClicked = 2 };

		private const string NEW_TAB_LABEL = "Open In New Tab";
		private const string NEW_WINDOW_LABEL = "Open In New Window";
		private const string CONTEXT_COPY_LABEL = "Copy (Inspect+)";
		private const string CONTEXT_PASTE_LABEL = "Paste (Inspect+)";
		private const string CONTEXT_PASTE_VALUES_LABEL = "Paste Values (Inspect+)";
		private const string CONTEXT_PASTE_FROM_BIN_LABEL = "Paste From Bin (Inspect+)";

		private const float BUTTON_DRAG_THRESHOLD_SQR = 600f;
		private const float HORIZONTAL_SCROLL_SPEED = 10f;
		private const float SCROLLABLE_LIST_ICON_WIDTH = 34f;
		private const float FAVORITES_REFRESH_INTERVAL = 0.5f;
		private const float ANIMATION_PLAYBACK_REFRESH_INTERVAL = 0.15f;
		private const float PREVIEW_HEADER_HEIGHT = 21f;
		private const float PREVIEW_HEADER_PADDING = 5f;
		private const float PREVIEW_INITIAL_HEIGHT = 250f;
		private const float PREVIEW_MIN_HEIGHT = 130f;
		private const float PREVIEW_COLLAPSE_HEIGHT = PREVIEW_MIN_HEIGHT * 0.5f;
		private const string PREVIEW_HEIGHT_PREF = "IPPreviewHeight";
#if APPLY_HORIZONTAL_PADDING
		private const float INSPECTOR_HORIZONTAL_PADDING = 5f;
#endif

		private static readonly List<InspectPlusWindow> windows = new List<InspectPlusWindow>( 8 );

		private static readonly GUIContent windowTitle = new GUIContent( "Inspect+" );
		private static readonly Vector2 windowMinSize = new Vector2( 300f, 300f );

		private static readonly GUIContent addComponentButtonLabel = new GUIContent( "Add Component" );
		private static readonly GUILayoutOption addComponentButtonHeight = GUILayout.Height( 30f );
		private static MethodInfo addComponentButton;

		private static readonly GUILayoutOption horizontalLineHeight = GUILayout.Height( 1f );
		private static readonly GUILayoutOption zeroButtonHeight = GUILayout.Height( 0f );
		private static readonly GUILayoutOption expandWidth = GUILayout.ExpandWidth( true );
		private static readonly GUILayoutOption expandHeight = GUILayout.ExpandHeight( true );

		private static readonly List<Component> components = new List<Component>( 16 );
		private static readonly Dictionary<Type, bool> componentsExpandableStates = new Dictionary<Type, bool>( 128 );
		private static readonly GUIStyle scrollableListIconGuiStyle = new GUIStyle();
		private static readonly GUIContent textSizeCalculator = new GUIContent();

		private static readonly DummyLogHandler dummyLogHandler = new DummyLogHandler();
		private static readonly Color activeButtonColor = new Color32( 245, 170, 10, 255 );

		private static InspectPlusWindow mainWindow;
		private static GUIContent favoritesIcon, historyIcon;
		private static GUIContent favoritesIconNoTooltip, historyIconNoTooltip;
		private static Rect lastWindowPosition;

		private static List<Object> objectsToOpenPasteBinWith;

		// These are not readonly to support serialization of the data
		// SerializeField makes history data persist between editor sessions (unfortunately, only assets persist, not scene objects)
		[SerializeField]
		private List<Object> history = new List<Object>( 8 );
		[SerializeField]
		private Object mainObject;

		private List<Editor> inspectorDrawers = new List<Editor>( 16 );
		private int inspectorDrawerCount;
		private int debugModeDrawerCount;

#if UNITY_2017_1_OR_NEWER
		private AssetImporterEditor inspectorAssetDrawer;
#else
		private Editor inspectorAssetDrawer;
		private static Type assetImporterEditorType;
		private static PropertyInfo assetImporterShowImportedObjectProperty;
#endif

#if UNITY_2018_1_OR_NEWER
		private static MethodInfo assetImporterEditorSetterMethod;
#else
		private static FieldInfo assetImporterEditorField;
#endif

#if UNITY_2019_2_OR_NEWER
		private static MethodInfo showApplyRevertDialogMethod;
#endif

		// Serializing CustomProjectWindow makes the TreeView's state (collapsed entries etc.) persist between editor sessions
		[SerializeField]
		private CustomProjectWindow projectWindow = new CustomProjectWindow();
		private bool showProjectWindow;
		private bool syncProjectWindowSelection;
		private Editor projectWindowSelectionEditor;

#if UNITY_2017_2_OR_NEWER
		private bool changingPlayMode;
#endif
		private bool shouldRepositionSelf;
		private bool shouldRepaint;
		private bool snapFavoritesToActiveObject;
		private bool snapHistoryToActiveObject;
		private Object pendingInspectTarget;
		private float pendingScrollAmount;
		private Vector2 buttonPressPosition;
		private double nextUpdateTime;
		private double nextAnimationRepaintTime;
		private bool objectBrowserWindowVisible;
		private double objectBrowserWindowCloseTime;

		private bool showFavorites, showHistory;
		private Vector2 favoritesScrollPosition, historyScrollPosition, inspectorScrollPosition;

		private float previewHeight;
		private float previewLastHeight;
		private bool previewHeaderClicked;
		private static GUIStyle previewHeaderGuiStyle;
		private static GUIStyle previewResizeAreaGuiStyle;
		private static GUIStyle previewBackgroundGuiStyle;

		private bool debugMode;
		private double debugModeRefreshTime;
		private double favoritesRefreshTime;

		private readonly List<DebugModeEntry> debugModeDrawers = new List<DebugModeEntry>( 16 );
		private readonly List<List<Object>> historyHolder = new List<List<Object>>( 1 );
		private readonly List<List<Object>> favoritesHolder = new List<List<Object>>( 4 );

		private GUILayoutOption favoritesHeight;
		private GUILayoutOption historyHeight;
		private GUILayoutOption compactListHeight;
		private GUILayoutOption previewHeaderHeight;
		private GUILayoutOption scrollableListIconSize;

		#region Initializers
		[MenuItem( "Window/Inspect+/New Window" )]
		private static void ShowNewWindow()
		{
			GetNewWindow();
		}

		[MenuItem( "Window/Inspect+/Paste Bin" )]
		private static void ShowPasteBinWindow()
		{
			PasteBinWindow.Show();
		}

		[MenuItem( "Window/Inspect+/Settings" )]
		private static void ShowSettings()
		{
			Selection.activeObject = InspectPlusSettings.Instance;
		}

		[InitializeOnLoadMethod]
		private static void Initialize()
		{
			Type addComponentWindow = typeof( EditorWindow ).Assembly.GetType( "UnityEditor.AddComponentWindow" );
			if( addComponentWindow == null )
				addComponentWindow = typeof( EditorWindow ).Assembly.GetType( "UnityEditor.AddComponent.AddComponentWindow" );
			if( addComponentWindow == null )
				addComponentWindow = typeof( EditorWindow ).Assembly.GetType( "UnityEditor.AdvancedDropdown.AddComponentWindow" );

			if( addComponentWindow != null )
				addComponentButton = addComponentWindow.GetMethod( "Show", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );
			else
			{
				Debug.LogWarning( "Couldn't fetch Add Component button" );
				addComponentButton = null;
			}

#if UNITY_2018_1_OR_NEWER
			assetImporterEditorSetterMethod = typeof( AssetImporterEditor ).GetMethod( "InternalSetAssetImporterTargetEditor", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance );
#elif UNITY_2017_1_OR_NEWER
			assetImporterEditorField = typeof( AssetImporterEditor ).GetField( "m_AssetEditor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
#else
			assetImporterEditorType = typeof( EditorApplication ).Assembly.GetType( "UnityEditor.AssetImporterInspector" );
			assetImporterShowImportedObjectProperty = assetImporterEditorType.GetProperty( "showImportedObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
			assetImporterEditorField = assetImporterEditorType.GetField( "m_AssetEditor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
#endif

#if UNITY_2019_2_OR_NEWER
			showApplyRevertDialogMethod = typeof( AssetImporterEditor ).GetMethod( "CheckForApplyOnClose", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance );
#endif

			favoritesIconNoTooltip = new GUIContent( EditorGUIUtility.Load( "Favorite Icon" ) as Texture );
			historyIconNoTooltip = new GUIContent( EditorGUIUtility.Load( "Search Icon" ) as Texture );
			favoritesIcon = new GUIContent( favoritesIconNoTooltip.image, "Favorites" );
			historyIcon = new GUIContent( historyIconNoTooltip.image, "History" );

			scrollableListIconGuiStyle.margin = new RectOffset( 2, 2, 2, 2 );
			scrollableListIconGuiStyle.alignment = TextAnchor.MiddleCenter;

			EditorApplication.contextualPropertyMenu -= OnPropertyRightClicked;
			EditorApplication.contextualPropertyMenu += OnPropertyRightClicked;
		}

		private void Awake()
		{
			wantsMouseEnterLeaveWindow = true;

			showFavorites = InspectPlusSettings.Instance.ShowFavoritesByDefault;
			showHistory = InspectPlusSettings.Instance.ShowHistoryByDefault;
			syncProjectWindowSelection = InspectPlusSettings.Instance.SyncProjectWindowSelection;

			// Window is restored after Unity is closed and then reopened
			if( history.Count > 0 )
			{
				for( int i = history.Count - 1; i >= 0; i-- )
				{
					if( !history[i] )
						history.RemoveAt( i );
				}

				if( history.Count == 0 )
					Close();
			}
		}

		private void OnEnable()
		{
			windows.Add( this );

			Undo.undoRedoPerformed -= OnUndoRedo;
			Undo.undoRedoPerformed += OnUndoRedo;
#if UNITY_2017_2_OR_NEWER
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
#if UNITY_2019_2_OR_NEWER
			EditorApplication.wantsToQuit -= ApplicationWantsToQuit;
			EditorApplication.wantsToQuit += ApplicationWantsToQuit;
#endif

			if( mainObject )
			{
				// This also makes sure that debug mode drawers are recreated
				InspectInternal( mainObject, false );
			}
			else
			{
				for( int i = 0; i < inspectorDrawers.Count; i++ )
					DestroyImmediate( inspectorDrawers[i] );

				inspectorDrawers.Clear();
				inspectorDrawerCount = 0;
				debugModeDrawerCount = 0;

				DestroyImmediate( projectWindowSelectionEditor );
				projectWindowSelectionEditor = null;

				previewHeight = EditorPrefs.GetFloat( PREVIEW_HEIGHT_PREF, PREVIEW_INITIAL_HEIGHT );
			}

			previewLastHeight = previewHeight >= PREVIEW_MIN_HEIGHT ? previewHeight : PREVIEW_INITIAL_HEIGHT;

			historyHolder.Add( history );
			favoritesHolder.Add( InspectPlusSettings.Instance.FavoriteAssets );

			projectWindow.OnSelectionChanged = ProjectWindowSelectionChanged;
			if( projectWindow.GetTreeView() != null )
				ProjectWindowSelectionChanged( projectWindow.GetTreeView().GetSelection() );

			RefreshSettings();
		}

		private void OnDisable()
		{
			windows.Remove( this );

			Undo.undoRedoPerformed -= OnUndoRedo;
#if UNITY_2017_2_OR_NEWER
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
#if UNITY_2019_2_OR_NEWER
			EditorApplication.wantsToQuit -= ApplicationWantsToQuit;
#endif

			historyHolder.Clear();
			favoritesHolder.Clear();

			if( mainWindow == this )
				mainWindow = null;
		}

		private void OnDestroy()
		{
			for( int i = 0; i < inspectorDrawers.Count; i++ )
				DestroyImmediate( inspectorDrawers[i] );

			inspectorDrawers.Clear();
			inspectorDrawerCount = 0;
			debugModeDrawerCount = 0;

			DestroyImmediate( projectWindowSelectionEditor );
			projectWindowSelectionEditor = null;

			SetInspectorAssetDrawer( null );
		}

#if UNITY_2019_2_OR_NEWER
		private bool ApplicationWantsToQuit()
		{
			SetInspectorAssetDrawer( null ); // Show Apply/Revert dialog if necessary
			return true;
		}
#endif

		private void OnFocus()
		{
			mainWindow = this;
		}

		private void OnUndoRedo()
		{
			shouldRepaint = true;
		}

#if UNITY_2017_2_OR_NEWER
		private void OnPlayModeStateChanged( PlayModeStateChange state )
		{
			if( state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode )
			{
				changingPlayMode = true;

				DestroyImmediate( projectWindowSelectionEditor );
				projectWindowSelectionEditor = null;
				SetInspectorAssetDrawer( null );
			}
			else
			{
				changingPlayMode = false;

				for( int i = 0; i < inspectorDrawerCount; i++ )
				{
					Editor editor = inspectorDrawers[i];
					if( editor && editor.target )
					{
						Object target = editor.target;
						DestroyImmediate( editor );
						editor = Editor.CreateEditor( target );

						inspectorDrawers[i] = editor;
					}
				}

				if( projectWindow.GetTreeView() != null )
					ProjectWindowSelectionChanged( projectWindow.GetTreeView().GetSelection() );
			}
		}
#endif

		private void RefreshSettings()
		{
			favoritesHeight = GUILayout.Height( InspectPlusSettings.Instance.FavoritesHeight );
			historyHeight = GUILayout.Height( InspectPlusSettings.Instance.HistoryHeight );
			compactListHeight = GUILayout.Height( InspectPlusSettings.Instance.CompactListHeight );
			previewHeaderHeight = GUILayout.Height( PREVIEW_HEADER_HEIGHT );
			scrollableListIconSize = GUILayout.Width( Mathf.Min( SCROLLABLE_LIST_ICON_WIDTH, InspectPlusSettings.Instance.FavoritesHeight, InspectPlusSettings.Instance.HistoryHeight ) );

			shouldRepaint = true;
		}

		private static InspectPlusWindow GetDefaultWindow()
		{
			InspectPlusWindow result = GetWindow<InspectPlusWindow>();
			result.titleContent = windowTitle;
			result.minSize = windowMinSize;
			result.Show();

			return result;
		}

		private static InspectPlusWindow GetNewWindow()
		{
			InspectPlusWindow result = CreateInstance<InspectPlusWindow>();
			result.titleContent = windowTitle;
			result.minSize = windowMinSize;
			result.shouldRepositionSelf = true;
			result.Show( true );

			return result;
		}
		#endregion

		#region Context Menu Buttons
		[MenuItem( "GameObject/Inspect+/" + NEW_TAB_LABEL, priority = 49 )]
		[MenuItem( "Assets/Inspect+/" + NEW_TAB_LABEL, priority = 1500 )]
		private static void MenuItemNewTab( MenuCommand command )
		{
			if( command.context )
				Inspect( PreferablyGameObject( command.context ), false );
			else
				Inspect( PreferablyGameObject( Selection.objects ), false );
		}

		[MenuItem( "GameObject/Inspect+/" + NEW_WINDOW_LABEL, priority = 49 )]
		[MenuItem( "Assets/Inspect+/" + NEW_WINDOW_LABEL, priority = 1500 )]
		private static void MenuItemNewWindow( MenuCommand command )
		{
			if( command.context )
				Inspect( PreferablyGameObject( command.context ), true );
			else
				Inspect( PreferablyGameObject( Selection.objects ), true );
		}

		[MenuItem( "CONTEXT/Component/" + NEW_TAB_LABEL, priority = 1500 )]
		[MenuItem( "CONTEXT/ScriptableObject/" + NEW_TAB_LABEL, priority = 1500 )]
		[MenuItem( "CONTEXT/AssetImporter/" + NEW_TAB_LABEL, priority = 1500 )]
		[MenuItem( "CONTEXT/Material/" + NEW_TAB_LABEL, priority = 1500 )]
		private static void ContextMenuItemNewTab( MenuCommand command )
		{
			Inspect( command.context, false );
		}

		[MenuItem( "CONTEXT/Component/" + NEW_WINDOW_LABEL, priority = 1500 )]
		[MenuItem( "CONTEXT/ScriptableObject/" + NEW_WINDOW_LABEL, priority = 1500 )]
		[MenuItem( "CONTEXT/AssetImporter/" + NEW_WINDOW_LABEL, priority = 1500 )]
		[MenuItem( "CONTEXT/Material/" + NEW_WINDOW_LABEL, priority = 1500 )]
		private static void ContextMenuItemNewWindow( MenuCommand command )
		{
			Inspect( command.context, true );
		}

		[MenuItem( "GameObject/Inspect+/Copy Value", priority = 49 )]
		[MenuItem( "Assets/Inspect+/Copy Value", priority = 1500 )]
		[MenuItem( "CONTEXT/Component/" + CONTEXT_COPY_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/ScriptableObject/" + CONTEXT_COPY_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/Material/" + CONTEXT_COPY_LABEL, priority = 1450 )]
		private static void ContextMenuItemCopyObject( MenuCommand command )
		{
			// Passing null as context parameter because we don't want to calculate a "./" RelativePath for this clipboard in XML mode
			if( command.context )
				PasteBinWindow.AddToClipboard( command.context, Utilities.GetDetailedObjectName( command.context ), null );
			else
				PasteBinWindow.AddToClipboard( Selection.activeObject, Utilities.GetDetailedObjectName( Selection.activeObject ), null );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_VALUES_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/ScriptableObject/" + CONTEXT_PASTE_VALUES_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/Material/" + CONTEXT_PASTE_VALUES_LABEL, priority = 1450 )]
		private static void ContextMenuItemPasteObject( MenuCommand command )
		{
			if( PasteBinWindow.ActiveClipboard != null )
				PasteBinWindow.ActiveClipboard.PasteToObject( command.context );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_FROM_BIN_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/ScriptableObject/" + CONTEXT_PASTE_FROM_BIN_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/Material/" + CONTEXT_PASTE_FROM_BIN_LABEL, priority = 1450 )]
		private static void ContextMenuItemPasteObjectFromBin( MenuCommand command )
		{
			// This happens when this button is clicked while multiple Objects were selected. In this case,
			// this function will be called once for each selected Object. We don't want to open a separate
			// paste bin window for each selected Object. Instead, show a single paste bin window that will
			// paste to all of the selected Objects. We aren't using Selection.objects because for components,
			// it will return the GameObject instead
			if( command.context )
			{
				if( objectsToOpenPasteBinWith == null )
					objectsToOpenPasteBinWith = new List<Object>( 2 ) { command.context };
				else
					objectsToOpenPasteBinWith.Add( command.context );

				EditorApplication.update -= CallPasteObjectFromBinOnce;
				EditorApplication.update += CallPasteObjectFromBinOnce;
			}
			else if( objectsToOpenPasteBinWith != null )
			{
				PasteValueFromBin( objectsToOpenPasteBinWith.ToArray() );
				objectsToOpenPasteBinWith = null;
			}
		}

		private static void CallPasteObjectFromBinOnce()
		{
			EditorApplication.update -= CallPasteObjectFromBinOnce;
			ContextMenuItemPasteObjectFromBin( new MenuCommand( null ) );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_VALUES_LABEL, validate = true )]
		[MenuItem( "CONTEXT/ScriptableObject/" + CONTEXT_PASTE_VALUES_LABEL, validate = true )]
		[MenuItem( "CONTEXT/Material/" + CONTEXT_PASTE_VALUES_LABEL, validate = true )]
		private static bool ContextMenuItemPasteObjectValidate( MenuCommand command )
		{
			if( !command.context )
			{
				Debug.LogError( "Encountered empty context, probably a missing script." );
				return false;
			}

			return PasteBinWindow.ActiveClipboard != null && PasteBinWindow.ActiveClipboard.CanPasteToObject( command.context );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_FROM_BIN_LABEL, validate = true )]
		[MenuItem( "CONTEXT/ScriptableObject/" + CONTEXT_PASTE_FROM_BIN_LABEL, validate = true )]
		[MenuItem( "CONTEXT/Material/" + CONTEXT_PASTE_FROM_BIN_LABEL, validate = true )]
		private static bool ContextMenuItemPasteObjectFromBinValidate( MenuCommand command )
		{
			if( !command.context )
			{
				Debug.LogError( "Encountered empty context, probably a missing script." );
				return false;
			}

			return true;
		}

		[MenuItem( "GameObject/Inspect+/" + NEW_TAB_LABEL, validate = true )]
		[MenuItem( "GameObject/Inspect+/" + NEW_WINDOW_LABEL, validate = true )]
		[MenuItem( "Assets/Inspect+/" + NEW_TAB_LABEL, validate = true )]
		[MenuItem( "Assets/Inspect+/" + NEW_WINDOW_LABEL, validate = true )]
		private static bool GameObjectMenuValidate( MenuCommand command )
		{
			return Selection.objects.Length > 0;
		}

		private static void OnPropertyRightClicked( GenericMenu menu, SerializedProperty property )
		{
			Object obj = null;
			bool isUnityObjectType = false;
			if( property.propertyType == SerializedPropertyType.ExposedReference )
			{
				obj = property.exposedReferenceValue;
				isUnityObjectType = true;
			}
			else if( property.propertyType == SerializedPropertyType.ObjectReference )
			{
				obj = property.objectReferenceValue;
				isUnityObjectType = true;
			}

			if( isUnityObjectType && property.hasMultipleDifferentValues )
			{
				string propertyPath = property.propertyPath;
				Object[] targets = property.serializedObject.targetObjects;

				bool containsComponents = false;
				for( int i = 0; i < targets.Length; i++ )
				{
					SerializedProperty _property = new SerializedObject( targets[i] ).FindProperty( propertyPath );
					if( _property.propertyType == SerializedPropertyType.ExposedReference )
					{
						targets[i] = _property.exposedReferenceValue;
						if( targets[i] is Component )
							containsComponents = true;
					}
					else if( _property.propertyType == SerializedPropertyType.ObjectReference )
					{
						targets[i] = _property.objectReferenceValue;
						if( targets[i] is Component )
							containsComponents = true;
					}
				}

				if( containsComponents )
				{
					menu.AddItem( new GUIContent( NEW_TAB_LABEL + "/All/GameObject" ), false, () => Inspect( PreferablyGameObject( targets ), false ) );
					menu.AddItem( new GUIContent( NEW_TAB_LABEL + "/All/Component" ), false, () => Inspect( targets, false ) );
					menu.AddItem( new GUIContent( NEW_WINDOW_LABEL + "/All/GameObject" ), false, () => Inspect( PreferablyGameObject( targets ), true ) );
					menu.AddItem( new GUIContent( NEW_WINDOW_LABEL + "/All/Component" ), false, () => Inspect( targets, true ) );
				}
				else
				{
					menu.AddItem( new GUIContent( NEW_TAB_LABEL + "/All" ), false, () => Inspect( targets, false ) );
					menu.AddItem( new GUIContent( NEW_WINDOW_LABEL + "/All" ), false, () => Inspect( targets, true ) );
				}

				for( int i = 0; i < targets.Length; i++ )
				{
					if( targets[i] )
						AddInspectButtonToMenu( menu, targets[i], "/" + targets[i].name );
				}

				menu.AddSeparator( "" );
			}
			else if( obj )
			{
				AddInspectButtonToMenu( menu, obj, "" );
				menu.AddSeparator( "" );
			}

			if( !property.hasMultipleDifferentValues && ( !isUnityObjectType || obj ) )
				menu.AddItem( new GUIContent( CONTEXT_COPY_LABEL ), false, CopyValue, property.Copy() );
			else
				menu.AddDisabledItem( new GUIContent( CONTEXT_COPY_LABEL ) );

			if( PasteBinWindow.ActiveClipboard == null || !property.CanPasteValue( PasteBinWindow.ActiveClipboard.RootValue, false ) )
				menu.AddDisabledItem( new GUIContent( CONTEXT_PASTE_LABEL ) );
			else
				menu.AddItem( new GUIContent( CONTEXT_PASTE_LABEL ), false, PasteValue, property.Copy() );

			menu.AddItem( new GUIContent( CONTEXT_PASTE_FROM_BIN_LABEL ), false, PasteValueFromBin, property.Copy() );
		}

		public static void OnObjectRightClicked( GenericMenu menu, Object obj )
		{
			AddInspectButtonToMenu( menu, obj, "" );
		}

		void IHasCustomMenu.AddItemsToMenu( GenericMenu menu )
		{
			if( !InspectPlusSettings.Instance.CompactFavoritesAndHistoryLists )
			{
				menu.AddItem( new GUIContent( "Show Favorites" ), showFavorites, () => showFavorites = !showFavorites );
				menu.AddItem( new GUIContent( "Show History" ), showHistory, () => showHistory = !showHistory );
				menu.AddSeparator( "" );
			}

			menu.AddItem( new GUIContent( "Debug Mode" ), debugMode, () => debugMode = !debugMode );
			menu.AddSeparator( "" );

			if( showProjectWindow )
			{
				menu.AddItem( new GUIContent( "Synchronize Selection" ), syncProjectWindowSelection, () =>
				{
					syncProjectWindowSelection = !syncProjectWindowSelection;

					CustomProjectWindowDrawer treeView = projectWindow.GetTreeView();
					if( treeView != null )
						treeView.SyncSelection = syncProjectWindowSelection;
				} );

				menu.AddSeparator( "" );
			}

			menu.AddItem( new GUIContent( "Clear Favorites" ), false, () =>
			{
				InspectPlusSettings.Instance.FavoriteAssets.Clear();
				EditorUtility.SetDirty( InspectPlusSettings.Instance );

				for( int i = 0; i < SceneFavoritesHolder.Instances.Count; i++ )
				{
					SceneFavoritesHolder.Instances[i].FavoriteObjects.Clear();
					SceneFavoritesHolder.Instances[i].SetSceneDirty();
				}
			} );

			menu.AddItem( new GUIContent( "Clear History" ), false, () =>
			{
				if( InspectPlusSettings.Instance.ClearingHistoryRemovesActiveObject )
					history.Clear();
				else
				{
					for( int i = history.Count - 1; i >= 0; i-- )
					{
						if( history[i] != mainObject )
							history.RemoveAt( i );
					}
				}
			} );

			menu.AddSeparator( "" );

			menu.AddItem( new GUIContent( "Refresh" ), false, RefreshSettings );
			menu.AddItem( new GUIContent( "Refresh All" ), false, () =>
			{
				for( int i = 0; i < windows.Count; i++ )
					windows[i].RefreshSettings();
			} );

			menu.AddSeparator( "" );

			menu.AddItem( new GUIContent( "Paste Bin" ), false, ShowPasteBinWindow );

			menu.AddItem( new GUIContent( "Settings" ), false, () => Selection.activeObject = InspectPlusSettings.Instance );

			menu.AddSeparator( "" );

			menu.AddItem( new GUIContent( "Close All" ), false, () =>
			{
				for( int i = windows.Count - 1; i >= 0; i-- )
					windows[i].Close();
			} );

			menu.AddItem( new GUIContent( "Close All But This" ), false, () =>
			{
				for( int i = windows.Count - 1; i >= 0; i-- )
				{
					if( windows[i] != this )
						windows[i].Close();
				}
			} );
		}

		private void OnScrollViewButtonRightClicked( GenericMenu menu, List<Object> list, int index )
		{
			menu.AddItem( new GUIContent( "Remove" ), false, () => RemoveObjectFromList( list, index ) );
			menu.AddSeparator( "" );

			// Allow switching between components of a GameObject
			if( list[index] )
			{
				GameObject go;
				if( list[index] is Component )
					go = ( (Component) list[index] ).gameObject;
				else if( list[index] is GameObject )
					go = (GameObject) list[index];
				else
					go = null;

				if( go )
				{
					components.Clear();
					go.GetComponents( components );

					menu.AddItem( new GUIContent( "Inspect/GameObject" ), false, () =>
					{
						if( list == history )
							list[index] = go;

						InspectInternal( go, false );
					} );

					for( int j = 0; j < components.Count; j++ )
					{
						Component component = components[j];
						if( component )
						{
							menu.AddItem( new GUIContent( "Inspect/" + component.GetType().Name ), false, () =>
							{
								if( list == history )
									list[index] = component;

								InspectInternal( component, false );
							} );
						}
					}
				}
			}

			menu.AddItem( new GUIContent( NEW_WINDOW_LABEL ), false, () => Inspect( list[index], true ) );
			menu.AddSeparator( "" );

			if( list == history )
				menu.AddItem( new GUIContent( "Add To Favorites" ), false, () => TryAddObjectToFavorites( list[index] ) );

			menu.AddItem( new GUIContent( "Open In Unity Inspector" ), false, () => Selection.activeObject = list[index] );
			menu.AddItem( new GUIContent( "Ping" ), false, () => EditorGUIUtility.PingObject( list[index] ) );
		}

		private static void AddInspectButtonToMenu( GenericMenu menu, Object obj, string path )
		{
			if( obj is Component )
			{
				string componentType = string.Concat( "/", obj.GetType().Name, " Component" );

				menu.AddItem( new GUIContent( string.Concat( NEW_TAB_LABEL, path, "/GameObject" ) ), false, () => Inspect( PreferablyGameObject( obj ), false ) );
				menu.AddItem( new GUIContent( string.Concat( NEW_TAB_LABEL, path, componentType ) ), false, () => Inspect( obj, false ) );
				menu.AddItem( new GUIContent( string.Concat( NEW_WINDOW_LABEL, path, "/GameObject" ) ), false, () => Inspect( PreferablyGameObject( obj ), true ) );
				menu.AddItem( new GUIContent( string.Concat( NEW_WINDOW_LABEL, path, componentType ) ), false, () => Inspect( obj, true ) );
			}
			else
			{
				menu.AddItem( new GUIContent( string.Concat( NEW_TAB_LABEL, path ) ), false, () => Inspect( obj, false ) );
				menu.AddItem( new GUIContent( string.Concat( NEW_WINDOW_LABEL, path ) ), false, () => Inspect( obj, true ) );
			}
		}
		#endregion

		#region Inspect Functions
		public static void Inspect( Object[] objs, bool newWindow )
		{
			for( int i = 0; i < objs.Length; i++ )
			{
				bool alreadyInspected = false;
				for( int j = 0; j < i; j++ )
				{
					if( objs[i] == objs[j] )
					{
						alreadyInspected = true;
						break;
					}
				}

				if( !alreadyInspected )
					Inspect( objs[i], newWindow );
			}
		}

		public static void Inspect( Object obj, bool newWindow )
		{
			if( !obj )
				return;

			if( obj is AssetImporter )
			{
				obj = AssetDatabase.LoadMainAssetAtPath( ( (AssetImporter) obj ).assetPath );
				if( !obj )
					return;
			}

			if( newWindow )
				GetNewWindow().InspectInternal( obj, true );
			else if( mainWindow )
			{
				mainWindow.InspectInternal( obj, true );
				mainWindow.shouldRepaint = true;
			}
			else
				GetDefaultWindow().InspectInternal( obj, true );
		}

		private void InspectInternal( Object obj, bool addHistoryEntry )
		{
			if( !obj )
				return;

			string assetPath = AssetDatabase.GetAssetPath( obj );

			inspectorDrawerCount = 0;
			debugModeDrawerCount = 0;
			debugModeRefreshTime = 0f;

#if UNITY_2017_1_OR_NEWER
			AssetImporterEditor _inspectorAssetDrawer = null;
#else
			Editor _inspectorAssetDrawer = null;
#endif
			if( !string.IsNullOrEmpty( assetPath ) && AssetDatabase.IsMainAsset( obj ) && !AssetDatabase.IsValidFolder( assetPath ) )
			{
				if( inspectorAssetDrawer )
				{
					AssetImporter assetImporter = inspectorAssetDrawer.target as AssetImporter;
					if( assetImporter && assetImporter.assetPath == assetPath )
						_inspectorAssetDrawer = inspectorAssetDrawer;
				}

				if( !_inspectorAssetDrawer )
				{
					AssetImporter assetImporter = AssetImporter.GetAtPath( AssetDatabase.GetAssetPath( obj ) );
					if( assetImporter )
					{
#if UNITY_2017_1_OR_NEWER
						ILogHandler unityLogHandler = Debug.unityLogger.logHandler;
#else
						ILogHandler unityLogHandler = Debug.logger.logHandler;
#endif
						Editor assetImporterEditor;
						try
						{
							// Creating AssetImporterEditors manually doesn't set the editor's "m_AssetEditor" field automatically for some reason,
							// it can result in exceptions being thrown in the AssetImporterEditors' Awake and/or OnEnable functions, ignore them
#if UNITY_2017_1_OR_NEWER
							Debug.unityLogger.logHandler = dummyLogHandler;
#else
							Debug.logger.logHandler = dummyLogHandler;
#endif
							assetImporterEditor = Editor.CreateEditor( assetImporter );
						}
						finally
						{
#if UNITY_2017_1_OR_NEWER
							Debug.unityLogger.logHandler = unityLogHandler;
#else
							Debug.logger.logHandler = unityLogHandler;
#endif
						}

						if( assetImporterEditor )
						{
#if UNITY_2017_1_OR_NEWER
							_inspectorAssetDrawer = assetImporterEditor as AssetImporterEditor;
#else
							if( assetImporterEditorType.IsAssignableFrom( assetImporterEditor.GetType() ) )
								_inspectorAssetDrawer = assetImporterEditor;
#endif
						}

						if( !_inspectorAssetDrawer )
							DestroyImmediate( assetImporterEditor );
						else
						{
							Editor objEditor = Editor.CreateEditor( obj );
							if( objEditor )
							{
#if UNITY_2018_1_OR_NEWER
								assetImporterEditorSetterMethod.Invoke( _inspectorAssetDrawer, new object[1] { objEditor } );
#else
								assetImporterEditorField.SetValue( _inspectorAssetDrawer, objEditor );
#endif
							}
						}
					}
				}
			}

			SetInspectorAssetDrawer( _inspectorAssetDrawer );

#if UNITY_2017_1_OR_NEWER
			if( !inspectorAssetDrawer || inspectorAssetDrawer.showImportedObject )
#else
			if( !inspectorAssetDrawer || (bool) assetImporterShowImportedObjectProperty.GetValue( inspectorAssetDrawer, null ) )
#endif
			{
				if( obj is GameObject )
				{
					components.Clear();
					( (GameObject) obj ).GetComponents( components );

					CreateEditorFor( obj );

					for( int i = 0; i < components.Count; i++ )
					{
						if( components[i] )
							CreateEditorFor( components[i] );
					}
				}
				else
					CreateEditorFor( obj );
			}

			for( int i = inspectorDrawers.Count - 1; i >= inspectorDrawerCount; i-- )
			{
				DestroyImmediate( inspectorDrawers[i] );
				inspectorDrawers.RemoveAt( i );
			}

			if( inspectorDrawerCount > 0 || inspectorAssetDrawer )
				mainObject = obj;

			// Show a project window instance while inspecting a directory
			if( !string.IsNullOrEmpty( assetPath ) && AssetDatabase.IsValidFolder( assetPath ) )
			{
				projectWindow.Show( assetPath );
				projectWindow.GetTreeView().SyncSelection = syncProjectWindowSelection;

				showProjectWindow = true;
			}
			else
				showProjectWindow = false;

			if( addHistoryEntry )
			{
				TryAddObjectToHistory( obj );
				snapHistoryToActiveObject = true;
			}
		}
		#endregion

		private void Update()
		{
			if( pendingInspectTarget )
			{
				InspectInternal( pendingInspectTarget, false );

				if( InspectPlusSettings.Instance.AutomaticallyPingSelectedObject )
					EditorGUIUtility.PingObject( pendingInspectTarget );

				pendingInspectTarget = null;
			}

			// Regularly check if components of the inspected GameObject has changed
			double time = EditorApplication.timeSinceStartup;
			if( time >= nextUpdateTime )
			{
				if( inspectorDrawerCount > 1 )
				{
					GameObject obj = inspectorDrawers[0].target as GameObject;
					if( obj )
						InspectInternal( obj, false );
				}

				if( !debugMode )
					shouldRepaint = true;

				nextUpdateTime = time + InspectPlusSettings.Instance.NormalModeRefreshInterval;
			}

			if( debugMode && time >= debugModeRefreshTime )
			{
				for( int i = 0; i < debugModeDrawerCount; i++ )
					debugModeDrawers[i].Refresh();

				shouldRepaint = true;
				debugModeRefreshTime = time + InspectPlusSettings.Instance.DebugModeRefreshInterval;
			}

			if( time >= favoritesRefreshTime )
			{
				favoritesHolder.Clear();
				favoritesHolder.Add( InspectPlusSettings.Instance.FavoriteAssets );
				for( int i = 0; i < SceneFavoritesHolder.Instances.Count; i++ )
					favoritesHolder.Add( SceneFavoritesHolder.Instances[i].FavoriteObjects );

				favoritesRefreshTime = time + FAVORITES_REFRESH_INTERVAL;
			}

#if UNITY_2017_1_OR_NEWER
			if( inspectorAssetDrawer && inspectorAssetDrawer.RequiresConstantRepaint() )
				shouldRepaint = true;
#endif

			if( AnimationMode.InAnimationMode() && time >= nextAnimationRepaintTime )
			{
				shouldRepaint = true;
				nextAnimationRepaintTime = time + ANIMATION_PLAYBACK_REFRESH_INTERVAL;
			}

			if( shouldRepositionSelf )
			{
				shouldRepositionSelf = false;

				Rect position = lastWindowPosition;
				position.position += new Vector2( 50f, 50f );
				this.position = position;
				lastWindowPosition = position;
			}

			if( shouldRepaint )
			{
				shouldRepaint = false;
				Repaint();
			}
		}

		private void OnProjectChange()
		{
			if( showProjectWindow )
				projectWindow.Refresh();
		}

		private void ProjectWindowSelectionChanged( IList<int> newSelection )
		{
			DestroyImmediate( projectWindowSelectionEditor );
			projectWindowSelectionEditor = null;

			if( newSelection != null && newSelection.Count > 0 )
			{
				Object[] selection = new Object[newSelection.Count];
				for( int i = 0; i < selection.Length; i++ )
				{
					Object obj = EditorUtility.InstanceIDToObject( newSelection[i] );
					if( !obj || ( i > 0 && selection[0].GetType() != obj.GetType() ) ) // All objects must be of same type
						return;

					selection[i] = obj;
				}

				projectWindowSelectionEditor = Editor.CreateEditor( selection );
				if( projectWindowSelectionEditor && !projectWindowSelectionEditor.HasPreviewGUI() )
				{
					DestroyImmediate( projectWindowSelectionEditor );
					projectWindowSelectionEditor = null;
				}
			}
		}

		#region GUI Functions
		private void OnGUI()
		{
#if UNITY_2017_2_OR_NEWER
			if( changingPlayMode )
				return;
#endif

			Event ev = Event.current;
			if( InspectPlusSettings.Instance.CompactFavoritesAndHistoryLists )
			{
				GUILayout.BeginHorizontal();
				DrawScrollableList( true );
				DrawScrollableList( false );
				GUILayout.EndHorizontal();
			}
			else
			{
				if( ev.type == EventType.ScrollWheel )
				{
					pendingScrollAmount = ev.delta.y * HORIZONTAL_SCROLL_SPEED;
					shouldRepaint = true;
				}

				if( showFavorites )
				{
					bool favoritesEmpty = true;
					for( int i = 0; i < favoritesHolder.Count; i++ )
					{
						if( favoritesHolder[i].Count > 0 )
						{
							favoritesEmpty = false;
							break;
						}
					}

					if( !favoritesEmpty )
						favoritesScrollPosition = DrawScrollableList( true );
				}

				if( showHistory && history.Count > 0 )
					historyScrollPosition = DrawScrollableList( false );

				if( ev.type == EventType.Repaint )
					pendingScrollAmount = 0f;
			}

			if( !mainObject )
				return;

			if( ev.type == EventType.MouseEnterWindow )
			{
				lastWindowPosition = position;
				shouldRepaint = true;
			}

			inspectorScrollPosition = EditorGUILayout.BeginScrollView( inspectorScrollPosition );

			GUILayout.BeginVertical(); // Needed on 2019.2 or newer for material Inspectors to be drawn correctly. Problematic line here: https://github.com/Unity-Technologies/UnityCsReference/blob/befa918e671668a919f25a5d57d521072d79f560/Editor/Mono/Inspector/MaterialEditor.cs#L1659

#if APPLY_HORIZONTAL_PADDING
			GUILayout.BeginHorizontal();
			GUILayout.Space( INSPECTOR_HORIZONTAL_PADDING );
			GUILayout.BeginVertical();
#endif

			if( debugMode )
			{
				GUILayout.Box( "(F)ield, (P)roperty, (S)tatic, (O)bsolete\n(+)Public, (#)Protected/Internal, (-)Private", expandWidth );

				for( int i = 0; i < debugModeDrawerCount; i++ )
					debugModeDrawers[i].DrawOnGUI();
			}
			else
			{
				GUILayout.Space( 0 ); // Somehow gets rid of the free space above the inspector header

				float windowWidth = position.width;
				bool originalWideMode = EditorGUIUtility.wideMode;
				float originalLabelWidth = EditorGUIUtility.labelWidth;

				if( inspectorAssetDrawer )
				{
					AdjustLabelWidth( windowWidth );

					inspectorAssetDrawer.DrawHeader();
					inspectorAssetDrawer.OnInspectorGUI();

					if( inspectorDrawerCount > 0 )
					{
						GUILayout.Space( 30 );

						Rect importedObjectHeaderRect = GUILayoutUtility.GetRect( 0f, 100000f, 21f, 21f );
						GUI.Box( importedObjectHeaderRect, "Imported Object" );

#if !UNITY_2019_3_OR_NEWER
						GUILayout.Space( -7 ); // Get rid of the space before the firstDrawer's header
#endif
					}
				}

				if( inspectorDrawerCount > 0 )
				{
					AdjustLabelWidth( windowWidth );

					Editor firstDrawer = inspectorDrawers[0];
					firstDrawer.DrawHeader();
					firstDrawer.OnInspectorGUI();

					for( int i = 1; i < inspectorDrawerCount; i++ )
					{
						AdjustLabelWidth( windowWidth );

						Object targetObject = inspectorDrawers[i].target;
						if( targetObject )
						{
							bool wasVisible = UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded( targetObject );
							bool isVisible = EditorGUILayout.InspectorTitlebar( wasVisible, targetObject, ShouldShowFoldoutFor( targetObject ) );
							if( wasVisible != isVisible )
								UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded( targetObject, isVisible );

							if( isVisible )
								inspectorDrawers[i].OnInspectorGUI();
						}
					}

					// Show Add Component button
					if( addComponentButton != null && mainObject is GameObject )
					{
#if UNITY_2018_3_OR_NEWER
						if( !PrefabUtility.IsPartOfImmutablePrefab( mainObject ) || !AssetDatabase.Contains( mainObject ) )
#else
						if( PrefabUtility.GetPrefabType( mainObject ) != PrefabType.ModelPrefab )
#endif
						{
							GUILayout.Space( 5 );
							DrawHorizontalLine();

							Rect rect = GUILayoutUtility.GetRect( addComponentButtonLabel, GUI.skin.button, addComponentButtonHeight );
							if( EditorGUI.DropdownButton( rect, addComponentButtonLabel, FocusType.Passive, GUI.skin.button ) )
							{
								if( (bool) addComponentButton.Invoke( null, new object[2] { rect, new GameObject[1] { (GameObject) mainObject } } ) )
									GUIUtility.ExitGUI();
							}

							GUILayout.Space( 5 );
						}
					}
				}

				EditorGUIUtility.wideMode = originalWideMode;
				EditorGUIUtility.labelWidth = originalLabelWidth;

				if( showProjectWindow )
				{
					GUILayout.Space( -4 ); // Get rid of the free space above the project window's header
					projectWindow.OnGUI();

					// This happens only when the mouse click is not captured by project window's TreeView
					// In this case, clear project window's selection
					if( ev.type == EventType.MouseDown && ev.button == 0 )
					{
						projectWindow.GetTreeView().SetSelection( new int[0] );
						ProjectWindowSelectionChanged( null );

						shouldRepaint = true;
					}
				}
			}

#if APPLY_HORIZONTAL_PADDING
			GUILayout.EndVertical();
			GUILayout.Space( INSPECTOR_HORIZONTAL_PADDING );
			GUILayout.EndHorizontal();
#endif

			GUILayout.EndVertical();

			EditorGUILayout.EndScrollView();

			if( !debugMode )
			{
				if( !showProjectWindow )
				{
					if( inspectorAssetDrawer && inspectorAssetDrawer.HasPreviewGUI() )
						DrawPreview( inspectorAssetDrawer );
					else
					{
						for( int i = 0; i < inspectorDrawerCount; i++ )
						{
							if( inspectorDrawers[i].HasPreviewGUI() )
							{
								DrawPreview( inspectorDrawers[i] );
								break;
							}
						}
					}
				}
				else
				{
					if( projectWindowSelectionEditor && projectWindowSelectionEditor.HasPreviewGUI() )
						DrawPreview( projectWindowSelectionEditor );
				}
			}
		}

		private Vector2 DrawScrollableList( bool drawingFavorites )
		{
			Vector2 scrollPosition = drawingFavorites ? favoritesScrollPosition : historyScrollPosition;
			List<List<Object>> lists = drawingFavorites ? favoritesHolder : historyHolder;
			GUIContent icon = drawingFavorites ? ( !objectBrowserWindowVisible ? favoritesIcon : favoritesIconNoTooltip ) : ( !objectBrowserWindowVisible ? historyIcon : historyIconNoTooltip );

			Event ev = Event.current;

			Rect scrollableListRect;
			if( InspectPlusSettings.Instance.CompactFavoritesAndHistoryLists )
			{
				GUILayout.Label( icon, GUI.skin.button, compactListHeight );
				if( ev.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
					ShowScrollableListContentsAsPopup( lists );

				scrollableListRect = GUILayoutUtility.GetLastRect();
			}
			else
			{
				GUILayoutOption height = drawingFavorites ? favoritesHeight : historyHeight;
				Color backgroundColor = GUI.backgroundColor;
				GUIStyle buttonStyle = GUI.skin.button;

				GUILayout.BeginHorizontal();
				GUILayout.Label( icon, scrollableListIconGuiStyle, scrollableListIconSize, height );
				if( ev.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
					ShowScrollableListContentsAsPopup( lists );

				scrollPosition = GUILayout.BeginScrollView( scrollPosition, height );
				GUILayout.BeginHorizontal();

				float? snapTargetPos = null;

				for( int i = 0, buttonIndex = 0; i < lists.Count; i++ )
				{
					List<Object> list = lists[i];
					for( int j = 0; j < list.Count; j++, buttonIndex++ )
					{
						if( ReferenceEquals( list[j], null ) )
						{
							RemoveObjectFromList( list, j );
							GUIUtility.ExitGUI();
						}

						if( list[j] == mainObject )
							GUI.backgroundColor = activeButtonColor;

						GUIContent buttonContent = EditorGUIUtility.ObjectContent( list[j], list[j].GetType() );
						textSizeCalculator.text = buttonContent.text;
						switch( DraggableButton( buttonContent, buttonStyle, list[j], zeroButtonHeight, expandHeight, GUILayout.Width( buttonStyle.CalcSize( textSizeCalculator ).x + 20f ) ) )
						{
							case ButtonState.LeftClicked:
								pendingInspectTarget = list[j];
								break;
							case ButtonState.MiddleClicked:
								RemoveObjectFromList( list, j );
								GUIUtility.ExitGUI();
								break;
							case ButtonState.RightClicked:
								GenericMenu menu = new GenericMenu();
								int index = j;
								OnScrollViewButtonRightClicked( menu, list, index );
								menu.ShowAsContext();

								break;
						}

						if( ev.type == EventType.Repaint && list[j] == mainObject )
						{
							if( ( snapHistoryToActiveObject && !drawingFavorites ) || ( snapFavoritesToActiveObject && drawingFavorites ) )
								snapTargetPos = buttonIndex > 0 ? GUILayoutUtility.GetLastRect().x : 0f;
						}

						GUI.backgroundColor = backgroundColor;
					}
				}

				GUILayout.EndHorizontal();
				GUILayout.EndScrollView();
				GUILayout.EndHorizontal();

				if( snapTargetPos.HasValue )
				{
					if( drawingFavorites )
						snapFavoritesToActiveObject = false;
					else
						snapHistoryToActiveObject = false;

					shouldRepaint = true;

					scrollPosition.x = snapTargetPos.Value;
					if( scrollPosition.x < 0f )
						scrollPosition.x = 0f;
				}

				scrollableListRect = GUILayoutUtility.GetLastRect();

				if( pendingScrollAmount != 0f && ev.type == EventType.Repaint && scrollableListRect.Contains( ev.mousePosition ) )
				{
					scrollPosition.x += pendingScrollAmount;
					if( scrollPosition.x < 0f )
						scrollPosition.x = 0f;

					shouldRepaint = true;
				}

				// Draw black separator line
				Rect separatorRect = scrollableListRect;
				separatorRect.y = scrollableListRect.yMax;
				separatorRect.height = 1f;
				EditorGUI.DrawRect( separatorRect, Color.black );
			}

			if( ( ev.type == EventType.DragPerform || ev.type == EventType.DragUpdated ) && scrollableListRect.Contains( ev.mousePosition ) )
			{
				// Accept drag&drop
				DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				if( ev.type == EventType.DragPerform )
				{
					DragAndDrop.AcceptDrag();

					Object[] draggedObjects = DragAndDrop.objectReferences;
					if( draggedObjects.Length > 0 )
					{
						Object objectToInspect = null;
						for( int i = 0; i < draggedObjects.Length; i++ )
						{
							if( draggedObjects[i] )
							{
								if( drawingFavorites )
									TryAddObjectToFavorites( draggedObjects[i] );
								else
								{
									TryAddObjectToHistory( draggedObjects[i] );
									objectToInspect = draggedObjects[i];
								}
							}
						}

						if( objectToInspect != null )
							InspectInternal( objectToInspect, true );
					}
				}

				ev.Use();
			}

			return scrollPosition;
		}

		private ButtonState DraggableButton( GUIContent content, GUIStyle style, Object objectValue, params GUILayoutOption[] options )
		{
			ButtonState result = ButtonState.Normal;
			Event ev = Event.current;

			Rect rect = GUILayoutUtility.GetRect( content, style, options );
			int controlID = GUIUtility.GetControlID( FocusType.Passive );
			switch( ev.GetTypeForControl( controlID ) )
			{
				case EventType.MouseDown:
					if( rect.Contains( ev.mousePosition ) )
					{
						GUIUtility.hotControl = controlID;
						buttonPressPosition = ev.mousePosition;
					}

					break;
				case EventType.MouseDrag:
					if( GUIUtility.hotControl == controlID && ev.button == 0 && ( ev.mousePosition - buttonPressPosition ).sqrMagnitude >= BUTTON_DRAG_THRESHOLD_SQR )
					{
						GUIUtility.hotControl = 0;

						if( objectValue )
						{
							// Credit: https://forum.unity.com/threads/editor-draganddrop-bug-system-needs-to-be-initialized-by-unity.219342/#post-1464056
							DragAndDrop.PrepareStartDrag();
							DragAndDrop.objectReferences = new Object[] { objectValue };
							DragAndDrop.StartDrag( "InspectButton" );
						}

						ev.Use();
					}

					break;
				case EventType.MouseUp:
					if( GUIUtility.hotControl == controlID )
					{
						GUIUtility.hotControl = 0;
						shouldRepaint = true;

						if( rect.Contains( ev.mousePosition ) )
							result = (ButtonState) ev.button;
					}
					break;
				case EventType.Repaint:
					style.Draw( rect, content, controlID );
					break;
			}

			if( ev.isMouse && GUIUtility.hotControl == controlID )
				ev.Use();

			return result;
		}

		private void ShowScrollableListContentsAsPopup( List<List<Object>> lists )
		{
			// Clicking the icon while the popup is visible will close the popup and then
			// immediately reopen it for some reason, avoid it
			if( EditorApplication.timeSinceStartup - objectBrowserWindowCloseTime <= 0.05 )
				return;

			List<Object> allObjects = new List<Object>( 8 );
			for( int i = 0; i < lists.Count; i++ )
			{
				List<Object> list = lists[i];
				for( int j = 0; j < list.Count; j++ )
				{
					if( list[j] && !allObjects.Contains( list[j] ) )
						allObjects.Add( list[j] );
				}
			}

			if( allObjects.Count == 0 )
				return;

			HashSet<Object> favoriteObjects = new HashSet<Object>();
			for( int i = 0; i < favoritesHolder.Count; i++ )
				favoriteObjects.UnionWith( favoritesHolder[i] );

			ObjectBrowserWindow window = CreateInstance<ObjectBrowserWindow>();
			ObjectBrowserWindow.SortType sortType = lists == favoritesHolder ? InspectPlusSettings.Instance.FavoritesSortType : InspectPlusSettings.Instance.HistorySortType;
			window.Initialize( allObjects, favoriteObjects, mainObject, sortType, ( Object obj ) =>
			{
				pendingInspectTarget = obj;

				if( lists == favoritesHolder )
					snapFavoritesToActiveObject = true;
				else
					snapHistoryToActiveObject = true;

				return true;
			}, ( Object obj, bool isFavorite ) =>
			{
				if( isFavorite )
					TryAddObjectToFavorites( obj );
				else
				{
					for( int i = 0; i < favoritesHolder.Count; i++ )
					{
						int index = favoritesHolder[i].IndexOf( obj );
						if( index >= 0 )
						{
							RemoveObjectFromList( favoritesHolder[i], index );
							break;
						}
					}
				}
			},
			( ObjectBrowserWindow.SortType sortType2 ) =>
			{
				objectBrowserWindowVisible = false;
				objectBrowserWindowCloseTime = EditorApplication.timeSinceStartup;

				if( lists == favoritesHolder && sortType2 != InspectPlusSettings.Instance.FavoritesSortType )
				{
					InspectPlusSettings.Instance.FavoritesSortType = sortType2;
					EditorUtility.SetDirty( InspectPlusSettings.Instance );
				}
				else if( lists == historyHolder && sortType2 != InspectPlusSettings.Instance.HistorySortType )
				{
					InspectPlusSettings.Instance.HistorySortType = sortType2;
					EditorUtility.SetDirty( InspectPlusSettings.Instance );
				}
			} );

			float windowWidth = position.width;
			Rect scrollableListIconRect = GUILayoutUtility.GetLastRect();
			scrollableListIconRect.x = 0f;
			scrollableListIconRect.width = windowWidth;
			scrollableListIconRect.position = GUIUtility.GUIToScreenPoint( scrollableListIconRect.position );

			if( !InspectPlusSettings.Instance.CompactFavoritesAndHistoryLists )
				scrollableListIconRect.height -= 2f;

			window.ShowAsDropDown( scrollableListIconRect, new Vector2( windowWidth, Mathf.Min( Screen.currentResolution.height * 0.5f, allObjects.Count * ( EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing ) + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing + 5f ) ) );
			objectBrowserWindowVisible = true;
		}

		// Credit: https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/Inspector/InspectorWindow.cs
		private void DrawPreview( Editor editor )
		{
			Event ev = Event.current;

			// Leave blank area between the preview and the inspector, if possible
			GUILayout.FlexibleSpace();

			if( previewHeaderGuiStyle == null )
			{
				previewHeaderGuiStyle = GUI.skin.FindStyle( "preToolbar" );
				previewResizeAreaGuiStyle = GUI.skin.FindStyle( "RL DragHandle" );
				previewBackgroundGuiStyle = GUI.skin.FindStyle( "preBackground" );

				if( previewHeaderGuiStyle == null )
					previewHeaderGuiStyle = EditorStyles.toolbar;
				if( previewResizeAreaGuiStyle == null )
				{
#if UNITY_2019_3_OR_NEWER
					previewResizeAreaGuiStyle = GUI.skin.horizontalScrollbarThumb;
#else
					previewResizeAreaGuiStyle = EditorStyles.helpBox;
#endif
				}
				if( previewBackgroundGuiStyle == null )
					previewBackgroundGuiStyle = EditorStyles.toolbar;
			}

			Rect headerRect = EditorGUILayout.BeginHorizontal( previewHeaderGuiStyle, previewHeaderHeight );

			// This flexible space is the preview resize area
			GUILayout.FlexibleSpace();
			Rect dragRect = GUILayoutUtility.GetLastRect();
			dragRect.height = PREVIEW_HEADER_HEIGHT;

			if( ev.type == EventType.Repaint )
			{
				Rect dragIconRect = new Rect( dragRect.x + PREVIEW_HEADER_PADDING, dragRect.y + ( PREVIEW_HEADER_HEIGHT - previewResizeAreaGuiStyle.fixedHeight ) * 0.5f - 1f,
					dragRect.width - 2f * PREVIEW_HEADER_PADDING, previewResizeAreaGuiStyle.fixedHeight );
#if UNITY_2019_3_OR_NEWER
				dragIconRect.y++;
#endif

				previewResizeAreaGuiStyle.Draw( dragIconRect, GUIContent.none, false, false, false, false );
			}

			if( previewHeight >= PREVIEW_MIN_HEIGHT )
				editor.OnPreviewSettings();

			EditorGUILayout.EndHorizontal();

			// Draw preview resize area
			// Credit: https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/GUI/PreviewResizer.cs
			int controlID = 1453541; // Magic unique(ish) number
			switch( ev.GetTypeForControl( controlID ) )
			{
				case EventType.MouseDown:
					if( dragRect.Contains( ev.mousePosition ) )
					{
						GUIUtility.hotControl = controlID;
						previewHeaderClicked = true;

						ev.Use();
					}

					break;
				case EventType.MouseDrag:
					if( GUIUtility.hotControl == controlID )
					{
						previewHeight = position.yMax - GUIUtility.GUIToScreenPoint( ev.mousePosition ).y;
						if( previewHeight < PREVIEW_MIN_HEIGHT )
							previewHeight = previewHeight < PREVIEW_COLLAPSE_HEIGHT ? 0f : PREVIEW_MIN_HEIGHT;

						previewHeaderClicked = false;
						ev.Use();
					}

					break;
				case EventType.MouseUp:
					if( GUIUtility.hotControl == controlID )
					{
						GUIUtility.hotControl = 0;
						if( previewHeaderClicked )
						{
							if( previewHeight >= PREVIEW_MIN_HEIGHT )
							{
								previewLastHeight = previewHeight;
								previewHeight = 0f;
							}
							else
								previewHeight = previewLastHeight;
						}

						EditorPrefs.SetFloat( PREVIEW_HEIGHT_PREF, previewHeight );
						ev.Use();
					}

					break;
				case EventType.Repaint:
					if( GUIUtility.hotControl == controlID )
					{
						dragRect.y -= 1000f;
						dragRect.height += 2000f;
						dragRect.width = position.width;
					}

					EditorGUIUtility.AddCursorRect( dragRect, MouseCursor.SplitResizeUpDown );
					break;
			}

			if( previewHeight >= PREVIEW_MIN_HEIGHT )
			{
				float maxHeight = position.height - 200f;
				if( previewHeight > maxHeight )
					previewHeight = maxHeight;

				// +0.5 pixel compensates a 1-pixel space that sometimes shows up between the preview header and the preview itself
				Rect previewPosition = GUILayoutUtility.GetRect( headerRect.width, previewHeight );
				previewPosition.yMin -= 0.5f;

				if( ev.type == EventType.Repaint )
					previewBackgroundGuiStyle.Draw( previewPosition, false, false, false, false );

				editor.DrawPreview( previewPosition );
			}
		}

		private void AdjustLabelWidth( float windowWidth )
		{
			EditorGUIUtility.wideMode = windowWidth > 330f;
			EditorGUIUtility.labelWidth = windowWidth < 350f ? 130f : windowWidth * 0.4f;
		}

		private void DrawHorizontalLine()
		{
			GUILayout.Box( "", expandWidth, horizontalLineHeight );
		}
		#endregion

		#region Helper Functions
		private bool TryAddObjectToHistory( Object obj )
		{
			for( int i = 0; i < history.Count; i++ )
			{
				if( history[i] == obj )
					return false;
			}

			history.Add( obj );
			return true;
		}

		private bool TryAddObjectToFavorites( Object obj )
		{
			SceneFavoritesHolder sceneFavoritesHolder = null;

			if( AssetDatabase.Contains( obj ) )
				sceneFavoritesHolder = null;
			else
			{
				Scene scene;
				if( obj is Component )
					scene = ( (Component) obj ).gameObject.scene;
				else if( obj is GameObject )
					scene = ( (GameObject) obj ).scene;
				else
					scene = new Scene();

				if( scene.IsValid() && scene.isLoaded )
				{
					for( int i = 0; i < SceneFavoritesHolder.Instances.Count; i++ )
					{
						if( SceneFavoritesHolder.Instances[i].gameObject.scene == scene )
						{
							sceneFavoritesHolder = SceneFavoritesHolder.Instances[i];
							break;
						}
					}

					if( !sceneFavoritesHolder )
						sceneFavoritesHolder = SceneFavoritesHolder.GetInstance( scene );
				}
			}

			if( sceneFavoritesHolder )
			{
				List<Object> favorites = sceneFavoritesHolder.FavoriteObjects;
				for( int i = 0; i < favorites.Count; i++ )
				{
					if( favorites[i] == obj )
						return false;
				}

				favorites.Add( obj );
				showFavorites = true;
				sceneFavoritesHolder.SetSceneDirty();
			}
			else
			{
				List<Object> favorites = InspectPlusSettings.Instance.FavoriteAssets;
				for( int i = 0; i < favorites.Count; i++ )
				{
					if( favorites[i] == obj )
						return false;
				}

				favorites.Add( obj );
				showFavorites = true;
				EditorUtility.SetDirty( InspectPlusSettings.Instance );
			}

			return true;
		}

		private void RemoveObjectFromList( List<Object> list, int index )
		{
			list.RemoveAt( index );

			if( list == history )
			{
				// If currently inspected object was opened in multiple tabs, do nothing
				for( int i = 0; i < list.Count; i++ )
				{
					if( list[i] == mainObject )
						return;
				}

				// Inspect the previous tab
				if( list.Count > 0 )
				{
					if( index >= list.Count )
						index = list.Count - 1;

					InspectInternal( list[index], false );
				}
			}
			else
			{
				bool isSceneFavoriteList = false;
				for( int i = 0; i < SceneFavoritesHolder.Instances.Count; i++ )
				{
					if( SceneFavoritesHolder.Instances[i].FavoriteObjects == list )
					{
						isSceneFavoriteList = true;
						SceneFavoritesHolder.Instances[i].SetSceneDirty();

						break;
					}
				}

				if( !isSceneFavoriteList )
					EditorUtility.SetDirty( InspectPlusSettings.Instance );
			}
		}

		private void CreateEditorFor( Object obj )
		{
			if( inspectorDrawerCount < inspectorDrawers.Count )
			{
				Editor editor = inspectorDrawers[inspectorDrawerCount];
				if( editor && editor.target == obj )
					inspectorDrawerCount++;
				else
				{
					if( editor && editor.target && editor.target.GetType() == obj.GetType() )
						Editor.CreateCachedEditor( obj, null, ref editor );
					else
					{
						DestroyImmediate( editor );
						editor = Editor.CreateEditor( obj );
					}

					if( editor )
						inspectorDrawers[inspectorDrawerCount++] = editor;
				}
			}
			else
			{
				Editor editor = Editor.CreateEditor( obj );
				if( editor )
				{
					inspectorDrawers.Add( editor );
					inspectorDrawerCount++;
				}
			}

			string name;
			if( obj is Component )
				name = obj.GetType().Name + " Component";
			else if( obj is GameObject )
				name = obj.name + " GameObject";
			else
				name = obj.name;

			VariableGetterHolder variableGetter = new VariableGetterHolder( name, new ConstantValueGetter( obj ).GetValue );

			if( debugModeDrawerCount < debugModeDrawers.Count )
				debugModeDrawers[debugModeDrawerCount++].Getter = variableGetter;
			else
			{
				debugModeDrawers.Add( new DebugModeEntry( null ) { Getter = variableGetter } );
				debugModeDrawerCount++;
			}
		}

#if UNITY_2017_1_OR_NEWER
		private void SetInspectorAssetDrawer( AssetImporterEditor assetDrawer )
#else
		private void SetInspectorAssetDrawer( Editor assetDrawer )
#endif
		{
			if( inspectorAssetDrawer == assetDrawer )
				return;

			if( inspectorAssetDrawer )
			{
#if UNITY_2019_2_OR_NEWER
				// On newer Unity versions, unfortunately the Apply/Revert dialog isn't displayed automatically when we stop inspecting an asset in Inspect+,
				// so we must show the Apply/Revert dialog manually and as long as user presses Cancel, continue showing the dialog
				bool applyRevertFinished;
				do
				{
					applyRevertFinished = (bool) showApplyRevertDialogMethod.Invoke( inspectorAssetDrawer, new object[] { true } );
				}
				while( !applyRevertFinished );
#endif

				DestroyImmediate( inspectorAssetDrawer );
			}

			inspectorAssetDrawer = assetDrawer;
		}

		private static void CopyValue( object obj )
		{
			PasteBinWindow.AddToClipboard( (SerializedProperty) obj );
		}

		private static void PasteValue( object obj )
		{
			if( PasteBinWindow.ActiveClipboard != null )
				( (SerializedProperty) obj ).PasteValue( PasteBinWindow.ActiveClipboard.RootValue );
		}

		private static void PasteValueFromBin( object obj )
		{
			if( obj is SerializedProperty || obj is Object[] )
			{
				PasteBinContextWindow window = CreateInstance<PasteBinContextWindow>();
				if( obj is SerializedProperty )
					window.Initialize( (SerializedProperty) obj );
				else
					window.Initialize( (Object[]) obj );

				window.position = new Rect( new Vector2( -9999f, -9999f ), new Vector2( window.PreferredWidth, 9999f ) );
				window.ShowPopup();
			}
			else
				Debug.LogError( "Passed parameter is neither a SerializedProperty nor an Object." );
		}

		private static Object PreferablyGameObject( Object obj )
		{
			if( !obj )
				return null;

			if( obj is Component )
				return ( (Component) obj ).gameObject;

			return obj;
		}

		private static Object[] PreferablyGameObject( Object[] objs )
		{
			for( int i = 0; i < objs.Length; i++ )
				objs[i] = PreferablyGameObject( objs[i] );

			return objs;
		}

		private bool ShouldShowFoldoutFor( Object obj )
		{
			Type type = obj.GetType();

			bool cachedResult;
			if( componentsExpandableStates.TryGetValue( type, out cachedResult ) )
				return cachedResult;

			SerializedProperty iterator = new SerializedObject( obj ).GetIterator();
			if( iterator.NextVisible( true ) )
			{
				do
				{
					if( EditorGUI.GetPropertyHeight( iterator, (GUIContent) null, true ) > 0f )
					{
						componentsExpandableStates[type] = true;
						return true;
					}
				}
				while( iterator.NextVisible( false ) );
			}

			componentsExpandableStates[type] = false;
			return false;
		}
		#endregion
	}
}