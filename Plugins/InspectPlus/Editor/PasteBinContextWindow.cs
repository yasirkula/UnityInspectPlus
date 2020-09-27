using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InspectPlusNamespace
{
	public class PasteBinContextWindow : EditorWindow
	{
		private readonly GUIContent smartPasteButtonLabel = new GUIContent( "Smart Paste", "Imagine objects A and B having children named C. When Smart Paste is enabled, if A.someVariable points to A.C, B.someVariable will point to B.C instead of A.C after the paste operation" );

		private readonly List<SerializedClipboard> clipboard = new List<SerializedClipboard>( 4 );
		private readonly List<object> clipboardValues = new List<object>( 4 );

		private SerializedProperty targetProperty;
		private Object[] targetObjects;

		private GUIStyle backgroundStyle;
		private bool shouldRepositionSelf = true;
		private bool shouldShowSmartPasteButton = false;

		private Vector2? prevMousePos;
		private Vector2 scrollPosition;

		public float PreferredWidth
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

				return width;
			}
		}

		public void Initialize( SerializedProperty property )
		{
			targetProperty = property;
			targetObjects = null;

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
		}

		public void Initialize( Object[] objects )
		{
			targetProperty = null;
			targetObjects = objects;

			List<SerializedClipboard> clipboardRaw = PasteBinWindow.GetSerializedClipboards();
			for( int i = 0; i < clipboardRaw.Count; i++ )
			{
				if( clipboardRaw[i].CanPasteToObject( objects[0] ) )
				{
					clipboard.Add( clipboardRaw[i] );
					clipboardValues.Add( clipboardRaw[i].RootValue.GetClipboardObject( null ) ); // RootValue won't be affected by smart copy-paste in this case

					shouldShowSmartPasteButton = true;
				}
			}
		}

		private void OnGUI()
		{
			if( backgroundStyle == null )
				backgroundStyle = new GUIStyle( GUI.skin.box ) { margin = new RectOffset( 0, 0, 0, 0 ), padding = new RectOffset( 0, 0, 0, 0 ) };

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
				InspectPlusSettings.Instance.SmartCopyPaste = GUILayout.Toggle( InspectPlusSettings.Instance.SmartCopyPaste, smartPasteButtonLabel, GUI.skin.button );
				if( EditorGUI.EndChangeCheck() )
				{
					EditorUtility.SetDirty( InspectPlusSettings.Instance );

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

			if( clipboard.Count == 0 )
				GUILayout.Label( "Nothing to paste here..." );
			else
			{
				// Traverse the list in reverse order so that the newest SerializedClipboards will be at the top of the list
				for( int i = clipboard.Count - 1; i >= 0; i-- )
				{
					PasteBinWindow.DrawClipboardOnGUI( clipboard[i], clipboardValues[i], false );

					if( ev.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
					{
						if( targetProperty != null )
							targetProperty.PasteValue( clipboard[i].RootValue );
						else if( targetObjects != null )
						{
							for( int j = 0; j < targetObjects.Length; j++ )
								clipboard[i].PasteToObject( targetObjects[j] );
						}
						else
							Debug.LogError( "Both the SerializedProperty and the target Objects are null!" );

						ev.Use();
						Close();
					}
				}
			}

			GUILayout.EndVertical();

			if( shouldRepositionSelf )
			{
				float preferredHeight = GUILayoutUtility.GetLastRect().height;
				if( preferredHeight > 10f )
				{
					shouldRepositionSelf = false;
					ShowAsDropDown( new Rect( GUIUtility.GUIToScreenPoint( ev.mousePosition ), Vector2.one ), new Vector2( position.width, preferredHeight + 15f ) );
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
		}
	}
}