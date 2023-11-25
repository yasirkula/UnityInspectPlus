using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using SceneObjectReference = InspectPlusNamespace.SerializedClipboard.IPSceneObjectReference;
using AssetReference = InspectPlusNamespace.SerializedClipboard.IPAssetReference;
using VectorClipboard = InspectPlusNamespace.SerializablePropertyExtensions.VectorClipboard;
using ArrayClipboard = InspectPlusNamespace.SerializablePropertyExtensions.ArrayClipboard;
using GenericObjectClipboard = InspectPlusNamespace.SerializablePropertyExtensions.GenericObjectClipboard;
using ManagedObjectClipboard = InspectPlusNamespace.SerializablePropertyExtensions.ManagedObjectClipboard;
using GameObjectHierarchyClipboard = InspectPlusNamespace.SerializablePropertyExtensions.GameObjectHierarchyClipboard;
using ComponentGroupClipboard = InspectPlusNamespace.SerializablePropertyExtensions.ComponentGroupClipboard;
using AssetFilesClipboard = InspectPlusNamespace.SerializablePropertyExtensions.AssetFilesClipboard;

namespace InspectPlusNamespace
{
	// Paste Bin: a collection of save data that is shared between all Unity instances on the computer
	// 
	// We aren't using EditorPrefs for synchronizing data between Unity editor instances because
	// changes made to EditorPrefs aren't reflected to other live Unity instances until domain reload.
	// We want the changes to be immediately available on the other Unity instances
	public class PasteBinWindow : EditorWindow, IHasCustomMenu
	{
		private const int CLIPBOARD_CAPACITY = 16;
		private const double CLIPBOARD_REFRESH_INTERVAL = 1.5;
		private const double CLIPBOARD_REFRESH_MIN_COOLDOWN = 0.1;
		private static readonly Color ACTIVE_CLIPBOARD_COLOR = new Color32( 245, 170, 35, 255 );

		private double clipboardRefreshTime;
		private static readonly List<SerializedClipboard> clipboard = new List<SerializedClipboard>( 4 );
		private readonly List<object> clipboardValues = new List<object>( 4 );

		private static bool loadedActiveClipboardOnly;

		private static PasteBinWindow mainWindow;

		private static double clipboardIndexLastCheckTime;
		private static double clipboardLastCheckTime;
		private static DateTime clipboardLastDateTime;

		private static int m_activeClipboardIndex;
		private static int ActiveClipboardIndex
		{
			get
			{
				// Don't refresh too frequently (too many file operations)
				if( EditorApplication.timeSinceStartup - clipboardIndexLastCheckTime >= CLIPBOARD_REFRESH_MIN_COOLDOWN )
				{
					clipboardIndexLastCheckTime = EditorApplication.timeSinceStartup;

					int index;
					if( File.Exists( ClipboardIndexSavePath ) && int.TryParse( File.ReadAllText( ClipboardIndexSavePath ), out index ) )
						m_activeClipboardIndex = index;
				}

				return m_activeClipboardIndex;
			}
			set
			{
				if( value >= 0 && value < clipboard.Count )
				{
					m_activeClipboardIndex = value;

					Directory.CreateDirectory( Path.GetDirectoryName( ClipboardIndexSavePath ) );
					File.WriteAllText( ClipboardIndexSavePath, m_activeClipboardIndex.ToString() );
				}
			}
		}

		public static SerializedClipboard ActiveClipboard
		{
			get
			{
				LoadClipboard( true );

				int activeClipboardIndex = ActiveClipboardIndex;
				if( activeClipboardIndex >= clipboard.Count )
					return null;

				// This is an edge case: after loading only the active clipboard entry with LoadClipboard(true),
				// if ActiveClipboardIndex is changed via another Unity project's Paste Bin window, then the
				// clipboard entry at that new index will be null because LoadClipboard(true) loads only the latest
				// clipboard entry and not the other entries. We must reload the whole clipboard data in this case
				if( clipboard[activeClipboardIndex] == null )
					LoadClipboard( false );

				return clipboard[activeClipboardIndex];
			}
		}

