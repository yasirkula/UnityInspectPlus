using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using SerializedComponent = InspectPlusNamespace.SerializedClipboard.IPComponentGroup;
using ComponentGroupClipboard = InspectPlusNamespace.SerializablePropertyExtensions.ComponentGroupClipboard;

namespace InspectPlusNamespace
{
	// This class is mostly a stripped version of PasteBinContextWindow
	public class ComponentGroupCopyPasteWindow : EditorWindow
	{
		private readonly GUIContent smartPasteOnButtonLabel = new GUIContent( "Smart Paste ON", PasteBinContextWindow.SMART_PASTE_TOOLTIP );
		private readonly GUIContent smartPasteOffButtonLabel = new GUIContent( "Smart Paste OFF", PasteBinContextWindow.SMART_PASTE_TOOLTIP );

		private Component[] targetComponents;

		private SerializedComponent targetSerializedComponentGroup;
		private SerializedComponent.ComponentInfo[] targetSerializedComponents;
		private Object[] targetGameObjectsToPasteTo;
		private bool[] componentSelectedStates;

		private GUIStyle backgroundStyle;
		private bool shouldRepositionSelf = true;

		private int hoveredComponentIndex = -1;
		private Vector2? prevMousePos;

		private readonly GUIContent componentGUIContent = new GUIContent();
		private readonly GUIContent cutGUIContent = new GUIContent( "Cut", "Selected components will be copied and then destroyed" );

		private Vector2 scrollPosition;

		private float PreferredWidth
		{
			get
			{
				if( targetComponents != null )
					return 400f;

				if( targetSerializedComponents == null || targetSerializedComponents.Length == 0 )
					return 250f;

				float width = 100f;
				for( int i = 0; i < targetSerializedComponents.Length; i++ )
				{
					float _width = EditorStyles.boldLabel.CalcSize( new GUIContent( targetSerializedComponents[i].Component.Label ) ).x + 50f;
					if( _width > width )
						width = _width;
				}

				// When width is smaller than ~350, horizontal scrollbar will show up
				return Mathf.Max( width, 350f );
			}
		}

		public void Initialize( Component[] components )
		{
			targetComponents = components;
			targetSerializedComponentGroup = null;
			targetSerializedComponents = null;
			targetGameObjectsToPasteTo = null;
			componentSelectedStates = new bool[targetComponents.Length];

			OnInitialize();
		}

		public void Initialize( SerializedComponent serializedComponentGroup, Object[] pasteTargets )
		{
			targetComponents = null;
			targetSerializedComponentGroup = serializedComponentGroup;
			targetSerializedComponents = SerializedComponent.SelectComponentsThatExistInProject( serializedComponentGroup.Components ).ToArray();
			targetGameObjectsToPasteTo = pasteTargets;
			componentSelectedStates = new bool[targetSerializedComponents.Length];

			OnInitialize();
		}

		private void OnInitialize()
		{
			for( int i = 0; i < componentSelectedStates.Length; i++ )
				componentSelectedStates[i] = true;

			position = new Rect( new Vector2( -9999f, -9999f ), new Vector2( PreferredWidth, 9999f ) );
			ShowPopup();
			Focus();
		}

		private void OnEnable()
		{
			wantsMouseMove = wantsMouseEnterLeaveWindow = true;
#if UNITY_2020_1_OR_NEWER
			wantsLessLayoutEvents = false;
#endif

			EditorApplication.update -= CheckWindowFocusRegularly;
			EditorApplication.update += CheckWindowFocusRegularly;
		}

		private void OnDisable()
		{
			EditorApplication.update -= CheckWindowFocusRegularly;
			PasteBinTooltip.Hide();
		}

		private void CheckWindowFocusRegularly()
		{
			// Happens in rare cases
			if( !this )
				EditorApplication.update -= CheckWindowFocusRegularly;
			else if( focusedWindow != this || EditorApplication.isCompiling )
				Close();
		}

