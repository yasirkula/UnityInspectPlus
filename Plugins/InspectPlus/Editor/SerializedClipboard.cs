using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using GenericObjectClipboard = InspectPlusNamespace.SerializablePropertyExtensions.GenericObjectClipboard;
using ArrayClipboard = InspectPlusNamespace.SerializablePropertyExtensions.ArrayClipboard;
using VectorClipboard = InspectPlusNamespace.SerializablePropertyExtensions.VectorClipboard;
using ManagedObjectClipboard = InspectPlusNamespace.SerializablePropertyExtensions.ManagedObjectClipboard;
using GameObjectHierarchyClipboard = InspectPlusNamespace.SerializablePropertyExtensions.GameObjectHierarchyClipboard;
using ComponentGroupClipboard = InspectPlusNamespace.SerializablePropertyExtensions.ComponentGroupClipboard;
using AssetFilesClipboard = InspectPlusNamespace.SerializablePropertyExtensions.AssetFilesClipboard;
#if UNITY_2021_2_OR_NEWER
using PrefabStage = UnityEditor.SceneManagement.PrefabStage;
using PrefabStageUtility = UnityEditor.SceneManagement.PrefabStageUtility;
#elif UNITY_2018_3_OR_NEWER
using PrefabStage = UnityEditor.Experimental.SceneManagement.PrefabStage;
using PrefabStageUtility = UnityEditor.Experimental.SceneManagement.PrefabStageUtility;
#endif

namespace InspectPlusNamespace
{
	public class SerializedClipboard
	{
		#region Serialized Types
		public enum IPObjectType
		{
			Null = 0, Type = 1,
			Asset = 2, AssetReference = 3,
			SceneObject = 4, SceneObjectReference = 5,
			ManagedObject = 6, ManagedReference = 7,
			Array = 8, GenericObject = 9,
			Vector = 10, Color = 11,
			Long = 12, Double = 13, String = 14,
			AnimationCurve = 15, Gradient = 16,
			GameObjectHierarchy = 17,
			ComponentGroup = 19,
			AssetFiles = 18
		};

		public abstract class IPObject
		{
			public string Name;

			protected SerializedClipboard root;

			protected IPObject( SerializedClipboard root )
			{
				this.root = root;
			}

			protected IPObject( SerializedClipboard root, string name ) : this( root )
			{
				Name = name;
			}

			public abstract object GetClipboardObject( Object context );

			public virtual void Serialize( BinaryWriter writer )
			{
				SerializeString( writer, Name );
			}

			public virtual void Deserialize( BinaryReader reader )
			{
				Name = DeserializeString( reader );
			}
		}

		public class IPNull : IPObject
		{
			public override object GetClipboardObject( Object context ) { return null; }

			public IPNull( SerializedClipboard root ) : base( root ) { }
			public IPNull( SerializedClipboard root, string name ) : base( root, name ) { }
		}

		public class IPType : IPObject
		{
			public string AssemblyQualifiedName;

			private bool typeRecreated;
			private Type m_type;
			public Type Type { get { return (Type) GetClipboardObject( null ); } } // Useful shorthand property

			// AssemblyQualifiedName -> Type lookup table to quickly resolve commonly used types
			private static readonly Dictionary<string, Type> typeLookupTable = new Dictionary<string, Type>( 64 );

			public IPType( SerializedClipboard root ) : base( root ) { }
			public IPType( SerializedClipboard root, Type type ) : base( root, type.Name )
			{
				AssemblyQualifiedName = type.AssemblyQualifiedName;
			}

			public override object GetClipboardObject( Object context )
			{
				// We want to call Type.GetType only once but it can return null. Thus, we aren't using "if( type == null )"
				if( typeRecreated )
					return m_type;

				typeRecreated = true;

				if( typeLookupTable.TryGetValue( AssemblyQualifiedName, out m_type ) )
					return m_type;

				try
				{
					m_type = Type.GetType( AssemblyQualifiedName );
					if( m_type != null )
						return m_type;

					// Unity classes' AssemblyQualifiedNames can change between Unity versions but their FullNames usually stay the same
					int fullNameEndIndex = AssemblyQualifiedName.IndexOf( ',' );
					if( fullNameEndIndex >= 0 )
					{
						string typeFullName = AssemblyQualifiedName.Substring( 0, fullNameEndIndex );
						if( typeFullName.StartsWith( "UnityEngine" ) )
						{
							// A very common Assembly change is UnityEngine <-> UnityEngine.CoreModule 
							if( AssemblyQualifiedName.Contains( "UnityEngine.CoreModule," ) )
							{
								// Type conversion from newer Unity versions to older Unity versions
								m_type = Type.GetType( AssemblyQualifiedName.Replace( "UnityEngine.CoreModule,", "UnityEngine," ) );
							}
							else if( AssemblyQualifiedName.Contains( "UnityEngine," ) )
							{
								// Type conversion from older Unity versions to newer Unity versions
								m_type = Type.GetType( AssemblyQualifiedName.Replace( "UnityEngine,", "UnityEngine.CoreModule," ) );
							}

							// Search all loaded Unity assemblies for the type
							if( m_type != null )
								return m_type;

							foreach( Assembly assembly in AppDomain.CurrentDomain.GetAssemblies() )
							{
								if( !assembly.FullName.StartsWith( "UnityEngine" ) )
									continue;
#if NET_4_6 || NET_STANDARD_2_0
								if( assembly.IsDynamic )
									continue;
#endif

								try
								{
									foreach( Type type in assembly.GetExportedTypes() )
									{
										if( type.FullName == typeFullName )
										{
											m_type = type;
											return m_type;
										}
									}
								}
								catch( NotSupportedException ) { }
								catch( FileNotFoundException ) { }
								catch( ReflectionTypeLoadException ) { }
								catch( Exception e )
								{
									Debug.LogError( "Couldn't search assembly for type: " + assembly.GetName().Name + "\n" + e.ToString() );
								}
							}
						}
						else
						{
							// Search all loaded assemblies for the type
							foreach( Assembly assembly in AppDomain.CurrentDomain.GetAssemblies() )
							{
#if NET_4_6 || NET_STANDARD_2_0
								if( assembly.IsDynamic )
									continue;
#endif

								try
								{
									foreach( Type type in assembly.GetExportedTypes() )
									{
										if( type.FullName == typeFullName )
										{
											m_type = type;
											return m_type;
										}
									}
								}
								catch( NotSupportedException ) { }
								catch( FileNotFoundException ) { }
								catch( ReflectionTypeLoadException ) { }
								catch( Exception e )
								{
									Debug.LogError( "Couldn't search assembly for type: " + assembly.GetName().Name + "\n" + e.ToString() );
								}
							}
						}
					}

					return m_type;
				}
				finally
				{
					typeLookupTable[AssemblyQualifiedName] = m_type;
				}
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				SerializeString( writer, AssemblyQualifiedName );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				AssemblyQualifiedName = DeserializeString( reader );
			}
		}

		public abstract class IPObjectWithChild : IPObject
		{
			public IPObject[] Children;

			protected IPObjectWithChild( SerializedClipboard root ) : base( root ) { }
			protected IPObjectWithChild( SerializedClipboard root, string name ) : base( root, name ) { }

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				root.SerializeArray( writer, Children );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				Children = root.DeserializeArray<IPObject>( reader );
			}
		}

		public abstract class IPObjectWithType : IPObject
		{
			public int TypeIndex;
			protected IPType serializedType;

			protected IPObjectWithType( SerializedClipboard root ) : base( root ) { }
			protected IPObjectWithType( SerializedClipboard root, string name, Type type ) : base( root, name )
			{
				TypeIndex = root.GetIndexOfTypeToSerialize( type, true );
			}

			public override object GetClipboardObject( Object context )
			{
				serializedType = root.Types[TypeIndex];
				return null;
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				writer.Write( TypeIndex );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				TypeIndex = reader.ReadInt32();
			}
		}

		public class IPArray : IPObjectWithChild
		{
			public string ElementType;

			public IPArray( SerializedClipboard root ) : base( root ) { }
			public IPArray( SerializedClipboard root, string name, ArrayClipboard value, Object source ) : base( root, name )
			{
				ElementType = value.elementType;
				Children = new IPObject[value.elements.Length];

				for( int i = 0; i < value.elements.Length; i++ )
					Children[i] = root.ConvertClipboardObjectToIPObject( value.elements[i], null, source );
			}

			public override object GetClipboardObject( Object context )
			{
				object[] elements = new object[Children != null ? Children.Length : 0];
				for( int i = 0; i < elements.Length; i++ )
					elements[i] = Children[i].GetClipboardObject( context );

				return new ArrayClipboard( ElementType, elements );
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				SerializeString( writer, ElementType );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				ElementType = DeserializeString( reader );
			}
		}

		public class IPGenericObject : IPObjectWithChild
		{
			public string Type;

			public IPGenericObject( SerializedClipboard root ) : base( root ) { }
			public IPGenericObject( SerializedClipboard root, string name, GenericObjectClipboard value, Object source ) : base( root, name )
			{
				Type = value.type;
				Children = new IPObject[value.variables.Length];

				for( int i = 0; i < value.variables.Length; i++ )
					Children[i] = root.ConvertClipboardObjectToIPObject( value.values[i], value.variables[i], source );
			}

			public override object GetClipboardObject( Object context )
			{
				string[] variables = new string[Children != null ? Children.Length : 0];
				object[] values = new object[variables.Length];
				for( int i = 0; i < variables.Length; i++ )
				{
					variables[i] = Children[i].Name;
					values[i] = Children[i].GetClipboardObject( context );
				}

				return new GenericObjectClipboard( Type, variables, values );
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				SerializeString( writer, Type );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				Type = DeserializeString( reader );
			}
		}

		// A [SerializeReference] object
		public class IPManagedObject : IPObjectWithType
		{
			public class NestedReference
			{
				public string RelativePath;
				public int ReferenceIndex;
			}

			public string Value;

			public NestedReference[] NestedManagedObjects;
			public NestedReference[] NestedSceneObjects;
			public NestedReference[] NestedAssets;

			// Each different UnityEngine.Object context will receive a different copy of this managed object. If the same object was used,
			// altering the managed object's nested assets/scene objects due to RelativePath would affect the previous contexts that this
			// managed object was assigned to, as well
			private readonly Dictionary<Object, ManagedObjectClipboard> valuesPerContext = new Dictionary<Object, ManagedObjectClipboard>( 2 );

