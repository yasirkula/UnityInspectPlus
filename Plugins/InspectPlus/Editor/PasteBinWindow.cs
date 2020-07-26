using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VectorClipboard = InspectPlusNamespace.SerializablePropertyExtensions.VectorClipboard;
using ArrayClipboard = InspectPlusNamespace.SerializablePropertyExtensions.ArrayClipboard;
using GenericObjectClipboard = InspectPlusNamespace.SerializablePropertyExtensions.GenericObjectClipboard;
using ManagedObjectClipboard = InspectPlusNamespace.SerializablePropertyExtensions.ManagedObjectClipboard;

namespace InspectPlusNamespace
{
	public class PasteBinWindow : EditorWindow, IHasCustomMenu
	{
		private const int CLIPBOARD_CAPACITY = 30;

		private static readonly Color activeClipboardColor = new Color32( 245, 170, 10, 255 );
		private static readonly GUILayoutOption expandWidth = GUILayout.ExpandWidth( true );

		private static readonly List<object> clipboard = new List<object>( 4 );
		private static readonly List<GUIContent> clipboardLabels = new List<GUIContent>( 4 );
		private static readonly List<Object> clipboardContexts = new List<Object>( 4 );

		private static PasteBinWindow mainWindow;

		private static int m_activeClipboardIndex;
		private static int ActiveClipboardIndex
		{
			get { return m_activeClipboardIndex; }
			set
			{
				if( value >= 0 && value < clipboard.Count && ( m_activeClipboardIndex != value || clipboard.Count == 1 ) )
				{
					m_activeClipboardIndex = value;

					if( InspectPlusSettings.Instance.UseXMLCopyFormat && clipboard[m_activeClipboardIndex] != null && !clipboard[m_activeClipboardIndex].Equals( null ) )
					{
						serializedClipboard = new SerializedClipboard();
						serializedClipboardXML = serializedClipboard.SerializeClipboardData( clipboard[m_activeClipboardIndex], !InspectPlusSettings.Instance.OneLineXML, clipboardContexts[m_activeClipboardIndex] );
						GUIUtility.systemCopyBuffer = serializedClipboardXML;
					}
				}
			}
		}

		public static object ActiveClipboard
		{
			get
			{
				if( InspectPlusSettings.Instance.UseXMLCopyFormat )
				{
					string systemCopyBuffer = GUIUtility.systemCopyBuffer;
					if( !string.IsNullOrEmpty( systemCopyBuffer ) && systemCopyBuffer.StartsWith( "<InspectPlus>" ) )
					{
						if( systemCopyBuffer != serializedClipboardXML )
						{
							serializedClipboardXML = systemCopyBuffer;
							serializedClipboard = new SerializedClipboard();
							serializedClipboard.Deserialize( serializedClipboardXML );
						}

						return serializedClipboard;
					}
				}

				return ActiveClipboardIndex < clipboard.Count ? clipboard[ActiveClipboardIndex] : null;
			}
		}

		private static SerializedClipboard serializedClipboard;
		private static string serializedClipboardXML;

		private MethodInfo gradientField;
		private static GUIStyle activeClipboardBackgroundStyle;
		private Vector2 scrollPosition;

		public static new void Show()
		{
			PasteBinWindow window = GetWindow<PasteBinWindow>();
			window.titleContent = new GUIContent( "Paste Bin" );
			window.minSize = new Vector2( 250f, 150f );
			( (EditorWindow) window ).Show();
		}