		private static string ClipboardIndexSavePath { get { return Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), "Unity" + Path.DirectorySeparatorChar + "UnityInspectPlus.index" ); } }
		private static string ClipboardSavePath { get { return Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), "Unity" + Path.DirectorySeparatorChar + "UnityInspectPlus.dat" ); } }

		private static GUIStyle clipboardLabelGUIStyle;
		internal static readonly MethodInfo gradientField = typeof( EditorGUILayout ).GetMethod( "GradientField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof( GUIContent ), typeof( Gradient ), typeof( GUILayoutOption[] ) }, null );

		private int clickedSerializedClipboardIndex = -1;
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

			EditorApplication.update -= RefreshClipboardRegularly;
			EditorApplication.update += RefreshClipboardRegularly;
			EditorSceneManager.sceneOpened -= OnSceneOpened;
			EditorSceneManager.sceneOpened += OnSceneOpened;
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneLoaded += OnSceneLoaded;

			if( !LoadClipboard() )
			{
				// When LoadClipboard returns true, clipboardValues are filled by LoadClipboard automatically
				clipboardValues.Clear();
				for( int i = 0; i < clipboard.Count; i++ )
					clipboardValues.Add( clipboard[i].RootValue.GetClipboardObject( null ) );
			}

			Repaint();
		}

		private void OnDisable()
		{
			EditorApplication.update -= RefreshClipboardRegularly;
			EditorSceneManager.sceneOpened -= OnSceneOpened;
			SceneManager.sceneLoaded -= OnSceneLoaded;
		}

		private void OnDestroy()
		{
			mainWindow = null;
		}

		void IHasCustomMenu.AddItemsToMenu( GenericMenu menu )
		{
			menu.AddItem( new GUIContent( "Clear" ), false, ClearClipboard );
			menu.AddItem( new GUIContent( "About" ), false, ShowAboutDialog );
		}

		private void RefreshClipboardRegularly()
		{
			if( EditorApplication.timeSinceStartup >= clipboardRefreshTime )
			{
				clipboardRefreshTime = EditorApplication.timeSinceStartup + CLIPBOARD_REFRESH_INTERVAL;
				LoadClipboard();
			}
		}

		private void OnSceneOpened( Scene scene, OpenSceneMode openSceneMode )
		{
			RefreshSceneObjectClipboards();
		}

		private void OnSceneLoaded( Scene scene, LoadSceneMode loadSceneMode )
		{
			RefreshSceneObjectClipboards();
		}

		private void RefreshSceneObjectClipboards()
		{
			for( int i = 0; i < clipboard.Count; i++ )
			{
				if( clipboard[i].RootType == SerializedClipboard.IPObjectType.SceneObjectReference )
					clipboardValues[i] = clipboard[i].RootValue.GetClipboardObject( null );
			}
		}

		private void OnGUI()
		{
			Event ev = Event.current;
			Color backgroundColor = GUI.backgroundColor;

			bool originalWideMode = EditorGUIUtility.wideMode;
			float originalLabelWidth = EditorGUIUtility.labelWidth;

			float windowWidth = position.width;
			EditorGUIUtility.wideMode = windowWidth > 330f;
			EditorGUIUtility.labelWidth = windowWidth < 350f ? 130f : windowWidth * 0.4f;

			EditorGUILayout.HelpBox( "The highlighted value will be used in Paste operations. You can right click a value to set it active or remove it.", MessageType.None );

			scrollPosition = EditorGUILayout.BeginScrollView( scrollPosition );

			// Traverse the list in reverse order so that the newest SerializedClipboards will be at the top of the list
			for( int i = clipboard.Count - 1; i >= 0; i-- )
			{
				if( clipboard[i] == null || clipboard[i].Equals( null ) )
				{
					RemoveClipboard( i-- );
					continue;
				}

				DrawClipboardOnGUI( clipboard[i], clipboardValues[i], ActiveClipboardIndex == i, true );

				if( ev.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
				{
					clickedSerializedClipboardIndex = i;
					ev.Use();
				}
				else if( ev.type == EventType.ContextClick && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
				{
					int j = i;

					GenericMenu menu = new GenericMenu();
					menu.AddItem( new GUIContent( "Select" ), false, SetActiveClipboard, j );
					menu.AddItem( new GUIContent( "Remove" ), false, RemoveClipboard, j );

					menu.AddSeparator( "" );
					menu.AddItem( new GUIContent( "Copy To System Clipboard" ), false, CopyClipboardToSystemBuffer, j );

					string systemBuffer = GUIUtility.systemCopyBuffer;
					if( !string.IsNullOrEmpty( systemBuffer ) && systemBuffer.StartsWith( "Inspect+", StringComparison.Ordinal ) )
						menu.AddItem( new GUIContent( "Paste From System Clipboard" ), false, PasteClipboardFromSystemBuffer, j );
					else
						menu.AddDisabledItem( new GUIContent( "Paste From System Clipboard" ) );

					menu.ShowAsContext();

					ev.Use();
				}
				else if( clickedSerializedClipboardIndex == i && ev.type == EventType.MouseUp && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
				{
					clickedSerializedClipboardIndex = -1;

					if( ev.button == 0 )
					{
						ActiveClipboardIndex = i;
						Repaint();
						ev.Use();
					}
					else if( ev.button == 2 )
					{
						RemoveClipboard( i );
						Repaint();
						ev.Use();
					}
				}
			}

			EditorGUILayout.EndScrollView();

			if( ev.type == EventType.MouseUp )
				clickedSerializedClipboardIndex = -1;
			else if( ( ev.type == EventType.DragPerform || ev.type == EventType.DragUpdated ) && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
			{
				// Accept drag&drop
				DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				if( ev.type == EventType.DragPerform )
				{
					DragAndDrop.AcceptDrag();

					Object[] draggedObjects = DragAndDrop.objectReferences;
					for( int i = 0; i < draggedObjects.Length; i++ )
						AddToClipboard( draggedObjects[i], Utilities.GetDetailedObjectName( draggedObjects[i] ), draggedObjects[i] );
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

		public static void DrawClipboardOnGUI( SerializedClipboard clipboard, object clipboardValue, bool isActiveClipboard, bool showTooltip )
		{
			if( clipboardLabelGUIStyle == null )
				clipboardLabelGUIStyle = new GUIStyle( EditorStyles.boldLabel ) { wordWrap = true };

			if( !isActiveClipboard )
				GUILayout.BeginVertical( PasteBinTooltip.Style );
			else
			{
				Color backgroundColor = GUI.backgroundColor;
				GUI.backgroundColor = Color.Lerp( backgroundColor, ACTIVE_CLIPBOARD_COLOR, 0.5f );
				GUILayout.BeginVertical( PasteBinTooltip.Style );
				GUI.backgroundColor = backgroundColor;
			}

			if( showTooltip )
				EditorGUILayout.LabelField( clipboard.LabelContent, clipboardLabelGUIStyle );
			else
				EditorGUILayout.LabelField( clipboard.Label, clipboardLabelGUIStyle );

			EditorGUI.indentLevel++;

			if( clipboardValue as Object )
				EditorGUILayout.ObjectField( GUIContent.none, clipboardValue as Object, typeof( Object ), true );
			else if( clipboardValue is long )
				EditorGUILayout.TextField( GUIContent.none, ( (long) clipboardValue ).ToString() );
			else if( clipboardValue is double )
				EditorGUILayout.TextField( GUIContent.none, ( (double) clipboardValue ).ToString() );
			else if( clipboardValue is Color )
				EditorGUILayout.ColorField( GUIContent.none, (Color) clipboardValue );
			else if( clipboardValue is string )
				EditorGUILayout.TextField( GUIContent.none, (string) clipboardValue );
			else if( clipboardValue is bool )
				EditorGUILayout.Toggle( GUIContent.none, (bool) clipboardValue );
			else if( clipboardValue is AnimationCurve )
				EditorGUILayout.CurveField( GUIContent.none, (AnimationCurve) clipboardValue );
			else if( clipboardValue is Gradient )
				gradientField.Invoke( null, new object[] { GUIContent.none, clipboardValue, null } );
			else if( clipboardValue is VectorClipboard )
				EditorGUILayout.Vector4Field( GUIContent.none, (VectorClipboard) clipboardValue );
			else if( clipboardValue is ArrayClipboard )
			{
				ArrayClipboard obj = (ArrayClipboard) clipboardValue;
				EditorGUILayout.TextField( GUIContent.none, string.Concat( obj.elementType, "[", obj.elements.Length, "] array" ) );
			}
			else if( clipboardValue is GenericObjectClipboard )
				EditorGUILayout.TextField( GUIContent.none, ( (GenericObjectClipboard) clipboardValue ).type + " object" );
			else if( clipboardValue is ManagedObjectClipboard )
				EditorGUILayout.TextField( GUIContent.none, ( (ManagedObjectClipboard) clipboardValue ).type + " object (SerializeField)" );
			else if( clipboardValue is GameObjectHierarchyClipboard )
				EditorGUILayout.TextField( GUIContent.none, ( (GameObjectHierarchyClipboard) clipboardValue ).name + " (Complete GameObject)" );
			else if( clipboardValue is ComponentGroupClipboard )
				EditorGUILayout.TextField( GUIContent.none, ( (ComponentGroupClipboard) clipboardValue ).name + " (Multiple Components)" );
			else if( clipboardValue is AssetFilesClipboard )
				EditorGUILayout.TextField( GUIContent.none, ( (AssetFilesClipboard) clipboardValue ).paths[0] + " (Asset File)" );
			else if( clipboard.RootValue is SceneObjectReference )
				EditorGUILayout.TextField( GUIContent.none, clipboard.RootUnityObjectType.Name + " object (Scene Object)" );
			else if( clipboard.RootValue is AssetReference )
				EditorGUILayout.TextField( GUIContent.none, clipboard.RootUnityObjectType.Name + " object (Asset)" );
			else
				EditorGUILayout.TextField( GUIContent.none, clipboard.RootValue.GetType().Name + " object" );

			EditorGUI.indentLevel--;
			GUILayout.EndVertical();
		}

		private void SetActiveClipboard( object obj )
		{
			int index = (int) obj;
			if( index < clipboard.Count )
				ActiveClipboardIndex = index;
		}

		public static void AddToClipboard( SerializedProperty prop )
		{
			object clipboard = prop.CopyValue();
			if( clipboard != null )
				AddToClipboard( clipboard, prop.name, string.Concat( Utilities.GetDetailedObjectName( prop.serializedObject.targetObject ), ".", prop.propertyPath.Replace( ".Array.data[", "[" ) ), prop.serializedObject.targetObject );
		}

		public static void AddToClipboard( object obj, string label, Object context )
		{
			AddToClipboard( obj, null, label, context );
		}

		public static void AddToClipboard( object obj, string propertyName, string label, Object context )
		{
			if( obj == null || obj.Equals( null ) )
				return;

			LoadClipboard();

			if( clipboard.Count >= CLIPBOARD_CAPACITY )
			{
				clipboard.RemoveAt( 0 );

				if( mainWindow )
					mainWindow.clipboardValues.RemoveAt( 0 );
			}

			clipboard.Add( new SerializedClipboard( obj, context, propertyName, label ) );
			ActiveClipboardIndex = clipboard.Count - 1;

			if( mainWindow )
			{
				mainWindow.clipboardValues.Add( clipboard[clipboard.Count - 1].RootValue.GetClipboardObject( null ) );
				mainWindow.Repaint();
			}

			// Call SaveClipboard in the next frame because sometimes AddToClipboard can be called in a batch (e.g. drag & drop,
			// context menu) and we don't want to execute multiple file save operations in the same frame for no reason
			EditorApplication.update -= SaveClipboardDelayed;
			EditorApplication.update += SaveClipboardDelayed;
		}

		public static void RemoveClipboard( SerializedClipboard clipboard )
		{
			int index = PasteBinWindow.clipboard.IndexOf( clipboard );
			if( index >= 0 )
			{
				RemoveClipboard( index );

				if( mainWindow )
					mainWindow.Repaint();
			}
		}

		private static void RemoveClipboard( object obj )
		{
			int index = (int) obj;
			if( index < clipboard.Count )
			{
				clipboard.RemoveAt( index );
				if( mainWindow )
					mainWindow.clipboardValues.RemoveAt( index );

				if( ActiveClipboardIndex > 0 && ActiveClipboardIndex >= clipboard.Count )
					ActiveClipboardIndex = clipboard.Count - 1;
			}

			SaveClipboard();
		}

		private void ClearClipboard()
		{
			clipboard.Clear();
			clipboardValues.Clear();

			ActiveClipboardIndex = 0;
			SaveClipboard();
		}

		private void CopyClipboardToSystemBuffer( object obj )
		{
			int index = (int) obj;
			if( index < clipboard.Count )
			{
				using( MemoryStream stream = new MemoryStream() )
				using( BinaryWriter writer = new BinaryWriter( stream ) )
				{
					clipboard[index].Serialize( writer );
					GUIUtility.systemCopyBuffer = "Inspect+" + Convert.ToBase64String( stream.ToArray() );
				}
			}
		}

		private void PasteClipboardFromSystemBuffer( object obj )
		{
			int index = (int) obj;
			string systemBuffer = GUIUtility.systemCopyBuffer;
			if( string.IsNullOrEmpty( systemBuffer ) || !systemBuffer.StartsWith( "Inspect+", StringComparison.Ordinal ) )
				return;

			using( MemoryStream stream = new MemoryStream( Convert.FromBase64String( systemBuffer.Substring( 8 ) ) ) )
			using( BinaryReader reader = new BinaryReader( stream ) )
			{
				SerializedClipboard _clipboard = new SerializedClipboard( reader );

				if( clipboard.Count >= CLIPBOARD_CAPACITY )
				{
					clipboard.RemoveAt( 0 );
					clipboardValues.RemoveAt( 0 );
				}

				clipboard.Insert( index, _clipboard );
				clipboardValues.Insert( index, _clipboard.RootValue.GetClipboardObject( null ) );

				ActiveClipboardIndex = index;

				Repaint();
				SaveClipboard();
			}
		}

		private void ShowAboutDialog()
		{
			EditorUtility.DisplayDialog( "Paste Bin", "Paste Bin save file is located at: " + ClipboardSavePath + ".\n\nThe same file is used in all Unity projects on this computer.", "OK" );
		}

		public static List<SerializedClipboard> GetSerializedClipboards()
		{
			LoadClipboard();
			return clipboard;
		}

		private static void SaveClipboardDelayed()
		{
			EditorApplication.update -= SaveClipboardDelayed;
			SaveClipboard();
		}

		private static void SaveClipboard()
		{
			try
			{
				Directory.CreateDirectory( Path.GetDirectoryName( ClipboardSavePath ) );

				using( FileStream stream = new FileStream( ClipboardSavePath, FileMode.Create, FileAccess.Write, FileShare.None ) )
				using( BinaryWriter writer = new BinaryWriter( stream ) )
				{
					writer.Write( clipboard.Count );

					// Writing the clipboard data in reverse order allows us to access the latest clipboard entry
					// immediately while reading the clipboard data. Then, we can skip the rest of the clipboard
					// data when possible (i.e. when loadActiveClipboardOnly=true in LoadClipboard)
					for( int i = clipboard.Count - 1; i >= 0; i-- )
						clipboard[i].Serialize( writer );
				}

				clipboardLastDateTime = File.GetLastWriteTimeUtc( ClipboardSavePath );
			}
			catch( Exception e )
			{
				Debug.LogException( e );
			}
		}

		private static bool LoadClipboard( bool loadActiveClipboardOnly = false )
		{
			if( EditorApplication.timeSinceStartup - clipboardLastCheckTime < CLIPBOARD_REFRESH_MIN_COOLDOWN )
				return false;

			clipboardLastCheckTime = EditorApplication.timeSinceStartup;

			FileInfo saveFile = new FileInfo( ClipboardSavePath );
			if( !saveFile.Exists )
				return false;

			// Don't reload clipboard if it is up-to-date
			bool shouldForceReload = !loadActiveClipboardOnly && loadedActiveClipboardOnly;
			if( !shouldForceReload && saveFile.LastWriteTimeUtc <= clipboardLastDateTime )
				return false;

			clipboardLastDateTime = saveFile.LastWriteTimeUtc;
			clipboard.Clear();

			try
			{
				using( FileStream stream = new FileStream( ClipboardSavePath, FileMode.Open, FileAccess.Read, FileShare.Read ) )
				using( BinaryReader reader = new BinaryReader( stream ) )
				{
					int clipboardSize = reader.ReadInt32();
					if( loadActiveClipboardOnly && ActiveClipboardIndex == clipboardSize - 1 )
					{
						// This is the case most of the time
						loadedActiveClipboardOnly = true;

						clipboard.Add( new SerializedClipboard( reader ) );

						// No need to deserialize the rest of the clipboard data
						for( int i = 1; i < clipboardSize; i++ )
							clipboard.Add( null );
					}
					else
					{
						loadedActiveClipboardOnly = false;

						for( int i = 0; i < clipboardSize; i++ )
							clipboard.Add( new SerializedClipboard( reader ) );
					}

					// We are writing the clipboard data in reverse order in SaveClipboard
					clipboard.Reverse();
				}
			}
			catch( IOException e )
			{
				Debug.LogException( e );
			}
			catch( Exception e )
			{
				Debug.LogWarning( "Couldn't load saved clipboard data (probably save format has changed in an update).\n" + e.ToString() );

				clipboard.Clear();
				saveFile.Delete();
			}

			if( mainWindow )
			{
				mainWindow.clipboardValues.Clear();
				for( int i = 0; i < clipboard.Count; i++ )
					mainWindow.clipboardValues.Add( clipboard[i].RootValue.GetClipboardObject( null ) );
			}

			return true;
		}
	}
}