using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InspectPlusNamespace.Extras
{
	public class DebugModeEntry
	{
		protected bool m_isExpanded;
		public bool IsExpanded
		{
			get { return m_isExpanded; }
			set
			{
				m_isExpanded = value;
				if( m_isExpanded )
					Refresh();
				else
				{
					Obj = null;
					PoolLists();
				}
			}
		}

		public VariableGetterHolder Getter;
		public object Obj;
		protected DebugModeEntry parent;
		protected string primitiveValue;

		protected DebugModeEnumerableEntry enumerableRoot;
		protected List<DebugModeEntry> variables;

		private static readonly Stack<List<DebugModeEntry>> pool = new Stack<List<DebugModeEntry>>( 32 );

		public DebugModeEntry( DebugModeEntry parent )
		{
			this.parent = parent;
		}

		public virtual void Refresh()
		{
			if( m_isExpanded )
			{
				Type prevType = Obj != null ? Obj.GetType() : null;
				Obj = Getter.Get( parent != null ? parent.Obj : null );

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
							enumerableRoot = new DebugModeEnumerableEntry( this ) { Getter = new VariableGetterHolder( "(IEnumerable) Elements", null ) };

						enumerableRoot.Refresh();
					}
					else if( enumerableRoot != null )
					{
						enumerableRoot.PoolLists();
						enumerableRoot = null;
					}

					if( variables == null )
					{
						VariableGetterHolder[] childGetters = Utilities.GetFilteredVariablesForType( Obj.GetType() );
						variables = PopList( childGetters.Length );
						for( int i = 0; i < childGetters.Length; i++ )
							variables.Add( new DebugModeEntry( this ) { Getter = childGetters[i] } );
					}

					for( int i = 0; i < variables.Count; i++ )
						variables[i].Refresh();
				}
			}
		}

		public void DrawOnGUI()
		{
			if( m_isExpanded )
			{
				if( !EditorGUILayout.Foldout( true, Getter.description, true ) )
				{
					IsExpanded = false;
					GUIUtility.ExitGUI();
				}
				else
				{
					if( Obj == null || Obj.Equals( null ) )
						EditorGUILayout.LabelField( "Null" );
					else if( primitiveValue != null ) // Variable is primitive
						EditorGUILayout.LabelField( primitiveValue );
					else
					{
						if( Obj is Object && parent != null ) // We want to expose the variables of root entries
						{
							EditorGUILayout.ObjectField( "", (Object) Obj, Obj.GetType(), true );

							Event ev = Event.current;
							if( ev.type == EventType.MouseDown && ev.button == 1 && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
							{
								GenericMenu menu = new GenericMenu();
								InspectPlusWindow.OnObjectRightClicked( menu, (Object) Obj );
								menu.ShowAsContext();

								GUIUtility.ExitGUI();
							}
						}
						else
						{
							if( enumerableRoot != null )
							{
								EditorGUI.indentLevel++;
								enumerableRoot.DrawOnGUI();
								EditorGUI.indentLevel--;
							}

							if( variables != null )
							{
								for( int i = 0; i < variables.Count; i++ )
								{
									EditorGUI.indentLevel++;
									variables[i].DrawOnGUI();
									EditorGUI.indentLevel--;
								}
							}
						}
					}
				}
			}
			else
			{
				if( EditorGUILayout.Foldout( false, Getter.description, true ) )
				{
					IsExpanded = true;
					GUIUtility.ExitGUI();
				}
			}
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
		private struct EnumerableValueGetter
		{
			public readonly DebugModeEnumerableEntry entry;
			public readonly int index;

			public EnumerableValueGetter( DebugModeEnumerableEntry entry, int index )
			{
				this.entry = entry;
				this.index = index;
			}

			public object GetValue( object obj )
			{
				return entry.GetEnumerableValue( index );
			}
		}

		public DebugModeEnumerableEntry( DebugModeEntry parent ) : base( parent )
		{
		}

		public override void Refresh()
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
						variables.Add( new DebugModeEntry( this ) { Getter = new VariableGetterHolder( i + ":", new EnumerableValueGetter( this, i ).GetValue ) } );

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
							entry = new DebugModeEntry( this ) { Getter = new VariableGetterHolder( index + ":", new EnumerableValueGetter( this, index ).GetValue ) };
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
	}
}