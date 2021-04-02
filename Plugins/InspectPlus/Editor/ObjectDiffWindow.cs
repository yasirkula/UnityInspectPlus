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