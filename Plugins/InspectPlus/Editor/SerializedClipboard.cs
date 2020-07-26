using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using GenericObjectClipboard = InspectPlusNamespace.SerializablePropertyExtensions.GenericObjectClipboard;
using ArrayClipboard = InspectPlusNamespace.SerializablePropertyExtensions.ArrayClipboard;
using VectorClipboard = InspectPlusNamespace.SerializablePropertyExtensions.VectorClipboard;
using ManagedObjectClipboard = InspectPlusNamespace.SerializablePropertyExtensions.ManagedObjectClipboard;

namespace InspectPlusNamespace
{
	public class SerializedClipboard
	{
		#region Serialized Types
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

			public virtual void WriteToXmlElement( XmlElement element )
			{
				AppendXmlAttribute( element, "Name", Name );
			}

			public virtual void ReadFromXmlElement( XmlElement element )
			{
				Name = ReadXmlAttributeAsString( element, "Name" );
			}

			protected void AppendXmlAttribute( XmlElement element, string name, string value )
			{
				if( value == null )
					return;

				XmlAttribute result = element.OwnerDocument.CreateAttribute( name );
				result.Value = value;
				element.Attributes.Append( result );
			}

			protected void AppendXmlAttribute( XmlElement element, string name, int value )
			{
				XmlAttribute result = element.OwnerDocument.CreateAttribute( name );
				result.Value = value.ToString( CultureInfo.InvariantCulture );
				element.Attributes.Append( result );
			}

			protected void AppendXmlAttribute( XmlElement element, string name, long value )
			{
				XmlAttribute result = element.OwnerDocument.CreateAttribute( name );
				result.Value = value.ToString( CultureInfo.InvariantCulture );
				element.Attributes.Append( result );
			}

			protected void AppendXmlAttribute( XmlElement element, string name, float value )
			{
				XmlAttribute result = element.OwnerDocument.CreateAttribute( name );
				result.Value = value.ToString( CultureInfo.InvariantCulture );
				element.Attributes.Append( result );
			}

			protected void AppendXmlAttribute( XmlElement element, string name, double value )
			{
				XmlAttribute result = element.OwnerDocument.CreateAttribute( name );
				result.Value = value.ToString( CultureInfo.InvariantCulture );
				element.Attributes.Append( result );
			}

			protected string ReadXmlAttributeAsString( XmlElement element, string name )
			{
				XmlAttribute attribute = element.Attributes[name];
				return attribute != null ? attribute.Value : null;
			}

			protected int ReadXmlAttributeAsInteger( XmlElement element, string name )
			{
				XmlAttribute attribute = element.Attributes[name];
				return attribute != null ? int.Parse( attribute.Value, CultureInfo.InvariantCulture ) : 0;
			}

			protected long ReadXmlAttributeAsLong( XmlElement element, string name )
			{
				XmlAttribute attribute = element.Attributes[name];
				return attribute != null ? long.Parse( attribute.Value, CultureInfo.InvariantCulture ) : 0L;
			}

			protected float ReadXmlAttributeAsFloat( XmlElement element, string name )
			{
				XmlAttribute attribute = element.Attributes[name];
				return attribute != null ? float.Parse( attribute.Value, CultureInfo.InvariantCulture ) : 0f;
			}

			protected double ReadXmlAttributeAsDouble( XmlElement element, string name )
			{
				XmlAttribute attribute = element.Attributes[name];
				return attribute != null ? double.Parse( attribute.Value, CultureInfo.InvariantCulture ) : 0.0;
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
				}

