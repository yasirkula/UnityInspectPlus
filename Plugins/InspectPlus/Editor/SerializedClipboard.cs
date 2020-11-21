﻿using System;
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
#if UNITY_2018_3_OR_NEWER
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
			GameObjectHierarchy = 17
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

			public IPType( SerializedClipboard root ) : base( root ) { }
			public IPType( SerializedClipboard root, Type type ) : base( root, type.Name )
			{
				AssemblyQualifiedName = type.AssemblyQualifiedName;
			}

			public override object GetClipboardObject( Object context )
			{
				// We want to call Type.GetType only once but it can return null. Thus, we aren't using "if( type == null )"
				if( !typeRecreated )
				{
					typeRecreated = true;

					m_type = Type.GetType( AssemblyQualifiedName );
					if( m_type == null )
					{
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
								if( m_type == null )
								{
									foreach( Assembly assembly in AppDomain.CurrentDomain.GetAssemblies() )
									{
										if( !assembly.FullName.StartsWith( "UnityEngine" ) )
											continue;

										foreach( Type type in assembly.GetExportedTypes() )
										{
											if( type.FullName == typeFullName )
											{
												m_type = type;
												break;
											}
										}

										if( m_type != null )
											break;
									}
								}
							}
						}
					}
				}

				return m_type;
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
					Debug.LogError( "Empty context encountered while deserializing managed object, please report it to Inspect+'s author." );
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
					SceneName = transform.gameObject.scene.name;

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
							if( asset.name == ObjectName && asset.GetType().Name == serializedType.Name && ( serializedType.Type == null || serializedType.Type == asset.GetType() ) )
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

			public bool IsActive;
			public int Layer;
			public string Tag;
			public int StaticFlags;
			public int HideFlags;

			public SerializedClipboard PrefabAsset;
			public bool IsPartOfParentPrefab;

			public ComponentInfo[] Components;
			public RemovedComponentInfo[] RemovedComponents;

			public IPGameObjectHierarchy[] Children;

			public IPGameObjectHierarchy( SerializedClipboard root ) : base( root ) { }
			public IPGameObjectHierarchy( SerializedClipboard root, GameObjectHierarchyClipboard value ) : base( root, value.source.name )
			{
				GameObject gameObject = value.source;
				Transform transform = gameObject.transform;

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

					Components = new ComponentInfo[_components.Count];
					for( int i = 0; i < _components.Count; i++ )
					{
						SerializedClipboard serializedComponent = new SerializedClipboard( _components[i], _components[i], null );
						bool componentEnabled = EditorUtility.GetObjectEnabled( _components[i] ) != 0;
						Components[i] = new ComponentInfo( serializedComponent, componentEnabled, (int) _components[i].hideFlags, GetIndexOfComponentByType( _components[i], _components ) );
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
							if( value.includeChildren || prefab.transform.childCount == 0 )
								PrefabAsset = new SerializedClipboard( prefab, null, null );
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
					if( value.includeChildren )
					{
						Children = new IPGameObjectHierarchy[transform.childCount];
						for( int i = 0; i < Children.Length; i++ )
							Children[i] = new IPGameObjectHierarchy( root, new GameObjectHierarchyClipboard( transform.GetChild( i ).gameObject, true ) );
					}
				}
				finally
				{
					InspectPlusSettings.Instance.SmartCopyPaste = smartCopyPasteEnabled;
				}
			}

			public override object GetClipboardObject( Object context )
			{
				return new GameObjectHierarchyClipboard( this );
			}

			public override void Serialize( BinaryWriter writer )
			{
				base.Serialize( writer );

				writer.Write( IsActive );
				writer.Write( Layer );
				writer.Write( StaticFlags );
				writer.Write( HideFlags );
				SerializeString( writer, Tag != "Untagged" ? Tag : null );

				if( PrefabAsset != null )
				{
					writer.Write( true );
					PrefabAsset.Serialize( writer );
				}
				else
					writer.Write( false );

				writer.Write( IsPartOfParentPrefab );

				writer.Write( Components.Length );
				for( int i = 0; i < Components.Length; i++ )
				{
					Components[i].Component.Serialize( writer );
					writer.Write( Components[i].Enabled );
					writer.Write( Components[i].HideFlags );
					writer.Write( Components[i].Index );
				}

				writer.Write( RemovedComponents.Length );
				for( int i = 0; i < RemovedComponents.Length; i++ )
				{
					RemovedComponents[i].Type.Serialize( writer );
					writer.Write( RemovedComponents[i].Index );
				}

				root.SerializeArray( writer, Children );
			}

			public override void Deserialize( BinaryReader reader )
			{
				base.Deserialize( reader );

				IsActive = reader.ReadBoolean();
				Layer = reader.ReadInt32();
				StaticFlags = reader.ReadInt32();
				HideFlags = reader.ReadInt32();

				Tag = DeserializeString( reader );
				if( string.IsNullOrEmpty( Tag ) )
					Tag = "Untagged";

				if( reader.ReadBoolean() )
					PrefabAsset = new SerializedClipboard( reader );

				IsPartOfParentPrefab = reader.ReadBoolean();

				Components = new ComponentInfo[reader.ReadInt32()];
				for( int i = 0; i < Components.Length; i++ )
				{
					SerializedClipboard component = new SerializedClipboard( reader );
					bool enabled = reader.ReadBoolean();
					int hideFlags = reader.ReadInt32();
					Components[i] = new ComponentInfo( component, enabled, hideFlags, reader.ReadInt32() );
				}

				RemovedComponents = new RemovedComponentInfo[reader.ReadInt32()];
				for( int i = 0; i < RemovedComponents.Length; i++ )
				{
					IPType type = new IPType( root );
					type.Deserialize( reader );
					RemovedComponents[i] = new RemovedComponentInfo( type, reader.ReadInt32() );
				}

				Children = root.DeserializeArray<IPGameObjectHierarchy>( reader );
			}

			public GameObject PasteHierarchy( Transform parent )
			{
				if( parent && AssetDatabase.Contains( parent ) )
				{
					Debug.LogError( "Can't paste Complete GameObject to a prefab or model Asset: " + parent, parent );
					return null;
				}

				List<GameObject> hierarchy = new List<GameObject>( Children != null && Children.Length > 0 ? 32 : 0 );
				List<ComponentToPaste[]> hierarchyComponents = new List<ComponentToPaste[]>( hierarchy.Capacity );

				// Recursively create child GameObjects (and their components). Creating all Components before pasting their
				// values allows us to restore references between Components (since all the Components will exist while
				// pasting the Component values)
				GameObject rootGameObject = null;
				CreateGameObjectsRecursively( parent, ref rootGameObject, hierarchy, hierarchyComponents );

				// Paste component values
				bool smartCopyPasteEnabled = InspectPlusSettings.Instance.SmartCopyPaste;
				try
				{
					// Smart copy-paste is mandatory for copy&pasting GameObject hierarchies in order to
					// be able to restore references between Components properly
					InspectPlusSettings.Instance.SmartCopyPaste = true;

					for( int i = 0; i < hierarchyComponents.Count; i++ )
					{
						ComponentToPaste[] componentsToPaste = hierarchyComponents[i];
						for( int j = 0; j < componentsToPaste.Length; j++ )
							componentsToPaste[j].SerializedComponent.PasteToObject( componentsToPaste[j].TargetComponent, false );
					}
				}
				finally
				{
					InspectPlusSettings.Instance.SmartCopyPaste = smartCopyPasteEnabled;
				}

				return hierarchy[0];
			}

			private void CreateGameObjectsRecursively( Transform parent, ref GameObject gameObject, List<GameObject> hierarchy, List<ComponentToPaste[]> hierarchyComponents )
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
				}

				gameObject.SetActive( IsActive );
				gameObject.layer = Layer;
				gameObject.tag = Tag;
				gameObject.hideFlags = (HideFlags) HideFlags;
				GameObjectUtility.SetStaticEditorFlags( gameObject, (StaticEditorFlags) StaticFlags );

				// Find the serialized components that exist in this Unity project
				List<ComponentInfo> validComponents = new List<ComponentInfo>( Components.Length );
				for( int i = 0; i < Components.Length; i++ )
				{
					IPType objectType = Components[i].Component.RootUnityObjectType;
					if( objectType != null && objectType.Type != null && typeof( Component ).IsAssignableFrom( objectType.Type ) )
						validComponents.Add( Components[i] );
				}

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

				// Add any necessary components to the GameObject (but don't paste their values yet; creating all Components before pasting their
				// values allows us to restore references between Components since all the Components will exist while pasting the Component values)
				ComponentToPaste[] componentsToPaste = new ComponentToPaste[validComponents.Count];
				for( int i = 0; i < validComponents.Count; i++ )
				{
					// We are calling GetComponents at each iteration because a component added in the previous iteration might automatically
					// add other component(s) to the GameObject (i.e. via RequireComponent)
					Type componentType = validComponents[i].Component.RootUnityObjectType.Type;
					Component[] components = gameObject.GetComponents( componentType );

					int componentIndex;
					Component targetComponent = FindComponentOfTypeClosestToIndex( componentType, components, validComponents[i].Index, out componentIndex );

					for( ; componentIndex < validComponents[i].Index; componentIndex++ )
						targetComponent = Undo.AddComponent( gameObject, componentType );

					// We can paste the values of enabled and hideFlags immediately, though
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

					componentsToPaste[i] = new ComponentToPaste( validComponents[i].Component, targetComponent );
				}

				hierarchy.Add( gameObject );
				hierarchyComponents.Add( componentsToPaste );

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
						List<IPGameObjectHierarchy> childrenList = new List<IPGameObjectHierarchy>( Children );

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

			public void PrintHierarchy( StringBuilder sb )
			{
				PrintHierarchyRecursively( sb, 0 );
			}

			private void PrintHierarchyRecursively( StringBuilder sb, int depth )
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

		public bool HasTooltip { get { return Values.Length > 1 || RootValue is IPGameObjectHierarchy; } }

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

		public SerializedClipboard( object clipboardData, Object source, string label )
		{
			Label = label;

			// For Component, ScriptableObject and materials, serialize the fields as well (for name-based paste operations)
			if( clipboardData is Component || clipboardData is ScriptableObject || clipboardData is Material )
				InitializeWithUnityObject( (Object) clipboardData, source );
			else
			{
				Values = new IPObject[1] { ConvertClipboardObjectToIPObject( clipboardData, null, source ) };
				Initialize( source );
			}
		}

		private void InitializeWithUnityObject( Object value, Object source )
		{
			SerializedObject serializedObject = new SerializedObject( value );
			int valueCount = 0;

			foreach( SerializedProperty property in serializedObject.EnumerateDirectChildren() )
			{
				if( property.name != "m_Script" )
					valueCount++;
			}

			IPObject[] serializedValues = new IPObject[valueCount + 1];
			serializedValues[0] = ConvertClipboardObjectToIPObject( value, null, source );

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
				case IPObjectType.GameObjectHierarchy: return new IPGameObjectHierarchy( this, (GameObjectHierarchyClipboard) obj );
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
			else
				Debug.LogError( string.Concat( "Couldn't add a ", objectType.Type.FullName, " Component to ", gameObject.name ) );

			return newComponent;
		}

		public GameObject PasteCompleteGameObject( GameObject parent )
		{
			return ( (IPGameObjectHierarchy) RootValue ).PasteHierarchy( parent ? parent.transform : null );
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