		private void OnEnable()
		{
			mainWindow = this;
			gradientField = typeof( EditorGUILayout ).GetMethod( "GradientField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new System.Type[] { typeof( GUIContent ), typeof( Gradient ), typeof( GUILayoutOption[] ) }, null );

			Repaint();
		}

		private void OnDestroy()
		{
			mainWindow = null;
		}

		void IHasCustomMenu.AddItemsToMenu( GenericMenu menu )
		{
			menu.AddItem( new GUIContent( "Clear" ), false, ClearClipboard );
		}

		public static void AddToClipboard( object obj, string label, Object context )
		{
			if( obj == null || obj.Equals( null ) )
				return;

			if( clipboard.Count >= CLIPBOARD_CAPACITY )
			{
				clipboard.RemoveAt( 0 );
				clipboardLabels.RemoveAt( 0 );
				clipboardContexts.RemoveAt( 0 );
			}

			clipboard.Add( obj );
			clipboardLabels.Add( new GUIContent( label, label ) );
			// Context Object is useful for Object, array, generic object and managed object clipboards to help calculate RelativePath values when XML serialization is used
			clipboardContexts.Add( ( obj is Object || obj is ArrayClipboard || obj is GenericObjectClipboard || obj is ManagedObjectClipboard ) ? context : null );

			ActiveClipboardIndex = clipboard.Count - 1;

			if( mainWindow )
				mainWindow.Repaint();
		}

		public static void AddToClipboard( SerializedProperty prop )
		{
			object clipboard = prop.CopyValue();
			if( clipboard != null )
				AddToClipboard( clipboard, string.Concat( prop.serializedObject.targetObject.name, ".", prop.serializedObject.targetObject.GetType().Name, ".", prop.name ), prop.serializedObject.targetObject );
		}

		private void OnGUI()
		{
			if( activeClipboardBackgroundStyle == null )
			{
				Texture2D background = new Texture2D( 1, 1 );
				background.SetPixel( 0, 0, activeClipboardColor );
				background.Apply( false, true );

				activeClipboardBackgroundStyle = new GUIStyle();
				activeClipboardBackgroundStyle.normal.background = background;
				activeClipboardBackgroundStyle.onNormal.background = background;
			}

			Event ev = Event.current;

			bool originalWideMode = EditorGUIUtility.wideMode;
			float originalLabelWidth = EditorGUIUtility.labelWidth;

			float windowWidth = position.width;
			EditorGUIUtility.wideMode = windowWidth > 330f;
			EditorGUIUtility.labelWidth = windowWidth < 350f ? 130f : windowWidth * 0.4f;

			EditorGUILayout.HelpBox( "The highlighted value will be used in Paste operations. You can right click a value to set it active or remove it. Note that changing these values here won't affect the source objects.", MessageType.None );

			scrollPosition = EditorGUILayout.BeginScrollView( scrollPosition );

			for( int i = 0; i < clipboard.Count; i++ )
			{
				if( clipboard[i] == null || clipboard[i].Equals( null ) )
				{
					RemoveClipboard( i-- );
					continue;
				}

				if( ActiveClipboardIndex == i )
					GUILayout.BeginHorizontal( activeClipboardBackgroundStyle );

				if( clipboard[i] == null || clipboard[i].Equals( null ) || clipboard[i] is Object )
					clipboard[i] = EditorGUILayout.ObjectField( clipboardLabels[i], clipboard[i] as Object, typeof( Object ), true );
				else if( clipboard[i] is long )
					clipboard[i] = EditorGUILayout.LongField( clipboardLabels[i], (long) clipboard[i] );
				else if( clipboard[i] is double )
					clipboard[i] = EditorGUILayout.DoubleField( clipboardLabels[i], (double) clipboard[i] );
				else if( clipboard[i] is Color )
					clipboard[i] = EditorGUILayout.ColorField( clipboardLabels[i], (Color) clipboard[i] );
				else if( clipboard[i] is string )
					clipboard[i] = EditorGUILayout.TextField( clipboardLabels[i], (string) clipboard[i] );
				else if( clipboard[i] is bool )
					clipboard[i] = EditorGUILayout.Toggle( clipboardLabels[i], (bool) clipboard[i] );
				else if( clipboard[i] is AnimationCurve )
					clipboard[i] = EditorGUILayout.CurveField( clipboardLabels[i], (AnimationCurve) clipboard[i] );
				else if( clipboard[i] is Gradient )
					clipboard[i] = gradientField.Invoke( null, new object[] { clipboardLabels[i], clipboard[i], null } );
				else if( clipboard[i] is VectorClipboard )
					clipboard[i] = (VectorClipboard) EditorGUILayout.Vector4Field( clipboardLabels[i], (VectorClipboard) clipboard[i] );
				else if( clipboard[i] is ArrayClipboard )
				{
					ArrayClipboard obj = (ArrayClipboard) clipboard[i];
					GUI.enabled = false;
					EditorGUILayout.TextField( clipboardLabels[i], string.Concat( obj.elementType, "[", obj.elements.Length, "] array" ) );
					GUI.enabled = true;
				}
				else if( clipboard[i] is GenericObjectClipboard )
				{
					GUI.enabled = false;
					EditorGUILayout.TextField( clipboardLabels[i], ( (GenericObjectClipboard) clipboard[i] ).type + " object" );
					GUI.enabled = true;
				}
				else if( clipboard[i] is ManagedObjectClipboard )
				{
					GUI.enabled = false;
					EditorGUILayout.TextField( clipboardLabels[i], ( (ManagedObjectClipboard) clipboard[i] ).type + " object (SerializeField)" );
					GUI.enabled = true;
				}
				else if( clipboard[i] is SerializedClipboard )
				{
					GUI.enabled = false;
					EditorGUILayout.TextField( clipboardLabels[i], ( (SerializedClipboard) clipboard[i] ).Values[0].GetType() + " object (XML)" );
					GUI.enabled = true;
				}

				if( ActiveClipboardIndex == i )
					GUILayout.EndHorizontal();

				if( ev.type == EventType.MouseDown && ev.button == 0 && /*ev.mousePosition.x <= EditorGUIUtility.labelWidth &&*/ GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
				{
					ActiveClipboardIndex = i;
					Repaint();
					ev.Use();
				}
				else if( ev.type == EventType.ContextClick && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
				{
					int j = i;

					GenericMenu menu = new GenericMenu();
					menu.AddItem( new GUIContent( "Select" ), false, SetActiveClipboard, j );
					menu.AddItem( new GUIContent( "Remove" ), false, RemoveClipboard, j );
					menu.ShowAsContext();
				}
			}

			EditorGUILayout.EndScrollView();

			if( ( ev.type == EventType.DragPerform || ev.type == EventType.DragUpdated ) && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
			{
				// Accept drag&drop
				DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				if( ev.type == EventType.DragPerform )
				{
					DragAndDrop.AcceptDrag();

					Object[] draggedObjects = DragAndDrop.objectReferences;
					for( int i = 0; i < draggedObjects.Length; i++ )
						AddToClipboard( draggedObjects[i], "DRAG&DROP", draggedObjects[i] );
				}

				ev.Use();
			}
			else if( ev.type == EventType.KeyDown )
			{
				if( ev.keyCode == KeyCode.Delete )
				{
					RemoveClipboard( ActiveClipboardIndex );
					Repaint();
					ev.Use();
				}
				else if( ev.keyCode == KeyCode.UpArrow )
				{
					ActiveClipboardIndex = Mathf.Max( 0, ActiveClipboardIndex - 1 );
					Repaint();
					ev.Use();
				}
				else if( ev.keyCode == KeyCode.DownArrow )
				{
					ActiveClipboardIndex = Mathf.Min( clipboard.Count - 1, ActiveClipboardIndex + 1 );
					Repaint();
					ev.Use();
				}
			}

			EditorGUIUtility.wideMode = originalWideMode;
			EditorGUIUtility.labelWidth = originalLabelWidth;
		}

		private void SetActiveClipboard( object obj )
		{
			int index = (int) obj;
			if( index < clipboard.Count )
				ActiveClipboardIndex = index;
		}

		private void RemoveClipboard( object obj )
		{
			int index = (int) obj;
			if( index < clipboard.Count )
			{
				clipboard.RemoveAt( index );
				clipboardLabels.RemoveAt( index );
				clipboardContexts.RemoveAt( index );

				if( ActiveClipboardIndex > 0 && ActiveClipboardIndex >= clipboard.Count )
					ActiveClipboardIndex = clipboard.Count - 1;
			}
		}

		private void ClearClipboard()
		{
			clipboard.Clear();
			ActiveClipboardIndex = 0;
		}
	}
}