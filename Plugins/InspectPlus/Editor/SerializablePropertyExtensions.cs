using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using IPObject = InspectPlusNamespace.SerializedClipboard.IPObject;

namespace InspectPlusNamespace
{
	public static class SerializablePropertyExtensions
	{
		#region Helper Classes
		public class GameObjectHierarchyClipboard
		{
			public readonly GameObject[] source;
			public readonly bool includeChildren;
			public readonly string name;

			public GameObjectHierarchyClipboard( GameObject[] source, bool includeChildren )
			{
				this.source = source;
				this.includeChildren = includeChildren;
				this.name = source[0].name;
			}

			public GameObjectHierarchyClipboard( string name )
			{
				this.source = null;
				this.includeChildren = true;
				this.name = name;
			}
		}

		public class ComponentGroupClipboard
		{
			public readonly Component[] components;
			public readonly string name;

			public ComponentGroupClipboard( Component[] components )
			{
				this.components = components;
				this.name = components[0].name;
			}

			public ComponentGroupClipboard( string name )
			{
				this.components = null;
				this.name = name;
			}
		}

		public class AssetFilesClipboard
		{
			public readonly string[] paths;

			public AssetFilesClipboard( string[] paths )
			{
				this.paths = paths;
			}
		}

		public class GenericObjectClipboard
		{
			public readonly string type;
			public readonly string[] variables;
			public readonly object[] values;

			public GenericObjectClipboard( string type, string[] variables, object[] values )
			{
				this.type = type;
				this.variables = variables;
				this.values = values;
			}
		}

		public class ArrayClipboard
		{
			public readonly string elementType;
			public readonly object[] elements;

			public ArrayClipboard( string elementType, object[] elements )
			{
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

			public override bool Equals( object obj )
			{
				VectorClipboard other = obj as VectorClipboard;
				return other != null && other.c1 == c1 && other.c2 == c2 && other.c3 == c3 && other.c4 == c4 && other.c5 == c5 && other.c6 == c6;
			}

			public override int GetHashCode()
			{
				return base.GetHashCode();
			}

			public static implicit operator VectorClipboard( Vector2 v ) { return new VectorClipboard( v.x, v.y ); }
			public static implicit operator VectorClipboard( Vector3 v ) { return new VectorClipboard( v.x, v.y, v.z ); }
			public static implicit operator VectorClipboard( Vector4 v ) { return new VectorClipboard( v.x, v.y, v.z, v.w ); }
			public static implicit operator VectorClipboard( Quaternion q ) { return new VectorClipboard( q.x, q.y, q.z, q.w ); }
			public static implicit operator VectorClipboard( Rect r ) { return new VectorClipboard( r.xMin, r.yMin, r.width, r.height ); }
			public static implicit operator VectorClipboard( Bounds b ) { return new VectorClipboard( b.center.x, b.center.y, b.center.z, b.extents.x, b.extents.y, b.extents.z ); }
			public static implicit operator VectorClipboard( Color c ) { return new VectorClipboard( c.r, c.g, c.b, c.a ); }
			public static implicit operator VectorClipboard( Color32 c ) { return new VectorClipboard( c.r, c.g, c.b, c.a ); }
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
			public static implicit operator Color( VectorClipboard v ) { return ( v.c1 > 2f || v.c2 > 2f || v.c3 > 2f || v.c4 > 2f ) ? (Color) (Color32) v : new Color( v.c1, v.c2, v.c3, v.c4 ); } // If values are in range 0-255, use Color32 instead
			public static implicit operator Color32( VectorClipboard v ) { return new Color32( (byte) Mathf.RoundToInt( v.c1 ), (byte) Mathf.RoundToInt( v.c2 ), (byte) Mathf.RoundToInt( v.c3 ), (byte) Mathf.RoundToInt( v.c4 ) ); }
#if UNITY_2017_2_OR_NEWER
			public static implicit operator Vector2Int( VectorClipboard v ) { return new Vector2Int( Mathf.RoundToInt( v.c1 ), Mathf.RoundToInt( v.c2 ) ); }
			public static implicit operator Vector3Int( VectorClipboard v ) { return new Vector3Int( Mathf.RoundToInt( v.c1 ), Mathf.RoundToInt( v.c2 ), Mathf.RoundToInt( v.c3 ) ); }
			public static implicit operator RectInt( VectorClipboard v ) { return new RectInt( Mathf.RoundToInt( v.c1 ), Mathf.RoundToInt( v.c2 ), Mathf.RoundToInt( v.c3 ), Mathf.RoundToInt( v.c4 ) ); }
			public static implicit operator BoundsInt( VectorClipboard v ) { return new BoundsInt( new Vector3Int( Mathf.RoundToInt( v.c1 ), Mathf.RoundToInt( v.c2 ), Mathf.RoundToInt( v.c3 ) ), new Vector3Int( Mathf.RoundToInt( v.c4 ), Mathf.RoundToInt( v.c5 ), Mathf.RoundToInt( v.c6 ) ) ); }
#endif
		}

