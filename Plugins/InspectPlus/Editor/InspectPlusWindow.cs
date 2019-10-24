#if UNITY_2019_3_OR_NEWER
#define APPLY_HORIZONTAL_PADDING // Inspector looks better with a padding in the new UI
#endif

using InspectPlusNamespace.Extras;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace InspectPlusNamespace
{
	public class InspectPlusWindow : EditorWindow, IHasCustomMenu
	{
		private enum ButtonState { Normal = -1, LeftClicked = 0, RightClicked = 1, MiddleClicked = 2 };

		private const string NEW_TAB_LABEL = "Open In New Tab";
		private const string NEW_WINDOW_LABEL = "Open In New Window";
		private const float BUTTON_DRAG_THRESHOLD_SQR = 600f;
		private const float HORIZONTAL_SCROLL_SPEED = 5f;
		private const float SCROLLABLE_LIST_ICON_WIDTH = 34f;
		private const float FAVORITES_REFRESH_INTERVAL = 0.5f;
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

		private static readonly Color activeButtonColor = new Color32( 245, 170, 10, 255 );

		private static readonly List<Component> components = new List<Component>( 16 );
		private static readonly Dictionary<Type, bool> componentsExpandableStates = new Dictionary<Type, bool>( 128 );
		private static readonly GUIStyle scrollableListIconGuiStyle = new GUIStyle();
		private static readonly GUIContent textSizeCalculator = new GUIContent();

		private static object clipboard;

		private static InspectPlusWindow mainWindow;
		private static GUIContent favoritesIcon, historyIcon;
		private static Rect lastWindowPosition;

		// These are not readonly to support serialization of the data
		// SerializeField makes history data persist between editor sessions
		[SerializeField]
		private List<Object> history = new List<Object>( 8 );
		private List<Editor> inspectorDrawers = new List<Editor>( 16 );
		private int inspectorDrawerCount;
		private Object mainObject;

		private bool shouldRepositionSelf;
		private bool shouldRepaint;
		private bool snapHistoryToActiveObject;
		private Object pendingInspectTarget;
		private float pendingScrollAmount;
		private Vector2 buttonPressPosition;
		private double nextUpdateTime;

		private bool showFavorites, showHistory, showPreview;
		private Vector2 favoritesScrollPosition, historyScrollPosition, inspectorScrollPosition;

		private bool debugMode;
		private double debugModeRefreshTime;
		private double favoritesRefreshTime;

		private readonly List<DebugModeEntry> debugModeDrawers = new List<DebugModeEntry>( 16 );
		private readonly List<List<Object>> historyHolder = new List<List<Object>>( 1 );
		private readonly List<List<Object>> favoritesHolder = new List<List<Object>>( 4 );

		private GUILayoutOption favoritesHeight;
		private GUILayoutOption historyHeight;
		private GUILayoutOption previewHeight;
		private GUILayoutOption scrollableListIconSize;

		#region Initializers
		//[MenuItem( "Window/Inspector+" )]
		//private static void Init()
		//{
		//	GetDefaultWindow();
		//}

		[InitializeOnLoadMethod]
		private static void InitializeExtensions()
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

			favoritesIcon = new GUIContent( EditorGUIUtility.Load( "Favorite Icon" ) as Texture, "Favorites" );
			historyIcon = new GUIContent( EditorGUIUtility.Load( "Search Icon" ) as Texture, "History" );

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
			showPreview = InspectPlusSettings.Instance.ShowPreviewByDefault;

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

			// Make sure that debug mode drawers are recreated
			if( inspectorDrawerCount > 0 )
			{
				if( inspectorDrawers[0].target )
					InspectInternal( inspectorDrawers[0].target, false );
				else
				{
					for( int i = 0; i < inspectorDrawers.Count; i++ )
						DestroyImmediate( inspectorDrawers[i] );

					inspectorDrawers.Clear();
					inspectorDrawerCount = 0;
				}
			}

			historyHolder.Add( history );
			favoritesHolder.Add( InspectPlusSettings.Instance.FavoriteAssets );

			RefreshSettings();
		}

		private void OnDisable()
		{
			windows.Remove( this );

			Undo.undoRedoPerformed -= OnUndoRedo;

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

			mainObject = null;
		}

		private void OnFocus()
		{
			mainWindow = this;
		}

		private void OnUndoRedo()
		{
			shouldRepaint = true;
		}

		private void RefreshSettings()
		{
			favoritesHeight = GUILayout.Height( InspectPlusSettings.Instance.FavoritesHeight );
			historyHeight = GUILayout.Height( InspectPlusSettings.Instance.HistoryHeight );
			previewHeight = GUILayout.Height( InspectPlusSettings.Instance.PreviewHeight );
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
		private static void ContextMenuItemNewTab( MenuCommand command )
		{
			Inspect( command.context, false );
		}

		[MenuItem( "CONTEXT/Component/" + NEW_WINDOW_LABEL, priority = 1500 )]
		private static void ContextMenuItemNewWindow( MenuCommand command )
		{
			Inspect( command.context, true );
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
					AddInspectButtonToMenu( menu, targets[i], "/" + targets[i].name );

				menu.AddSeparator( "" );
			}
			else if( obj )
			{
				AddInspectButtonToMenu( menu, obj, "" );
				menu.AddSeparator( "" );
			}

			if( !property.hasMultipleDifferentValues && ( !isUnityObjectType || obj ) )
				menu.AddItem( new GUIContent( "Copy Value" ), false, CopyValue, property.Copy() );
			else
				menu.AddDisabledItem( new GUIContent( "Copy Value" ) );

			if( !property.CanPasteValue( clipboard ) )
				menu.AddDisabledItem( new GUIContent( "Paste Value" ) );
			else
				menu.AddItem( new GUIContent( "Paste Value" ), false, PasteValue, property.Copy() );
		}

		public static void OnObjectRightClicked( GenericMenu menu, Object obj )
		{
			AddInspectButtonToMenu( menu, obj, "" );
		}

		void IHasCustomMenu.AddItemsToMenu( GenericMenu menu )
		{
			menu.AddItem( new GUIContent( "Show Favorites" ), showFavorites, () => showFavorites = !showFavorites );
			menu.AddItem( new GUIContent( "Show History" ), showHistory, () => showHistory = !showHistory );
			menu.AddItem( new GUIContent( "Show Preview" ), showPreview, () => showPreview = !showPreview );
			menu.AddSeparator( "" );

			menu.AddItem( new GUIContent( "Debug Mode" ), debugMode, () => debugMode = !debugMode );
			menu.AddSeparator( "" );

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

			inspectorDrawerCount = 0;
			debugModeRefreshTime = 0f;

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

			for( int i = inspectorDrawers.Count - 1; i >= inspectorDrawerCount; i-- )
			{
				DestroyImmediate( inspectorDrawers[i] );
				inspectorDrawers.RemoveAt( i );
			}


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
				for( int i = 0; i < inspectorDrawerCount; i++ )
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

		#region GUI Functions
		private void OnGUI()
		{
			mainObject = inspectorDrawerCount > 0 ? inspectorDrawers[0].target : null;

			Event ev = Event.current;
			if( ev.type == EventType.ScrollWheel )
				pendingScrollAmount = ev.delta.y * HORIZONTAL_SCROLL_SPEED;

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

			if( !mainObject )
				return;

			if( ev.type == EventType.MouseEnterWindow )
			{
				lastWindowPosition = position;
				shouldRepaint = true;
			}

			inspectorScrollPosition = EditorGUILayout.BeginScrollView( inspectorScrollPosition );

#if APPLY_HORIZONTAL_PADDING
			GUILayout.BeginHorizontal();
			GUILayout.Space( INSPECTOR_HORIZONTAL_PADDING );
			GUILayout.BeginVertical();
#endif

			if( debugMode )
			{
				GUILayout.Box( "(F)ield, (P)roperty, (S)tatic, (O)bsolete\n(+)Public, (#)Protected/Internal, (-)Private", expandWidth );

				for( int i = 0; i < inspectorDrawerCount; i++ )
					debugModeDrawers[i].DrawOnGUI();
			}
			else
			{
				Editor firstDrawer = inspectorDrawers[0];
				firstDrawer.DrawHeader();
				firstDrawer.OnInspectorGUI();

				for( int i = 1; i < inspectorDrawerCount; i++ )
				{
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

				if( showPreview )
				{
					for( int i = 0; i < inspectorDrawerCount; i++ )
					{
						if( inspectorDrawers[i].HasPreviewGUI() )
						{
							inspectorDrawers[i].DrawPreview( EditorGUILayout.GetControlRect( expandWidth, previewHeight ) );
							break;
						}
					}
				}
			}

#if APPLY_HORIZONTAL_PADDING
			GUILayout.EndVertical();
			GUILayout.Space( INSPECTOR_HORIZONTAL_PADDING );
			GUILayout.EndHorizontal();
#endif

			EditorGUILayout.EndScrollView();
		}

		private Vector2 DrawScrollableList( bool drawingFavorites )
		{
			Vector2 scrollPosition = drawingFavorites ? favoritesScrollPosition : historyScrollPosition;
			List<List<Object>> lists = drawingFavorites ? favoritesHolder : historyHolder;
			GUIContent icon = drawingFavorites ? favoritesIcon : historyIcon;
			GUILayoutOption height = drawingFavorites ? favoritesHeight : historyHeight;

			Color backgroundColor = GUI.backgroundColor;
			GUIStyle buttonStyle = GUI.skin.button;
			Event ev = Event.current;

			GUILayout.BeginHorizontal();
			GUILayout.Label( icon, scrollableListIconGuiStyle, scrollableListIconSize, height );
			scrollPosition = GUILayout.BeginScrollView( scrollPosition, height );
			GUILayout.BeginHorizontal();

			float? snapTargetPos = null;

			for( int i = 0; i < lists.Count; i++ )
			{
				List<Object> list = lists[i];
				for( int j = 0; j < list.Count; j++ )
				{
					if( list[j] == mainObject )
					{
						GUI.backgroundColor = activeButtonColor;

						if( snapHistoryToActiveObject && !drawingFavorites && ev.type == EventType.Repaint )
							snapTargetPos = j > 0 ? GUILayoutUtility.GetLastRect().xMax : 0f;
					}

					if( ReferenceEquals( list[j], null ) )
					{
						RemoveObjectFromList( list, j );
						GUIUtility.ExitGUI();
					}

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

					GUI.backgroundColor = backgroundColor;
				}
			}

			GUILayout.EndHorizontal();
			GUILayout.EndScrollView();
			GUILayout.EndHorizontal();

			if( snapTargetPos.HasValue )
			{
				snapHistoryToActiveObject = false;
				shouldRepaint = true;

				scrollPosition.x = snapTargetPos.Value;
				if( scrollPosition.x < 0f )
					scrollPosition.x = 0f;
			}

			if( ( ev.type == EventType.DragPerform || ev.type == EventType.DragUpdated || ( pendingScrollAmount != 0f && ev.type == EventType.Repaint ) ) && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
			{
				if( pendingScrollAmount != 0f )
				{
					scrollPosition.x += pendingScrollAmount;
					if( scrollPosition.x < 0f )
						scrollPosition.x = 0f;

					shouldRepaint = true;
				}
				else
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
			}

			DrawHorizontalLine();
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
						result = (ButtonState) ev.button;
						shouldRepaint = true;
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

			if( inspectorDrawerCount - 1 < debugModeDrawers.Count )
				debugModeDrawers[inspectorDrawerCount - 1].Getter = variableGetter;
			else
				debugModeDrawers.Add( new DebugModeEntry( null ) { Getter = variableGetter } );
		}

		private static void CopyValue( object obj )
		{
			clipboard = ( (SerializedProperty) obj ).CopyValue();
		}

		private static void PasteValue( object obj )
		{
			( (SerializedProperty) obj ).PasteValue( clipboard );
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