				return m_type;
			}

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "FullName", AssemblyQualifiedName );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				AssemblyQualifiedName = ReadXmlAttributeAsString( element, "FullName" );
			}
		}

		public abstract class IPObjectWithChild : IPObject
		{
			public IPObject[] Children;

			protected IPObjectWithChild( SerializedClipboard root ) : base( root ) { }
			protected IPObjectWithChild( SerializedClipboard root, string name ) : base( root, name ) { }

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				root.CreateObjectArrayInXmlElement( element, Children );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				Children = root.ReadObjectArrayFromXmlElement<IPObject>( element );
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

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "TypeIndex", TypeIndex );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				TypeIndex = ReadXmlAttributeAsInteger( element, "TypeIndex" );
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
					Children[i] = root.GetSerializableDataFromClipboardData( value.elements[i], null, source );
			}

			public override object GetClipboardObject( Object context )
			{
				object[] elements = new object[Children != null ? Children.Length : 0];
				for( int i = 0; i < elements.Length; i++ )
					elements[i] = Children[i].GetClipboardObject( context );

				return new ArrayClipboard( ElementType, elements );
			}

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "ElementType", ElementType );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				ElementType = ReadXmlAttributeAsString( element, "ElementType" );
			}
		}

		public class IPGenericObject : IPObjectWithChild
		{
			public string Type;

			public IPGenericObject( SerializedClipboard root ) : base( root ) { }
			public IPGenericObject( SerializedClipboard root, string name, GenericObjectClipboard value, Object source ) : base( root, name )
			{
				Type = value.type;
				Children = new IPObject[value.values.Length];

				for( int i = 0; i < value.values.Length; i++ )
					Children[i] = root.GetSerializableDataFromClipboardData( value.values[i], null, source );
			}

			public override object GetClipboardObject( Object context )
			{
				object[] values = new object[Children != null ? Children.Length : 0];
				for( int i = 0; i < values.Length; i++ )
					values[i] = Children[i].GetClipboardObject( context );

				return new GenericObjectClipboard( Type, values );
			}

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "Type", Type );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				Type = ReadXmlAttributeAsString( element, "Type" );
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

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "Value", Value );

				WriteNestedReferencesToChildXmlElement( element, NestedManagedObjects, "NestedManagedObjects" );
				WriteNestedReferencesToChildXmlElement( element, NestedSceneObjects, "NestedSceneObjects" );
				WriteNestedReferencesToChildXmlElement( element, NestedAssets, "NestedAssets" );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				Value = ReadXmlAttributeAsString( element, "Value" );

				XmlNodeList childNodes = element.ChildNodes;
				if( childNodes != null )
				{
					for( int i = 0; i < childNodes.Count; i++ )
					{
						XmlElement childNode = (XmlElement) childNodes[i];

						if( childNode.Name == "NestedManagedObjects" )
							NestedManagedObjects = ReadNestedReferencesFromXmlElement( childNode );
						else if( childNode.Name == "NestedSceneObjects" )
							NestedSceneObjects = ReadNestedReferencesFromXmlElement( childNode );
						else if( childNode.Name == "NestedAssets" )
							NestedAssets = ReadNestedReferencesFromXmlElement( childNode );
					}
				}
			}

			private void WriteNestedReferencesToChildXmlElement( XmlElement element, NestedReference[] references, string childElementName )
			{
				if( references != null && references.Length > 0 )
				{
					XmlElement arrayRoot = element.OwnerDocument.CreateElement( childElementName );
					element.AppendChild( arrayRoot );

					for( int i = 0; i < references.Length; i++ )
					{
						XmlElement childElement = arrayRoot.OwnerDocument.CreateElement( "Reference" );
						AppendXmlAttribute( childElement, "RelativePath", references[i].RelativePath );
						AppendXmlAttribute( childElement, "ManagedRefIndex", references[i].ReferenceIndex );
						arrayRoot.AppendChild( childElement );
					}
				}
			}

			private NestedReference[] ReadNestedReferencesFromXmlElement( XmlElement element )
			{
				XmlNodeList childNodes = element.ChildNodes;
				if( childNodes == null || childNodes.Count == 0 )
					return null;

				NestedReference[] result = new NestedReference[childNodes.Count];
				for( int i = 0; i < childNodes.Count; i++ )
				{
					XmlElement childNode = (XmlElement) childNodes[i];
					result[i] = new NestedReference()
					{
						RelativePath = ReadXmlAttributeAsString( childNode, "RelativePath" ),
						ReferenceIndex = ReadXmlAttributeAsInteger( childNode, "ManagedRefIndex" )
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

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "ManagedRefIndex", ManagedRefIndex );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				ManagedRefIndex = ReadXmlAttributeAsInteger( element, "ManagedRefIndex" );
			}
		}

		public abstract class IPUnityObject : IPObjectWithType
		{
			public string ObjectName;
			public string Path;
			public string RelativePath;

			protected IPUnityObject( SerializedClipboard root ) : base( root ) { }
			protected IPUnityObject( SerializedClipboard root, string name, Type type ) : base( root, name, type ) { }

			public override object GetClipboardObject( Object context )
			{
				base.GetClipboardObject( context );

				// Try finding the object with RelativePath first
				return ResolveRelativePath( context, RelativePath, serializedType );
			}

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "ObjectName", ObjectName );
				AppendXmlAttribute( element, "Path", Path );
				AppendXmlAttribute( element, "RelativePath", RelativePath );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				ObjectName = ReadXmlAttributeAsString( element, "ObjectName" );
				Path = ReadXmlAttributeAsString( element, "Path" );
				RelativePath = ReadXmlAttributeAsString( element, "RelativePath" );
			}

			protected string CalculateRelativePath( Object source, Object context, string targetPath = null )
			{
				if( !InspectPlusSettings.Instance.UseRelativePathsInXML )
					return null;

				if( !source || !context || ( !( source is Component ) && !( source is GameObject ) ) || ( !( context is Component ) && !( context is GameObject ) ) )
					return null;

				Transform sourceTransform = source is Component ? ( (Component) source ).transform : ( (GameObject) source ).transform;
				Transform targetTransform = context is Component ? ( (Component) context ).transform : ( (GameObject) context ).transform;
				if( sourceTransform == targetTransform )
					return "./";

				if( sourceTransform.root != targetTransform.root )
					return null;

				if( targetTransform.IsChildOf( sourceTransform ) )
				{
					string _result = targetTransform.name;
					while( targetTransform.parent != sourceTransform )
					{
						targetTransform = targetTransform.parent;
						_result = string.Concat( targetTransform.name, "/", _result );
					}

					return _result;
				}

				if( sourceTransform.IsChildOf( targetTransform ) )
				{
					string _result = "../";
					while( sourceTransform.parent != targetTransform )
					{
						sourceTransform = sourceTransform.parent;
						_result += "../";
					}

					return _result;
				}

				// Credit: https://stackoverflow.com/a/9042938/2373034
				string sourcePath = CalculatePath( sourceTransform );
				if( string.IsNullOrEmpty( targetPath ) )
					targetPath = CalculatePath( targetTransform );

				int commonPrefixEndIndex = 0;
				for( int i = 0; i < targetPath.Length && i < sourcePath.Length && targetPath[i] == sourcePath[i]; i++ )
				{
					if( targetPath[i] == '/' )
						commonPrefixEndIndex = i;
				}

				string result = "../";
				for( int i = commonPrefixEndIndex + 1; i < sourcePath.Length; i++ )
				{
					if( sourcePath[i] == '/' )
						result += "../";
				}

				return result + targetPath.Substring( commonPrefixEndIndex + 1 );
			}

			protected Object ResolveRelativePath( Object source, string relativePath, IPType targetType )
			{
				if( !InspectPlusSettings.Instance.UseRelativePathsInXML )
					return null;

				if( string.IsNullOrEmpty( relativePath ) || !source || ( !( source is Component ) && !( source is GameObject ) ) )
					return null;

				Transform result = source is Component ? ( (Component) source ).transform : ( (GameObject) source ).transform;
				if( relativePath != "./" ) // "./" means RelativePath points to the target object itself
				{
					int index = 0;
					while( index < relativePath.Length && relativePath.StartsWith( "../" ) )
					{
						result = result.parent;
						if( !result )
							return null;

						index += 3;
					}

					while( index < relativePath.Length )
					{
						int separatorIndex = relativePath.IndexOf( '/', index );
						if( separatorIndex < 0 )
							separatorIndex = relativePath.Length;

						result = result.Find( relativePath.Substring( index, separatorIndex - index ) );
						if( !result )
							return null;

						index = separatorIndex + 1;
					}
				}

				return GetObjectOfTypeFromTransform( result, targetType );
			}

			protected Object GetObjectOfTypeFromTransform( Transform source, IPType type )
			{
				if( type.Name == "GameObject" )
					return source.gameObject;
				else
				{
					Component[] components = source.GetComponents<Component>();
					for( int i = 0; i < components.Length; i++ )
					{
						if( components[i].GetType().Name == type.Name )
						{
							if( type.Type == null || type.Type == components[i].GetType() )
								return components[i];
						}
					}
				}

				return null;
			}

			protected string CalculatePath( Transform transform )
			{
				string result = transform.name;

#if UNITY_2018_3_OR_NEWER
				UnityEditor.Experimental.SceneManagement.PrefabStage openPrefabStage = UnityEditor.Experimental.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
				if( openPrefabStage != null && openPrefabStage.IsPartOfPrefabContents( transform.gameObject ) )
				{
					Transform prefabStageRoot = openPrefabStage.prefabContentsRoot.transform;
					while( transform.parent && transform != prefabStageRoot )
					{
						transform = transform.parent;
						result = string.Concat( transform.name, "/", result );
					}
				}
				else
#endif
				{
					while( transform.parent )
					{
						transform = transform.parent;
						result = string.Concat( transform.name, "/", result );
					}
				}

				return result;
			}
		}

		public class IPSceneObject : IPUnityObject
		{
			public string SceneName;

			private bool objectRecreated;
			private Object m_object;

			public IPSceneObject( SerializedClipboard root ) : base( root ) { }
			public IPSceneObject( SerializedClipboard root, Object value, Object source ) : base( root, null, value.GetType() )
			{
				ObjectName = value.name;

				if( value is Component || value is GameObject )
				{
					Transform transform = value is Component ? ( (Component) value ).transform : ( (GameObject) value ).transform;

#if UNITY_2018_3_OR_NEWER
					UnityEditor.Experimental.SceneManagement.PrefabStage openPrefabStage = UnityEditor.Experimental.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
					if( openPrefabStage == null || !openPrefabStage.IsPartOfPrefabContents( transform.gameObject ) )
#endif
						SceneName = transform.gameObject.scene.name;

					Path = CalculatePath( transform );
					RelativePath = CalculateRelativePath( source, value, Path );
				}
			}

			public override object GetClipboardObject( Object context )
			{
				// First, try to resolve the RelativePath. We can't cache it as it depends on the target
				object baseResult = base.GetClipboardObject( context );
				if( baseResult != null && !baseResult.Equals( null ) )
					return baseResult;

				if( objectRecreated )
					return m_object;

				objectRecreated = true;

				if( !string.IsNullOrEmpty( Path ) )
				{
					// Search all open scenes to find the object reference
					// Don't use GameObject.Find because it can't find inactive objects
					string[] pathComponents = Path.Split( '/' );

#if UNITY_2018_3_OR_NEWER
					// Search the currently open prefab stage (if any)
					UnityEditor.Experimental.SceneManagement.PrefabStage openPrefabStage = UnityEditor.Experimental.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
					if( openPrefabStage != null )
					{
						Object result = FindObjectInScene( new GameObject[1] { openPrefabStage.prefabContentsRoot }, pathComponents );
						if( result )
						{
							m_object = result;
							return result;
						}
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
						Object result = FindObjectInScene( scenes[originalSceneIndex].GetRootGameObjects(), pathComponents );
						if( result )
						{
							m_object = result;
							return result;
						}
					}

					// If object isn't found, search other scenes
					for( int i = 0; i < scenes.Length; i++ )
					{
						if( i != originalSceneIndex )
						{
							Object result = FindObjectInScene( scenes[i].GetRootGameObjects(), pathComponents );
							if( result )
							{
								m_object = result;
								return result;
							}
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
							{
								m_object = objects[i];
								return objects[i];
							}
						}
					}
				}

				return null;
			}

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "SceneName", SceneName );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				SceneName = ReadXmlAttributeAsString( element, "SceneName" );
			}

			private Object FindObjectInScene( GameObject[] sceneRoot, string[] pathComponents )
			{
				for( int i = 0; i < sceneRoot.Length; i++ )
				{
					Object result = TraverseHierarchyRecursively( sceneRoot[i].transform, pathComponents, 0 );
					if( result )
						return result;
				}

				return null;
			}

			private Object TraverseHierarchyRecursively( Transform obj, string[] path, int pathIndex )
			{
				if( obj.name != path[pathIndex] )
					return null;

				if( pathIndex == path.Length - 1 )
					return GetObjectOfTypeFromTransform( obj, serializedType );

				for( int i = obj.childCount - 1; i >= 0; i-- )
				{
					Object result = TraverseHierarchyRecursively( obj.GetChild( i ), path, pathIndex + 1 );
					if( result )
						return result;
				}

				return null;
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
			public IPAsset( SerializedClipboard root, Object value, Object source ) : base( root, null, value.GetType() )
			{
				ObjectName = value.name;
				Path = AssetDatabase.GetAssetPath( value );
				RelativePath = CalculateRelativePath( source, value );
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

				if( !string.IsNullOrEmpty( Path ) )
				{
					Object result = FindAssetAtPath( Path );
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

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "Value", Value );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				Value = ReadXmlAttributeAsString( element, "Value" );
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
							if( asset.name == ObjectName && asset.GetType().Name == serializedType.Name )
							{
								if( serializedType.Type == null || serializedType.Type == asset.GetType() )
									return asset;
							}
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

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "SceneObjectIndex", SceneObjectIndex );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				SceneObjectIndex = ReadXmlAttributeAsInteger( element, "SceneObjectIndex" );
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

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "AssetIndex", AssetIndex );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				AssetIndex = ReadXmlAttributeAsInteger( element, "AssetIndex" );
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

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );

				AppendXmlAttribute( element, "C1", C1 );
				AppendXmlAttribute( element, "C2", C2 );
				AppendXmlAttribute( element, "C3", C3 );

				if( C6 != 0f )
				{
					AppendXmlAttribute( element, "C4", C4 );
					AppendXmlAttribute( element, "C5", C5 );
					AppendXmlAttribute( element, "C6", C6 );
				}
				else if( C5 != 0f )
				{
					AppendXmlAttribute( element, "C4", C4 );
					AppendXmlAttribute( element, "C5", C5 );
				}
				else if( C4 != 0f )
					AppendXmlAttribute( element, "C4", C4 );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );

				C1 = ReadXmlAttributeAsFloat( element, "C1" );
				C2 = ReadXmlAttributeAsFloat( element, "C2" );
				C3 = ReadXmlAttributeAsFloat( element, "C3" );
				C4 = ReadXmlAttributeAsFloat( element, "C4" );
				C5 = ReadXmlAttributeAsFloat( element, "C5" );
				C6 = ReadXmlAttributeAsFloat( element, "C6" );
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

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "Value", Value );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				Value = ReadXmlAttributeAsLong( element, "Value" );
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

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "Value", Value );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				Value = ReadXmlAttributeAsDouble( element, "Value" );
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

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "Value", Value );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				Value = ReadXmlAttributeAsString( element, "Value" );
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

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "Value", Value );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				Value = ReadXmlAttributeAsString( element, "Value" );
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

			public override void WriteToXmlElement( XmlElement element )
			{
				base.WriteToXmlElement( element );
				AppendXmlAttribute( element, "Value", Value );
			}

			public override void ReadFromXmlElement( XmlElement element )
			{
				base.ReadFromXmlElement( element );
				Value = ReadXmlAttributeAsString( element, "Value" );
			}
		}
		#endregion

		public IPType[] Types;
		public IPSceneObject[] SceneObjects;
		public IPAsset[] Assets;
		public IPManagedObject[] ManagedObjects;
		public IPObject[] Values;

		private List<Type> typesToSerialize;
		private List<Object> sceneObjectsToSerialize;
		private List<Object> assetsToSerialize;
		private List<ManagedObjectClipboard> managedObjectsToSerialize;

		#region Serialization Functions
		public string SerializeProperty( SerializedProperty property, bool prettyPrint )
		{
			return SerializeClipboardData( property.CopyValue(), prettyPrint, property.serializedObject.targetObject );
		}

		public string SerializeClipboardData( object clipboardData, bool prettyPrint, Object source )
		{
			// For Component, ScriptableObject and materials, serialize the fields as well (for name-based paste operations)
			if( clipboardData is Component || clipboardData is ScriptableObject || clipboardData is Material )
				return SerializeUnityObject( (Object) clipboardData, prettyPrint, source );

			Values = new IPObject[1] { GetSerializableDataFromClipboardData( clipboardData, null, source ) };
			return SerializeToXml( prettyPrint, source );
		}

		private string SerializeUnityObject( Object value, bool prettyPrint, Object source )
		{
			SerializedObject serializedObject = new SerializedObject( value );
			int valueCount = 0;

			foreach( SerializedProperty property in serializedObject.EnumerateDirectChildren() )
			{
				if( property.name != "m_Script" )
					valueCount++;
			}

			IPObject[] serializedValues = new IPObject[valueCount + 1];
			serializedValues[0] = GetSerializableDataFromClipboardData( value, null, source );

			if( valueCount > 0 )
			{
				int valueIndex = 1;
				foreach( SerializedProperty property in serializedObject.EnumerateDirectChildren() )
				{
					if( property.name != "m_Script" )
						serializedValues[valueIndex++] = GetSerializableDataFromClipboardData( property.CopyValue(), property.name, value );
				}
			}

			Values = serializedValues;
			return SerializeToXml( prettyPrint, source );
		}

		private string SerializeToXml( bool prettyPrint, Object source )
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

			XmlDocument xmlDocument = new XmlDocument();
			XmlElement rootElement = xmlDocument.CreateElement( "InspectPlus" );
			xmlDocument.AppendChild( rootElement );

			CreateObjectArrayInChildXmlElement( rootElement, Types, "Types" );
			CreateObjectArrayInChildXmlElement( rootElement, SceneObjects, "SceneObjects" );
			CreateObjectArrayInChildXmlElement( rootElement, Assets, "Assets" );
			CreateObjectArrayInChildXmlElement( rootElement, ManagedObjects, "ManagedObjects" );
			CreateObjectArrayInChildXmlElement( rootElement, Values, "Values" );

			XmlWriterSettings settings = new XmlWriterSettings
			{
				Indent = prettyPrint,
				OmitXmlDeclaration = true
			};

			using( StringWriter textWriter = new StringWriter() )
			using( XmlWriter xmlWriter = XmlWriter.Create( textWriter, settings ) )
			{
				xmlDocument.Save( xmlWriter );
				return textWriter.ToString();
			}
		}

		public void Deserialize( string xmlContents )
		{
			Types = null;
			ManagedObjects = null;
			Values = null;

			XmlDocument xmlDocument = new XmlDocument();
			xmlDocument.LoadXml( xmlContents );

			XmlNodeList childNodes = xmlDocument.DocumentElement.ChildNodes;
			for( int i = 0; i < childNodes.Count; i++ )
			{
				XmlElement childNode = (XmlElement) childNodes[i];

				if( childNode.Name == "Types" )
					Types = ReadObjectArrayFromXmlElement<IPType>( childNode );
				else if( childNode.Name == "SceneObjects" )
					SceneObjects = ReadObjectArrayFromXmlElement<IPSceneObject>( childNode );
				else if( childNode.Name == "Assets" )
					Assets = ReadObjectArrayFromXmlElement<IPAsset>( childNode );
				else if( childNode.Name == "ManagedObjects" )
					ManagedObjects = ReadObjectArrayFromXmlElement<IPManagedObject>( childNode );
				else if( childNode.Name == "Values" )
					Values = ReadObjectArrayFromXmlElement<IPObject>( childNode );
			}
		}

		private IPObject GetSerializableDataFromClipboardData( object obj, string name, Object source )
		{
			if( obj == null || obj.Equals( null ) )
				return new IPNull( this, name );
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
			if( obj is ArrayClipboard )
				return new IPArray( this, name, (ArrayClipboard) obj, source );
			if( obj is GenericObjectClipboard )
				return new IPGenericObject( this, name, (GenericObjectClipboard) obj, source );
			if( obj is VectorClipboard )
				return new IPVector( this, name, (VectorClipboard) obj );
			if( obj is ManagedObjectClipboard )
			{
				object value = ( (ManagedObjectClipboard) obj ).value;
				if( value == null || value.Equals( null ) )
					return new IPNull( this, name );
				else
					return new IPManagedReference( this, name, (ManagedObjectClipboard) obj );
			}
			if( obj is long )
				return new IPLong( this, name, (long) obj );
			if( obj is double )
				return new IPDouble( this, name, (double) obj );
			if( obj is string )
				return new IPString( this, name, (string) obj );
			if( obj is AnimationCurve )
				return new IPAnimationCurve( this, name, (AnimationCurve) obj );
			if( obj is Gradient )
				return new IPGradient( this, name, (Gradient) obj );

			return new IPNull( this, name );
		}

		private XmlElement ConvertObjectToXmlElement( IPObject entry, XmlElement parent )
		{
			string elementName;
			if( entry is IPNull )
				elementName = "Null";
			else if( entry is IPAsset )
				elementName = "Asset";
			else if( entry is IPSceneObject )
				elementName = "SceneObject";
			else if( entry is IPAssetReference )
				elementName = "AssetRef";
			else if( entry is IPSceneObjectReference )
				elementName = "SceneObjectRef";
			else if( entry is IPArray )
				elementName = "Array";
			else if( entry is IPGenericObject )
				elementName = "Generic";
			else if( entry is IPVector )
				elementName = "Vector";
			else if( entry is IPManagedReference )
				elementName = "ManagedRef";
			else if( entry is IPLong )
				elementName = "Long";
			else if( entry is IPDouble )
				elementName = "Double";
			else if( entry is IPString )
				elementName = "String";
			else if( entry is IPAnimationCurve )
				elementName = "Curve";
			else if( entry is IPGradient )
				elementName = "Gradient";
			else if( entry is IPType )
				elementName = "Type";
			else if( entry is IPManagedObject )
				elementName = "ManagedObject";
			else
				elementName = "Unknown";

			XmlElement result = parent.OwnerDocument.CreateElement( elementName );
			entry.WriteToXmlElement( result );
			return result;
		}

		private IPObject ConvertXmlElementToObject( XmlElement element )
		{
			IPObject result;
			string elementName = element.Name;
			if( elementName == "Null" )
				result = new IPNull( this );
			else if( elementName == "Asset" )
				result = new IPAsset( this );
			else if( elementName == "SceneObject" )
				result = new IPSceneObject( this );
			else if( elementName == "AssetRef" )
				result = new IPAssetReference( this );
			else if( elementName == "SceneObjectRef" )
				result = new IPSceneObjectReference( this );
			else if( elementName == "Array" )
				result = new IPArray( this );
			else if( elementName == "Generic" )
				result = new IPGenericObject( this );
			else if( elementName == "Vector" )
				result = new IPVector( this );
			else if( elementName == "ManagedRef" )
				result = new IPManagedReference( this );
			else if( elementName == "Long" )
				result = new IPLong( this );
			else if( elementName == "Double" )
				result = new IPDouble( this );
			else if( elementName == "String" )
				result = new IPString( this );
			else if( elementName == "Curve" )
				result = new IPAnimationCurve( this );
			else if( elementName == "Gradient" )
				result = new IPGradient( this );
			else if( elementName == "Type" )
				result = new IPType( this );
			else if( elementName == "ManagedObject" )
				result = new IPManagedObject( this );
			else
				result = new IPNull( this );

			result.ReadFromXmlElement( element );
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

		private int GetIndexOfObjectInList<T>( T obj, ref List<T> list, bool addEntryIfNotExists ) where T : class
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

		private void CreateObjectArrayInChildXmlElement( XmlElement element, IPObject[] array, string childElementName )
		{
			if( array != null && array.Length > 0 )
			{
				XmlElement arrayRoot = element.OwnerDocument.CreateElement( childElementName );
				element.AppendChild( arrayRoot );
				CreateObjectArrayInXmlElement( arrayRoot, array );
			}
		}

		private void CreateObjectArrayInXmlElement( XmlElement element, IPObject[] array )
		{
			if( array != null && array.Length > 0 )
			{
				for( int i = 0; i < array.Length; i++ )
					element.AppendChild( ConvertObjectToXmlElement( array[i], element ) );
			}
		}

		private T[] ReadObjectArrayFromXmlElement<T>( XmlElement element ) where T : IPObject
		{
			XmlNodeList childNodes = element.ChildNodes;
			if( childNodes == null || childNodes.Count == 0 )
				return null;

			T[] result = new T[childNodes.Count];
			for( int i = 0; i < childNodes.Count; i++ )
				result[i] = (T) ConvertXmlElementToObject( (XmlElement) childNodes[i] );

			return result;
		}
		#endregion
	}
}