		public class ManagedObjectClipboard
		{
			public class NestedManagedObject
			{
				public readonly object reference;
				public readonly string relativePath;

				public NestedManagedObject( object reference, string relativePath )
				{
					this.reference = reference;
					this.relativePath = relativePath;
				}
			}

			public class NestedUnityObject
			{
				public readonly Object reference;
				public readonly string relativePath;

				public NestedUnityObject( Object reference, string relativePath )
				{
					this.reference = reference;
					this.relativePath = relativePath;
				}
			}

			public readonly string type;
			public readonly object value;
			public readonly NestedManagedObject[] nestedManagedObjects;
			public readonly NestedUnityObject[] nestedUnityObjects;

			public ManagedObjectClipboard( string type, object value, NestedManagedObject[] nestedManagedObjects, NestedUnityObject[] nestedUnityObjects )
			{
				this.type = type;
				this.value = value;
				this.nestedManagedObjects = nestedManagedObjects;
				this.nestedUnityObjects = nestedUnityObjects;
			}

			public override bool Equals( object obj )
			{
				return obj is ManagedObjectClipboard && ( (ManagedObjectClipboard) obj ).value == value;
			}

			public override int GetHashCode()
			{
				return base.GetHashCode();
			}
		}
		#endregion

		private delegate FieldInfo FieldInfoGetter( SerializedProperty p, out Type t );

		private static readonly FieldInfoGetter fieldInfoGetter;
		private static readonly PropertyInfo gradientValueGetter;
		private static readonly PropertyInfo inspectorModeGetter;
#if !UNITY_2017_2_OR_NEWER
		private static readonly PropertyInfo transformRotationOrderGetter;
		private static readonly MethodInfo transformDisplayedRotationGetter;
#endif

