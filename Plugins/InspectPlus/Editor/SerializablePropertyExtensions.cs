using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InspectPlusNamespace
{
	public static class SerializablePropertyExtensions
	{
		public class GenericObjectClipboard
		{
			public readonly string type;
			public readonly object[] values;

			public GenericObjectClipboard( string type, object[] values )
			{
				this.type = type;
				this.values = values;
			}
		}

		public class ArrayClipboard
		{
			public readonly int size;
			public readonly string elementType;
			public readonly object[] elements;

			public ArrayClipboard( int size, string elementType, object[] elements )
			{
				this.size = size;
				this.elementType = elementType;
				this.elements = elements;
			}
		}

		public class VectorClipboard
		{
			public readonly float c1, c2, c3, c4, c5, c6;

			public VectorClipboard( float c1 = 0f, float c2 = 0f, float c3 = 0f, float c4 = 0f, float c5 = 0f, float c6 = 0f )
			{
				this.c1 = c1;
				this.c2 = c2;
				this.c3 = c3;
				this.c4 = c4;
				this.c5 = c5;
				this.c6 = c6;
			}

			public static implicit operator VectorClipboard( Vector2 v ) { return new VectorClipboard( v.x, v.y ); }
			public static implicit operator VectorClipboard( Vector3 v ) { return new VectorClipboard( v.x, v.y, v.z ); }
			public static implicit operator VectorClipboard( Vector4 v ) { return new VectorClipboard( v.x, v.y, v.z, v.w ); }
			public static implicit operator VectorClipboard( Quaternion q ) { return new VectorClipboard( q.x, q.y, q.z, q.w ); }
			public static implicit operator VectorClipboard( Rect r ) { return new VectorClipboard( r.xMin, r.yMin, r.width, r.height ); }
			public static implicit operator VectorClipboard( Bounds b ) { return new VectorClipboard( b.center.x, b.center.y, b.center.z, b.extents.x, b.extents.y, b.extents.z ); }
#if UNITY_2017_2_OR_NEWER
			public static implicit operator VectorClipboard( Vector2Int v ) { return new VectorClipboard( v.x, v.y ); }
			public static implicit operator VectorClipboard( Vector3Int v ) { return new VectorClipboard( v.x, v.y, v.z ); }
			public static implicit operator VectorClipboard( RectInt r ) { return new VectorClipboard( r.xMin, r.yMin, r.width, r.height ); }
			public static implicit operator VectorClipboard( BoundsInt b ) { return new VectorClipboard( b.position.x, b.position.y, b.position.z, b.size.x, b.size.y, b.size.z ); }
#endif

			public static implicit operator Vector2( VectorClipboard v ) { return new Vector2( v.c1, v.c2 ); }
			public static implicit operator Vector3( VectorClipboard v ) { return new Vector3( v.c1, v.c2, v.c3 ); }
			public static implicit operator Vector4( VectorClipboard v ) { return new Vector4( v.c1, v.c2, v.c3, v.c4 ); }
			public static implicit operator Quaternion( VectorClipboard v ) { return new Quaternion( v.c1, v.c2, v.c3, v.c4 ); }
			public static implicit operator Rect( VectorClipboard v ) { return new Rect( v.c1, v.c2, v.c3, v.c4 ); }
			public static implicit operator Bounds( VectorClipboard v ) { return new Bounds( new Vector3( v.c1, v.c2, v.c3 ), new Vector3( v.c4, v.c5, v.c6 ) * 2f ); }
#if UNITY_2017_2_OR_NEWER
			public static implicit operator Vector2Int( VectorClipboard v ) { return new Vector2Int( Mathf.RoundToInt( v.c1 ), Mathf.RoundToInt( v.c2 ) ); }
			public static implicit operator Vector3Int( VectorClipboard v ) { return new Vector3Int( Mathf.RoundToInt( v.c1 ), Mathf.RoundToInt( v.c2 ), Mathf.RoundToInt( v.c3 ) ); }
			public static implicit operator RectInt( VectorClipboard v ) { return new RectInt( Mathf.RoundToInt( v.c1 ), Mathf.RoundToInt( v.c2 ), Mathf.RoundToInt( v.c3 ), Mathf.RoundToInt( v.c4 ) ); }
			public static implicit operator BoundsInt( VectorClipboard v ) { return new BoundsInt( new Vector3Int( Mathf.RoundToInt( v.c1 ), Mathf.RoundToInt( v.c2 ), Mathf.RoundToInt( v.c3 ) ), new Vector3Int( Mathf.RoundToInt( v.c4 ), Mathf.RoundToInt( v.c5 ), Mathf.RoundToInt( v.c6 ) ) ); }
#endif
		}

		private static readonly PropertyInfo gradientValueGetter;

		static SerializablePropertyExtensions()
		{
			gradientValueGetter = typeof( SerializedProperty ).GetProperty( "gradientValue", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance );
		}

		public static object CopyValue( this SerializedProperty property )
		{
			switch( property.propertyType )
			{
				case SerializedPropertyType.AnimationCurve: return property.animationCurveValue;
				case SerializedPropertyType.ArraySize: return (long) property.arraySize;
				case SerializedPropertyType.Boolean: return property.boolValue;
				case SerializedPropertyType.Bounds: return (VectorClipboard) property.boundsValue;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.BoundsInt: return (VectorClipboard) property.boundsIntValue;
#endif
				case SerializedPropertyType.Character: return property.longValue;
				case SerializedPropertyType.Color: return property.colorValue;
				case SerializedPropertyType.Enum: return (long) property.intValue;
				case SerializedPropertyType.ExposedReference: return property.exposedReferenceValue;
				case SerializedPropertyType.Float: return property.doubleValue;
				case SerializedPropertyType.Gradient: return gradientValueGetter.GetValue( property, null );
				case SerializedPropertyType.Integer: return property.longValue;
				case SerializedPropertyType.LayerMask: return property.longValue;
				case SerializedPropertyType.ObjectReference: return property.objectReferenceValue;
				case SerializedPropertyType.Quaternion: return (VectorClipboard) property.quaternionValue;
				case SerializedPropertyType.Rect: return (VectorClipboard) property.rectValue;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.RectInt: return (VectorClipboard) property.rectIntValue;
#endif
				case SerializedPropertyType.String: return property.stringValue;
				case SerializedPropertyType.Vector2: return (VectorClipboard) property.vector2Value;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.Vector2Int: return (VectorClipboard) property.vector2IntValue;
#endif
				case SerializedPropertyType.Vector3: return (VectorClipboard) property.vector3Value;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.Vector3Int: return (VectorClipboard) property.vector3IntValue;
#endif
				case SerializedPropertyType.Vector4: return (VectorClipboard) property.vector4Value;
				case SerializedPropertyType.Generic:
					if( property.isArray )
					{
						object[] elements = new object[property.arraySize];
						for( int i = 0; i < elements.Length; i++ )
							elements[i] = CopyValue( property.GetArrayElementAtIndex( i ) );

						return new ArrayClipboard( elements.Length, property.arrayElementType, elements );
					}
#if UNITY_2017_1_OR_NEWER
					else if( property.isFixedBuffer )
					{
						object[] elements = new object[property.fixedBufferSize];
						for( int i = 0; i < elements.Length; i++ )
							elements[i] = CopyValue( property.GetFixedBufferElementAtIndex( i ) );

						return new ArrayClipboard( elements.Length, elements.Length > 0 ? property.GetFixedBufferElementAtIndex( 0 ).type : null, elements );
					}
#endif
					else if( property.hasVisibleChildren )
					{
						SerializedProperty endProperty = property.GetEndProperty();
						SerializedProperty iterator = property.Copy();
						iterator.NextVisible( true );

						int count = 0;
						while( !SerializedProperty.EqualContents( endProperty, iterator ) )
						{
							count++;
							iterator.NextVisible( false );
						}

						string type = property.type;
						object[] values = new object[count];

						if( count > 0 )
						{
							property.NextVisible( true );

							for( int i = 0; i < count; i++ )
							{
								values[i] = CopyValue( property );
								property.NextVisible( false );
							}
						}

						return new GenericObjectClipboard( type, values );
					}
					else
						return null;
				default: return null;
			}
		}

		public static void PasteValue( this SerializedProperty property, object clipboard )
		{
			PasteValueInternal( property, clipboard, true );
		}

		private static void PasteValueInternal( SerializedProperty property, object clipboard, bool applyModifiedProperties )
		{
			switch( property.propertyType )
			{
				case SerializedPropertyType.AnimationCurve: property.animationCurveValue = (AnimationCurve) clipboard; break;
				case SerializedPropertyType.ArraySize:
					if( clipboard is long ) property.arraySize = (int) (long) clipboard;
					else if( clipboard is double ) property.arraySize = (int) (double) clipboard;
					break;
				case SerializedPropertyType.Boolean:
					if( clipboard is bool ) property.boolValue = (bool) clipboard;
					else if( clipboard is long ) property.boolValue = ( (long) clipboard ) != 0L;
					else if( clipboard is double ) property.boolValue = ( (double) clipboard ) != 0.0;
					break;
				case SerializedPropertyType.Bounds: property.boundsValue = (VectorClipboard) clipboard; break;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.BoundsInt: property.boundsIntValue = (VectorClipboard) clipboard; break;
#endif
				case SerializedPropertyType.Character:
					if( clipboard is long ) property.intValue = (int) (long) clipboard;
					else if( clipboard is string ) property.intValue = ( (string) clipboard )[0];
					break;
				case SerializedPropertyType.Color: property.colorValue = (Color) clipboard; break;
				case SerializedPropertyType.Enum: property.intValue = (int) (long) clipboard; break;
				case SerializedPropertyType.ExposedReference: TryAssignClipboardToObjectProperty( property, clipboard, false ); break;
				case SerializedPropertyType.Float:
					if( clipboard is long ) property.doubleValue = (long) clipboard;
					else if( clipboard is double ) property.doubleValue = (double) clipboard;
					break;
				case SerializedPropertyType.Gradient: gradientValueGetter.SetValue( property, clipboard, null ); break;
				case SerializedPropertyType.Integer:
					if( clipboard is long ) property.longValue = (long) clipboard;
					else if( clipboard is double ) property.longValue = (long) (double) clipboard;
					break;
				case SerializedPropertyType.LayerMask: property.intValue = (int) (long) clipboard; break;
				case SerializedPropertyType.ObjectReference: TryAssignClipboardToObjectProperty( property, clipboard, false ); break;
				case SerializedPropertyType.Quaternion: property.quaternionValue = (VectorClipboard) clipboard; break;
				case SerializedPropertyType.Rect: property.rectValue = (VectorClipboard) clipboard; break;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.RectInt: property.rectIntValue = (VectorClipboard) clipboard; break;
#endif
				case SerializedPropertyType.String: property.stringValue = clipboard.ToString(); break;
				case SerializedPropertyType.Vector2: property.vector2Value = (VectorClipboard) clipboard; break;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.Vector2Int: property.vector2IntValue = (VectorClipboard) clipboard; break;
#endif
				case SerializedPropertyType.Vector3: property.vector3Value = (VectorClipboard) clipboard; break;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.Vector3Int: property.vector3IntValue = (VectorClipboard) clipboard; break;
#endif
				case SerializedPropertyType.Vector4: property.vector4Value = (VectorClipboard) clipboard; break;
				case SerializedPropertyType.Generic:
					if( property.isArray )
					{
						ArrayClipboard array = (ArrayClipboard) clipboard;
						property.arraySize = array.size;
						for( int i = 0; i < array.size; i++ )
						{
							SerializedProperty element = property.GetArrayElementAtIndex( i );
							PasteValueInternal( element, array.elements[i], false );
						}
					}
#if UNITY_2017_1_OR_NEWER
					else if( property.isFixedBuffer )
					{
						ArrayClipboard array = (ArrayClipboard) clipboard;
						int count = Mathf.Min( array.size, property.fixedBufferSize );
						for( int i = 0; i < count; i++ )
						{
							SerializedProperty element = property.GetFixedBufferElementAtIndex( i );
							PasteValueInternal( element, array.elements[i], false );
						}
					}
#endif
					else if( property.hasVisibleChildren )
					{
						GenericObjectClipboard obj = (GenericObjectClipboard) clipboard;
						if( obj.values.Length > 0 )
						{
							property.NextVisible( true );

							for( int i = 0; i < obj.values.Length; i++ )
							{
								PasteValueInternal( property, obj.values[i], false );
								property.NextVisible( false );
							}
						}
					}

					break;
			}

			if( applyModifiedProperties )
				property.serializedObject.ApplyModifiedProperties();
		}

		public static bool CanPasteValue( this SerializedProperty property, object clipboard )
		{
			if( clipboard == null || clipboard.Equals( null ) )
				return false;

			if( !property.editable )
				return false;

			switch( property.propertyType )
			{
				case SerializedPropertyType.AnimationCurve: return clipboard is AnimationCurve;
				case SerializedPropertyType.ArraySize: return ( clipboard is long && (long) clipboard >= 0L ) || ( clipboard is double && (double) clipboard >= 0.0 );
				case SerializedPropertyType.Boolean: return clipboard is bool || clipboard is long || clipboard is double;
				case SerializedPropertyType.Bounds: return clipboard is VectorClipboard;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.BoundsInt: return clipboard is VectorClipboard;
#endif
				case SerializedPropertyType.Character: return ( clipboard is long && (long) clipboard <= 255L && (long) clipboard >= 0L ) || ( clipboard is string && ( (string) clipboard ).Length > 0 );
				case SerializedPropertyType.Color: return clipboard is Color;
				case SerializedPropertyType.Enum: return clipboard is long;
				case SerializedPropertyType.ExposedReference: return TryAssignClipboardToObjectProperty( property, clipboard, true );
				case SerializedPropertyType.Float: return clipboard is double || clipboard is long;
				case SerializedPropertyType.Gradient: return clipboard is Gradient;
				case SerializedPropertyType.Integer: return clipboard is long || clipboard is double;
				case SerializedPropertyType.LayerMask: return clipboard is long;
				case SerializedPropertyType.ObjectReference: return TryAssignClipboardToObjectProperty( property, clipboard, true );
				case SerializedPropertyType.Quaternion: return clipboard is VectorClipboard;
				case SerializedPropertyType.Rect: return clipboard is VectorClipboard;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.RectInt: return clipboard is VectorClipboard;
#endif
				case SerializedPropertyType.String: return true;
				case SerializedPropertyType.Vector2: return clipboard is VectorClipboard;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.Vector2Int: return clipboard is VectorClipboard;
#endif
				case SerializedPropertyType.Vector3: return clipboard is VectorClipboard;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.Vector3Int: return clipboard is VectorClipboard;
#endif
				case SerializedPropertyType.Vector4: return clipboard is VectorClipboard;
				case SerializedPropertyType.Generic:
					if( property.isArray )
						return clipboard is ArrayClipboard && ( (ArrayClipboard) clipboard ).elementType == property.arrayElementType;
#if UNITY_2017_1_OR_NEWER
					else if( property.isFixedBuffer )
						return clipboard is ArrayClipboard && property.fixedBufferSize > 0 && ( (ArrayClipboard) clipboard ).elementType == property.GetFixedBufferElementAtIndex( 0 ).type;
#endif
					else if( property.hasVisibleChildren )
						return clipboard is GenericObjectClipboard && ( (GenericObjectClipboard) clipboard ).type == property.type;
					else
						return false;
				default: return false;
			}
		}

		private static bool TryAssignClipboardToObjectProperty( SerializedProperty property, object clipboard, bool nonDestructive )
		{
			Object obj = clipboard as Object;
			if( clipboard != null && !clipboard.Equals( null ) && !obj ) // Allow null values but don't allow non-Object values
				return false;

			bool isObjectReference = property.propertyType == SerializedPropertyType.ObjectReference;
			try
			{
				SerializedProperty prop = nonDestructive ? property.Copy() : property;

				if( isObjectReference )
				{
					prop.objectReferenceValue = obj;
					if( prop.objectReferenceValue == obj )
						return true;
				}
				else
				{
					prop.exposedReferenceValue = obj;
					if( prop.exposedReferenceValue == obj )
						return true;
				}

				if( !obj )
					return false;

				// Allow assigning components to GameObjects and vice versa
				string type = prop.type;
				if( type.IndexOf( "PPtr<" ) == 0 )
					type = type[5] == '$' ? type.Substring( 6, type.Length - 7 ) : type.Substring( 5, type.Length - 6 );

				if( type == "GameObject" && obj is Component )
				{
					obj = ( (Component) obj ).gameObject;

					if( isObjectReference )
					{
						prop.objectReferenceValue = obj;
						if( prop.objectReferenceValue == obj )
							return true;
					}
					else
					{
						prop.exposedReferenceValue = obj;
						if( prop.exposedReferenceValue == obj )
							return true;
					}
				}
				else if( obj is Component )
				{
					// Try to avoid the "GetComponent requires component inherits from MonoBehaviour" warning
					Type _type = typeof( Vector3 ).Assembly.GetType( "UnityEngine." + type );
					if( _type != null && !typeof( Component ).IsAssignableFrom( _type ) )
						return false;

					obj = ( (Component) obj ).GetComponent( type );
					if( obj )
					{
						if( isObjectReference )
						{
							prop.objectReferenceValue = obj;
							if( prop.objectReferenceValue == obj )
								return true;
						}
						else
						{
							prop.exposedReferenceValue = obj;
							if( prop.exposedReferenceValue == obj )
								return true;
						}
					}
				}
				else if( obj is GameObject )
				{
					// Try to avoid the "GetComponent requires component inherits from MonoBehaviour" warning
					Type _type = typeof( Vector3 ).Assembly.GetType( "UnityEngine." + type );
					if( _type != null && !typeof( Component ).IsAssignableFrom( _type ) )
						return false;

					obj = ( (GameObject) obj ).GetComponent( type );
					if( obj )
					{
						if( isObjectReference )
						{
							prop.objectReferenceValue = obj;
							if( prop.objectReferenceValue == obj )
								return true;
						}
						else
						{
							prop.exposedReferenceValue = obj;
							if( prop.exposedReferenceValue == obj )
								return true;
						}
					}
				}
			}
			catch { }
			finally
			{
				if( nonDestructive )
					property.serializedObject.Update(); // Prevent changes from being applied to the object
			}

			return false;
		}
	}
}