			public IPManagedObject( SerializedClipboard root ) : base( root ) { }
			public IPManagedObject( SerializedClipboard root, ManagedObjectClipboard clipboard, Object source ) : base( root, null, clipboard.value.GetType() )
			{
				Value = EditorJsonUtility.ToJson( clipboard.value );

				if( source )
					valuesPerContext[source] = clipboard;

				ManagedObjectClipboard.NestedManagedObject[] nestedManagedObjects = clipboard.nestedManagedObjects;
				if( nestedManagedObjects != null && nestedManagedObjects.Length > 0 )
				{
					NestedManagedObjects = new NestedReference[nestedManagedObjects.Length];

					int validNestedManagedObjectCount = 0;
					for( int i = 0; i < nestedManagedObjects.Length; i++ )
					{
						object nestedReference = nestedManagedObjects[i].reference;
						if( nestedReference == null || nestedReference.Equals( null ) )
							continue;

						int referenceIndex = root.GetIndexOfManagedObjectToSerialize( nestedReference, false );
						if( referenceIndex >= 0 )
						{
							NestedManagedObjects[validNestedManagedObjectCount++] = new NestedReference()
							{
								ReferenceIndex = referenceIndex,
								RelativePath = nestedManagedObjects[i].relativePath
							};
						}
					}

					if( validNestedManagedObjectCount == 0 )
						NestedManagedObjects = null;
					else if( validNestedManagedObjectCount != NestedManagedObjects.Length )
						Array.Resize( ref NestedManagedObjects, validNestedManagedObjectCount );
				}

				ManagedObjectClipboard.NestedUnityObject[] nestedUnityObjects = clipboard.nestedUnityObjects;
				if( nestedUnityObjects != null && nestedUnityObjects.Length > 0 )
				{
					NestedSceneObjects = new NestedReference[nestedUnityObjects.Length];
					NestedAssets = new NestedReference[nestedUnityObjects.Length];

					int validNestedSceneObjectCount = 0;
					int validNestedAssetCount = 0;
					for( int i = 0; i < nestedUnityObjects.Length; i++ )
					{
						Object nestedReference = nestedUnityObjects[i].reference;
						if( !nestedReference )
							continue;

						if( AssetDatabase.Contains( nestedReference ) )
						{
							int referenceIndex = root.GetIndexOfAssetToSerialize( nestedReference, true );
							if( referenceIndex >= 0 )
							{
								NestedAssets[validNestedAssetCount++] = new NestedReference()
								{
									ReferenceIndex = referenceIndex,
									RelativePath = nestedUnityObjects[i].relativePath
								};
							}
						}
						else
						{
							int referenceIndex = root.GetIndexOfSceneObjectToSerialize( nestedReference, true );
							if( referenceIndex >= 0 )
							{
								NestedSceneObjects[validNestedSceneObjectCount++] = new NestedReference()
								{
									ReferenceIndex = referenceIndex,
									RelativePath = nestedUnityObjects[i].relativePath
								};
							}
						}
					}

					if( validNestedSceneObjectCount == 0 )
						NestedSceneObjects = null;
					else if( validNestedSceneObjectCount != NestedSceneObjects.Length )
						Array.Resize( ref NestedSceneObjects, validNestedSceneObjectCount );

					if( validNestedAssetCount == 0 )
						NestedAssets = null;
					else if( validNestedAssetCount != NestedAssets.Length )
						Array.Resize( ref NestedAssets, validNestedAssetCount );
				}
			}

			public override object GetClipboardObject( Object context )
			{
				// Initialize serializedType
				base.GetClipboardObject( context );

				if( !context )
				{
					Debug.LogWarning( "Empty context encountered while deserializing managed object." );
					return null;
				}

				ManagedObjectClipboard cachedResult;
				if( valuesPerContext.TryGetValue( context, out cachedResult ) )
					return cachedResult;

				if( serializedType.Type != null )
				{
					object value;
					if( serializedType.Type.GetConstructor( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null ) != null )
						value = Activator.CreateInstance( serializedType.Type, true );
					else
						value = FormatterServices.GetUninitializedObject( serializedType.Type );

					EditorJsonUtility.FromJsonOverwrite( Value, value );

					// No need to fill the nestedManagedObjects and nestedUnityObjects arrays, they aren't used by SerializablePropertyExtensions
					cachedResult = new ManagedObjectClipboard( serializedType.Name, value, null, null );

					// Avoiding StackOverflowException if cyclic nested references exist
					valuesPerContext[context] = cachedResult;

					// We've assigned cachedResult its value before filling in the nested managed objects because a nested managed object can also reference
					// this managed object (cyclic reference) and in which case, we want our cachedResult value to be ready
					ApplyNestedReferencesToValue( value, NestedManagedObjects, root.ManagedObjects, context );
					ApplyNestedReferencesToValue( value, NestedSceneObjects, root.SceneObjects, context );
					ApplyNestedReferencesToValue( value, NestedAssets, root.Assets, context );
				}

				valuesPerContext[context] = cachedResult;
				return cachedResult;
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );

				SerializeString( writer, Value );
				SerializeNestedReferences( writer, NestedManagedObjects );
				SerializeNestedReferences( writer, NestedSceneObjects );
				SerializeNestedReferences( writer, NestedAssets );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );

