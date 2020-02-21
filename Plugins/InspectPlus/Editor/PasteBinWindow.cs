using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using CLVector = InspectPlusNamespace.SerializablePropertyExtensions.VectorClipboard;
using CLArray = InspectPlusNamespace.SerializablePropertyExtensions.ArrayClipboard;
using CLGeneric = InspectPlusNamespace.SerializablePropertyExtensions.GenericObjectClipboard;

namespace InspectPlusNamespace
{
	public class PasteBinWindow : EditorWindow, IHasCustomMenu
	{
		private const int CLIPBOARD_CAPACITY = 30;

		private static readonly Color activeClipboardColor = new Color32( 245, 170, 10, 255 );
		private static GUIStyle activeClipboardBackgroundStyle;

		private static readonly List<object> clipboard = new List<object>( 4 );
		private static readonly List<GUIContent> clipboardLabels = new List<GUIContent>( 4 );

		private static PasteBinWindow mainWindow;

		private static int activeClipboardIndex;
		public static object ActiveClipboard { get { return activeClipboardIndex < clipboard.Count ? clipboard[activeClipboardIndex] : null; } }

		private MethodInfo gradientField;
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

		public static void AddToClipboard( object obj, string label )
		{
			if( obj == null || obj.Equals( null ) )
				return;

			if( clipboard.Count >= CLIPBOARD_CAPACITY )
			{
				clipboard.RemoveAt( 0 );
				clipboardLabels.RemoveAt( 0 );
			}

			clipboard.Add( obj );
			clipboardLabels.Add( new GUIContent( label, label ) );

			activeClipboardIndex = clipboard.Count - 1;

			if( mainWindow )
				mainWindow.Repaint();
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

				if( activeClipboardIndex == i )
					GUILayout.BeginHorizontal( activeClipboardBackgroundStyle );

				if( clipboard[i] is Object )
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
				else if( clipboard[i] is CLVector )
					clipboard[i] = (CLVector) EditorGUILayout.Vector4Field( clipboardLabels[i], (CLVector) clipboard[i] );
				else if( clipboard[i] is CLArray )
				{
					CLArray obj = (CLArray) clipboard[i];
					GUI.enabled = false;
					EditorGUILayout.TextField( clipboardLabels[i], string.Concat( obj.elementType, "[", obj.size, "] array" ) );
					GUI.enabled = true;
				}
				else if( clipboard[i] is CLGeneric )
				{
					GUI.enabled = false;
					EditorGUILayout.TextField( clipboardLabels[i], ( (CLGeneric) clipboard[i] ).type + " object" );
					GUI.enabled = true;
				}

				if( activeClipboardIndex == i )
					GUILayout.EndHorizontal();

				if( ev.type == EventType.MouseDown && ev.button == 0 && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
				{
					activeClipboardIndex = i;
					Repaint();
					ev.Use();
				}
				else if( ev.type == EventType.ContextClick && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
				{
					int j = i;

					GenericMenu menu = new GenericMenu();
					menu.AddItem( new GUIContent( "Copy" ), false, SetActiveClipboard, j );
					menu.AddItem( new GUIContent( "Remove" ), false, RemoveClipboard, j );
					menu.ShowAsContext();

					GUIUtility.keyboardControl = 0;
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
						AddToClipboard( draggedObjects[i], "DRAG&DROP" );
				}

				ev.Use();
			}
			else if( ev.type == EventType.KeyDown )
			{
				// KeyCode.Delete won't be captured by PasteBinWindow if an ObjectField has keyboard focus, therefore
				// there are some "GUIUtility.keyboardControl = 0;" calls here and there to remove keyboard focus
				if( ev.keyCode == KeyCode.Delete )
				{
					RemoveClipboard( activeClipboardIndex );
					Repaint();
					ev.Use();
				}
				else if( ev.keyCode == KeyCode.UpArrow )
				{
					activeClipboardIndex = Mathf.Max( 0, activeClipboardIndex - 1 );
					GUIUtility.keyboardControl = 0;
					Repaint();
					ev.Use();
				}
				else if( ev.keyCode == KeyCode.DownArrow )
				{
					activeClipboardIndex = Mathf.Min( clipboard.Count - 1, activeClipboardIndex + 1 );
					GUIUtility.keyboardControl = 0;
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
				activeClipboardIndex = index;
		}

		private void RemoveClipboard( object obj )
		{
			int index = (int) obj;
			if( index < clipboard.Count )
			{
				clipboard.RemoveAt( index );
				clipboardLabels.RemoveAt( index );

				if( activeClipboardIndex > 0 && activeClipboardIndex >= clipboard.Count )
					activeClipboardIndex = clipboard.Count - 1;
			}
		}

		private void ClearClipboard()
		{
			clipboard.Clear();
			activeClipboardIndex = 0;
		}
	}
}