		private void OnGUI()
		{
			if( backgroundStyle == null )
				backgroundStyle = new GUIStyle( PasteBinTooltip.Style ) { margin = new RectOffset( 0, 0, 0, 0 ), padding = new RectOffset( 0, 0, 0, 0 ) };

			Event ev = Event.current;

			Color backgroundColor = GUI.backgroundColor;
			GUI.backgroundColor = Color.Lerp( backgroundColor, new Color( 0.5f, 0.5f, 0.5f, 1f ), 0.325f );

			GUILayout.BeginVertical( backgroundStyle );

			GUI.backgroundColor = backgroundColor;

			scrollPosition = EditorGUILayout.BeginScrollView( scrollPosition );

			GUILayout.BeginVertical();

			if( targetComponents != null )
				GUILayout.Label( "Select components to copy:", EditorStyles.boldLabel );
			else
			{
				GUILayout.BeginHorizontal();
				GUILayout.Label( "Select components to paste:", EditorStyles.boldLabel );

				if( targetSerializedComponents != null && targetSerializedComponents.Length > 0 )
				{
					EditorGUI.BeginChangeCheck();
					InspectPlusSettings.Instance.SmartCopyPaste = GUILayout.Toggle( InspectPlusSettings.Instance.SmartCopyPaste, InspectPlusSettings.Instance.SmartCopyPaste ? smartPasteOnButtonLabel : smartPasteOffButtonLabel, GUI.skin.button );
					if( EditorGUI.EndChangeCheck() )
						InspectPlusSettings.Instance.Save();
				}

				GUILayout.EndHorizontal();
			}

			if( componentSelectedStates != null && componentSelectedStates.Length > 0 )
			{
				bool allComponentsSelected = componentSelectedStates[0];
				for( int i = 1; i < componentSelectedStates.Length; i++ )
				{
					if( componentSelectedStates[i] != allComponentsSelected )
					{
						allComponentsSelected = true;
						EditorGUI.showMixedValue = true;

						break;
					}
				}

				EditorGUI.BeginChangeCheck();
				allComponentsSelected = EditorGUILayout.ToggleLeft( "All", allComponentsSelected, EditorStyles.boldLabel );
				if( EditorGUI.EndChangeCheck() )
				{
					for( int i = 0; i < componentSelectedStates.Length; i++ )
						componentSelectedStates[i] = allComponentsSelected;
				}

				EditorGUI.showMixedValue = false;
			}

			if( targetComponents != null )
			{
				for( int i = 0; i < targetComponents.Length; i++ )
				{
					componentGUIContent.text = ObjectNames.GetInspectorTitle( targetComponents[i] );
					componentGUIContent.image = AssetPreview.GetMiniThumbnail( targetComponents[i] );
					if( !componentGUIContent.image )
						componentGUIContent.image = EditorGUIUtility.IconContent( "cs Script Icon" ).image;

					componentSelectedStates[i] = EditorGUILayout.ToggleLeft( componentGUIContent, componentSelectedStates[i] );
				}

				EditorGUILayout.Space();

				GUI.enabled = System.Array.IndexOf( componentSelectedStates, true ) >= 0;

				if( GUILayout.Button( "Copy", GUILayout.Height( EditorGUIUtility.singleLineHeight * 1.5f ) ) )
					CopySelectedComponents( false );

				GUI.backgroundColor = Color.yellow;
				if( GUILayout.Button( cutGUIContent ) )
					CopySelectedComponents( true );
				GUI.backgroundColor = backgroundColor;

				GUI.enabled = true;
			}
			else
			{
				if( targetSerializedComponents == null || targetSerializedComponents.Length == 0 )
					GUILayout.Label( "Nothing to paste here..." );
				else
				{
					int hoveredComponentIndex = -1;

					for( int i = 0; i < targetSerializedComponents.Length; i++ )
					{
						componentGUIContent.text = targetSerializedComponents[i].Component.RootUnityObjectType.Name;
						componentGUIContent.image = AssetPreview.GetMiniTypeThumbnail( targetSerializedComponents[i].Component.RootUnityObjectType.Type );
						if( !componentGUIContent.image )
							componentGUIContent.image = EditorGUIUtility.IconContent( "cs Script Icon" ).image;

						componentSelectedStates[i] = EditorGUILayout.ToggleLeft( componentGUIContent, componentSelectedStates[i] );

						if( hoveredComponentIndex < 0 && ( ev.type == EventType.MouseDown || ev.type == EventType.MouseMove ) && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
							hoveredComponentIndex = i;
					}

					if( ev.type == EventType.MouseMove && this.hoveredComponentIndex != hoveredComponentIndex )
						OnHoveredComponentChanged( hoveredComponentIndex );
				}

				EditorGUILayout.Space();

				GUI.enabled = System.Array.IndexOf( componentSelectedStates, true ) >= 0;

				if( GUILayout.Button( "Paste", GUILayout.Height( EditorGUIUtility.singleLineHeight * 1.5f ) ) )
					PasteSelectedComponents();

				GUI.enabled = true;
			}

			GUILayout.EndVertical();

			if( shouldRepositionSelf )
			{
				float preferredHeight = GUILayoutUtility.GetLastRect().height;
				if( preferredHeight > 10f )
				{
					Vector2 size = new Vector2( position.width, preferredHeight + 15f );
					position = Utilities.GetScreenFittedRect( new Rect( GUIUtility.GUIToScreenPoint( ev.mousePosition ) - size * 0.5f, size ) );

					shouldRepositionSelf = false;
					GUIUtility.ExitGUI();
				}
			}

			EditorGUILayout.EndScrollView();
			GUILayout.EndVertical();

			// Make the window draggable
			if( ev.type == EventType.MouseDown )
				prevMousePos = GUIUtility.GUIToScreenPoint( ev.mousePosition );
			else if( ev.type == EventType.MouseDrag && prevMousePos.HasValue )
			{
				Vector2 mousePos = GUIUtility.GUIToScreenPoint( ev.mousePosition );
				Rect _position = position;
				_position.position += mousePos - prevMousePos.Value;
				position = _position;

				prevMousePos = mousePos;
				ev.Use();
			}
			else if( ev.type == EventType.MouseUp )
				prevMousePos = null;
			else if( ev.type == EventType.MouseLeaveWindow )
				PasteBinTooltip.Hide();
		}

		private void OnHoveredComponentChanged( int hoveredComponentIndex )
		{
			this.hoveredComponentIndex = hoveredComponentIndex;
			if( hoveredComponentIndex < 0 || !targetSerializedComponents[hoveredComponentIndex].Component.HasTooltip )
				PasteBinTooltip.Hide();
			else
				PasteBinTooltip.Show( position, targetSerializedComponents[hoveredComponentIndex].Component.LabelContent.tooltip );

			Repaint();
		}

		private void CopySelectedComponents( bool destroyComponentsAfterwards )
		{
			List<Component> selectedComponents = new List<Component>( targetComponents.Length );
			for( int i = 0; i < targetComponents.Length; i++ )
			{
				if( componentSelectedStates[i] )
					selectedComponents.Add( targetComponents[i] );
			}

			string label = Utilities.GetDetailedObjectName( selectedComponents[0] );
			if( selectedComponents.Count > 1 )
				label += " (and " + ( selectedComponents.Count - 1 ) + " more)";

			PasteBinWindow.AddToClipboard( new ComponentGroupClipboard( selectedComponents.ToArray() ), selectedComponents[0].name, label + " (Multiple Components)", null );

			if( destroyComponentsAfterwards )
			{
				bool someComponentsAreDeleted;
				do
				{
					Component[] allComponents = selectedComponents[0].GetComponents<Component>();
					someComponentsAreDeleted = false;

					for( int i = selectedComponents.Count - 1; i >= 0; i-- )
					{
						if( !IsComponentRequiredByOthers( selectedComponents[i], allComponents ) )
						{
							Undo.DestroyObjectImmediate( selectedComponents[i] );
							selectedComponents.RemoveAt( i );

							someComponentsAreDeleted = true;
						}
					}
				} while( someComponentsAreDeleted && selectedComponents.Count > 0 );
			}

			Close();
		}

		private void PasteSelectedComponents()
		{
			List<SerializedComponent.ComponentInfo> selectedComponents = new List<SerializedComponent.ComponentInfo>( targetSerializedComponents.Length );
			for( int i = 0; i < targetSerializedComponents.Length; i++ )
			{
				if( componentSelectedStates[i] )
					selectedComponents.Add( targetSerializedComponents[i] );
			}

			for( int i = 0; i < targetGameObjectsToPasteTo.Length; i++ )
				targetSerializedComponentGroup.PasteComponents( (GameObject) targetGameObjectsToPasteTo[i], selectedComponents.ToArray() );

			Close();
		}

		private bool IsComponentRequiredByOthers( Component component, Component[] allComponents )
		{
			if( component is Transform )
				return true;

			System.Type componentType = component.GetType();
			foreach( Component otherComponent in allComponents )
			{
				if( otherComponent && otherComponent != component )
				{
					foreach( RequireComponent requireComponent in otherComponent.GetType().GetCustomAttributes( typeof( RequireComponent ), true ) )
					{
						if( requireComponent.m_Type0 == componentType || requireComponent.m_Type1 == componentType || requireComponent.m_Type2 == componentType )
							return true;
					}
				}
			}

			return false;
		}
	}
}