using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InspectPlusNamespace
{
	public class PasteBinContextWindow : EditorWindow
	{
		public enum PasteType { Normal, ComponentAsNew, CompleteGameObject, ComponentGroup, AssetFiles };

		internal const string SMART_PASTE_TOOLTIP = "Imagine objects A and B having children named C. When Smart Paste is enabled and A is pasted to B, if A.someVariable points to A.C, B.someVariable will point to B.C instead of A.C";

		private readonly GUIContent smartPasteOnButtonLabel = new GUIContent( "Smart Paste ON", SMART_PASTE_TOOLTIP );
		private readonly GUIContent smartPasteOffButtonLabel = new GUIContent( "Smart Paste OFF", SMART_PASTE_TOOLTIP );

		private readonly List<SerializedClipboard> clipboard = new List<SerializedClipboard>( 4 );
		private readonly List<object> clipboardValues = new List<object>( 4 );

		private SerializedProperty targetProperty;
		private Object[] targetObjects;

		private PasteType pasteType;

		private GUIStyle backgroundStyle;
		private bool shouldRepositionSelf = true;
		private bool shouldResizeSelf = false;
		private bool shouldShowSmartPasteButton = false;

		private int hoveredClipboardIndex = -1;
		private Vector2? prevMousePos;

		private Vector2 scrollPosition;

		private float PreferredWidth
		{
			get
			{
				if( clipboard.Count == 0 )
					return 250f;

				float width = 100f;
				for( int i = 0; i < clipboard.Count; i++ )
				{
					float _width = EditorStyles.boldLabel.CalcSize( new GUIContent( clipboard[i].Label ) ).x + 50f;
					if( _width > width )
						width = _width;
				}

				// When width is smaller than ~250, horizontal scrollbar will show up
				return Mathf.Max( width, 300f );
			}
		}

		public void Initialize( SerializedProperty property )
		{
			targetProperty = property;
			targetObjects = null;
			pasteType = PasteType.Normal;

			Object context = property.serializedObject.targetObject;
			List<SerializedClipboard> clipboardRaw = PasteBinWindow.GetSerializedClipboards();
			for( int i = 0; i < clipboardRaw.Count; i++ )
			{
				object value = clipboardRaw[i].RootValue.GetClipboardObject( context );
				if( targetProperty.CanPasteValue( value, false ) )
				{
					clipboard.Add( clipboardRaw[i] );
					clipboardValues.Add( value );

					if( !shouldShowSmartPasteButton )
					{
						switch( clipboardRaw[i].RootType )
						{
							case SerializedClipboard.IPObjectType.Array:
							case SerializedClipboard.IPObjectType.AssetReference:
							case SerializedClipboard.IPObjectType.GenericObject:
							case SerializedClipboard.IPObjectType.ManagedReference:
							case SerializedClipboard.IPObjectType.SceneObjectReference:
								shouldShowSmartPasteButton = true;
								break;
						}
					}
				}
			}

			OnInitialize();
		}

		public void Initialize( Object[] objects, PasteType pasteType )
		{
			targetProperty = null;
			targetObjects = objects;
			this.pasteType = pasteType;

			List<SerializedClipboard> clipboardRaw = PasteBinWindow.GetSerializedClipboards();
			for( int i = 0; i < clipboardRaw.Count; i++ )
			{
				switch( pasteType )
				{
					case PasteType.Normal:
						if( !clipboardRaw[i].CanPasteToObject( objects[0] ) )
							continue;

						shouldShowSmartPasteButton = true; break;
					case PasteType.ComponentAsNew:
						if( !clipboardRaw[i].CanPasteAsNewComponent( (Component) objects[0] ) )
							continue;

						shouldShowSmartPasteButton = true; break;
					case PasteType.CompleteGameObject: if( !clipboardRaw[i].CanPasteCompleteGameObject( (GameObject) objects[0] ) ) continue; break;
					case PasteType.ComponentGroup: if( !clipboardRaw[i].CanPasteComponentGroup( (GameObject) objects[0] ) ) continue; break;
					case PasteType.AssetFiles: if( !clipboardRaw[i].CanPasteAssetFiles( objects ) ) continue; break;
				}

				clipboard.Add( clipboardRaw[i] );
				clipboardValues.Add( clipboardRaw[i].RootValue.GetClipboardObject( null ) );
			}

			OnInitialize();
		}

		private void OnInitialize()
		{
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

			if( !shouldShowSmartPasteButton )
				GUILayout.Label( "Select value to paste:", EditorStyles.boldLabel );
			else
			{
				GUILayout.BeginHorizontal();
				GUILayout.Label( "Select value to paste:", EditorStyles.boldLabel );

				EditorGUI.BeginChangeCheck();
				InspectPlusSettings.Instance.SmartCopyPaste = GUILayout.Toggle( InspectPlusSettings.Instance.SmartCopyPaste, InspectPlusSettings.Instance.SmartCopyPaste ? smartPasteOnButtonLabel : smartPasteOffButtonLabel, GUI.skin.button );
				if( EditorGUI.EndChangeCheck() )
				{
					InspectPlusSettings.Instance.Save();

					if( targetProperty != null )
					{
						// Refresh values
						Object context = targetProperty.serializedObject.targetObject;
						for( int i = 0; i < clipboard.Count; i++ )
						{
							SerializedClipboard.IPObjectType type = clipboard[i].RootType;
							if( type == SerializedClipboard.IPObjectType.AssetReference || type == SerializedClipboard.IPObjectType.SceneObjectReference )
								clipboardValues[i] = clipboard[i].RootValue.GetClipboardObject( context );
						}
					}
				}

				GUILayout.EndHorizontal();
			}

			int hoveredClipboardIndex = -1;

			if( clipboard.Count == 0 )
				GUILayout.Label( "Nothing to paste here..." );
			else
			{
				// Traverse the list in reverse order so that the newest SerializedClipboards will be at the top of the list
				for( int i = clipboard.Count - 1; i >= 0; i-- )
				{
					PasteBinWindow.DrawClipboardOnGUI( clipboard[i], clipboardValues[i], this.hoveredClipboardIndex == i, false );

					if( hoveredClipboardIndex < 0 && ( ev.type == EventType.MouseDown || ev.type == EventType.MouseMove ) && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
						hoveredClipboardIndex = i;
				}
			}

			if( ev.type == EventType.MouseMove && this.hoveredClipboardIndex != hoveredClipboardIndex )
				OnHoveredClipboardChanged( hoveredClipboardIndex );

			if( ev.type == EventType.MouseDown && hoveredClipboardIndex >= 0 )
			{
				int mouseButton = ev.button;
				ev.Use();

				if( mouseButton == 0 )
				{
					PasteClipboard( hoveredClipboardIndex );
					GUIUtility.ExitGUI();
				}
				else if( mouseButton == 1 )
				{
					GenericMenu menu = new GenericMenu();
					menu.AddItem( new GUIContent( "Paste" ), false, PasteClipboard, hoveredClipboardIndex );
					menu.AddItem( new GUIContent( "Delete" ), false, RemoveClipboard, hoveredClipboardIndex );
					menu.ShowAsContext();

					GUIUtility.ExitGUI();
				}
				else
					RemoveClipboard( hoveredClipboardIndex );
			}

			GUILayout.EndVertical();

			if( shouldRepositionSelf || shouldResizeSelf )
			{
				float preferredHeight = GUILayoutUtility.GetLastRect().height;
				if( preferredHeight > 10f )
				{
					Vector2 size = new Vector2( position.width, preferredHeight + 15f );

					if( shouldRepositionSelf )
						position = Utilities.GetScreenFittedRect( new Rect( GUIUtility.GUIToScreenPoint( ev.mousePosition ) - size * 0.5f, size ) );
					else if( shouldResizeSelf )
						position = new Rect( position.position, size );

					shouldRepositionSelf = false;
					shouldResizeSelf = false;

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

		private void OnHoveredClipboardChanged( int hoveredClipboardIndex )
		{
			this.hoveredClipboardIndex = hoveredClipboardIndex;
			if( hoveredClipboardIndex < 0 || !clipboard[hoveredClipboardIndex].HasTooltip )
				PasteBinTooltip.Hide();
			else
				PasteBinTooltip.Show( position, clipboard[hoveredClipboardIndex].LabelContent.tooltip );

			Repaint();
		}

		private void PasteClipboard( object obj )
		{
			int index = (int) obj;

			if( targetProperty != null )
				targetProperty.PasteValue( clipboard[index] );
			else if( targetObjects != null )
			{
				if( pasteType == PasteType.AssetFiles )
					clipboard[index].PasteAssetFiles( targetObjects );
				else if( pasteType == PasteType.ComponentGroup )
					CreateInstance<ComponentGroupCopyPasteWindow>().Initialize( (SerializedClipboard.IPComponentGroup) clipboard[index].RootValue, targetObjects );
				else
				{
					for( int j = 0; j < targetObjects.Length; j++ )
					{
						switch( pasteType )
						{
							case PasteType.Normal: clipboard[index].PasteToObject( targetObjects[j] ); break;
							case PasteType.ComponentAsNew: clipboard[index].PasteAsNewComponent( (Component) targetObjects[j] ); break;
							case PasteType.CompleteGameObject: clipboard[index].PasteCompleteGameObject( (GameObject) targetObjects[j], Event.current == null || ( !Event.current.control && !Event.current.command && !Event.current.shift ) ); break; // Don't preserve objects' world space positions if CTRL or Shift key are held
						}
					}
				}
			}
			else
				Debug.LogError( "Both the SerializedProperty and the target Objects are null!" );

			Close();
		}

		private void RemoveClipboard( object obj )
		{
			int index = (int) obj;
			if( index >= clipboard.Count )
				return;

			PasteBinWindow.RemoveClipboard( clipboard[index] );

			clipboard.RemoveAt( index );
			clipboardValues.RemoveAt( index );

			hoveredClipboardIndex = -1;
			PasteBinTooltip.Hide();

			shouldResizeSelf = true;
			Repaint();
		}
	}
}