using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InspectPlusNamespace
{
	public class ObjectDiffWindow : EditorWindow
	{
		private enum DiffType { Same, Different, Obj1Extra, Obj2Extra, DifferentChildren };

		// Diffs are stored in a tree consisting of DiffNodes. Each DiffNode contains SerializedProperties with the same propertyPath from compared objects,
		// or a single SerializedProperty if that SerializedProperty's propertyPath doesn't exist on the other object
		private class DiffNode
		{
			public DiffType type;
			public DiffNode[] children;
			public SerializedProperty prop1, prop2;

			public DiffNode( DiffType type, SerializedProperty prop1, SerializedProperty prop2 )
			{
				this.type = type;
				this.prop1 = prop1;
				this.prop2 = prop2;
			}

			public void SetExpandedState()
			{
				if( prop1 != null )
					prop1.isExpanded = type == DiffType.DifferentChildren;
				if( prop2 != null )
					prop2.isExpanded = type == DiffType.DifferentChildren;

				if( children != null )
				{
					for( int i = 0; i < children.Length; i++ )
						children[i].SetExpandedState();
				}
			}
		}

		private const float DIFF_RESULTS_EDGE_PADDING = 5f;
		private const float COPY_VALUE_BUTTON_PADDING = 3f;
		private const float COPY_VALUE_BUTTON_WIDTH = 20f;

		private readonly Color COLUMN1_COLOR = new Color32( 0, 0, 0, 0 );
		private readonly Color COLUMN2_COLOR = new Color32( 128, 128, 128, 25 );
		private readonly Color DIFFERENT_PROPERTY_COLOR_LIGHT_SKIN = new Color32( 255, 255, 0, 100 );
		private readonly Color DIFFERENT_PROPERTY_COLOR_DARK_SKIN = new Color32( 255, 255, 0, 40 );
		private readonly Color MISSING_PROPERTY_COLOR = new Color32( 255, 0, 0, 100 );

		private readonly GUIContent COPY_TO_OBJ1_BUTTON = new GUIContent( "<", "Copy the value from right to left" );
		private readonly GUIContent COPY_TO_OBJ2_BUTTON = new GUIContent( ">", "Copy the value from left to right" );

#pragma warning disable 0649
		[SerializeField] // Needed to access these properties via SerializedObject
		private Object obj1, obj2;
		[SerializeField] // Preserve diffed objects between Unity sessions
		private Object diffedObj1, diffedObj2;
		private SerializedObject diffedSO1, diffedSO2;

		[SerializeField]
		private bool showSameValues = true;
#pragma warning restore 0649

		private DiffNode rootDiffNode;

		private SerializedObject windowSerialized;

		private Rect scrollViewRect = new Rect();
		private Vector2 scrollViewRange;
		private Vector2 scrollPosition;

		public static new void Show()
		{
			ObjectDiffWindow window = GetWindow<ObjectDiffWindow>();
			window.titleContent = new GUIContent( "Diff" );
			window.minSize = new Vector2( 500f, 150f );
			( (EditorWindow) window ).Show();
		}

		private void OnEnable()
		{
			windowSerialized = new SerializedObject( this );

			// Easiest way to preserve data between assembly reloads is to recalculate the diff
			RefreshDiff();

			Undo.undoRedoPerformed -= RefreshDiff;
			Undo.undoRedoPerformed += RefreshDiff;
		}

		private void OnDisable()
		{
			Undo.undoRedoPerformed -= RefreshDiff;
		}

		private void RefreshDiff()
		{
			if( diffedObj1 && diffedObj2 )
				CalculateDiff( diffedObj1, diffedObj2, false );

			Repaint();
		}

		private void OnGUI()
		{
			scrollViewRect.x = 0f; // We must add DIFF_RESULTS_EDGE_PADDING after BeginScrollView, not here
			scrollViewRect.width = EditorGUIUtility.currentViewWidth - 2f * DIFF_RESULTS_EDGE_PADDING;

			// If vertical scrollbar is visible, decrease width
			if( position.height < scrollViewRect.height )
				scrollViewRect.width -= GUI.skin.verticalScrollbar.CalcSize( GUIContent.none ).x;

			scrollPosition = GUI.BeginScrollView( new Rect( Vector2.zero, position.size ), scrollPosition, scrollViewRect );

			scrollViewRect.x = DIFF_RESULTS_EDGE_PADDING;
			scrollViewRect.height = DIFF_RESULTS_EDGE_PADDING; // We'll recalculate height inside scroll view

			GUI.Box( GetRect( EditorGUIUtility.singleLineHeight * 1.25f, 2f ), "OBJECTS" );

			// Show obj1 and obj2 properties via PropertyField so that right clicking the property shows Copy/Paste context menu
			Rect rect = GetRect( EditorGUIUtility.singleLineHeight, 3f );
			windowSerialized.Update();
			EditorGUI.PropertyField( new Rect( rect.x, rect.y, rect.width * 0.5f, rect.height ), windowSerialized.FindProperty( "obj1" ), GUIContent.none, false );
			EditorGUI.PropertyField( new Rect( rect.x + rect.width * 0.5f, rect.y, rect.width * 0.5f, rect.height ), windowSerialized.FindProperty( "obj2" ), GUIContent.none, false );
			windowSerialized.ApplyModifiedPropertiesWithoutUndo();

			GUI.enabled = obj1 && obj2 && obj1 != obj2;
			if( GUI.Button( GetRect( EditorGUIUtility.singleLineHeight * 1.5f ), "Calculate Diff" ) )
			{
				CalculateDiff( obj1, obj2, true );
				GUIUtility.ExitGUI();
			}
			GUI.enabled = true;

			if( diffedSO1 != null && diffedSO2 != null )
			{
				if( diffedSO1.targetObject )
					diffedSO1.Update();
				if( diffedSO2.targetObject )
					diffedSO2.Update();

				scrollViewRect.height += 10f;
				EditorGUI.HelpBox( GetRect( EditorGUIUtility.singleLineHeight * 2f, 2f ), "Diff results are NOT refreshed automatically.", MessageType.Info );

				// Paint each column with different color
				EditorGUI.DrawRect( new Rect( scrollViewRect.x, scrollViewRect.yMax, scrollViewRect.width * 0.5f, 10000f ), COLUMN1_COLOR );
				EditorGUI.DrawRect( new Rect( scrollViewRect.x + scrollViewRect.width * 0.5f, scrollViewRect.yMax, scrollViewRect.width * 0.5f, 10000f ), COLUMN2_COLOR );

				// Draw diffedObj1 and diffedObj2 because user might change obj1 and obj2 after calculating the diff (these properties also support Copy/Paste context menu)
				rect = GetRect( EditorGUIUtility.singleLineHeight, 3f );

				EditorGUI.PropertyField( new Rect( rect.x, rect.y, rect.width * 0.5f, rect.height ), windowSerialized.FindProperty( "diffedObj1" ), GUIContent.none, false );
				EditorGUI.PropertyField( new Rect( rect.x + rect.width * 0.5f, rect.y, rect.width * 0.5f, rect.height ), windowSerialized.FindProperty( "diffedObj2" ), GUIContent.none, false );

				showSameValues = GUI.Toggle( GetRect( EditorGUIUtility.singleLineHeight * 1.5f, 3f ), showSameValues, showSameValues ? "Show Same Values: ON" : "Show Same Values: OFF", GUI.skin.button );

				// Draw diff results
				DrawDiffNode( rootDiffNode );

				// Apply any changes made to the displayed SerializedProperties
				if( diffedSO1.targetObject )
					diffedSO1.ApplyModifiedProperties();
				if( diffedSO2.targetObject )
					diffedSO2.ApplyModifiedProperties();
			}

			scrollViewRect.height += DIFF_RESULTS_EDGE_PADDING;
			GUI.EndScrollView();
		}

		private void DrawDiffNode( DiffNode node )
		{
			scrollViewRange = new Vector2( scrollPosition.y, scrollPosition.y + position.height );

			// Diff nodes' expandable SerializedProperties should be expanded or collapsed simultaneously
			bool prop1Expanded = node.prop1 != null ? node.prop1.isExpanded : false;
			bool prop2Expanded = node.prop2 != null ? node.prop2.isExpanded : false;
			if( prop1Expanded != prop2Expanded && node.prop1 != null && node.prop2 != null )
				node.prop2.isExpanded = prop2Expanded = prop1Expanded;

			Rect prop1Rect, prop2Rect;
			if( node.type == DiffType.DifferentChildren )
			{
				bool isRootNode = node.prop1 == null || node.prop2 == null;
				if( !isRootNode )
				{
					// Highlight the background only if the SerializedProperty isn't expanded (to let the user know that there is a diff inside this DiffNode)
					if( GetDiffRects( Mathf.Max( EditorGUI.GetPropertyHeight( node.prop1, null, false ), EditorGUI.GetPropertyHeight( node.prop2, null, false ) ), prop1Expanded || prop2Expanded ? Color.clear : ( EditorGUIUtility.isProSkin ? DIFFERENT_PROPERTY_COLOR_DARK_SKIN : DIFFERENT_PROPERTY_COLOR_LIGHT_SKIN ), out prop1Rect, out prop2Rect ) )
					{
						DrawCopyValueButtons( node, prop1Rect, prop2Rect );

						EditorGUI.PropertyField( prop1Rect, node.prop1, false );
						EditorGUI.PropertyField( prop2Rect, node.prop2, false );
					}
				}

				// Don't draw child nodes if SerializedProperty is collapsed
				if( isRootNode || prop1Expanded )
				{
					if( !isRootNode )
						EditorGUI.indentLevel++;

					DiffNode[] children = node.children;
					for( int i = 0; i < children.Length; i++ )
					{
						try
						{
							DrawDiffNode( children[i] );
						}
						catch( InvalidOperationException )
						{
							// A DiffNode's SerializedProperty became invalid (e.g. if it was an array element, that array element is now deleted)
							// Remove the problematic DiffNode and repaint the window to reflect the changes
							if( children.Length == 1 )
							{
								node.type = DiffType.Same;
								node.children = null;
							}
							else
							{
								RemoveArrayElement( ref children, i );

								DiffType? diffType = GetCombinedDiffType( children );
								if( diffType.HasValue && diffType.Value != DiffType.DifferentChildren )
								{
									// All children have the same diff type, transfer that diff type to this parent diff node
									node.type = diffType.Value;
									node.children = null;
								}
							}

							EditorApplication.delayCall += Repaint;
							GUIUtility.ExitGUI();
						}
					}

					if( !isRootNode )
						EditorGUI.indentLevel--;
				}
			}
			else if( node.type == DiffType.Obj1Extra )
			{
				if( GetDiffRects( EditorGUI.GetPropertyHeight( node.prop1, null, true ), Color.clear, out prop1Rect, out prop2Rect ) )
				{
					EditorGUI.PropertyField( prop1Rect, node.prop1, true );
					EditorGUI.DrawRect( prop2Rect, MISSING_PROPERTY_COLOR );
				}
			}
			else if( node.type == DiffType.Obj2Extra )
			{
				if( GetDiffRects( EditorGUI.GetPropertyHeight( node.prop2, null, true ), Color.clear, out prop1Rect, out prop2Rect ) )
				{
					EditorGUI.DrawRect( prop1Rect, MISSING_PROPERTY_COLOR );
					EditorGUI.PropertyField( prop2Rect, node.prop2, true );
				}
			}
			else if( showSameValues || node.type != DiffType.Same )
			{
				if( GetDiffRects( Mathf.Max( EditorGUI.GetPropertyHeight( node.prop1, null, true ), EditorGUI.GetPropertyHeight( node.prop2, null, true ) ), node.type == DiffType.Same ? Color.clear : ( EditorGUIUtility.isProSkin ? DIFFERENT_PROPERTY_COLOR_DARK_SKIN : DIFFERENT_PROPERTY_COLOR_LIGHT_SKIN ), out prop1Rect, out prop2Rect ) )
				{
					if( node.type != DiffType.Same )
						DrawCopyValueButtons( node, prop1Rect, prop2Rect );

					EditorGUI.PropertyField( prop1Rect, node.prop1, true );
					EditorGUI.PropertyField( prop2Rect, node.prop2, true );
				}
			}

			// Diff nodes' expandable SerializedProperties should be expanded or collapsed simultaneously
			if( node.prop1 != null && node.prop1.isExpanded != prop1Expanded )
			{
				if( node.prop2 != null )
					node.prop2.isExpanded = node.prop1.isExpanded;
			}
			else if( node.prop2 != null && node.prop2.isExpanded != prop2Expanded )
			{
				if( node.prop1 != null )
					node.prop1.isExpanded = prop1Expanded = node.prop2.isExpanded;
			}
		}

		// Draw buttons to copy values from one SerializedProperty to another
		private void DrawCopyValueButtons( DiffNode node, Rect prop1Rect, Rect prop2Rect )
		{
			if( !node.prop1.serializedObject.targetObject || !node.prop2.serializedObject.targetObject )
				return;

			if( GUI.Button( new Rect( prop1Rect.xMax + COPY_VALUE_BUTTON_PADDING, prop1Rect.y, COPY_VALUE_BUTTON_WIDTH, EditorGUIUtility.singleLineHeight ), COPY_TO_OBJ1_BUTTON ) )
			{
				object obj2Value = node.prop2.CopyValue();
				if( node.prop1.CanPasteValue( obj2Value, true ) )
				{
					node.prop1.PasteValue( obj2Value, true );

					RefreshDiff();
					GUIUtility.ExitGUI();
				}
			}

			if( GUI.Button( new Rect( prop2Rect.x - COPY_VALUE_BUTTON_WIDTH - COPY_VALUE_BUTTON_PADDING, prop2Rect.y, COPY_VALUE_BUTTON_WIDTH, EditorGUIUtility.singleLineHeight ), COPY_TO_OBJ2_BUTTON ) )
			{
				object obj1Value = node.prop1.CopyValue();
				if( node.prop2.CanPasteValue( obj1Value, true ) )
				{
					node.prop2.PasteValue( obj1Value, true );

					RefreshDiff();
					GUIUtility.ExitGUI();
				}
			}
		}

		// Calculate Rects to draw DiffNodes' SerializedProperties into
		private bool GetDiffRects( float propertyHeight, Color backgroundColor, out Rect prop1Rect, out Rect prop2Rect )
		{
			Rect rect = GetRect( propertyHeight + EditorGUIUtility.standardVerticalSpacing );

			// Cull SerializedProperty if it isn't visible
			if( rect.yMax < scrollViewRange.x || rect.y > scrollViewRange.y )
			{
				prop1Rect = new Rect();
				prop2Rect = new Rect();

				return false;
			}

			float halfWidth = rect.width * 0.5f;

			if( backgroundColor.a > 0f )
				EditorGUI.DrawRect( rect, backgroundColor );

			rect.yMin += EditorGUIUtility.standardVerticalSpacing;
			rect.width = halfWidth - COPY_VALUE_BUTTON_WIDTH - COPY_VALUE_BUTTON_PADDING;
			prop1Rect = rect;

			rect.x += halfWidth + COPY_VALUE_BUTTON_WIDTH + COPY_VALUE_BUTTON_PADDING;
			prop2Rect = rect;

			return true;
		}

		private Rect GetRect( float height )
		{
			Rect result = new Rect( scrollViewRect.x, scrollViewRect.yMax, scrollViewRect.width, height );
			scrollViewRect.height += height;

			return result;
		}

		private Rect GetRect( float height, float extraSpace )
		{
			Rect result = new Rect( scrollViewRect.x, scrollViewRect.yMax, scrollViewRect.width, height );
			scrollViewRect.height += height + extraSpace;

			return result;
		}

		private void CalculateDiff( Object obj1, Object obj2, bool calculateElapsedTime )
		{
			if( !obj1 || !obj2 || obj1 == obj2 )
				return;

			double startTime = calculateElapsedTime ? EditorApplication.timeSinceStartup : 0.0;
			bool calculatingNewDiff = obj1 != diffedObj1 || obj2 != diffedObj2;

			diffedObj1 = obj1;
			diffedObj2 = obj2;

			diffedSO1 = new SerializedObject( obj1 );
			diffedSO2 = new SerializedObject( obj2 );

			rootDiffNode = new DiffNode( DiffType.DifferentChildren, null, null );

			List<DiffNode> _diffNodes;
			CompareProperties( diffedSO1.EnumerateDirectChildren(), diffedSO2.EnumerateDirectChildren(), out _diffNodes );

			rootDiffNode.children = _diffNodes.ToArray();

			if( calculatingNewDiff )
				rootDiffNode.SetExpandedState();

			if( calculateElapsedTime )
				Debug.Log( string.Concat( "Calculated diff in ", ( EditorApplication.timeSinceStartup - startTime ).ToString( "F3" ), " seconds." ) );
		}

		private void CompareProperties( IEnumerable<SerializedProperty> properties1, IEnumerable<SerializedProperty> properties2, out List<DiffNode> diffNodes )
		{
			diffNodes = new List<DiffNode>( 8 );
			Dictionary<string, SerializedProperty> childProperties2 = new Dictionary<string, SerializedProperty>( 32 );
			foreach( SerializedProperty property in properties2 )
			{
				string propertyPath = property.propertyPath;
				if( propertyPath != "m_Script" )
					childProperties2[propertyPath] = property.Copy();
			}

			foreach( SerializedProperty childProp1 in properties1 )
			{
				string propertyPath = childProp1.propertyPath;
				if( propertyPath == "m_Script" )
					continue;

				SerializedProperty childProp2;
				if( !childProperties2.TryGetValue( propertyPath, out childProp2 ) )
					diffNodes.Add( new DiffNode( DiffType.Obj1Extra, diffedSO1.FindProperty( propertyPath ), null ) );
				else
				{
					childProperties2.Remove( propertyPath );

					if( childProp1.propertyType != SerializedPropertyType.Generic || childProp2.propertyType != SerializedPropertyType.Generic )
					{
						object childProp1Value = childProp1.CopyValue();
						object childProp2Value = childProp2.CopyValue();
						if( ( childProp1Value != null && childProp1Value.Equals( childProp2Value ) ) || ( childProp1Value == null && childProp2Value == null ) )
							diffNodes.Add( new DiffNode( DiffType.Same, diffedSO1.FindProperty( propertyPath ), diffedSO2.FindProperty( propertyPath ) ) );
						else
							diffNodes.Add( new DiffNode( DiffType.Different, diffedSO1.FindProperty( propertyPath ), diffedSO2.FindProperty( propertyPath ) ) );
					}
					else
					{
						List<DiffNode> _diffNodes;
						if( childProp1.isArray && childProp2.isArray )
							CompareProperties( EnumerateArrayElements( childProp1.Copy() ), EnumerateArrayElements( childProp2.Copy() ), out _diffNodes );
#if UNITY_2017_1_OR_NEWER
						else if( childProp1.isFixedBuffer && childProp2.isFixedBuffer )
							CompareProperties( EnumerateFixedBufferElements( childProp1.Copy() ), EnumerateFixedBufferElements( childProp2.Copy() ), out _diffNodes );
#endif
						else if( childProp1.hasChildren && childProp2.hasChildren )
							CompareProperties( childProp1.EnumerateDirectChildren(), childProp2.EnumerateDirectChildren(), out _diffNodes );
						else
						{
							diffNodes.Add( new DiffNode( childProp1.hasChildren || childProp2.hasChildren ? DiffType.Different : DiffType.Same, diffedSO1.FindProperty( propertyPath ), diffedSO2.FindProperty( propertyPath ) ) );
							continue;
						}

						if( _diffNodes.Count == 0 )
							diffNodes.Add( new DiffNode( DiffType.Same, diffedSO1.FindProperty( propertyPath ), diffedSO2.FindProperty( propertyPath ) ) );
						else
						{
							DiffType? diffType = GetCombinedDiffType( _diffNodes );
							if( !diffType.HasValue || diffType.Value == DiffType.DifferentChildren )
								diffNodes.Add( new DiffNode( DiffType.DifferentChildren, diffedSO1.FindProperty( propertyPath ), diffedSO2.FindProperty( propertyPath ) ) { children = _diffNodes.ToArray() } );
							else
							{
								// If childProp1 and childProp2's diff results are grouped in a single category, replace those results with a single root DiffNode
								switch( diffType.Value )
								{
									case DiffType.Same: diffNodes.Add( new DiffNode( DiffType.Same, diffedSO1.FindProperty( propertyPath ), diffedSO2.FindProperty( propertyPath ) ) ); break;
									case DiffType.Different:
									case DiffType.Obj1Extra:
									case DiffType.Obj2Extra: diffNodes.Add( new DiffNode( DiffType.Different, diffedSO1.FindProperty( propertyPath ), diffedSO2.FindProperty( propertyPath ) ) ); break;
								}
							}
						}
					}
				}
			}

			foreach( KeyValuePair<string, SerializedProperty> kvPair in childProperties2 )
				diffNodes.Add( new DiffNode( DiffType.Obj2Extra, null, diffedSO2.FindProperty( kvPair.Key ) ) );
		}

		private IEnumerable<SerializedProperty> EnumerateArrayElements( SerializedProperty property )
		{
			for( int i = 0, length = property.arraySize; i < length; i++ )
				yield return property.GetArrayElementAtIndex( i );
		}

#if UNITY_2017_1_OR_NEWER
		private IEnumerable<SerializedProperty> EnumerateFixedBufferElements( SerializedProperty property )
		{
			for( int i = 0, length = property.fixedBufferSize; i < length; i++ )
				yield return property.GetFixedBufferElementAtIndex( i );
		}
#endif

		private DiffType? GetCombinedDiffType( IList<DiffNode> nodes )
		{
			DiffType? diffType = nodes[0].type;
			for( int j = 0; j < nodes.Count; j++ )
			{
				if( nodes[j].type != diffType.Value )
					return null;
			}

			return diffType;
		}

		private void RemoveArrayElement<T>( ref T[] array, int index )
		{
			for( int i = index + 1; i < array.Length; i++ )
				array[i - 1] = array[i];

			Array.Resize( ref array, array.Length - 1 );
		}
	}
}