				Value = DeserializeString( reader );
				NestedManagedObjects = DeserializeNestedReferences( reader );
				NestedSceneObjects = DeserializeNestedReferences( reader );
				NestedAssets = DeserializeNestedReferences( reader );
			}

			private void SerializeNestedReferences( BinaryWriter writer, NestedReference[] references )
			{
				if( references == null || references.Length == 0 )
					writer.Write( 0 );
				else
				{
					writer.Write( references.Length );

					for( int i = 0; i < references.Length; i++ )
					{
						SerializeString( writer, references[i].RelativePath );
						writer.Write( references[i].ReferenceIndex );
					}
				}
			}

			private NestedReference[] DeserializeNestedReferences( BinaryReader reader )
			{
				int arraySize = reader.ReadInt32();
				if( arraySize <= 0 )
					return null;

				NestedReference[] result = new NestedReference[arraySize];
				for( int i = 0; i < arraySize; i++ )
				{
					result[i] = new NestedReference()
					{
						RelativePath = DeserializeString( reader ),
						ReferenceIndex = reader.ReadInt32()
					};
				}

				return result;
			}

			private void ApplyNestedReferencesToValue( object value, NestedReference[] nestedReferences, IPObject[] referenceContainer, Object context )
			{
				if( nestedReferences != null )
				{
					for( int i = 0; i < nestedReferences.Length; i++ )
					{
						object nestedReference = referenceContainer[nestedReferences[i].ReferenceIndex].GetClipboardObject( context );
						if( nestedReference is ManagedObjectClipboard )
							nestedReference = ( (ManagedObjectClipboard) nestedReference ).value;

						if( nestedReference != null && !nestedReference.Equals( null ) )
							SerializablePropertyExtensions.TraversePathAndSetFieldValue( value, nestedReferences[i].RelativePath, nestedReference );
					}
				}
			}
		}

		public class IPManagedReference : IPObject
		{
			public int ManagedRefIndex;

			public IPManagedReference( SerializedClipboard root ) : base( root ) { }
			public IPManagedReference( SerializedClipboard root, string name, ManagedObjectClipboard value ) : base( root, name )
			{
				ManagedRefIndex = root.GetIndexOfManagedObjectToSerialize( value, true );
			}

			public override object GetClipboardObject( Object context )
			{
				return root.ManagedObjects[ManagedRefIndex].GetClipboardObject( context );
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				writer.Write( ManagedRefIndex );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				ManagedRefIndex = reader.ReadInt32();
			}
		}

		public abstract class IPUnityObject : IPObjectWithType
		{
			[Serializable]
			public struct PathComponent
			{
				private const int PATH_INDEX_THIS_OBJECT = -44566; // Equivalent of ./
				private const int PATH_INDEX_PARENT_OBJECT = -55677; // Equivalent of ../

				// If there are multiple siblings with the same name, this object's sibling index is stored in Index
				public string Name;
				public int Index;

				public bool PointsToSelf { get { return string.IsNullOrEmpty( Name ) && Index == PATH_INDEX_THIS_OBJECT; } }
				public bool PointsToParent { get { return string.IsNullOrEmpty( Name ) && Index == PATH_INDEX_PARENT_OBJECT; } }

				public PathComponent( string name, int index )
				{
					Name = name;
					Index = index;
				}

				public PathComponent( Transform source )
				{
					Name = source.name;
					Index = GetIndexOfTransformByName( source );
				}

				public PathComponent( bool pointsToSelfOrParent ) // false: self, true: parent
				{
					Name = null;
					Index = pointsToSelfOrParent ? PATH_INDEX_PARENT_OBJECT : PATH_INDEX_THIS_OBJECT;
				}
			}

			public string ObjectName;

			public PathComponent[] Path;
			public PathComponent[] RelativePath;

			// For serialized Components, this value holds the index of the Component among all Components of the same type in the GameObject
			public int ComponentIndex;

			protected IPUnityObject( SerializedClipboard root ) : base( root ) { }
			protected IPUnityObject( SerializedClipboard root, Object value, Object source ) : base( root, null, value.GetType() )
			{
				if( value && value is Component )
					ComponentIndex = GetIndexOfComponentByType( (Component) value, ( (Component) value ).GetComponents<Component>() );

				// Calculate RelativePath
				if( !source || !value || ( !( source is Component ) && !( source is GameObject ) ) || ( !( value is Component ) && !( value is GameObject ) ) )
					return;

				Transform sourceTransform = source is Component ? ( (Component) source ).transform : ( (GameObject) source ).transform;
				Transform targetTransform = value is Component ? ( (Component) value ).transform : ( (GameObject) value ).transform;
				if( sourceTransform == targetTransform )
					RelativePath = new PathComponent[1] { new PathComponent( false ) };
				else if( sourceTransform.root == targetTransform.root )
				{
					List<PathComponent> pathComponents = new List<PathComponent>( 5 );
					while( !targetTransform.IsChildOf( sourceTransform ) )
					{
						pathComponents.Add( new PathComponent( true ) );
						sourceTransform = sourceTransform.parent;
					}

					int insertIndex = pathComponents.Count;
					while( targetTransform != sourceTransform )
					{
						pathComponents.Insert( insertIndex, new PathComponent( targetTransform ) );
						targetTransform = targetTransform.parent;
					}

					RelativePath = pathComponents.ToArray();
				}
			}

			public override object GetClipboardObject( Object context )
			{
				base.GetClipboardObject( context );

				// Try finding the object with RelativePath
				if( !InspectPlusSettings.Instance.SmartCopyPaste )
					return null;

				if( RelativePath == null || RelativePath.Length == 0 || !context || ( !( context is Component ) && !( context is GameObject ) ) )
					return null;

				Transform sourceTransform = context is Component ? ( (Component) context ).transform : ( (GameObject) context ).transform;
				if( RelativePath.Length == 1 && RelativePath[0].PointsToSelf )
					return GetTargetObjectFromTransform( sourceTransform );

				int index = 0;
				for( ; index < RelativePath.Length && RelativePath[index].PointsToParent; index++ )
				{
					sourceTransform = sourceTransform.parent;
					if( !sourceTransform )
						return null;
				}

				return TraverseHierarchyRecursively( sourceTransform, RelativePath, index );
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				SerializeString( writer, ObjectName );
				SerializePathComponents( writer, Path );
				SerializePathComponents( writer, RelativePath );
				writer.Write( ComponentIndex );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				ObjectName = DeserializeString( reader );
				Path = DeserializePathComponents( reader );
				RelativePath = DeserializePathComponents( reader );
				ComponentIndex = reader.ReadInt32();
			}

			private void SerializePathComponents( BinaryWriter writer, PathComponent[] pathComponents )
			{
				if( pathComponents == null || pathComponents.Length == 0 )
					writer.Write( 0 );
				else
				{
					writer.Write( pathComponents.Length );

					for( int i = 0; i < pathComponents.Length; i++ )
					{
						SerializeString( writer, pathComponents[i].Name );
						writer.Write( pathComponents[i].Index );
					}
				}
			}

			private PathComponent[] DeserializePathComponents( BinaryReader reader )
			{
				int arraySize = reader.ReadInt32();
				if( arraySize <= 0 )
					return null;

				PathComponent[] result = new PathComponent[arraySize];
				for( int i = 0; i < arraySize; i++ )
				{
					string name = DeserializeString( reader );
					result[i] = new PathComponent( name, reader.ReadInt32() );
				}

				return result;
			}

			protected Object TraverseHierarchyRecursively( Transform obj, PathComponent[] path, int pathIndex )
			{
				if( pathIndex >= path.Length )
					return GetTargetObjectFromTransform( obj );

				Object result = null;
				int siblingIndex = -1;
				for( int i = 0; i < obj.childCount; i++ )
				{
					Transform child = obj.GetChild( i );
					if( child.name == path[pathIndex].Name )
					{
						Object _result = TraverseHierarchyRecursively( child, path, pathIndex + 1 );
						if( _result )
							result = _result;

						if( ++siblingIndex >= path[pathIndex].Index && result )
							break;
					}
				}

				return result;
			}

			private Object GetTargetObjectFromTransform( Transform source )
			{
				if( serializedType.Name == "GameObject" )
					return source.gameObject;
				else
				{
					Component[] components = source.GetComponents<Component>();
					Component targetComponent = null;

					if( serializedType.Type != null && typeof( Component ).IsAssignableFrom( serializedType.Type ) )
					{
						int componentIndex;
						targetComponent = FindComponentOfTypeClosestToIndex( serializedType.Type, components, ComponentIndex, out componentIndex );
					}
					else
					{
						int componentIndex = -1;
						for( int i = 0; i < components.Length; i++ )
						{
							if( components[i] && components[i].GetType().Name == serializedType.Name )
							{
								targetComponent = components[i];
								if( ++componentIndex >= ComponentIndex )
									break;
							}
						}
					}

					return targetComponent;
				}
			}
		}

		public class IPSceneObject : IPUnityObject
		{
			public string SceneName;

			public IPSceneObject( SerializedClipboard root ) : base( root ) { }
			public IPSceneObject( SerializedClipboard root, Object value, Object source ) : base( root, value, source )
			{
				ObjectName = value.name;

				if( value is Component || value is GameObject )
				{
					Transform transform = value is Component ? ( (Component) value ).transform : ( (GameObject) value ).transform;
					Transform pathIterateTarget = null;
#if UNITY_2018_3_OR_NEWER
					PrefabStage openPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
					if( openPrefabStage != null && openPrefabStage.IsPartOfPrefabContents( transform.gameObject ) )
						pathIterateTarget = openPrefabStage.prefabContentsRoot.transform.parent;
					else
#endif
					{
						SceneName = transform.gameObject.scene.name;
					}

					// Calculate Path
					List<PathComponent> pathComponents = new List<PathComponent>( 5 );
					while( transform != pathIterateTarget )
					{
						pathComponents.Insert( 0, new PathComponent( transform ) );
						transform = transform.parent;
					}

					Path = pathComponents.ToArray();
				}
			}

			public override object GetClipboardObject( Object context )
			{
				// First, try to resolve the RelativePath
				object baseResult = base.GetClipboardObject( context );
				if( baseResult != null && !baseResult.Equals( null ) )
					return baseResult;

				if( Path != null && Path.Length > 0 )
				{
					// Search all open scenes to find the object reference
					// Don't use GameObject.Find because it can't find inactive objects

#if UNITY_2018_3_OR_NEWER
					// Search the currently open prefab stage (if any)
					PrefabStage openPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
					if( openPrefabStage != null )
					{
						Object result = FindObjectInScene( new GameObject[1] { openPrefabStage.prefabContentsRoot } );
						if( result )
							return result;
					}
#endif

					int originalSceneIndex = -1;
					Scene[] scenes = new Scene[SceneManager.sceneCount];
					for( int i = 0; i < scenes.Length; i++ )
					{
						scenes[i] = SceneManager.GetSceneAt( i );
						if( scenes[i].name == SceneName )
							originalSceneIndex = i;
					}

					// Try finding the object in the scene with matching name first
					if( originalSceneIndex >= 0 )
					{
						Object result = FindObjectInScene( scenes[originalSceneIndex].GetRootGameObjects() );
						if( result )
							return result;
					}

					// If object isn't found, search other scenes
					for( int i = 0; i < scenes.Length; i++ )
					{
						if( i != originalSceneIndex )
						{
							Object result = FindObjectInScene( scenes[i].GetRootGameObjects() );
							if( result )
								return result;
						}
					}
				}
				else
				{
					// Search all objects of specified type
					if( serializedType.Type != null )
					{
						Object[] objects = Object.FindObjectsOfType( serializedType.Type );
						for( int i = 0; i < objects.Length; i++ )
						{
							if( objects[i].name == ObjectName )
								return objects[i];
						}
					}
				}

				return null;
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				SerializeString( writer, SceneName );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				SceneName = DeserializeString( reader );
			}

			private Object FindObjectInScene( GameObject[] sceneRoot )
			{
				Object result = null;
				int siblingIndex = -1;
				for( int i = 0; i < sceneRoot.Length; i++ )
				{
					if( sceneRoot[i].transform.name == Path[0].Name )
					{
						Object _result = TraverseHierarchyRecursively( sceneRoot[i].transform, Path, 1 );
						if( _result )
							result = _result;

						if( ++siblingIndex >= Path[0].Index && result )
							break;
					}
				}

				return result;
			}
		}

		public class IPAsset : IPUnityObject
		{
			[Serializable]
			private class AssetWrapper
			{
				public Object value;
			}

			public string Value;

			private bool assetRecreated;
			private Object m_asset;

			public IPAsset( SerializedClipboard root ) : base( root ) { }
			public IPAsset( SerializedClipboard root, Object value, Object source ) : base( root, value, source )
			{
				ObjectName = value.name;
				Path = new PathComponent[1] { new PathComponent( AssetDatabase.GetAssetPath( value ), 0 ) };
				Value = EditorJsonUtility.ToJson( new AssetWrapper() { value = value } );
			}

			public override object GetClipboardObject( Object context )
			{
				// First, try to resolve the RelativePath. We can't cache it as it depends on the target
				object baseResult = base.GetClipboardObject( context );
				if( baseResult != null && !baseResult.Equals( null ) )
					return baseResult;

				if( assetRecreated )
					return m_asset;

				assetRecreated = true;

				AssetWrapper wrapper = new AssetWrapper();
				EditorJsonUtility.FromJsonOverwrite( Value, wrapper );
				if( wrapper.value )
				{
					m_asset = wrapper.value;
					return wrapper.value;
				}

				if( Path != null && Path.Length > 0 )
				{
					Object result = FindAssetAtPath( Path[0].Name );
					if( result )
					{
						m_asset = result;
						return result;
					}
				}

				string[] guids = AssetDatabase.FindAssets( ObjectName + " t:" + serializedType.Name );
				if( guids != null )
				{
					for( int i = 0; i < guids.Length; i++ )
					{
						Object result = FindAssetAtPath( AssetDatabase.GUIDToAssetPath( guids[i] ) );
						if( result )
						{
							m_asset = result;
							return result;
						}
					}
				}

				return null;
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				SerializeString( writer, Value );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				Value = DeserializeString( reader );
			}

			private Object FindAssetAtPath( string path )
			{
				if( !string.IsNullOrEmpty( path ) )
				{
					Object[] assets = AssetDatabase.LoadAllAssetsAtPath( path );
					if( assets != null )
					{
						for( int i = 0; i < assets.Length; i++ )
						{
							Object asset = assets[i];
							if( asset && asset.name == ObjectName && asset.GetType().Name == serializedType.Name && ( serializedType.Type == null || serializedType.Type == asset.GetType() ) )
								return asset;
						}
					}
				}

				return null;
			}
		}

		public class IPSceneObjectReference : IPObject
		{
			public int SceneObjectIndex;

			public IPSceneObjectReference( SerializedClipboard root ) : base( root ) { }
			public IPSceneObjectReference( SerializedClipboard root, string name, Object value ) : base( root, name )
			{
				SceneObjectIndex = root.GetIndexOfSceneObjectToSerialize( value, true );
			}

			public override object GetClipboardObject( Object context )
			{
				return root.SceneObjects[SceneObjectIndex].GetClipboardObject( context );
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				writer.Write( SceneObjectIndex );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				SceneObjectIndex = reader.ReadInt32();
			}
		}

		public class IPAssetReference : IPObject
		{
			public int AssetIndex;

			public IPAssetReference( SerializedClipboard root ) : base( root ) { }
			public IPAssetReference( SerializedClipboard root, string name, Object value ) : base( root, name )
			{
				AssetIndex = root.GetIndexOfAssetToSerialize( value, true );
			}

			public override object GetClipboardObject( Object context )
			{
				return root.Assets[AssetIndex].GetClipboardObject( context );
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				writer.Write( AssetIndex );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				AssetIndex = reader.ReadInt32();
			}
		}

		public class IPGameObjectHierarchy : IPObject
		{
			public IPGameObjectChild[] GameObjects;

			public IPVector[] WorldPositions;
			public IPVector[] WorldRotations;
			public IPVector[] WorldScales;

			public IPGameObjectHierarchy( SerializedClipboard root ) : base( root ) { }
			public IPGameObjectHierarchy( SerializedClipboard root, string name, GameObjectHierarchyClipboard value ) : base( root, name )
			{
				GameObjects = new IPGameObjectChild[value.source.Length];
				WorldPositions = new IPVector[value.source.Length];
				WorldRotations = new IPVector[value.source.Length];
				WorldScales = new IPVector[value.source.Length];

				for( int i = 0; i < GameObjects.Length; i++ )
				{
					GameObject gameObject = value.source[i];

					GameObjects[i] = new IPGameObjectChild( root, gameObject, value.includeChildren );
					WorldPositions[i] = new IPVector( root, null, gameObject.transform.position );
					WorldRotations[i] = new IPVector( root, null, gameObject.transform.rotation );
					WorldScales[i] = new IPVector( root, null, gameObject.transform.lossyScale );
				}
			}

			public override object GetClipboardObject( Object context )
			{
				return new GameObjectHierarchyClipboard( GameObjects[0].Name );
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );

				writer.Write( GameObjects.Length );
				for( int i = 0; i < GameObjects.Length; i++ )
					GameObjects[i].Serialize( writer );

				root.SerializeArray( writer, WorldPositions );
				root.SerializeArray( writer, WorldRotations );
				root.SerializeArray( writer, WorldScales );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );

				GameObjects = new IPGameObjectChild[reader.ReadInt32()];
				for( int i = 0; i < GameObjects.Length; i++ )
				{
					GameObjects[i] = new IPGameObjectChild();
					GameObjects[i].Deserialize( root, reader );
				}

				WorldPositions = root.DeserializeArray<IPVector>( reader );
				WorldRotations = root.DeserializeArray<IPVector>( reader );
				WorldScales = root.DeserializeArray<IPVector>( reader );
			}

			public GameObject[] PasteHierarchy( Transform parent, bool preserveWorldSpacePosition )
			{
				if( parent && AssetDatabase.Contains( parent ) )
				{
					Debug.LogError( "Can't paste Complete GameObject to a prefab or model Asset: " + parent, parent );
					return new GameObject[0];
				}

#if UNITY_2018_3_OR_NEWER
				// If a parent isn't set and we are in Prefab mode, set the parent to the prefab root
				if( !parent )
				{
					PrefabStage openPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
					if( openPrefabStage != null )
						parent = openPrefabStage.prefabContentsRoot.transform;
				}
#endif

				GameObject[] result = new GameObject[GameObjects.Length];
				List<GameObject> hierarchy = new List<GameObject>( GameObjects.Length * 32 );
				List<IPComponentGroup.ComponentToPaste[]> hierarchyComponents = new List<IPComponentGroup.ComponentToPaste[]>( hierarchy.Capacity );

				// Recursively create child GameObjects (and their components). Creating all Components before pasting
				// their values allows us to restore references between Components (since all the Components will
				// exist while pasting the Component values)
				for( int i = 0; i < GameObjects.Length; i++ )
				{
					GameObject rootGameObject = null;
					GameObjects[i].CreateGameObjectsRecursively( parent, ref rootGameObject, hierarchy, hierarchyComponents );
					result[i] = rootGameObject;
				}

				// Paste component values
				bool smartCopyPasteEnabled = InspectPlusSettings.Instance.SmartCopyPaste;
				try
				{
					// Smart copy-paste is mandatory for copy&pasting GameObject hierarchies in order to
					// be able to restore references between Components properly
					InspectPlusSettings.Instance.SmartCopyPaste = true;

					for( int i = 0; i < hierarchyComponents.Count; i++ )
					{
						IPComponentGroup.ComponentToPaste[] componentsToPaste = hierarchyComponents[i];
						for( int j = 0; j < componentsToPaste.Length; j++ )
							componentsToPaste[j].SerializedComponent.PasteToObject( componentsToPaste[j].TargetComponent, false );
					}
				}
				finally
				{
					InspectPlusSettings.Instance.SmartCopyPaste = smartCopyPasteEnabled;
				}

				// Preserve world space positions, if needed
				if( preserveWorldSpacePosition )
				{
					Transform dummyGO = null;
					try
					{
						// Setting object's lossy scale is far from trivial when object is skewed. No matter what I tried,
						// I couldn't replicate Unity's world space copy-paste behaviour. The only way to exactly reproduce
						// the same lossyScale values is to use Unity's SetParent(parent, true) function so that Unity handles
						// the calculation of lossyScale for us
						dummyGO = new GameObject().transform;

						for( int i = 0; i < result.Length; i++ )
						{
							if( !result[i] )
								continue;

							if( !parent )
							{
								result[i].transform.localPosition = (VectorClipboard) WorldPositions[i].GetClipboardObject( null );
								result[i].transform.localRotation = (VectorClipboard) WorldRotations[i].GetClipboardObject( null );
								result[i].transform.localScale = (VectorClipboard) WorldScales[i].GetClipboardObject( null );
							}
							else
							{
								dummyGO.SetParent( null, false );
								dummyGO.localPosition = (VectorClipboard) WorldPositions[i].GetClipboardObject( null );
								dummyGO.localRotation = (VectorClipboard) WorldRotations[i].GetClipboardObject( null );
								dummyGO.localScale = (VectorClipboard) WorldScales[i].GetClipboardObject( null );
								dummyGO.SetParent( parent, true );

								result[i].transform.localPosition = dummyGO.localPosition;
								result[i].transform.localRotation = dummyGO.localRotation;
								result[i].transform.localScale = dummyGO.localScale;
							}
						}
					}
					finally
					{
						if( dummyGO )
							Object.DestroyImmediate( dummyGO.gameObject );
					}
				}

				return result;
			}

			public void PrintHierarchy( StringBuilder sb )
			{
				for( int i = 0; i < GameObjects.Length; i++ )
				{
					GameObjects[i].PrintHierarchyRecursively( sb, 0 );

					if( i < GameObjects.Length - 1 )
						sb.Append( "\n" );
				}
			}
		}

		public class IPGameObjectChild
		{
			[Serializable]
			public struct RemovedComponentInfo
			{
				public IPType Type;
				public int Index;

				public RemovedComponentInfo( IPType type, int index )
				{
					Type = type;
					Index = index;
				}
			}

			public string Name;
			public bool IsActive;
			public int Layer;
			public string Tag;
			public int StaticFlags;
			public int HideFlags;

			public SerializedClipboard PrefabAsset;
			public bool IsPartOfParentPrefab;

			public IPComponentGroup.ComponentInfo[] Components;
			public RemovedComponentInfo[] RemovedComponents;

			public IPGameObjectChild[] Children;

			public IPGameObjectChild() { }
			public IPGameObjectChild( SerializedClipboard root, GameObject gameObject, bool includeChildren )
			{
				Transform transform = gameObject.transform;

				Name = gameObject.name;
				IsActive = gameObject.activeSelf;
				Layer = gameObject.layer;
				Tag = gameObject.tag;
				StaticFlags = (int) GameObjectUtility.GetStaticEditorFlags( gameObject );
				HideFlags = (int) gameObject.hideFlags;

				bool smartCopyPasteEnabled = InspectPlusSettings.Instance.SmartCopyPaste;
				try
				{
					// Smart copy-paste is mandatory for copy&pasting GameObject hierarchies in order to
					// be able to restore references between Components properly
					InspectPlusSettings.Instance.SmartCopyPaste = true;

					// Fetch components
					List<Component> _components = new List<Component>( 6 );
					gameObject.GetComponents( _components );

					for( int i = _components.Count - 1; i >= 0; i-- )
					{
						if( !_components[i] )
						{
							_components.RemoveAt( i );
							continue;
						}
					}

					GameObject prefab = null;
#if UNITY_2018_3_OR_NEWER
					if( PrefabUtility.GetPrefabInstanceStatus( gameObject ) == PrefabInstanceStatus.Connected )
						prefab = PrefabUtility.GetCorrespondingObjectFromSource( gameObject ) as GameObject;
#else
					PrefabType prefabType = PrefabUtility.GetPrefabType( gameObject );
					if( prefabType == PrefabType.ModelPrefabInstance || prefabType == PrefabType.PrefabInstance )
						prefab = PrefabUtility.GetPrefabParent( gameObject ) as GameObject;
#endif

					if( !prefab )
						RemovedComponents = new RemovedComponentInfo[0];
					else
					{
						Component[] prefabComponents = prefab.GetComponents<Component>();
						List<Component> _removedComponents = new List<Component>( prefabComponents ); // Components that are removed from the instance

						int newComponentsStartIndex = _components.Count;
						for( int i = _components.Count - 1; i >= 0; i-- )
						{
#if UNITY_2018_3_OR_NEWER
							Component originalComponent = PrefabUtility.GetCorrespondingObjectFromSource( _components[i] ) as Component;
#else
							Component originalComponent = PrefabUtility.GetPrefabParent( _components[i] ) as Component;
#endif
							if( !originalComponent || !_removedComponents.Remove( originalComponent ) )
							{
								// Move Components that are newly added to the instance to the end of the list
								_components.Insert( newComponentsStartIndex--, _components[i] );
								_components.RemoveAt( i );
							}
						}

						for( int i = _removedComponents.Count - 1; i >= 0; i-- )
						{
							if( !_removedComponents[i] || _removedComponents[i].GetType() == typeof( Transform ) )
								_removedComponents.RemoveAt( i );
						}

						RemovedComponents = new RemovedComponentInfo[_removedComponents.Count];
						for( int i = 0; i < _removedComponents.Count; i++ )
							RemovedComponents[i] = new RemovedComponentInfo( new IPType( root, _removedComponents[i].GetType() ), GetIndexOfComponentByType( _removedComponents[i], prefabComponents ) );
					}

					Components = new IPComponentGroup.ComponentInfo[_components.Count];
					for( int i = 0; i < _components.Count; i++ )
					{
						SerializedClipboard serializedComponent = new SerializedClipboard( _components[i], _components[i], null, null );
						bool componentEnabled = EditorUtility.GetObjectEnabled( _components[i] ) != 0;
						Components[i] = new IPComponentGroup.ComponentInfo( serializedComponent, componentEnabled, (int) _components[i].hideFlags, GetIndexOfComponentByType( _components[i], _components ) );
					}

					// Store prefab asset (if it is a prefab instance's root GameObject)
					if( prefab )
					{
#if UNITY_2018_3_OR_NEWER
						GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot( gameObject );
#else
						GameObject prefabRoot = PrefabUtility.FindPrefabRoot( gameObject );
#endif
						if( prefabRoot == gameObject )
						{
							// If child objects aren't copied and this prefab has child objects, don't save the prefab data
							if( includeChildren || prefab.transform.childCount == 0 )
								PrefabAsset = new SerializedClipboard( prefab, null, null, null );
						}
						else if( prefabRoot && transform.parent )
						{
#if UNITY_2018_3_OR_NEWER
							if( prefabRoot == PrefabUtility.GetOutermostPrefabInstanceRoot( transform.parent.gameObject ) )
#else
							if( prefabRoot == PrefabUtility.FindPrefabRoot( transform.parent.gameObject ) )
#endif
								IsPartOfParentPrefab = true;
						}
					}

					// Fetch children
					if( includeChildren )
					{
						Children = new IPGameObjectChild[transform.childCount];
						for( int i = 0; i < Children.Length; i++ )
							Children[i] = new IPGameObjectChild( root, transform.GetChild( i ).gameObject, true );
					}
				}
				finally
				{
					InspectPlusSettings.Instance.SmartCopyPaste = smartCopyPasteEnabled;
				}
			}

			public void Serialize( BinaryWriter writer )
			{
				writer.Write( IsActive );
				writer.Write( Layer );
				writer.Write( StaticFlags );
				writer.Write( HideFlags );
				SerializeString( writer, Name );
				SerializeString( writer, Tag != "Untagged" ? Tag : null );

				if( PrefabAsset != null )
				{
					writer.Write( true );
					PrefabAsset.Serialize( writer );
				}
				else
					writer.Write( false );

				writer.Write( IsPartOfParentPrefab );

				IPComponentGroup.SerializeComponents( writer, Components );

				writer.Write( RemovedComponents.Length );
				for( int i = 0; i < RemovedComponents.Length; i++ )
				{
					RemovedComponents[i].Type.Serialize( writer );
					writer.Write( RemovedComponents[i].Index );
				}

				if( Children == null || Children.Length == 0 )
					writer.Write( 0 );
				else
				{
					writer.Write( Children.Length );

					for( int i = 0; i < Children.Length; i++ )
						Children[i].Serialize( writer );
				}
			}

			public void Deserialize( SerializedClipboard root, BinaryReader reader )
			{
				IsActive = reader.ReadBoolean();
				Layer = reader.ReadInt32();
				StaticFlags = reader.ReadInt32();
				HideFlags = reader.ReadInt32();
				Name = DeserializeString( reader );
				Tag = DeserializeString( reader );
				if( string.IsNullOrEmpty( Tag ) )
					Tag = "Untagged";

				if( reader.ReadBoolean() )
					PrefabAsset = new SerializedClipboard( reader );

				IsPartOfParentPrefab = reader.ReadBoolean();

				Components = IPComponentGroup.DeserializeComponents( reader );

				RemovedComponents = new RemovedComponentInfo[reader.ReadInt32()];
				for( int i = 0; i < RemovedComponents.Length; i++ )
				{
					IPType type = new IPType( root );
					type.Deserialize( reader );
					RemovedComponents[i] = new RemovedComponentInfo( type, reader.ReadInt32() );
				}

				int arraySize = reader.ReadInt32();
				if( arraySize > 0 )
				{
					Children = new IPGameObjectChild[arraySize];
					for( int i = 0; i < arraySize; i++ )
					{
						Children[i] = new IPGameObjectChild();
						Children[i].Deserialize( root, reader );
					}
				}
			}

			public void CreateGameObjectsRecursively( Transform parent, ref GameObject gameObject, List<GameObject> hierarchy, List<IPComponentGroup.ComponentToPaste[]> hierarchyComponents )
			{
				bool isPrefabInstance = gameObject;
				if( !isPrefabInstance )
				{
					GameObject prefabAsset = PrefabAsset == null ? null : PrefabAsset.RootValue.GetClipboardObject( null ) as GameObject;
					if( !prefabAsset )
					{
						// Create a new GameObject
						gameObject = new GameObject( Name );
					}
					else
					{
						// Instantiate the source prefab asset
						gameObject = PrefabUtility.InstantiatePrefab( prefabAsset ) as GameObject;
						gameObject.name = Name;

						isPrefabInstance = true;
					}

					Undo.RegisterCreatedObjectUndo( gameObject, "Paste Complete GameObject" );
					if( parent )
						Undo.SetTransformParent( gameObject.transform, parent, "Paste Complete GameObject" );
					else
						gameObject.transform.SetAsLastSibling();
				}

				gameObject.SetActive( IsActive );
				gameObject.layer = Layer;
				gameObject.tag = Tag;
				gameObject.hideFlags = (HideFlags) HideFlags;
				GameObjectUtility.SetStaticEditorFlags( gameObject, (StaticEditorFlags) StaticFlags );

				// Destroy removed components (if any)
				if( RemovedComponents.Length > 0 )
				{
					Component[] components = gameObject.GetComponents<Component>();
					List<Component> componentsToRemove = new List<Component>( RemovedComponents.Length );
					for( int i = 0; i < RemovedComponents.Length; i++ )
					{
						Type componentType = RemovedComponents[i].Type.Type;
						if( componentType == null || !typeof( Component ).IsAssignableFrom( componentType ) )
							continue;

						int componentIndex;
						Component componentToRemove = FindComponentOfTypeClosestToIndex( componentType, components, RemovedComponents[i].Index, out componentIndex );
						if( componentIndex == RemovedComponents[i].Index )
							componentsToRemove.Add( componentToRemove );
					}

					for( int i = 0; i < componentsToRemove.Count; i++ )
						Undo.DestroyObjectImmediate( componentsToRemove[i] );
				}

				hierarchy.Add( gameObject );
				hierarchyComponents.Add( IPComponentGroup.AddComponentsToGameObject( Components, gameObject, false ) );

				// Recursively create child GameObjects (and their components)
				if( Children != null )
				{
					if( !isPrefabInstance )
					{
						for( int i = 0; i < Children.Length; i++ )
						{
							GameObject childGO = null;
							Children[i].CreateGameObjectsRecursively( gameObject.transform, ref childGO, hierarchy, hierarchyComponents );
						}
					}
					else
					{
						List<IPGameObjectChild> childrenList = new List<IPGameObjectChild>( Children );

						// First, find child GameObjects that were instantiated with this prefab and remove them from children list
						for( int i = 0; i < gameObject.transform.childCount; i++ )
						{
							GameObject childGO = gameObject.transform.GetChild( i ).gameObject;
							string childName = childGO.name;
							for( int j = 0; j < childrenList.Count; j++ )
							{
								if( childrenList[j].Name == childName && childrenList[j].IsPartOfParentPrefab )
								{
									childrenList[j].CreateGameObjectsRecursively( gameObject.transform, ref childGO, hierarchy, hierarchyComponents );
									childrenList.RemoveAt( j );
									break;
								}
							}
						}

						// Remaining GameObjects are not part of this instantiated prefab, they will be created from scratch
						for( int i = 0; i < childrenList.Count; i++ )
						{
							GameObject childGO = null;
							childrenList[i].CreateGameObjectsRecursively( gameObject.transform, ref childGO, hierarchy, hierarchyComponents );

							// Preserve sibling index order
							if( childGO )
								childGO.transform.SetSiblingIndex( Array.IndexOf( Children, childrenList[i] ) );
						}
					}
				}
			}

			public void PrintHierarchyRecursively( StringBuilder sb, int depth )
			{
				for( int i = 0; i < depth; i++ )
					sb.Append( "   " );

				sb.AppendLine( Name );

				if( Children != null )
				{
					for( int i = 0; i < Children.Length; i++ )
						Children[i].PrintHierarchyRecursively( sb, depth + 1 );
				}
			}
		}

		public class IPComponentGroup : IPObject
		{
			[Serializable]
			public struct ComponentInfo
			{
				public SerializedClipboard Component;
				public bool Enabled;
				public int HideFlags;
				public int Index;

				public ComponentInfo( SerializedClipboard component, bool enabled, int hideFlags, int index )
				{
					Component = component;
					Enabled = enabled;
					HideFlags = hideFlags;
					Index = index;
				}
			}

			public struct ComponentToPaste
			{
				public readonly SerializedClipboard SerializedComponent;
				public readonly Component TargetComponent;

				public ComponentToPaste( SerializedClipboard serializedComponent, Component targetComponent )
				{
					SerializedComponent = serializedComponent;
					TargetComponent = targetComponent;
				}
			}

			public ComponentInfo[] Components;

			public IPComponentGroup( SerializedClipboard root ) : base( root ) { }
			public IPComponentGroup( SerializedClipboard root, string name, ComponentGroupClipboard value ) : base( root, name )
			{
				List<Component> _components = new List<Component>( value.components );
				for( int i = _components.Count - 1; i >= 0; i-- )
				{
					if( !_components[i] )
					{
						_components.RemoveAt( i );
						continue;
					}
				}

				Components = new ComponentInfo[_components.Count];
				for( int i = 0; i < _components.Count; i++ )
				{
					SerializedClipboard serializedComponent = new SerializedClipboard( _components[i], _components[i], null, _components[i].GetType().Name );
					bool componentEnabled = EditorUtility.GetObjectEnabled( _components[i] ) != 0;
					Components[i] = new ComponentInfo( serializedComponent, componentEnabled, (int) _components[i].hideFlags, GetIndexOfComponentByType( _components[i], _components ) );
				}
			}

			public override object GetClipboardObject( Object context )
			{
				return new ComponentGroupClipboard( Name );
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				SerializeComponents( writer, Components );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				Components = DeserializeComponents( reader );
			}

			public static void SerializeComponents( BinaryWriter writer, ComponentInfo[] array )
			{
				writer.Write( array.Length );
				for( int i = 0; i < array.Length; i++ )
				{
					array[i].Component.Serialize( writer );
					writer.Write( array[i].Enabled );
					writer.Write( array[i].HideFlags );
					writer.Write( array[i].Index );
				}
			}

			public static ComponentInfo[] DeserializeComponents( BinaryReader reader )
			{
				ComponentInfo[] result = new ComponentInfo[reader.ReadInt32()];
				for( int i = 0; i < result.Length; i++ )
				{
					SerializedClipboard component = new SerializedClipboard( reader );
					bool enabled = reader.ReadBoolean();
					int hideFlags = reader.ReadInt32();
					result[i] = new ComponentInfo( component, enabled, hideFlags, reader.ReadInt32() );
				}

				return result;
			}

			public Component[] PasteComponents( GameObject target, ComponentInfo[] filteredComponents = null )
			{
				if( !target || AssetDatabase.Contains( target ) )
				{
					Debug.LogError( "Can't paste ComponentGroup to a non-existing GameObject, prefab or model Asset: " + target, target );
					return new Component[0];
				}

				// Create the components first
				ComponentToPaste[] componentsToPaste = AddComponentsToGameObject( filteredComponents ?? Components, target, true );

				// Paste component values
				Component[] result = new Component[componentsToPaste.Length];
				for( int i = 0; i < componentsToPaste.Length; i++ )
				{
					componentsToPaste[i].SerializedComponent.PasteToObject( componentsToPaste[i].TargetComponent, false );
					result[i] = componentsToPaste[i].TargetComponent;
				}

				return result;
			}

			// ignoreExistingComponents=false: Don't hesitate to paste serialized ComponentInfos to existing components
			// ignoreExistingComponents=true: Try to create new components for each ComponentInfo. However, if it fails when e.g. trying to
			//								  create Rigidbody when one already exists, use the existing component as fallback
			public static ComponentToPaste[] AddComponentsToGameObject( ComponentInfo[] components, GameObject gameObject, bool ignoreExistingComponents )
			{
				HashSet<Component> existingComponents = ignoreExistingComponents ? new HashSet<Component>( gameObject.GetComponents<Component>() ) : null;

				// Find the serialized components that exist in this Unity project
				List<ComponentInfo> validComponents = SelectComponentsThatExistInProject( components );

				// Add any necessary components to the GameObject (but don't paste their values yet; creating all Components before pasting their
				// values allows us to restore references between Components since all the Components will exist while pasting the Component values)
				ComponentToPaste[] result = new ComponentToPaste[validComponents.Count];
				for( int i = 0; i < validComponents.Count; i++ )
				{
					// We are calling GetComponents at each iteration because a component added in the previous iteration might automatically
					// add other component(s) to the GameObject (i.e. via RequireComponent)
					Type componentType = validComponents[i].Component.RootUnityObjectType.Type;
					Component[] _components = gameObject.GetComponents( componentType );

					Component targetComponent = null;
					if( ignoreExistingComponents ) // Create a new component if possible
					{
						// First, check if one of these existing components was added automatically via RequireComponent; in which case, use it
						for( int j = _components.Length - 1; j >= 0; j-- )
						{
							if( _components[j] && _components[j].GetType() == componentType && !existingComponents.Contains( _components[j] ) )
							{
								targetComponent = _components[j];
								break;
							}
						}

						// All existing components were part of the original GameObject, add a new component
						if( !targetComponent && componentType != typeof( Transform ) )
							targetComponent = Undo.AddComponent( gameObject, componentType );

						// Don't paste to this component twice
						if( targetComponent )
							existingComponents.Add( targetComponent );
					}

					if( !targetComponent )
					{
						// Try to find the component among the list of existing components
						int componentIndex;
						targetComponent = FindComponentOfTypeClosestToIndex( componentType, _components, validComponents[i].Index, out componentIndex );

						// We already call Undo.AddComponent when ignoreExistingComponents=true in the above lines
						if( !ignoreExistingComponents )
						{
							// Add new components until the serialized component's Index is reached
							for( ; componentIndex < validComponents[i].Index; componentIndex++ )
								targetComponent = Undo.AddComponent( gameObject, componentType );
						}
					}

					// In the following edge case, Undo.AddComponent may return null:
					// GameObject has 2 components: Image and SomeComponent. In project A, SomeComponent doesn't have any dependencies.
					// In project B, SomeComponent's implementation differs from project A such that SomeComponent now has a
					// RequireComponent(Text) attribute. When pasting the GameObject to project B, Unity will complain at Undo.AddComponent:
					// "Can't add 'Text' to object because a 'Image' is already added to the game object!"
					// That's because in this project, trying to add SomeComponent to the GameObject will force Unity to add a Text component
					// to the same GameObject but it will fail because only 1 Graphic component can exist on an object and that is the Image
					// component in this case. Hence, Undo.AddComponent will set targetComponent to null
					if( !targetComponent )
					{
						Array.Resize( ref result, result.Length - 1 );
						validComponents.RemoveAt( i-- );

						continue;
					}

					// We can paste the values of enabled and hideFlags immediately, though
					Undo.RecordObject( targetComponent, "Modify Component" );
					targetComponent.hideFlags = (HideFlags) validComponents[i].HideFlags;

					// Credit: https://github.com/Unity-Technologies/UnityCsReference/blob/61f92bd79ae862c4465d35270f9d1d57befd1761/Editor/Mono/EditorGUI.cs#L5092-L5164
					SerializedProperty enabledProperty = new SerializedObject( targetComponent ).FindProperty( "m_Enabled" );
					if( enabledProperty != null && enabledProperty.propertyType == SerializedPropertyType.Boolean )
					{
						enabledProperty.boolValue = validComponents[i].Enabled;
						enabledProperty.serializedObject.ApplyModifiedProperties();
					}
					else if( EditorUtility.GetObjectEnabled( targetComponent ) != -1 )
						EditorUtility.SetObjectEnabled( targetComponent, validComponents[i].Enabled );

					result[i] = new ComponentToPaste( validComponents[i].Component, targetComponent );
				}

				return result;
			}

			// Returns the serialized components that exist in this Unity project (i.e. can be pasted to this Unity project)
			public static List<ComponentInfo> SelectComponentsThatExistInProject( ComponentInfo[] allComponents )
			{
				List<ComponentInfo> result = new List<ComponentInfo>( allComponents.Length );
				for( int i = 0; i < allComponents.Length; i++ )
				{
					IPType objectType = allComponents[i].Component.RootUnityObjectType;
					if( objectType != null && objectType.Type != null && !objectType.Type.IsAbstract && typeof( Component ).IsAssignableFrom( objectType.Type ) )
						result.Add( allComponents[i] );
				}

				return result;
			}

			public void PrintComponents( StringBuilder sb )
			{
				for( int i = 0; i < Components.Length; i++ )
					sb.AppendLine( Components[i].Component.RootUnityObjectType.Name );
			}
		}

		public class IPAssetFiles : IPObject
		{
			public string[] Paths;

			public IPAssetFiles( SerializedClipboard root ) : base( root ) { }
			public IPAssetFiles( SerializedClipboard root, string name, AssetFilesClipboard value ) : base( root, name )
			{
				Paths = value.paths;
			}

			public override object GetClipboardObject( Object context )
			{
				return new AssetFilesClipboard( Paths );
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );

				writer.Write( Paths.Length );
				for( int i = 0; i < Paths.Length; i++ )
					SerializeString( writer, Paths[i] );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );

				Paths = new string[reader.ReadInt32()];
				for( int i = 0; i < Paths.Length; i++ )
					Paths[i] = DeserializeString( reader );
			}

			public string[] PasteFiles( string[] parentFolders )
			{
				string[][] pastes = new string[Paths.Length][];
				bool hasConflicts = false;
				for( int i = 0; i < Paths.Length; i++ )
				{
					if( !File.Exists( Paths[i] ) && !Directory.Exists( Paths[i] ) )
						continue;

					pastes[i] = new string[parentFolders.Length];

					for( int j = 0; j < parentFolders.Length; j++ )
					{
						if( string.IsNullOrEmpty( parentFolders[j] ) || !Directory.Exists( parentFolders[j] ) )
							continue;

						pastes[i][j] = Path.Combine( parentFolders[j], Path.GetFileName( Paths[i] ) );
						if( !hasConflicts && ( File.Exists( pastes[i][j] ) || Directory.Exists( pastes[i][j] ) ) )
							hasConflicts = true;
					}
				}

				bool overwriteConflicts = true;
				if( hasConflicts )
				{
					int conflictDialog = EditorUtility.DisplayDialogComplex( "Overwrite", "Some files already exist in the destination.", "Replace", "Cancel", "Rename" );
					if( conflictDialog == 1 )
						return new string[0];

					overwriteConflicts = conflictDialog == 0;
				}

				List<string> result = new List<string>( Paths.Length * parentFolders.Length );

				AssetDatabase.StartAssetEditing();
				try
				{
					for( int i = 0; i < pastes.Length; i++ )
					{
						if( pastes[i] == null )
							continue;

						string sourcePath = Paths[i];
						bool isSourceFile = File.Exists( sourcePath );

						string[] _pastes = pastes[i];
						for( int j = 0; j < _pastes.Length; j++ )
						{
							if( string.IsNullOrEmpty( _pastes[j] ) )
								continue;

							string destinationPath = overwriteConflicts ? _pastes[j] : AssetDatabase.GenerateUniqueAssetPath( _pastes[j] );
							if( sourcePath == destinationPath )
								continue;

							if( isSourceFile )
								File.Copy( sourcePath, destinationPath, true );
							else
							{
								if( !Directory.Exists( destinationPath ) )
									Directory.CreateDirectory( destinationPath );

								CopyDirectory( new DirectoryInfo( sourcePath ), ( destinationPath.EndsWith( "/" ) || destinationPath.EndsWith( "\\" ) ) ? destinationPath : ( destinationPath + Path.DirectorySeparatorChar ) );
							}

							if( File.Exists( sourcePath + ".meta" ) )
								File.Copy( sourcePath + ".meta", destinationPath + ".meta", true );

							result.Add( destinationPath );
						}
					}
				}
				finally
				{
					AssetDatabase.StopAssetEditing();
					AssetDatabase.Refresh();
				}

				return result.ToArray();
			}

			private void CopyDirectory( DirectoryInfo fromDir, string toAbsolutePath )
			{
				FileInfo[] files = fromDir.GetFiles();
				for( int i = 0; i < files.Length; i++ )
					files[i].CopyTo( toAbsolutePath + files[i].Name, true );

				DirectoryInfo[] subDirectories = fromDir.GetDirectories();
				for( int i = 0; i < subDirectories.Length; i++ )
				{
					string directoryAbsolutePath = toAbsolutePath + subDirectories[i].Name + Path.DirectorySeparatorChar;
					Directory.CreateDirectory( directoryAbsolutePath );
					CopyDirectory( subDirectories[i], directoryAbsolutePath );
				}
			}
		}

		public class IPVector : IPObject
		{
			public float C1;
			public float C2;
			public float C3;
			public float C4;
			public float C5;
			public float C6;

			public IPVector( SerializedClipboard root ) : base( root ) { }
			public IPVector( SerializedClipboard root, string name, VectorClipboard value ) : base( root, name )
			{
				C1 = value.c1;
				C2 = value.c2;
				C3 = value.c3;
				C4 = value.c4;
				C5 = value.c5;
				C6 = value.c6;
			}

			public override object GetClipboardObject( Object context ) { return new VectorClipboard( C1, C2, C3, C4, C5, C6 ); }

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );

				writer.Write( C1 );
				writer.Write( C2 );
				writer.Write( C3 );
				writer.Write( C4 );
				writer.Write( C5 );
				writer.Write( C6 );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );

				C1 = reader.ReadSingle();
				C2 = reader.ReadSingle();
				C3 = reader.ReadSingle();
				C4 = reader.ReadSingle();
				C5 = reader.ReadSingle();
				C6 = reader.ReadSingle();
			}
		}

		public class IPColor : IPObject
		{
			public float R;
			public float G;
			public float B;
			public float A;

			public IPColor( SerializedClipboard root ) : base( root ) { }
			public IPColor( SerializedClipboard root, string name, Color value ) : base( root, name )
			{
				R = value.r;
				G = value.g;
				B = value.b;
				A = value.a;
			}

			public override object GetClipboardObject( Object context ) { return new Color( R, G, B, A ); }

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );

				writer.Write( R );
				writer.Write( G );
				writer.Write( B );
				writer.Write( A );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );

				R = reader.ReadSingle();
				G = reader.ReadSingle();
				B = reader.ReadSingle();
				A = reader.ReadSingle();
			}
		}

		public class IPLong : IPObject
		{
			public long Value;

			public IPLong( SerializedClipboard root ) : base( root ) { }
			public IPLong( SerializedClipboard root, string name, long value ) : base( root, name )
			{
				Value = value;
			}

			public override object GetClipboardObject( Object context ) { return Value; }

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				writer.Write( Value );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				Value = reader.ReadInt64();
			}
		}

		public class IPDouble : IPObject
		{
			public double Value;

			public IPDouble( SerializedClipboard root ) : base( root ) { }
			public IPDouble( SerializedClipboard root, string name, double value ) : base( root, name )
			{
				Value = value;
			}

			public override object GetClipboardObject( Object context ) { return Value; }

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				writer.Write( Value );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				Value = reader.ReadDouble();
			}
		}

		public class IPString : IPObject
		{
			public string Value;

			public IPString( SerializedClipboard root ) : base( root ) { }
			public IPString( SerializedClipboard root, string name, string value ) : base( root, name )
			{
				Value = value;
			}

			public override object GetClipboardObject( Object context ) { return Value; }

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				SerializeString( writer, Value );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				Value = DeserializeString( reader );
			}
		}

		public class IPAnimationCurve : IPObject
		{
			[Serializable]
			private class AnimationCurveWrapper
			{
				public AnimationCurve value;
			}

			public string Value;

			public IPAnimationCurve( SerializedClipboard root ) : base( root ) { }
			public IPAnimationCurve( SerializedClipboard root, string name, AnimationCurve value ) : base( root, name )
			{
				Value = EditorJsonUtility.ToJson( new AnimationCurveWrapper() { value = value } );
			}

			public override object GetClipboardObject( Object context )
			{
				AnimationCurveWrapper wrapper = new AnimationCurveWrapper();
				EditorJsonUtility.FromJsonOverwrite( Value, wrapper );
				return wrapper.value;
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				SerializeString( writer, Value );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				Value = DeserializeString( reader );
			}
		}

		public class IPGradient : IPObject
		{
			[Serializable]
			private class GradientWrapper
			{
				public Gradient value;
			}

			public string Value;

			public IPGradient( SerializedClipboard root ) : base( root ) { }
			public IPGradient( SerializedClipboard root, string name, Gradient value ) : base( root, name )
			{
				Value = EditorJsonUtility.ToJson( new GradientWrapper() { value = value } );
			}

			public override object GetClipboardObject( Object context )
			{
				GradientWrapper wrapper = new GradientWrapper();
				EditorJsonUtility.FromJsonOverwrite( Value, wrapper );
				return wrapper.value;
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );
				SerializeString( writer, Value );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );
				Value = DeserializeString( reader );
			}
		}
		#endregion

		private static readonly Dictionary<Type, IPObjectType> serializedTypeToEnumLookup = new Dictionary<Type, IPObjectType>()
		{
			{ typeof( IPNull ), IPObjectType.Null },
			{ typeof( IPType ), IPObjectType.Type },
			{ typeof( IPAsset ), IPObjectType.Asset },
			{ typeof( IPAssetReference ), IPObjectType.AssetReference },
			{ typeof( IPSceneObject ), IPObjectType.SceneObject },
			{ typeof( IPSceneObjectReference ), IPObjectType.SceneObjectReference },
			{ typeof( IPGameObjectHierarchy ), IPObjectType.GameObjectHierarchy },
			{ typeof( IPComponentGroup ), IPObjectType.ComponentGroup },
			{ typeof( IPAssetFiles ), IPObjectType.AssetFiles },
			{ typeof( IPManagedObject ), IPObjectType.ManagedObject },
			{ typeof( IPManagedReference ), IPObjectType.ManagedReference },
			{ typeof( IPArray ), IPObjectType.Array },
			{ typeof( IPGenericObject ), IPObjectType.GenericObject },
			{ typeof( IPVector ), IPObjectType.Vector },
			{ typeof( IPColor ), IPObjectType.Color },
			{ typeof( IPLong ), IPObjectType.Long },
			{ typeof( IPDouble ), IPObjectType.Double },
			{ typeof( IPString ), IPObjectType.String },
			{ typeof( IPAnimationCurve ), IPObjectType.AnimationCurve },
			{ typeof( IPGradient ), IPObjectType.Gradient },
		};

		private static readonly Dictionary<Type, IPObjectType> typeToEnumLookup = new Dictionary<Type, IPObjectType>()
		{
			{ typeof( GameObjectHierarchyClipboard ), IPObjectType.GameObjectHierarchy },
			{ typeof( ComponentGroupClipboard ), IPObjectType.ComponentGroup },
			{ typeof( AssetFilesClipboard ), IPObjectType.AssetFiles },
			{ typeof( ManagedObjectClipboard ), IPObjectType.ManagedObject },
			{ typeof( ArrayClipboard ), IPObjectType.Array },
			{ typeof( GenericObjectClipboard ), IPObjectType.GenericObject },
			{ typeof( VectorClipboard ), IPObjectType.Vector },
			{ typeof( Color ), IPObjectType.Color },
			{ typeof( long ), IPObjectType.Long },
			{ typeof( double ), IPObjectType.Double },
			{ typeof( string ), IPObjectType.String },
			{ typeof( AnimationCurve ), IPObjectType.AnimationCurve },
			{ typeof( Gradient ), IPObjectType.Gradient },
		};

		public IPType[] Types;
		public IPSceneObject[] SceneObjects;
		public IPAsset[] Assets;
		public IPManagedObject[] ManagedObjects;
		public IPObject[] Values;

		public IPObject RootValue { get { return Values[0]; } }

		public IPObjectType RootType
		{
			get
			{
				IPObjectType typeEnum;
				if( !serializedTypeToEnumLookup.TryGetValue( Values[0].GetType(), out typeEnum ) )
					typeEnum = IPObjectType.Null;

				return typeEnum;
			}
		}

		public IPType RootUnityObjectType
		{
			get
			{
				if( RootValue is IPSceneObjectReference )
					return Types[SceneObjects[( (IPSceneObjectReference) RootValue ).SceneObjectIndex].TypeIndex];
				else if( RootValue is IPAssetReference )
					return Types[Assets[( (IPAssetReference) RootValue ).AssetIndex].TypeIndex];
				else
					return null;
			}
		}

		// When the SerializedClipboard is created from a SerializedProperty, its root value's Name won't be empty
		public bool HasSerializedPropertyOrigin { get { return !string.IsNullOrEmpty( RootValue.Name ); } }

		public bool HasTooltip { get { return Values.Length > 1 || RootValue is IPGameObjectHierarchy || RootValue is IPComponentGroup || RootValue is IPAssetFiles; } }

		public string Label;
		private GUIContent m_labelContent;
		public GUIContent LabelContent
		{
			get
			{
				if( m_labelContent == null )
				{
					if( !HasTooltip )
						m_labelContent = new GUIContent( Label, Label );
					else if( RootValue is IPGameObjectHierarchy )
					{
						StringBuilder sb = Utilities.stringBuilder;
						sb.Length = 0;

						sb.Append( "Complete GameObject Hierarchy:\n\n" );
						( (IPGameObjectHierarchy) RootValue ).PrintHierarchy( sb );

						m_labelContent = new GUIContent( Label, sb.ToString() );
					}
					else if( RootValue is IPComponentGroup )
					{
						StringBuilder sb = Utilities.stringBuilder;
						sb.Length = 0;

						sb.Append( "Multiple Components:\n\n" );
						( (IPComponentGroup) RootValue ).PrintComponents( sb );

						m_labelContent = new GUIContent( Label, sb.ToString() );
					}
					else if( RootValue is IPAssetFiles )
					{
						StringBuilder sb = Utilities.stringBuilder;
						sb.Length = 0;

						sb.Append( "Asset Files:\n\n" );
						string[] paths = ( (IPAssetFiles) RootValue ).Paths;
						for( int i = 0; i < paths.Length; i++ )
							sb.Append( "- " ).Append( paths[i] ).Append( "\n" );

						m_labelContent = new GUIContent( Label, sb.ToString() );
					}
					else
					{
						StringBuilder sb = Utilities.stringBuilder;
						sb.Length = 0;
						sb.Append( Label ).Append( "\n\n" );

						for( int i = 1; i < Values.Length; i++ )
						{
							sb.Append( Values[i].Name ).Append( ": " );

							IPObjectType typeEnum;
							if( !serializedTypeToEnumLookup.TryGetValue( Values[i].GetType(), out typeEnum ) )
								sb.Append( Values[i].GetType().Name );
							else
							{
								switch( typeEnum )
								{
									case IPObjectType.Null: sb.Append( "<null>" ); break;
									case IPObjectType.AssetReference:
										IPAsset asset = Assets[( (IPAssetReference) Values[i] ).AssetIndex];
										sb.Append( asset.ObjectName ).Append( " (" ).Append( Types[asset.TypeIndex].Name ).Append( " asset)" ); break;
									case IPObjectType.SceneObjectReference:
										IPSceneObject sceneObject = SceneObjects[( (IPSceneObjectReference) Values[i] ).SceneObjectIndex];
										sb.Append( sceneObject.ObjectName ).Append( " (" ).Append( Types[sceneObject.TypeIndex].Name ).Append( " scene object)" ); break;
									case IPObjectType.Array:
										IPArray array = (IPArray) Values[i];
										sb.Append( array.ElementType ).Append( " array with " ).Append( array.Children == null ? 0 : array.Children.Length ).Append( " element(s)" ); break;
									case IPObjectType.GenericObject: sb.Append( ( (IPGenericObject) Values[i] ).Type ).Append( " object" ); break;
									case IPObjectType.Vector:
										IPVector vector = (IPVector) Values[i];
										sb.Append( "Vector(" ).Append( vector.C1.ToString( "F1" ) ).Append( ", " ).Append( vector.C2.ToString( "F1" ) ).Append( ", " ).Append( vector.C3.ToString( "F1" ) ).Append( ", " ).
											Append( vector.C4.ToString( "F1" ) ).Append( ", " ).Append( vector.C5.ToString( "F1" ) ).Append( ", " ).Append( vector.C6.ToString( "F1" ) ).Append( ")" ); break;
									case IPObjectType.Color:
										Color32 color = (Color) Values[i].GetClipboardObject( null );
										sb.Append( "Color(" ).Append( color.r ).Append( ", " ).Append( color.g ).Append( ", " ).Append( color.b ).Append( ", " ).Append( color.a ).Append( ")" ); break;
									case IPObjectType.Double: sb.Append( (double) Values[i].GetClipboardObject( null ) ); break;
									case IPObjectType.Long: sb.Append( (long) Values[i].GetClipboardObject( null ) ); break;
									case IPObjectType.String: sb.Append( (string) Values[i].GetClipboardObject( null ) ); break;
									default: sb.Append( typeEnum ); break;
								}
							}

							if( i < Values.Length - 1 )
								sb.Append( "\n" );
						}

						m_labelContent = new GUIContent( Label, sb.ToString() );
					}
				}

				return m_labelContent;
			}
		}

		private List<Type> typesToSerialize;
		private List<Object> sceneObjectsToSerialize;
		private List<Object> assetsToSerialize;
		private List<ManagedObjectClipboard> managedObjectsToSerialize;

		#region Constructors
		public SerializedClipboard( BinaryReader reader )
		{
			Deserialize( reader );
		}

		public SerializedClipboard( object clipboardData, Object source, string name, string label )
		{
			Label = label;

			// For Component, ScriptableObject and materials, serialize the fields as well (for name-based paste operations)
			if( clipboardData is Component || clipboardData is ScriptableObject || clipboardData is Material )
				InitializeWithUnityObject( (Object) clipboardData, source, name );
			else
			{
				Values = new IPObject[1] { ConvertClipboardObjectToIPObject( clipboardData, name, source ) };
				Initialize( source );
			}
		}

		private void InitializeWithUnityObject( Object value, Object source, string name )
		{
			SerializedObject serializedObject = new SerializedObject( value );
			int valueCount = 0;

			foreach( SerializedProperty property in serializedObject.EnumerateDirectChildren() )
			{
				if( property.name != "m_Script" )
					valueCount++;
			}

			IPObject[] serializedValues = new IPObject[valueCount + 1];
			serializedValues[0] = ConvertClipboardObjectToIPObject( value, name, source );

			if( valueCount > 0 )
			{
				int valueIndex = 1;
				foreach( SerializedProperty property in serializedObject.EnumerateDirectChildren() )
				{
					if( property.name != "m_Script" )
						serializedValues[valueIndex++] = ConvertClipboardObjectToIPObject( property.CopyValue(), property.name, value );
				}
			}

			Values = serializedValues;
			Initialize( source );
		}

		private void Initialize( Object source )
		{
			// Managed objects must be serialized first since these objects may fill the scene objects list and assets list as they are serialized
			if( managedObjectsToSerialize != null && managedObjectsToSerialize.Count > 0 )
			{
				ManagedObjects = new IPManagedObject[managedObjectsToSerialize.Count];
				for( int i = 0; i < managedObjectsToSerialize.Count; i++ )
					ManagedObjects[i] = new IPManagedObject( this, managedObjectsToSerialize[i], source );

				managedObjectsToSerialize = null;
			}

			if( sceneObjectsToSerialize != null && sceneObjectsToSerialize.Count > 0 )
			{
				SceneObjects = new IPSceneObject[sceneObjectsToSerialize.Count];
				for( int i = 0; i < sceneObjectsToSerialize.Count; i++ )
					SceneObjects[i] = new IPSceneObject( this, sceneObjectsToSerialize[i], source );

				sceneObjectsToSerialize = null;
			}

			if( assetsToSerialize != null && assetsToSerialize.Count > 0 )
			{
				Assets = new IPAsset[assetsToSerialize.Count];
				for( int i = 0; i < assetsToSerialize.Count; i++ )
					Assets[i] = new IPAsset( this, assetsToSerialize[i], source );

				assetsToSerialize = null;
			}

			// Types must be serialized last since other lists may fill the Types list as they are serialized
			if( typesToSerialize != null && typesToSerialize.Count > 0 )
			{
				Types = new IPType[typesToSerialize.Count];
				for( int i = 0; i < typesToSerialize.Count; i++ )
					Types[i] = new IPType( this, typesToSerialize[i] );

				typesToSerialize = null;
			}
		}
		#endregion

		#region Serialization Functions
		public void Serialize( BinaryWriter writer )
		{
			SerializeString( writer, Label );
			SerializeArray( writer, Types );
			SerializeArray( writer, SceneObjects );
			SerializeArray( writer, Assets );
			SerializeArray( writer, ManagedObjects );
			SerializeArray( writer, Values );
		}

		public void Deserialize( BinaryReader reader )
		{
			Label = DeserializeString( reader );
			Types = DeserializeArray<IPType>( reader );
			SceneObjects = DeserializeArray<IPSceneObject>( reader );
			Assets = DeserializeArray<IPAsset>( reader );
			ManagedObjects = DeserializeArray<IPManagedObject>( reader );
			Values = DeserializeArray<IPObject>( reader );
		}

		private void SerializeArray( BinaryWriter writer, IPObject[] array )
		{
			if( array == null || array.Length == 0 )
				writer.Write( 0 );
			else
			{
				writer.Write( array.Length );

				for( int i = 0; i < array.Length; i++ )
					SerializeIPObject( writer, array[i] );
			}
		}

		private T[] DeserializeArray<T>( BinaryReader reader ) where T : IPObject
		{
			int arraySize = reader.ReadInt32();
			if( arraySize <= 0 )
				return null;

			T[] result = new T[arraySize];
			for( int i = 0; i < arraySize; i++ )
			{
				result[i] = (T) InstantiateIPObject( (IPObjectType) reader.ReadInt32() );
				result[i].Deserialize( reader );
			}

			return result;
		}

		private IPObject ConvertClipboardObjectToIPObject( object obj, string name, Object source )
		{
			if( obj == null || obj.Equals( null ) )
				return new IPNull( this, name );

			IPObjectType typeEnum;
			if( !typeToEnumLookup.TryGetValue( obj.GetType(), out typeEnum ) )
			{
				if( obj is Object )
				{
					Object value = (Object) obj;
					if( !value )
						return new IPNull( this, name );
					else if( AssetDatabase.Contains( value ) )
						return new IPAssetReference( this, name, value );
					else
						return new IPSceneObjectReference( this, name, value );
				}

				return new IPNull( this, name );
			}

			switch( typeEnum )
			{
				case IPObjectType.Array: return new IPArray( this, name, (ArrayClipboard) obj, source );
				case IPObjectType.GenericObject: return new IPGenericObject( this, name, (GenericObjectClipboard) obj, source );
				case IPObjectType.GameObjectHierarchy: return new IPGameObjectHierarchy( this, name, (GameObjectHierarchyClipboard) obj );
				case IPObjectType.ComponentGroup: return new IPComponentGroup( this, name, (ComponentGroupClipboard) obj );
				case IPObjectType.AssetFiles: return new IPAssetFiles( this, name, (AssetFilesClipboard) obj );
				case IPObjectType.Vector: return new IPVector( this, name, (VectorClipboard) obj );
				case IPObjectType.Color: return new IPColor( this, name, (Color) obj );
				case IPObjectType.Long: return new IPLong( this, name, (long) obj );
				case IPObjectType.Double: return new IPDouble( this, name, (double) obj );
				case IPObjectType.String: return new IPString( this, name, (string) obj );
				case IPObjectType.AnimationCurve: return new IPAnimationCurve( this, name, (AnimationCurve) obj );
				case IPObjectType.Gradient: return new IPGradient( this, name, (Gradient) obj );
				case IPObjectType.ManagedObject:
					object value = ( (ManagedObjectClipboard) obj ).value;
					if( value == null || value.Equals( null ) )
						return new IPNull( this, name );
					else
						return new IPManagedReference( this, name, (ManagedObjectClipboard) obj );
				default: return new IPNull( this, name );
			}
		}

		private void SerializeIPObject( BinaryWriter writer, IPObject entry )
		{
			IPObjectType typeEnum;
			if( !serializedTypeToEnumLookup.TryGetValue( entry.GetType(), out typeEnum ) )
				typeEnum = IPObjectType.Null;

			writer.Write( (int) typeEnum );
			entry.Serialize( writer );
		}

		private IPObject InstantiateIPObject( IPObjectType typeEnum )
		{
			switch( typeEnum )
			{
				case IPObjectType.Null: return new IPNull( this );
				case IPObjectType.Type: return new IPType( this );
				case IPObjectType.Asset: return new IPAsset( this );
				case IPObjectType.AssetReference: return new IPAssetReference( this );
				case IPObjectType.SceneObject: return new IPSceneObject( this );
				case IPObjectType.SceneObjectReference: return new IPSceneObjectReference( this );
				case IPObjectType.GameObjectHierarchy: return new IPGameObjectHierarchy( this );
				case IPObjectType.ComponentGroup: return new IPComponentGroup( this );
				case IPObjectType.AssetFiles: return new IPAssetFiles( this );
				case IPObjectType.ManagedObject: return new IPManagedObject( this );
				case IPObjectType.ManagedReference: return new IPManagedReference( this );
				case IPObjectType.Array: return new IPArray( this );
				case IPObjectType.GenericObject: return new IPGenericObject( this );
				case IPObjectType.Vector: return new IPVector( this );
				case IPObjectType.Color: return new IPColor( this );
				case IPObjectType.Long: return new IPLong( this );
				case IPObjectType.Double: return new IPDouble( this );
				case IPObjectType.String: return new IPString( this );
				case IPObjectType.AnimationCurve: return new IPAnimationCurve( this );
				case IPObjectType.Gradient: return new IPGradient( this );
				default: return new IPNull( this );
			}
		}
		#endregion

		#region Paste Functions
		public bool CanPasteToObject( Object target )
		{
			if( !target || ( target.hideFlags & HideFlags.NotEditable ) == HideFlags.NotEditable )
				return false;

			// Clipboard must contain the RootValue's fields, as well
			if( Values.Length <= 1 )
				return false;

			// Allow pasting materials to materials only
			IPType objectType = RootUnityObjectType;
			bool isSerializedObjectMaterial = objectType != null && objectType.Type != null && typeof( Material ).IsAssignableFrom( objectType.Type );
			if( isSerializedObjectMaterial != ( target is Material ) )
				return false;

			// Make sure that there is at least 1 serialized property that can be pasted to target Object
			HashSet<string> sourcePropertiesSerialized = new HashSet<string>();
			for( int i = 1; i < Values.Length; i++ )
			{
				if( !string.IsNullOrEmpty( Values[i].Name ) )
					sourcePropertiesSerialized.Add( Values[i].Name );
			}

			SerializedObject targetSerializedObject = new SerializedObject( target );
			foreach( SerializedProperty property in targetSerializedObject.EnumerateDirectChildren() )
			{
				if( property.name != "m_Script" && sourcePropertiesSerialized.Contains( property.name ) )
					return true;
			}

			return false;
		}

		public bool CanPasteAsNewComponent( Component target )
		{
			if( !target || ( target.hideFlags & HideFlags.NotEditable ) == HideFlags.NotEditable )
				return false;

			IPType objectType = RootUnityObjectType;
			return objectType != null && objectType.Type != null && typeof( Component ).IsAssignableFrom( objectType.Type );
		}

		public bool CanPasteCompleteGameObject( GameObject parent )
		{
			if( !( RootValue is IPGameObjectHierarchy ) )
				return false;

			return !parent || !AssetDatabase.Contains( parent );
		}

		public bool CanPasteComponentGroup( GameObject target )
		{
			if( !( RootValue is IPComponentGroup ) )
				return false;

			return target && !AssetDatabase.Contains( target );
		}

		public bool CanPasteAssetFiles( Object[] parentFolders )
		{
			return parentFolders.Length > 0 && RootValue is IPAssetFiles;
		}

		public bool CanPasteAssetFiles( string[] parentFolders )
		{
			return parentFolders.Length > 0 && RootValue is IPAssetFiles;
		}

		public void PasteToObject( Object target, bool logModifiedProperties = true )
		{
			if( !target )
				return;

			// Perform a name-wise paste
			Dictionary<string, IPObject> sourcePropertiesSerialized = new Dictionary<string, IPObject>( 32 );
			for( int i = 1; i < Values.Length; i++ )
			{
				if( !string.IsNullOrEmpty( Values[i].Name ) )
					sourcePropertiesSerialized[Values[i].Name] = Values[i];
			}

			StringBuilder sb = Utilities.stringBuilder;
			if( logModifiedProperties )
			{
				sb.Length = 0;
				sb.AppendLine( "Pasted variable(s):" );
			}

			int pastes = 0;
			SerializedObject targetSerializedObject = new SerializedObject( target );
			foreach( SerializedProperty property in targetSerializedObject.EnumerateDirectChildren() )
			{
				if( property.name == "m_Script" )
					continue;

				IPObject matchingProperty;
				if( sourcePropertiesSerialized.TryGetValue( property.name, out matchingProperty ) )
				{
					object value = matchingProperty.GetClipboardObject( target );
					if( property.CanPasteValue( value, true ) )
					{
						property.PasteValue( value, true );
						pastes++;

						if( logModifiedProperties )
							sb.Append( "- " ).AppendLine( property.name );
					}
				}
			}

			if( pastes > 0 )
			{
				targetSerializedObject.ApplyModifiedProperties();

				if( logModifiedProperties )
					Debug.Log( sb.ToString() );
			}
		}

		public Component PasteAsNewComponent( Component target )
		{
			if( !target )
				return null;

			IPType objectType = RootUnityObjectType;
			if( objectType == null )
				return null;

			if( objectType.Type == null )
			{
				Debug.LogError( string.Concat( "Type \"", objectType.AssemblyQualifiedName, "\" doesn't exist in the project" ) );
				return null;
			}

			GameObject gameObject = target.gameObject;
			Component newComponent = Undo.AddComponent( gameObject, objectType.Type );
			if( newComponent )
				PasteToObject( newComponent, false );

			return newComponent;
		}

		public GameObject[] PasteCompleteGameObject( GameObject parent, bool preserveWorldSpacePosition )
		{
			GameObject[] result = ( (IPGameObjectHierarchy) RootValue ).PasteHierarchy( parent ? parent.transform : null, preserveWorldSpacePosition );
			Selection.objects = result;
			return result;
		}

		public Component[] PasteComponentGroup( GameObject target, IPComponentGroup.ComponentInfo[] filteredComponents = null )
		{
			return ( (IPComponentGroup) RootValue ).PasteComponents( target, filteredComponents );
		}

		public string[] PasteAssetFiles( Object[] parentFolders, bool logPastedFiles = true )
		{
			string[] _parentFolders = new string[parentFolders.Length];
			for( int i = 0; i < parentFolders.Length; i++ )
				_parentFolders[i] = AssetDatabase.GetAssetPath( parentFolders[i] );

			return PasteAssetFiles( _parentFolders, logPastedFiles );
		}

		public string[] PasteAssetFiles( string[] parentFolders, bool logPastedFiles = true )
		{
			Utilities.ConvertAbsolutePathsToRelativePaths( parentFolders );

			string[] result = ( (IPAssetFiles) RootValue ).PasteFiles( parentFolders );
			if( result.Length > 0 )
			{
				if( logPastedFiles )
				{
					StringBuilder sb = Utilities.stringBuilder;
					sb.Length = 0;

					sb.AppendLine( "Pasted asset file(s):" );
					for( int i = 0; i < result.Length; i++ )
						sb.Append( "- " ).AppendLine( result[i] );

					Debug.Log( sb.ToString() );
				}

				Object[] selection = new Object[result.Length];
				for( int i = 0; i < result.Length; i++ )
					selection[i] = AssetDatabase.LoadMainAssetAtPath( result[i] );

				Selection.objects = selection;
			}

			return result;
		}
		#endregion

		#region Helper Functions
		private int GetIndexOfTypeToSerialize( Type type, bool addEntryIfNotExists )
		{
			return GetIndexOfObjectInList( type, ref typesToSerialize, addEntryIfNotExists );
		}

		private int GetIndexOfSceneObjectToSerialize( Object sceneObject, bool addEntryIfNotExists )
		{
			return GetIndexOfObjectInList( sceneObject, ref sceneObjectsToSerialize, addEntryIfNotExists );
		}

		private int GetIndexOfAssetToSerialize( Object asset, bool addEntryIfNotExists )
		{
			return GetIndexOfObjectInList( asset, ref assetsToSerialize, addEntryIfNotExists );
		}

		private int GetIndexOfManagedObjectToSerialize( ManagedObjectClipboard obj, bool addEntryIfNotExists )
		{
			if( managedObjectsToSerialize == null )
				managedObjectsToSerialize = new List<ManagedObjectClipboard>( 4 );

			for( int i = managedObjectsToSerialize.Count - 1; i >= 0; i-- )
			{
				if( managedObjectsToSerialize[i].value == obj.value )
					return i;
			}

			if( !addEntryIfNotExists )
				return -1;

			managedObjectsToSerialize.Add( obj );
			return managedObjectsToSerialize.Count - 1;
		}

		private int GetIndexOfManagedObjectToSerialize( object obj, bool addEntryIfNotExists )
		{
			if( managedObjectsToSerialize == null )
				managedObjectsToSerialize = new List<ManagedObjectClipboard>( 4 );

			for( int i = managedObjectsToSerialize.Count - 1; i >= 0; i-- )
			{
				if( managedObjectsToSerialize[i].value == obj )
					return i;
			}

			if( !addEntryIfNotExists )
				return -1;

			managedObjectsToSerialize.Add( new ManagedObjectClipboard( obj.GetType().Name, obj, null, null ) );
			return managedObjectsToSerialize.Count - 1;
		}

		private static int GetIndexOfObjectInList<T>( T obj, ref List<T> list, bool addEntryIfNotExists ) where T : class
		{
			if( list == null )
				list = new List<T>( 4 );

			for( int i = list.Count - 1; i >= 0; i-- )
			{
				if( list[i] == obj )
					return i;
			}

			if( !addEntryIfNotExists )
				return -1;

			list.Add( obj );
			return list.Count - 1;
		}

		// Returns sibling index of the Transform among all of its siblings with the same name
		private static int GetIndexOfTransformByName( Transform transform )
		{
			string name = transform.name;
			int siblingIndex = -1;
			Transform parent = transform.parent;
			if( parent )
			{
				for( int i = 0; i < parent.childCount; i++ )
				{
					Transform child = parent.GetChild( i );
					if( child.name == name )
					{
						siblingIndex++;
						if( child == transform )
							break;
					}
				}
			}
			else
			{
				GameObject gameObject = transform.gameObject;
#if UNITY_2018_3_OR_NEWER
				PrefabStage openPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
				if( openPrefabStage != null && openPrefabStage.IsPartOfPrefabContents( gameObject ) )
					return 0;
#endif

				GameObject[] rootObjects = gameObject.scene.GetRootGameObjects();
				for( int i = 0; i < rootObjects.Length; i++ )
				{
					if( rootObjects[i].name == name )
					{
						siblingIndex++;
						if( rootObjects[i] == gameObject )
							break;
					}

				}
			}

			return siblingIndex;
		}

		// Returns index of the Component among all Components of the same type
		private static int GetIndexOfComponentByType( Component component, IList<Component> allComponents )
		{
			Type componentType = component.GetType();
			int componentIndex = -1;
			for( int i = 0; i < allComponents.Count; i++ )
			{
				if( allComponents[i] && allComponents[i].GetType() == componentType )
				{
					componentIndex++;
					if( allComponents[i] == component )
						break;
				}
			}

			return componentIndex;
		}

		// Returns Component at specified index among all Components of the same type
		private static Component FindComponentOfTypeClosestToIndex( Type componentType, IList<Component> allComponents, int targetComponentIndex, out int foundComponentIndex )
		{
			Component component = null;
			foundComponentIndex = -1;
			for( int i = 0; i < allComponents.Count; i++ )
			{
				if( allComponents[i] && allComponents[i].GetType() == componentType && ++foundComponentIndex >= targetComponentIndex )
				{
					component = allComponents[i];
					break;
				}
			}

			return component;
		}

		private static void SerializeString( BinaryWriter writer, string value )
		{
			writer.Write( value ?? "" );
		}

		private static string DeserializeString( BinaryReader reader )
		{
			string result = reader.ReadString();
			return result.Length > 0 ? result : null;
		}
		#endregion
	}
}