		static SerializablePropertyExtensions()
		{
#if UNITY_2019_3_OR_NEWER
			MethodInfo fieldInfoGetterMethod = typeof( Editor ).Assembly.GetType( "UnityEditor.ScriptAttributeUtility" ).GetMethod( "GetFieldInfoAndStaticTypeFromProperty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );
#else
			MethodInfo fieldInfoGetterMethod = typeof( Editor ).Assembly.GetType( "UnityEditor.ScriptAttributeUtility" ).GetMethod( "GetFieldInfoFromProperty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );
#endif

			fieldInfoGetter = (FieldInfoGetter) Delegate.CreateDelegate( typeof( FieldInfoGetter ), fieldInfoGetterMethod );
			gradientValueGetter = typeof( SerializedProperty ).GetProperty( "gradientValue", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance );
			inspectorModeGetter = typeof( SerializedObject ).GetProperty( "inspectorMode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance );

#if !UNITY_2017_2_OR_NEWER
			transformRotationOrderGetter = typeof( Transform ).GetProperty( "rotationOrder", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance );
			transformDisplayedRotationGetter = typeof( Transform ).GetMethod( "GetLocalEulerAngles", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance );
#endif
		}

		public static object CopyValue( this SerializedProperty property )
		{
			switch( property.propertyType )
			{
				case SerializedPropertyType.AnimationCurve: return property.animationCurveValue;
				case SerializedPropertyType.ArraySize: return (long) property.intValue;
				case SerializedPropertyType.Boolean: return property.boolValue ? 1L : 0L;
				case SerializedPropertyType.Bounds: return (VectorClipboard) property.boundsValue;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.BoundsInt: return (VectorClipboard) property.boundsIntValue;
#endif
				case SerializedPropertyType.Character: return property.longValue;
				case SerializedPropertyType.Color: return property.colorValue;
				case SerializedPropertyType.Enum: return (long) property.intValue;
				case SerializedPropertyType.ExposedReference: return property.exposedReferenceValue;
#if UNITY_2017_1_OR_NEWER
				case SerializedPropertyType.FixedBufferSize: return (long) property.intValue;
#endif
				case SerializedPropertyType.Float: return property.doubleValue;
				case SerializedPropertyType.Gradient: return gradientValueGetter.GetValue( property, null );
				case SerializedPropertyType.Integer: return property.longValue;
				case SerializedPropertyType.LayerMask: return property.longValue;
#if UNITY_2019_3_OR_NEWER
				case SerializedPropertyType.ManagedReference:
					object value = property.GetTargetObject();
					if( value == null )
						return null;

					// Find nested managed references and assets/scene objects
					List<ManagedObjectClipboard.NestedManagedObject> nestedManagedObjects = null;
					List<ManagedObjectClipboard.NestedUnityObject> nestedUnityObjects = null;
					SerializedProperty subProperty = property.Copy();
					SerializedProperty endProperty = property.GetEndProperty( true );
					if( subProperty.Next( true ) )
					{
						int relativePathStartIndex = property.propertyPath.Length + 1; // Relative path must skip this path and the following '.' (period)
						while( !SerializedProperty.EqualContents( subProperty, endProperty ) )
						{
							if( subProperty.propertyType == SerializedPropertyType.ManagedReference )
							{
								object nestedManagedObjectValue = subProperty.GetTargetObject();
								if( nestedManagedObjectValue != null )
								{
									if( nestedManagedObjects == null )
										nestedManagedObjects = new List<ManagedObjectClipboard.NestedManagedObject>( 2 );

									nestedManagedObjects.Add( new ManagedObjectClipboard.NestedManagedObject( nestedManagedObjectValue, subProperty.propertyPath.Substring( relativePathStartIndex ) ) );
								}
							}
							else if( subProperty.propertyType == SerializedPropertyType.ObjectReference || subProperty.propertyType == SerializedPropertyType.ExposedReference )
							{
								Object nestedUnityObjectValue = subProperty.GetTargetObject() as Object;
								if( nestedUnityObjectValue )
								{
									if( nestedUnityObjects == null )
										nestedUnityObjects = new List<ManagedObjectClipboard.NestedUnityObject>( 2 );

									nestedUnityObjects.Add( new ManagedObjectClipboard.NestedUnityObject( nestedUnityObjectValue, subProperty.propertyPath.Substring( relativePathStartIndex ) ) );
								}
							}

							subProperty.Next( subProperty.propertyType == SerializedPropertyType.Generic );
						}
					}

					return new ManagedObjectClipboard( property.type, value, nestedManagedObjects == null ? null : nestedManagedObjects.ToArray(), nestedUnityObjects == null ? null : nestedUnityObjects.ToArray() );
#endif
				case SerializedPropertyType.ObjectReference: return property.objectReferenceValue;
				case SerializedPropertyType.Quaternion:
					// Special case: copy Transform's Rotation as localEulerAngles instead of localRotation
					if( property.name == "m_LocalRotation" && property.serializedObject.targetObject is Transform && (InspectorMode) inspectorModeGetter.GetValue( property.serializedObject, null ) == InspectorMode.Normal )
					{
						Transform targetTransform = (Transform) property.serializedObject.targetObject;
#if UNITY_2017_2_OR_NEWER
						return (VectorClipboard) TransformUtils.GetInspectorRotation( targetTransform );
#else
						return (VectorClipboard) (Vector3) transformDisplayedRotationGetter.Invoke( targetTransform, new object[1] { transformRotationOrderGetter.GetValue( targetTransform, null ) } );
#endif
					}
					else
						return (VectorClipboard) property.quaternionValue;
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

						return new ArrayClipboard( property.arrayElementType, elements );
					}
#if UNITY_2017_1_OR_NEWER
					else if( property.isFixedBuffer )
					{
						object[] elements = new object[property.fixedBufferSize];
						for( int i = 0; i < elements.Length; i++ )
							elements[i] = CopyValue( property.GetFixedBufferElementAtIndex( i ) );

						return new ArrayClipboard( elements.Length > 0 ? property.GetFixedBufferElementAtIndex( 0 ).type : null, elements );
					}
#endif
					else if( property.hasChildren )
					{
						int count = 0;
						foreach( SerializedProperty iterator in property.EnumerateDirectChildren() )
							count++;

						string type = property.type;
						string[] variables = new string[count];
						object[] values = new object[count];

						if( count > 0 )
						{
							int variableIndex = 0;
							foreach( SerializedProperty iterator in property.EnumerateDirectChildren() )
							{
								variables[variableIndex] = iterator.name;
								values[variableIndex++] = CopyValue( iterator );
							}
						}

						return new GenericObjectClipboard( type, variables, values );
					}
					else
						return null;
				default: return null;
			}
		}

		public static void PasteValue( this SerializedProperty property, SerializedClipboard clipboard )
		{
			// There is one edge case in Smart Copy & Paste system:
			// Imagine right clicking object A's Transform and copying it with Inspect+. Serialized component's RelativePath will
			// point to self (Transform itself). Now, imagine right clicking a property of object B in the Inspector and pasting
			// the copied Transform there. Normally, if SmartCopyPaste is enabled, B.property would now point to B because RelativePath
			// (which is 'self') would be resolved to object B. However, in this scenario, regardless of the value of SmartCopyPaste,
			// we'd expect B.property to point to A because we are explicity copying A's Transform and pasting it to B. If we had wanted
			// to paste object B itself to B.property, we'd simply drag&drop B from Hierarchy to B.property. So, in this scenario, we must ignore
			// the value of SmartCopyPaste. One way of doing it is to pass null to GetClipboardObject
			bool shouldIgnoreSmartCopyPaste = ( property.propertyType == SerializedPropertyType.ObjectReference || property.propertyType == SerializedPropertyType.ExposedReference ) && !clipboard.HasSerializedPropertyOrigin;

			if( property.serializedObject.isEditingMultipleObjects && InspectPlusSettings.Instance.SmartCopyPaste && !shouldIgnoreSmartCopyPaste )
			{
				// Smart paste should be applied to each selected Object separately
				Object[] targetObjects = property.serializedObject.targetObjects;
				Object context = property.serializedObject.context;
				string propertyPath = property.propertyPath;
				for( int i = 0; i < targetObjects.Length; i++ )
				{
					SerializedProperty _property = new SerializedObject( targetObjects[i], context ).FindProperty( propertyPath );
					PasteValue( _property, clipboard.RootValue.GetClipboardObject( targetObjects[i] ), true );
				}
			}
			else
				PasteValue( property, clipboard.RootValue.GetClipboardObject( shouldIgnoreSmartCopyPaste ? null : property.serializedObject.targetObject ), true );
		}

		public static void PasteValue( this SerializedProperty property, object clipboard, bool applyModifiedProperties )
		{
			switch( property.propertyType )
			{
				case SerializedPropertyType.AnimationCurve:
					if( clipboard is AnimationCurve ) property.animationCurveValue = (AnimationCurve) clipboard;
					break;
				case SerializedPropertyType.ArraySize:
					if( clipboard is long ) property.arraySize = (int) (long) clipboard;
					else if( clipboard is double ) property.arraySize = (int) (double) clipboard;
					break;
				case SerializedPropertyType.Boolean:
					if( clipboard is bool ) property.boolValue = (bool) clipboard;
					else if( clipboard is long ) property.boolValue = ( (long) clipboard ) != 0L;
					else if( clipboard is double ) property.boolValue = ( (double) clipboard ) != 0.0;
					break;
				case SerializedPropertyType.Bounds:
					if( clipboard is VectorClipboard ) property.boundsValue = (VectorClipboard) clipboard;
					break;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.BoundsInt:
					if( clipboard is VectorClipboard ) property.boundsIntValue = (VectorClipboard) clipboard;
					break;
#endif
				case SerializedPropertyType.Character:
					if( clipboard is long ) property.intValue = (int) (long) clipboard;
					else if( clipboard is string ) property.intValue = ( (string) clipboard )[0];
					break;
				case SerializedPropertyType.Color:
					if( clipboard is Color ) property.colorValue = (Color) clipboard;
					else if( clipboard is VectorClipboard ) property.colorValue = (VectorClipboard) clipboard;
					break;
				case SerializedPropertyType.Enum:
					if( clipboard is long ) property.intValue = (int) (long) clipboard;
					break;
				case SerializedPropertyType.ExposedReference: TryAssignClipboardToObjectProperty( property, clipboard, false ); break;
				case SerializedPropertyType.Float:
					if( clipboard is long ) property.doubleValue = (long) clipboard;
					else if( clipboard is double ) property.doubleValue = (double) clipboard;
					break;
				case SerializedPropertyType.Gradient:
					if( clipboard is Gradient ) gradientValueGetter.SetValue( property, clipboard, null );
					break;
				case SerializedPropertyType.Integer:
					if( clipboard is long ) property.longValue = (long) clipboard;
					else if( clipboard is double ) property.longValue = (long) (double) clipboard;
					break;
				case SerializedPropertyType.LayerMask:
					if( clipboard is long ) property.intValue = (int) (long) clipboard;
					break;
#if UNITY_2019_3_OR_NEWER
				case SerializedPropertyType.ManagedReference:
					if( clipboard == null )
						property.managedReferenceValue = null;
					else if( clipboard is ManagedObjectClipboard )
					{
						// property.managedReferenceValue copies the value, so assigning the same value to 2 different
						// SerializedProperty's managedReferenceValue will result in 2 copies of that value, which is not the
						// SerializeReference way. But if we assign the value with reflection, then the original value won't be
						// copied at each assignment. For this reason, try assigning the value with reflection first and if that
						// fails, fallback to managedReferenceValue
						try
						{
							if( !property.serializedObject.isEditingMultipleObjects )
							{
								Undo.RecordObject( property.serializedObject.targetObject, string.Empty );
								property.SetTargetObject( ( (ManagedObjectClipboard) clipboard ).value );
							}
							else
							{
								Object[] targetObjects = property.serializedObject.targetObjects;
								for( int i = 0; i < targetObjects.Length; i++ )
								{
									Undo.RecordObject( targetObjects[i], string.Empty );
									new SerializedObject( targetObjects[i] ).FindProperty( property.propertyPath ).SetTargetObject( ( (ManagedObjectClipboard) clipboard ).value );
								}
							}
						}
						catch( Exception e )
						{
							Debug.LogException( e );
							property.managedReferenceValue = ( (ManagedObjectClipboard) clipboard ).value;
						}
					}

					break;
#endif
				case SerializedPropertyType.ObjectReference: TryAssignClipboardToObjectProperty( property, clipboard, false ); break;
				case SerializedPropertyType.Quaternion:
					if( clipboard is VectorClipboard )
					{
						// Special case: paste Transform's Rotation as localEulerAngles instead of localRotation
						if( property.name == "m_LocalRotation" && property.serializedObject.targetObject is Transform && (InspectorMode) inspectorModeGetter.GetValue( property.serializedObject, null ) == InspectorMode.Normal )
							property.quaternionValue = Quaternion.Euler( (VectorClipboard) clipboard );
						else
							property.quaternionValue = (VectorClipboard) clipboard;
					}

					break;
				case SerializedPropertyType.Rect:
					if( clipboard is VectorClipboard ) property.rectValue = (VectorClipboard) clipboard;
					break;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.RectInt:
					if( clipboard is VectorClipboard ) property.rectIntValue = (VectorClipboard) clipboard;
					break;
#endif
				case SerializedPropertyType.String: property.stringValue = clipboard != null ? clipboard.ToString() : ""; break;
				case SerializedPropertyType.Vector2:
					if( clipboard is VectorClipboard ) property.vector2Value = (VectorClipboard) clipboard;
					else if( clipboard is Color ) property.vector2Value = (VectorClipboard) (Color) clipboard;
					break;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.Vector2Int:
					if( clipboard is VectorClipboard ) property.vector2IntValue = (VectorClipboard) clipboard;
					else if( clipboard is Color ) property.vector2IntValue = (VectorClipboard) (Color32) (Color) clipboard;
					break;
#endif
				case SerializedPropertyType.Vector3:
					if( clipboard is VectorClipboard ) property.vector3Value = (VectorClipboard) clipboard;
					else if( clipboard is Color ) property.vector3Value = (VectorClipboard) (Color) clipboard;
					break;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.Vector3Int:
					if( clipboard is VectorClipboard ) property.vector3IntValue = (VectorClipboard) clipboard;
					else if( clipboard is Color ) property.vector3IntValue = (VectorClipboard) (Color32) (Color) clipboard;
					break;
#endif
				case SerializedPropertyType.Vector4:
					if( clipboard is VectorClipboard ) property.vector4Value = (VectorClipboard) clipboard;
					else if( clipboard is Color ) property.vector4Value = (VectorClipboard) (Color) clipboard;
					break;
				case SerializedPropertyType.Generic:
					if( property.isArray && clipboard is ArrayClipboard )
					{
						ArrayClipboard array = (ArrayClipboard) clipboard;
						property.arraySize = array.elements.Length;
						for( int i = 0; i < array.elements.Length; i++ )
						{
							SerializedProperty element = property.GetArrayElementAtIndex( i );
							PasteValue( element, array.elements[i], false );
						}
					}
#if UNITY_2017_1_OR_NEWER
					else if( property.isFixedBuffer && clipboard is ArrayClipboard )
					{
						ArrayClipboard array = (ArrayClipboard) clipboard;
						int count = Mathf.Min( array.elements.Length, property.fixedBufferSize );
						for( int i = 0; i < count; i++ )
						{
							SerializedProperty element = property.GetFixedBufferElementAtIndex( i );
							PasteValue( element, array.elements[i], false );
						}
					}
#endif
					else if( property.hasChildren && clipboard is GenericObjectClipboard )
					{
						GenericObjectClipboard obj = (GenericObjectClipboard) clipboard;
						if( obj.variables.Length > 0 )
						{
							int variableIndex = 0;
							foreach( SerializedProperty iterator in property.EnumerateDirectChildren() )
							{
								string variable = iterator.name;

								// Unless the target class/struct's variables has changed, this if condition will always
								// be true and we won't have to call Array.IndexOf at all
								if( variableIndex < obj.variables.Length && variable == obj.variables[variableIndex] )
									PasteValue( iterator, obj.values[variableIndex++], false );
								else
								{
									int _variableIndex = Array.IndexOf( obj.variables, variable );
									if( _variableIndex >= 0 )
									{
										PasteValue( iterator, obj.values[_variableIndex], false );
										variableIndex = _variableIndex + 1;
									}
								}
							}
						}
					}

					break;
			}

			if( applyModifiedProperties )
				property.serializedObject.ApplyModifiedProperties();
		}

		public static bool CanPasteValue( this SerializedProperty property, IPObject clipboard, bool allowNullObjectValues )
		{
			return clipboard != null && CanPasteValue( property, clipboard.GetClipboardObject( property.serializedObject.targetObject ), allowNullObjectValues );
		}

		public static bool CanPasteValue( this SerializedProperty property, object clipboard, bool allowNullObjectValues )
		{
			if( !property.editable )
				return false;

			if( clipboard == null || clipboard.Equals( null ) )
			{
				if( !allowNullObjectValues )
					return false;

				switch( property.propertyType )
				{
					case SerializedPropertyType.ExposedReference: return true;
#if UNITY_2019_3_OR_NEWER
					case SerializedPropertyType.ManagedReference: return true;
#endif
					case SerializedPropertyType.ObjectReference: return true;
					default: return false;
				}
			}

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
				case SerializedPropertyType.Color: return clipboard is Color || clipboard is VectorClipboard;
				case SerializedPropertyType.Enum: return clipboard is long;
				case SerializedPropertyType.ExposedReference: return TryAssignClipboardToObjectProperty( property, clipboard, true );
				case SerializedPropertyType.Float: return clipboard is double || clipboard is long;
				case SerializedPropertyType.Gradient: return clipboard is Gradient;
				case SerializedPropertyType.Integer: return clipboard is long || clipboard is double;
				case SerializedPropertyType.LayerMask: return clipboard is long;
#if UNITY_2019_3_OR_NEWER
				case SerializedPropertyType.ManagedReference:
					if( clipboard is ManagedObjectClipboard )
					{
						string fieldTypeRaw = property.managedReferenceFieldTypename;
						if( string.IsNullOrEmpty( fieldTypeRaw ) )
							return ( (ManagedObjectClipboard) clipboard ).type == property.type;

						string[] fieldTypeSplit = fieldTypeRaw.Split( ' ' );
						Type fieldType = Assembly.Load( fieldTypeSplit[0] ).GetType( fieldTypeSplit[1] );

						return fieldType.IsAssignableFrom( ( (ManagedObjectClipboard) clipboard ).value.GetType() );
					}
					else
						return false;
#endif
				case SerializedPropertyType.ObjectReference: return TryAssignClipboardToObjectProperty( property, clipboard, true );
				case SerializedPropertyType.Quaternion: return clipboard is VectorClipboard;
				case SerializedPropertyType.Rect: return clipboard is VectorClipboard;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.RectInt: return clipboard is VectorClipboard;
#endif
				case SerializedPropertyType.String: return true;
				case SerializedPropertyType.Vector2: return clipboard is VectorClipboard || clipboard is Color;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.Vector2Int: return clipboard is VectorClipboard || clipboard is Color;
#endif
				case SerializedPropertyType.Vector3: return clipboard is VectorClipboard || clipboard is Color;
#if UNITY_2017_2_OR_NEWER
				case SerializedPropertyType.Vector3Int: return clipboard is VectorClipboard || clipboard is Color;
#endif
				case SerializedPropertyType.Vector4: return clipboard is VectorClipboard || clipboard is Color;
				case SerializedPropertyType.Generic:
					if( property.isArray )
						return clipboard is ArrayClipboard && ( (ArrayClipboard) clipboard ).elementType == property.arrayElementType;
#if UNITY_2017_1_OR_NEWER
					else if( property.isFixedBuffer )
						return clipboard is ArrayClipboard && property.fixedBufferSize > 0 && ( (ArrayClipboard) clipboard ).elementType == property.GetFixedBufferElementAtIndex( 0 ).type;
#endif
					else if( property.hasChildren )
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

		#region SerializedProperty Enumerators
		public static IEnumerable<SerializedProperty> EnumerateDirectChildren( this SerializedObject serializedObject )
		{
			return EnumerateDirectChildrenInternal( serializedObject.GetIterator(), true );
		}

		public static IEnumerable<SerializedProperty> EnumerateDirectChildren( this SerializedProperty property )
		{
			return EnumerateDirectChildrenInternal( property, false );
		}

		private static IEnumerable<SerializedProperty> EnumerateDirectChildrenInternal( SerializedProperty property, bool isRootProperty )
		{
			if( !property.hasChildren )
				yield break;

			SerializedProperty propertyAll = property.Copy();
			SerializedProperty propertyVisible = property.Copy();
			SerializedProperty endProperty = isRootProperty ? null : property.GetEndProperty( true );
			if( propertyAll.Next( true ) )
			{
				bool iteratingVisible = propertyVisible.NextVisible( true );
				do
				{
					bool isVisible = iteratingVisible && SerializedProperty.EqualContents( propertyAll, propertyVisible );
					if( isVisible )
						iteratingVisible = propertyVisible.NextVisible( false );
					else
					{
						Type propFieldType;
						if( fieldInfoGetter( propertyAll, out propFieldType ) != null )
							isVisible = true;
					}

					if( isVisible )
						yield return propertyAll;
				} while( propertyAll.Next( false ) && !SerializedProperty.EqualContents( propertyAll, endProperty ) );
			}
		}
		#endregion

		#region SerializedProperty Value Getter With Reflection
		public static object GetTargetObject( this SerializedProperty property )
		{
			return TraversePathAndGetFieldValue( property.serializedObject.targetObject, property.propertyPath );
		}

		// Credit: http://answers.unity.com/answers/425602/view.html
		public static object TraversePathAndGetFieldValue( object source, string rawPath )
		{
			object result = source;
			string[] path = rawPath.Replace( ".Array.data[", "[" ).Split( '.' );
			for( int i = 0; i < path.Length; i++ )
			{
				string pathElement = path[i];

				int arrayStartIndex = pathElement.IndexOf( '[' );
				if( arrayStartIndex < 0 )
					result = GetFieldValue( result, pathElement );
				else
				{
					string variableName = pathElement.Substring( 0, arrayStartIndex );

					int arrayEndIndex = pathElement.IndexOf( ']', arrayStartIndex + 1 );
					int arrayElementIndex = int.Parse( pathElement.Substring( arrayStartIndex + 1, arrayEndIndex - arrayStartIndex - 1 ) );
					result = GetFieldValue( result, variableName, arrayElementIndex );
				}
			}

			return result;
		}

		// Credit: http://answers.unity.com/answers/425602/view.html
		private static object GetFieldValue( object source, string fieldName )
		{
			if( source == null )
				return null;

			FieldInfo fieldInfo = null;
			Type type = source.GetType();
			while( fieldInfo == null && type != typeof( object ) )
			{
				fieldInfo = type.GetField( fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly );
				type = type.BaseType;
			}

			if( fieldInfo != null )
				return fieldInfo.GetValue( source );

			PropertyInfo propertyInfo = null;
			type = source.GetType();
			while( propertyInfo == null && type != typeof( object ) )
			{
				propertyInfo = type.GetProperty( fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.IgnoreCase );
				type = type.BaseType;
			}

			if( propertyInfo != null )
				return propertyInfo.GetValue( source, null );

			if( fieldName.Length > 2 && fieldName.StartsWith( "m_", StringComparison.OrdinalIgnoreCase ) )
				return GetFieldValue( source, fieldName.Substring( 2 ) );

			return null;
		}

		// Credit: http://answers.unity.com/answers/425602/view.html
		private static object GetFieldValue( object source, string fieldName, int arrayIndex )
		{
			IEnumerable enumerable = GetFieldValue( source, fieldName ) as IEnumerable;
			if( enumerable == null )
				return null;

			if( enumerable is IList )
				return ( (IList) enumerable )[arrayIndex];

			IEnumerator enumerator = enumerable.GetEnumerator();
			for( int i = 0; i <= arrayIndex; i++ )
				enumerator.MoveNext();

			return enumerator.Current;
		}
		#endregion

		#region SerializedProperty Value Setter With Reflection
		public static void SetTargetObject( this SerializedProperty property, object value )
		{
			TraversePathAndSetFieldValue( property.serializedObject.targetObject, property.propertyPath, value );
		}

		// Credit: http://answers.unity.com/answers/425602/view.html
		public static void TraversePathAndSetFieldValue( object source, string rawPath, object value )
		{
			// Assume we have component A which has a struct variable called B and we want to change B's C variable's value
			// with this function. If all we do is get B's corresponding FieldInfo for C and call its SetValue function, we
			// won't really change the value of A.B.C because B is a struct which was boxed when we called SetValue and we
			// essentially changed a copy of B, not B itself. So, we need to keep a reference to our boxed B variable, change
			// its C variable and then assign the boxed B value back to A. This way, we will in fact change the value of A.B.C
			// 
			// In this code, there are 2 for loops. In the first loop, we are basically storing the boxed values (B) in setValues
			// and at the end of the loop, we change B.C's value. In the second loop, we assign boxed values back to their parent
			// variables (assigning boxed B value back to A)
			string[] path = rawPath.Replace( ".Array.data[", "[" ).Split( '.' );
			object[] setValues = new object[path.Length];
			setValues[0] = source;
			for( int i = 0; i < path.Length; i++ )
			{
				string pathElement = path[i];

				int arrayStartIndex = pathElement.IndexOf( '[' );
				if( arrayStartIndex < 0 )
				{
					if( i < path.Length - 1 )
						setValues[i + 1] = GetFieldValue( setValues[i], pathElement );
					else
						SetFieldValue( setValues[i], pathElement, value );
				}
				else
				{
					string variableName = pathElement.Substring( 0, arrayStartIndex );

					int arrayEndIndex = pathElement.IndexOf( ']', arrayStartIndex + 1 );
					int arrayElementIndex = int.Parse( pathElement.Substring( arrayStartIndex + 1, arrayEndIndex - arrayStartIndex - 1 ) );
					if( i < path.Length - 1 )
						setValues[i + 1] = GetFieldValue( setValues[i], pathElement, arrayElementIndex );
					else
						SetFieldValue( setValues[i], variableName, arrayElementIndex, value );
				}
			}

			for( int i = path.Length - 2; i >= 0; i-- )
			{
				string pathElement = path[i];

				int arrayStartIndex = pathElement.IndexOf( '[' );
				if( arrayStartIndex < 0 )
					SetFieldValue( setValues[i], pathElement, setValues[i + 1] );
				else
				{
					string variableName = pathElement.Substring( 0, arrayStartIndex );

					int arrayEndIndex = pathElement.IndexOf( ']', arrayStartIndex + 1 );
					int arrayElementIndex = int.Parse( pathElement.Substring( arrayStartIndex + 1, arrayEndIndex - arrayStartIndex - 1 ) );
					SetFieldValue( setValues[i], variableName, arrayElementIndex, setValues[i + 1] );
				}
			}
		}

		// Credit: http://answers.unity.com/answers/425602/view.html
		private static void SetFieldValue( object source, string fieldName, object value )
		{
			if( source == null )
				return;

			FieldInfo fieldInfo = null;
			Type type = source.GetType();
			while( fieldInfo == null && type != typeof( object ) )
			{
				fieldInfo = type.GetField( fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly );
				type = type.BaseType;
			}

			if( fieldInfo != null )
			{
				fieldInfo.SetValue( source, value );
				return;
			}

			PropertyInfo propertyInfo = null;
			type = source.GetType();
			while( propertyInfo == null && type != typeof( object ) )
			{
				propertyInfo = type.GetProperty( fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.IgnoreCase );
				type = type.BaseType;
			}

			if( propertyInfo != null )
			{
				propertyInfo.SetValue( source, value, null );
				return;
			}

			if( fieldName.Length > 2 && fieldName.StartsWith( "m_", StringComparison.OrdinalIgnoreCase ) )
				SetFieldValue( source, fieldName.Substring( 2 ), value );
		}

		// Credit: http://answers.unity.com/answers/425602/view.html
		private static void SetFieldValue( object source, string fieldName, int arrayIndex, object value )
		{
			IEnumerable enumerable = GetFieldValue( source, fieldName ) as IEnumerable;
			if( enumerable is IList )
				( (IList) enumerable )[arrayIndex] = value;
		}
		#endregion
	}
}