/* PREVIOUS ARCHIVED METHOD

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InspectPlusNamespace
{
	public class ObjectDiffWindow : EditorWindow
	{
		private const int DIFF_RESULTS_EDGE_PADDING = 5;
		private const float DIFF_RESULTS_CENTER_PADDING = 3f;
		private const float DIFF_RESULTS_SPACING = 5f;
		private const float DIFF_LABELS_SPACING = 2f;

		private readonly Color ALTERNATING_ROW_COLOR = new Color32( 128, 128, 128, 40 );

		private readonly GUIContent COPY_TO_OBJ1_BUTTON = new GUIContent( "<-", "Copy the value from right to left" );
		private readonly GUIContent COPY_TO_OBJ2_BUTTON = new GUIContent( "->", "Copy the value from left to right" );

		private readonly GUILayoutOption HORIZONTAL_LINE_HEIGHT = GUILayout.Height( 1f );
		private readonly GUILayoutOption EXPAND_WIDTH = GUILayout.ExpandWidth( true );

#pragma warning disable 0649
		[SerializeField] // Needed to access these properties via SerializedObject
		private Object obj1, obj2;
		[SerializeField] // Preserve diffed objects between Unity sessions
		private Object diffedObj1, diffedObj2;
		private SerializedObject diffedSO1, diffedSO2;
#pragma warning restore 0649

		private SerializedProperty[][] sameProperties, differentProperties;
		private SerializedProperty[] obj1ExtraProperties, obj2ExtraProperties;
		private bool showSameProperties = true, showDifferentProperties = true, showObj1ExtraProperties = true, showObj2ExtraProperties = true;
		private string obj1ExtraPropertiesTitle, obj2ExtraPropertiesTitle;

		private GUIStyle boldMiddleAlignedLabelStyle;
		private GUIStyle diffBackgroundStyle;

		private SerializedObject windowSerialized;
		private Vector2 scrollPosition;

		public static new void Show()
		{
			ObjectDiffWindow window = GetWindow<ObjectDiffWindow>();
			window.titleContent = new GUIContent( "Diff" );
			window.minSize = new Vector2( 300f, 150f );
			( (EditorWindow) window ).Show();
		}

		private void OnEnable()
		{
			windowSerialized = new SerializedObject( this );

			// Easiest way to preserve data between assembly reloads is to recalculate the diff
			if( diffedObj1 && diffedObj2 )
				CalculateDiff( diffedObj1, diffedObj2 );

			Undo.undoRedoPerformed -= Repaint;
			Undo.undoRedoPerformed += Repaint;
		}

		private void OnDisable()
		{
			Undo.undoRedoPerformed -= Repaint;
		}

		private void OnGUI()
		{
			if( boldMiddleAlignedLabelStyle == null )
				boldMiddleAlignedLabelStyle = new GUIStyle( EditorStyles.boldLabel ) { alignment = TextAnchor.MiddleCenter };

			if( diffBackgroundStyle == null || !diffBackgroundStyle.normal.background )
			{
				// Create 2x1 point-filtered Texture that will be used to create alternating column colors
				Texture2D diffBackground = new Texture2D( 2, 1, TextureFormat.RGBA32, false ) { filterMode = FilterMode.Point };
				diffBackground.SetPixels32( new Color32[2] { new Color32( 0, 0, 0, 0 ), new Color32( 128, 128, 128, 25 ) } );
				diffBackground.Apply( false, true );
				diffBackground.hideFlags = HideFlags.HideAndDontSave;

				diffBackgroundStyle = new GUIStyle() { normal = new GUIStyleState() { background = diffBackground }, padding = new RectOffset( DIFF_RESULTS_EDGE_PADDING, DIFF_RESULTS_EDGE_PADDING, 0, 0 ) };
			}

			scrollPosition = EditorGUILayout.BeginScrollView( scrollPosition );

			GUILayout.Box( "OBJECTS", EXPAND_WIDTH );

			GUILayout.BeginHorizontal();
			windowSerialized.Update(); // Show obj1 and obj2 properties via PropertyField so that right clicking the property shows Copy/Paste context menu
			EditorGUILayout.PropertyField( windowSerialized.FindProperty( "obj1" ), GUIContent.none, false );
			EditorGUILayout.PropertyField( windowSerialized.FindProperty( "obj2" ), GUIContent.none, false );
			windowSerialized.ApplyModifiedPropertiesWithoutUndo();
			GUILayout.EndHorizontal();

			GUI.enabled = obj1 && obj2 && obj1 != obj2;
			if( GUILayout.Button( "Calculate Diff", GUILayout.Height( EditorGUIUtility.singleLineHeight * 1.5f ) ) )
			{
				CalculateDiff( obj1, obj2 );
				GUIUtility.ExitGUI();
			}
			GUI.enabled = true;

			if( diffedSO1 != null && diffedSO2 != null )
			{
				if( diffedSO1.targetObject )
					diffedSO1.Update();
				if( diffedSO2.targetObject )
					diffedSO2.Update();

				DrawHorizontalLine();

				GUILayout.BeginVertical( diffBackgroundStyle );

				// Draw diffed Objects again in case user changes obj1 and obj2 afterwards
				GUI.enabled = false;
				GUILayout.BeginHorizontal();
				EditorGUILayout.ObjectField( GUIContent.none, diffedObj1, typeof( Object ), true );
				EditorGUILayout.ObjectField( GUIContent.none, diffedObj2, typeof( Object ), true );
				GUILayout.EndHorizontal();
				GUI.enabled = true;

				if( differentProperties.Length > 0 )
				{
					showDifferentProperties = GUILayout.Toggle( showDifferentProperties, "Differences", GUI.skin.button, GUILayout.Height( EditorGUIUtility.singleLineHeight * 1.5f ) );
					if( showDifferentProperties )
					{
						for( int i = 0; i < differentProperties.Length; i++ )
						{
							try
							{
								DrawDiff( differentProperties[i], i % 2 == 1 );
							}
							catch( InvalidOperationException )
							{
								// A SerializedProperty became invalid (e.g. if it was an array element, that array element is now deleted)
								// Remove the problematic SerializedProperty and repaint the window to reflect the changes
								RemoveArrayElement( ref differentProperties, i );
								EditorApplication.delayCall += Repaint;
								GUIUtility.ExitGUI();
							}
						}
					}

					DrawHorizontalLine();
				}

				if( sameProperties.Length > 0 )
				{
					showSameProperties = GUILayout.Toggle( showSameProperties, "Similarities", GUI.skin.button, GUILayout.Height( EditorGUIUtility.singleLineHeight * 1.5f ) );
					if( showSameProperties )
					{
						for( int i = 0; i < sameProperties.Length; i++ )
						{
							try
							{
								DrawDiff( sameProperties[i], i % 2 == 1 );
							}
							catch( InvalidOperationException )
							{
								RemoveArrayElement( ref sameProperties, i );
								EditorApplication.delayCall += Repaint;
								GUIUtility.ExitGUI();
							}
						}
					}

					DrawHorizontalLine();
				}

				if( obj1ExtraProperties.Length > 0 )
				{
					showObj1ExtraProperties = GUILayout.Toggle( showObj1ExtraProperties, obj1ExtraPropertiesTitle, GUI.skin.button, GUILayout.Height( EditorGUIUtility.singleLineHeight * 1.5f ) );
					if( showObj1ExtraProperties )
					{
						for( int i = 0; i < obj1ExtraProperties.Length; i++ )
						{
							try
							{
								DrawDiff( obj1ExtraProperties[i], true, i % 2 == 1 );
							}
							catch( InvalidOperationException )
							{
								RemoveArrayElement( ref obj1ExtraProperties, i );
								EditorApplication.delayCall += Repaint;
								GUIUtility.ExitGUI();
							}
						}
					}

					DrawHorizontalLine();
				}

				if( obj2ExtraProperties.Length > 0 )
				{
					showObj2ExtraProperties = GUILayout.Toggle( showObj2ExtraProperties, obj2ExtraPropertiesTitle, GUI.skin.button, GUILayout.Height( EditorGUIUtility.singleLineHeight * 1.5f ) );
					if( showObj2ExtraProperties )
					{
						for( int i = 0; i < obj2ExtraProperties.Length; i++ )
						{
							try
							{
								DrawDiff( obj2ExtraProperties[i], false, i % 2 == 1 );
							}
							catch( InvalidOperationException )
							{
								RemoveArrayElement( ref obj2ExtraProperties, i );
								EditorApplication.delayCall += Repaint;
								GUIUtility.ExitGUI();
							}
						}
					}

					DrawHorizontalLine();
				}

				GUILayout.EndVertical();

				if( diffedSO1.targetObject )
					diffedSO1.ApplyModifiedProperties();
				if( diffedSO2.targetObject )
					diffedSO2.ApplyModifiedProperties();
			}

			EditorGUILayout.EndScrollView();
		}

		private void DrawDiff( SerializedProperty property, bool leftAligned, bool useAlternateBackground )
		{
			Rect labelRect, prop1Rect, prop2Rect;
			GetDiffRects( EditorGUI.GetPropertyHeight( property, GUIContent.none, true ), useAlternateBackground, out labelRect, out prop1Rect, out prop2Rect );

			EditorGUI.LabelField( labelRect, property.propertyPath, boldMiddleAlignedLabelStyle );
			EditorGUI.PropertyField( leftAligned ? prop1Rect : prop2Rect, property, GUIContent.none, true );
		}

		private void DrawDiff( SerializedProperty[] properties, bool useAlternateBackground )
		{
			Rect labelRect, prop1Rect, prop2Rect;
			GetDiffRects( Mathf.Max( EditorGUI.GetPropertyHeight( properties[0], GUIContent.none, true ), EditorGUI.GetPropertyHeight( properties[1], GUIContent.none, true ) ), useAlternateBackground, out labelRect, out prop1Rect, out prop2Rect );

			EditorGUI.LabelField( labelRect, properties[0].propertyPath, boldMiddleAlignedLabelStyle );

			if( GUI.Button( new Rect( labelRect.position, new Vector2( 25f, labelRect.height ) ), COPY_TO_OBJ1_BUTTON ) && properties[0].serializedObject.targetObject )
			{
				object obj2Value = properties[1].CopyValue();
				if( properties[0].CanPasteValue( obj2Value, true ) )
					properties[0].PasteValue( obj2Value, true );
			}

			if( GUI.Button( new Rect( labelRect.xMax - 25f, labelRect.y, 25f, labelRect.height ), COPY_TO_OBJ2_BUTTON ) && properties[1].serializedObject.targetObject )
			{
				object obj1Value = properties[0].CopyValue();
				if( properties[1].CanPasteValue( obj1Value, true ) )
					properties[1].PasteValue( obj1Value, true );
			}

			EditorGUI.PropertyField( prop1Rect, properties[0], GUIContent.none, true );
			EditorGUI.PropertyField( prop2Rect, properties[1], GUIContent.none, true );
		}

		private void DrawHorizontalLine()
		{
			GUILayout.Space( 10f );
			GUILayout.Box( "", EXPAND_WIDTH, HORIZONTAL_LINE_HEIGHT );
		}

		private void GetDiffRects( float propertyHeight, bool useAlternateBackground, out Rect labelRect, out Rect prop1Rect, out Rect prop2Rect )
		{
			propertyHeight += EditorGUIUtility.singleLineHeight + DIFF_RESULTS_SPACING + DIFF_LABELS_SPACING;
			Rect rect = GUILayoutUtility.GetRect( EditorGUIUtility.fieldWidth, 10000f, propertyHeight, propertyHeight );
			float halfWidth = rect.width * 0.5f;

			if( useAlternateBackground )
				EditorGUI.DrawRect( rect, ALTERNATING_ROW_COLOR );

			rect.yMin += DIFF_RESULTS_SPACING;
			labelRect = rect;
			labelRect.height = EditorGUIUtility.singleLineHeight;

			rect.yMin += EditorGUIUtility.singleLineHeight + DIFF_LABELS_SPACING;
			rect.width = halfWidth - DIFF_RESULTS_CENTER_PADDING;
			prop1Rect = rect;

			rect.x += halfWidth + DIFF_RESULTS_CENTER_PADDING;
			prop2Rect = rect;
		}

		private void CalculateDiff( Object obj1, Object obj2 )
		{
			if( !obj1 || !obj2 || obj1 == obj2 )
				return;

			List<string> _obj1ExtraProperties, _obj2ExtraProperties, _sameProperties, _differentProperties;
			CompareProperties( new SerializedObject( obj1 ).EnumerateDirectChildren(), new SerializedObject( obj2 ).EnumerateDirectChildren(), out _obj1ExtraProperties, out _obj2ExtraProperties, out _sameProperties, out _differentProperties );

			diffedSO1 = new SerializedObject( obj1 );
			diffedSO2 = new SerializedObject( obj2 );

			_obj1ExtraProperties.Remove( "m_Script" );
			_obj2ExtraProperties.Remove( "m_Script" );
			_sameProperties.Remove( "m_Script" );
			_differentProperties.Remove( "m_Script" );

			obj1ExtraProperties = new SerializedProperty[_obj1ExtraProperties.Count];
			for( int i = 0; i < _obj1ExtraProperties.Count; i++ )
				obj1ExtraProperties[i] = diffedSO1.FindProperty( _obj1ExtraProperties[i] );

			obj2ExtraProperties = new SerializedProperty[_obj2ExtraProperties.Count];
			for( int i = 0; i < _obj2ExtraProperties.Count; i++ )
				obj2ExtraProperties[i] = diffedSO2.FindProperty( _obj2ExtraProperties[i] );

			sameProperties = new SerializedProperty[_sameProperties.Count][];
			for( int i = 0; i < _sameProperties.Count; i++ )
				sameProperties[i] = new SerializedProperty[2] { diffedSO1.FindProperty( _sameProperties[i] ), diffedSO2.FindProperty( _sameProperties[i] ) };

			differentProperties = new SerializedProperty[_differentProperties.Count][];
			for( int i = 0; i < _differentProperties.Count; i++ )
				differentProperties[i] = new SerializedProperty[2] { diffedSO1.FindProperty( _differentProperties[i] ), diffedSO2.FindProperty( _differentProperties[i] ) };

			diffedObj1 = obj1;
			diffedObj2 = obj2;

			obj1ExtraPropertiesTitle = string.Concat( "Additional properties of \"", EditorGUIUtility.ObjectContent( obj1, obj1.GetType() ).text, "\"" );
			obj2ExtraPropertiesTitle = string.Concat( "Additional properties of \"", EditorGUIUtility.ObjectContent( obj2, obj2.GetType() ).text, "\"" );
		}

		private void CompareProperties( IEnumerable<SerializedProperty> properties1, IEnumerable<SerializedProperty> properties2, out List<string> extraProperties1, out List<string> extraProperties2, out List<string> sameProperties, out List<string> differentProperties )
		{
			extraProperties1 = new List<string>( 8 );
			extraProperties2 = new List<string>( 8 );
			sameProperties = new List<string>( 8 );
			differentProperties = new List<string>( 8 );

			Dictionary<string, SerializedProperty> childProperties1 = new Dictionary<string, SerializedProperty>( 32 );
			foreach( SerializedProperty property in properties1 )
				childProperties1[property.propertyPath] = property.Copy();

			foreach( SerializedProperty childProp2 in properties2 )
			{
				SerializedProperty childProp1;
				if( !childProperties1.TryGetValue( childProp2.propertyPath, out childProp1 ) )
					extraProperties2.Add( childProp2.propertyPath );
				else
				{
					childProperties1.Remove( childProp2.propertyPath );

					if( childProp1.propertyType != SerializedPropertyType.Generic || childProp2.propertyType != SerializedPropertyType.Generic )
					{
						object childProp1Value = childProp1.CopyValue();
						object childProp2Value = childProp2.CopyValue();
						if( ( childProp1Value != null && childProp1Value.Equals( childProp2Value ) ) || ( childProp1Value == null && childProp2Value == null ) )
							sameProperties.Add( childProp1.propertyPath );
						else
							differentProperties.Add( childProp1.propertyPath );
					}
					else
					{
						List<string> _extraProperties1, _extraProperties2, _sameProperties, _differentProperties;

						if( childProp1.isArray && childProp2.isArray )
							CompareProperties( EnumerateArrayElements( childProp1.Copy() ), EnumerateArrayElements( childProp2.Copy() ), out _extraProperties1, out _extraProperties2, out _sameProperties, out _differentProperties );
#if UNITY_2017_1_OR_NEWER
						else if( childProp1.isFixedBuffer && childProp2.isFixedBuffer )
							CompareProperties( EnumerateFixedBufferElements( childProp1.Copy() ), EnumerateFixedBufferElements( childProp2.Copy() ), out _extraProperties1, out _extraProperties2, out _sameProperties, out _differentProperties );
#endif
						else if( childProp1.hasChildren && childProp2.hasChildren )
							CompareProperties( childProp1.EnumerateDirectChildren(), childProp2.EnumerateDirectChildren(), out _extraProperties1, out _extraProperties2, out _sameProperties, out _differentProperties );
						else
						{
							differentProperties.Add( childProp1.propertyPath );
							continue;
						}

						// If childProp1 and childProp2's diff results are grouped in a single category, replace those results with a single root element: childProp1.propertyPath
						if( _extraProperties1.Count == 0 && _extraProperties2.Count == 0 && _sameProperties.Count == 0 && _differentProperties.Count > 0 )
							differentProperties.Add( childProp1.propertyPath );
						else if( _extraProperties1.Count == 0 && _extraProperties2.Count == 0 && _sameProperties.Count > 0 && _differentProperties.Count == 0 )
							sameProperties.Add( childProp1.propertyPath );
						else if( _extraProperties1.Count == 0 && _extraProperties2.Count > 0 && _sameProperties.Count == 0 && _differentProperties.Count == 0 )
							extraProperties2.Add( childProp1.propertyPath );
						else if( _extraProperties1.Count > 0 && _extraProperties2.Count == 0 && _sameProperties.Count == 0 && _differentProperties.Count == 0 )
							extraProperties1.Add( childProp1.propertyPath );
						else
						{
							extraProperties1.AddRange( _extraProperties1 );
							extraProperties2.AddRange( _extraProperties2 );
							sameProperties.AddRange( _sameProperties );
							differentProperties.AddRange( _differentProperties );
						}
					}
				}
			}

			foreach( KeyValuePair<string, SerializedProperty> kvPair in childProperties1 )
				extraProperties1.Add( kvPair.Key );
		}

		private IEnumerable<SerializedProperty> EnumerateArrayElements( SerializedProperty property )
		{
			for( int i = 0, length = property.arraySize; i < length; i++ )
				yield return property.GetArrayElementAtIndex( i );
		}

#if UNITY_2017_1_OR_NEWER
		private IEnumerable<SerializedProperty> EnumerateFixedBufferElements( SerializedProperty property )
		{
			for( int i = 0, length = property.fixedBufferSize; i < length; i++ )
				yield return property.GetFixedBufferElementAtIndex( i );
		}
#endif

		private void RemoveArrayElement<T>( ref T[] array, int index )
		{
			for( int i = index + 1; i < array.Length; i++ )
				array[i - 1] = array[i];

			Array.Resize( ref array, array.Length - 1 );
		}
	}
}

 */