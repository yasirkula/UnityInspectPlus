using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InspectPlusNamespace
{
	public class DebugModeEntry
	{
		protected bool m_isExpanded;
		public bool IsExpanded
		{
			get { return m_isExpanded; }
			set
			{
				if( m_isExpanded == value )
					return;

				m_isExpanded = value;
				if( m_isExpanded )
					Refresh();
				else
				{
					Obj = null;
					forceShowNestedVariables = false;
					PoolLists();
					RefreshLabel();
				}
			}
		}

		public VariableGetterHolder Variable;
		public object Obj;
		protected DebugModeEntry parent;
		protected readonly GUIContent guiContent = new();
		protected string primitiveValue;

		protected DebugModeEnumerableEntry enumerableRoot;
		protected List<DebugModeEntry> variables;
		private bool forceShowNestedVariables;

		private string searchTerm;

		private static readonly Stack<List<DebugModeEntry>> pool = new Stack<List<DebugModeEntry>>( 32 );

		public DebugModeEntry( DebugModeEntry parent )
		{
			this.parent = parent;
		}

		public void SetSearchTerm( string searchTerm )
		{
			if( searchTerm == string.Empty )
				searchTerm = null;

			if( this.searchTerm != searchTerm )
			{
				this.searchTerm = searchTerm;
				RefreshLabel();
			}
		}

		public void Refresh()
		{
			RefreshInternal();
			RefreshLabel();
		}

		protected virtual void RefreshInternal()
		{
			if( m_isExpanded )
			{
				Type prevType = Obj != null ? Obj.GetType() : null;
				Obj = Variable.Get( parent != null ? parent.Obj : null );

				if( Obj == null || Obj.Equals( null ) )
					PoolLists();
				else
				{
					if( Obj.GetType() != prevType )
						PoolLists();

					// Cache ToString() values of primitives since they won't change until next Refresh
					primitiveValue = Obj.GetType().IsPrimitiveUnityType() ? Obj.ToString() : null;

					if( Obj is IEnumerable && !( Obj is Transform ) )
					{
						if( enumerableRoot == null )
							enumerableRoot = new DebugModeEnumerableEntry( this ) { Variable = new VariableGetterHolder( "(IEnumerable) Elements", Obj.GetType(), null, null ) };

						enumerableRoot.Refresh();
					}
					else if( enumerableRoot != null )
					{
						enumerableRoot.PoolLists();
						enumerableRoot = null;
					}

					if( !( Obj is ICollection ) ) // Display only the enumerable elements of ICollections
					{
						if( variables == null )
						{
							VariableGetterHolder[] childGetters = Utilities.GetFilteredVariablesForObject( Obj );
							variables = PopList( childGetters.Length );
							for( int i = 0; i < childGetters.Length; i++ )
								variables.Add( new DebugModeEntry( this ) { Variable = childGetters[i] } );
						}

						for( int i = 0; i < variables.Count; i++ )
							variables[i].Refresh();
					}
				}
			}
		}

		private void RefreshLabel()
		{
			guiContent.text = Variable.description;

			/// If <see cref="Obj"/>'s Type is different than <see cref="VariableGetterHolder.type"/>, it's useful to show it in Inspector so that we can see exactly what we're inspecting.
			bool showObjType = Obj != null && Obj is not Object && Obj.GetType() != Variable.type;
			if( showObjType || !string.IsNullOrEmpty( searchTerm ) )
			{
				StringBuilder sb = Utilities.stringBuilder.Clear().Append( Variable.description );
				if( showObjType )
					sb.Append( " -> " ).AppendType( Obj.GetType() );

				if( !string.IsNullOrEmpty( searchTerm ) )
					sb.Append( " [Search: " ).Append( searchTerm ).Append( ']' );

				guiContent.text = sb.ToString();
			}
		}

		public void DrawOnGUI( bool flattenChildren = false )
		{
			if( flattenChildren )
			{
				IsExpanded = true;
				searchTerm = parent?.searchTerm;
			}
			else
			{
				if( !string.IsNullOrEmpty( parent?.searchTerm ) && !guiContent.text.ContainsIgnoreCase( parent.searchTerm ) )
					return;
			}

			if( m_isExpanded )
			{
				if( !flattenChildren && !DrawFoldout() )
				{
					IsExpanded = false;
					GUIUtility.ExitGUI();
				}
				else
				{
					if( !flattenChildren )
						EditorGUI.indentLevel++;

					if( parent == null || !DrawValueOnGUI() || forceShowNestedVariables )
					{
						if( forceShowNestedVariables )
							EditorGUI.indentLevel++;

						if( enumerableRoot != null )
							enumerableRoot.DrawOnGUI( variables == null ); // If only the enumerable elements exist, flatten them

						if( variables != null )
						{
							for( int i = 0; i < variables.Count; i++ )
								variables[i].DrawOnGUI();
						}

						if( forceShowNestedVariables )
							EditorGUI.indentLevel--;
					}

					if( !flattenChildren )
						EditorGUI.indentLevel--;
				}
			}
			else
			{
				if( DrawFoldout() )
				{
					IsExpanded = true;
					GUIUtility.ExitGUI();
				}
			}
		}

		private bool DrawFoldout()
		{
			Rect rect = GUILayoutUtility.GetRect( EditorGUIUtility.fieldWidth, EditorGUIUtility.fieldWidth, EditorGUIUtility.singleLineHeight, EditorGUIUtility.singleLineHeight, EditorStyles.foldout );
			bool isExpanded = EditorGUI.Foldout( rect, m_isExpanded, guiContent, true, EditorStyles.foldout );
			if( Event.current.type == EventType.MouseDown && Event.current.button == 1 && rect.Contains( Event.current.mousePosition ) )
			{
				GenericMenu menu = new();
				menu.AddItem( new GUIContent( "Search" ), false, () => StringInputDialog.Show( "Enter search term:", searchTerm, SetSearchTerm ) );
				if( !string.IsNullOrEmpty( searchTerm ) )
					menu.AddItem( new GUIContent( "Reset Search" ), false, () => SetSearchTerm( null ) );

				menu.ShowAsContext();
			}

			return isExpanded;
		}

		private bool DrawValueOnGUI()
		{
			EditorGUI.BeginChangeCheck();

			object newValue = Obj;
			if( Obj == null || Obj.Equals( null ) )
			{
				if( typeof( Object ).IsAssignableFrom( Variable.type ) )
					newValue = EditorGUILayout.ObjectField( GUIContent.none, null, Variable.type, true );
				else
					EditorGUILayout.LabelField( "Null" );
			}
			else if( Obj is Object )
			{
				Type objType = Obj.GetType();
				if( typeof( Object ).IsAssignableFrom( Variable.type ) && Variable.type.IsAssignableFrom( objType ) )
					objType = Variable.type;

				EditorGUI.EndChangeCheck(); /// Toggling <see cref="forceShowNestedVariables"/> shouldn't count as a change
				Rect pickerRect = EditorGUILayout.GetControlRect( false, EditorGUIUtility.singleLineHeight );
				float indentation = EditorGUI.IndentedRect( pickerRect ).x - pickerRect.x;
				forceShowNestedVariables = EditorGUI.Foldout( new Rect( pickerRect.x, pickerRect.y, pickerRect.height + indentation, pickerRect.height ), forceShowNestedVariables, GUIContent.none, true );

				EditorGUI.BeginChangeCheck();
				pickerRect.xMin += pickerRect.height + 2f;
				newValue = EditorGUI.ObjectField( pickerRect, (Object) Obj, objType, true );

				Event ev = Event.current;
				if( ev.type == EventType.MouseDown && ev.button == 1 && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
				{
					GenericMenu menu = new GenericMenu();
					MenuItems.OnObjectRightClicked( menu, (Object) Obj );
					menu.ShowAsContext();

					GUIUtility.ExitGUI();
				}
			}
			else if( Obj is bool )
				newValue = EditorGUILayout.ToggleLeft( GUIContent.none, (bool) Obj );
			else if( Obj is int )
				newValue = EditorGUILayout.DelayedIntField( GUIContent.none, (int) Obj );
			else if( Obj is float )
				newValue = EditorGUILayout.DelayedFloatField( GUIContent.none, (float) Obj );
			else if( Obj is string )
				newValue = EditorGUILayout.DelayedTextField( GUIContent.none, (string) Obj );
			else if( Obj is double )
				newValue = EditorGUILayout.DelayedDoubleField( GUIContent.none, (double) Obj );
			else if( Obj is long )
				newValue = EditorGUILayout.LongField( GUIContent.none, (long) Obj );
			else if( Obj is Vector3 )
				newValue = EditorGUILayout.Vector3Field( GUIContent.none, (Vector3) Obj );
			else if( Obj is Vector2 )
				newValue = EditorGUILayout.Vector2Field( GUIContent.none, (Vector2) Obj );
			else if( Obj is Vector3Int )
				newValue = EditorGUILayout.Vector3IntField( GUIContent.none, (Vector3Int) Obj );
			else if( Obj is Vector2Int )
				newValue = EditorGUILayout.Vector2IntField( GUIContent.none, (Vector2Int) Obj );
			else if( Obj is Vector4 )
				newValue = EditorGUILayout.Vector4Field( GUIContent.none, (Vector4) Obj );
			else if( Obj is Quaternion )
				newValue = Quaternion.Euler( EditorGUILayout.Vector3Field( GUIContent.none, ( (Quaternion) Obj ).eulerAngles ) ); // Edit Quaternions as Euler angles
			else if( Obj is Enum )
				newValue = EditorGUILayout.EnumPopup( GUIContent.none, (Enum) Obj );
			else if( Obj is Color )
				newValue = EditorGUILayout.ColorField( GUIContent.none, (Color) Obj );
			else if( Obj is Color32 )
				newValue = (Color32) EditorGUILayout.ColorField( GUIContent.none, (Color32) Obj );
			else if( Obj is LayerMask ) // Credit: http://answers.unity.com/answers/1387522/view.html
				newValue = InternalEditorUtility.ConcatenatedLayersMaskToLayerMask( EditorGUILayout.MaskField( InternalEditorUtility.LayerMaskToConcatenatedLayersMask( (LayerMask) Obj ), InternalEditorUtility.layers ) );
			else if( Obj is Rect )
				newValue = EditorGUILayout.RectField( GUIContent.none, (Rect) Obj );
			else if( Obj is Bounds )
				newValue = EditorGUILayout.BoundsField( GUIContent.none, (Bounds) Obj );
			else if( Obj is RectInt )
				newValue = EditorGUILayout.RectIntField( GUIContent.none, (RectInt) Obj );
			else if( Obj is BoundsInt )
				newValue = EditorGUILayout.BoundsIntField( GUIContent.none, (BoundsInt) Obj );
			else if( Obj is AnimationCurve )
				newValue = EditorGUILayout.CurveField( GUIContent.none, (AnimationCurve) Obj );
			else if( Obj is Gradient )
				newValue = PasteBinWindow.gradientField.Invoke( null, new object[] { GUIContent.none, (Gradient) Obj, null } );
			else if( primitiveValue != null ) // Variable is primitive
			{
				EditorGUILayout.TextField( primitiveValue );

				EditorGUI.EndChangeCheck();
				return true;
			}
			else
			{
				EditorGUI.EndChangeCheck();
				return false;
			}

			if( EditorGUI.EndChangeCheck() )
			{
				Object modifiedPrefabInstance = null;
				DebugModeEntry _parent = parent;
				while( _parent != null )
				{
					if( _parent.Obj as Object )
					{
						Undo.RecordObject( (Object) _parent.Obj, "Change Value" );
						if( _parent.Obj is Component )
							Undo.RecordObject( ( (Component) _parent.Obj ).gameObject, "Change Value" ); // Required for at least name and tag properties

						if( PrefabUtility.IsPartOfPrefabInstance( (Object) _parent.Obj ) )
							modifiedPrefabInstance = _parent.Obj as Object;

						break;
					}

					_parent = _parent.parent;
				}

				if( !ReferenceEquals( Obj, newValue ) && newValue != null && Obj.GetType() != newValue.GetType() )
					PoolLists();

				Obj = newValue;
				Variable.Set( parent != null ? parent.Obj : null, newValue );
				Refresh();

				if( modifiedPrefabInstance != null )
					PrefabUtility.RecordPrefabInstancePropertyModifications( (Object) _parent.Obj ); // Required for prefab modifications (without this, Redo doesn't work for prefab variables)

				GUIUtility.ExitGUI();
			}

			return true;
		}

		protected List<DebugModeEntry> PopList( int preferredSize = 8 )
		{
			if( pool.Count > 0 )
				return pool.Pop();

			return new List<DebugModeEntry>( preferredSize );
		}

		public void PoolLists()
		{
			if( enumerableRoot != null )
			{
				enumerableRoot.PoolLists();
				enumerableRoot = null;
			}

			if( variables != null )
			{
				for( int i = 0; i < variables.Count; i++ )
					variables[i].PoolLists();

				pool.Push( variables );

				variables.Clear();
				variables = null;
			}
		}
	}

	public class DebugModeEnumerableEntry : DebugModeEntry
	{
		private readonly struct EnumerableValueWrapper
		{
			public readonly DebugModeEnumerableEntry entry;
			public readonly int index;

			public EnumerableValueWrapper( DebugModeEnumerableEntry entry, int index )
			{
				this.entry = entry;
				this.index = index;
			}

			public object GetValue( object obj )
			{
				return entry.GetEnumerableValue( index );
			}

			public void SetValue( object obj, object value )
			{
				entry.SetEnumerableValue( index, value );
			}
		}

		public DebugModeEnumerableEntry( DebugModeEntry parent ) : base( parent )
		{
		}

		protected override void RefreshInternal()
		{
			if( m_isExpanded )
			{
				Obj = parent.Obj;

				if( variables == null )
					variables = PopList( Obj is ICollection ? ( (ICollection) Obj ).Count : 8 );

				if( Obj is IList )
				{
					int count = ( (IList) Obj ).Count;

					// Add new entries to variables if there aren't enough entries
					for( int i = variables.Count; i < count; i++ )
					{
						Type listType = Obj.GetType();
						Type elementType;
						if( listType.IsArray && listType.GetArrayRank() == 1 )
							elementType = listType.GetElementType();
						else if( listType.IsGenericType )
							elementType = listType.GetGenericArguments()[0];
						else
							elementType = typeof( object );

						EnumerableValueWrapper valueWrapper = new EnumerableValueWrapper( this, i );
						variables.Add( new DebugModeEntry( this ) { Variable = new VariableGetterHolder( i + ":", elementType, valueWrapper.GetValue, valueWrapper.SetValue ) } );
					}

					// Remove excessive entries from variables
					for( int i = variables.Count - 1; i >= count; i-- )
					{
						variables[i].PoolLists();
						variables.RemoveAt( i );
					}
				}
				else
				{
					int index = 0;
					foreach( object element in (IEnumerable) Obj )
					{
						DebugModeEntry entry;
						if( index < variables.Count )
							entry = variables[index];
						else
						{
							EnumerableValueWrapper valueWrapper = new EnumerableValueWrapper( this, index );
							entry = new DebugModeEntry( this ) { Variable = new VariableGetterHolder( index + ":", typeof( object ), valueWrapper.GetValue, valueWrapper.SetValue ) };
							variables.Add( entry );
						}

						entry.Obj = element;
						index++;
					}

					for( int i = variables.Count - 1; i >= index; i-- )
					{
						variables[i].PoolLists();
						variables.RemoveAt( i );
					}
				}

				for( int i = 0; i < variables.Count; i++ )
					variables[i].Refresh();
			}
		}

		private object GetEnumerableValue( int index )
		{
			if( Obj is IList )
				return ( (IList) Obj )[index];

			return variables[index].Obj;
		}

		private void SetEnumerableValue( int index, object value )
		{
			if( Obj is IList )
				( (IList) Obj )[index] = value;
		}
	}
}