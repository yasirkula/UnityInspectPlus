using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InspectPlusNamespace
{
	public class ObjectBrowserWindow : EditorWindow
	{
		public class NameComparer : IComparer<Object>
		{
			public int Compare( Object x, Object y )
			{
				return EditorUtility.NaturalCompare( x.name, y.name );
			}
		}

		public class TypeComparer : IComparer<Object>
		{
			public int Compare( Object x, Object y )
			{
				if( x == y )
					return 0;

				System.Type type1 = x.GetType();
				System.Type type2 = y.GetType();

				Texture2D preview1 = AssetPreview.GetMiniTypeThumbnail( type1 );
				Texture2D preview2 = AssetPreview.GetMiniTypeThumbnail( type2 );

				// 1. Compare Type thumbnails
				int result;
				if( preview1 != null && preview2 != null )
				{
					result = preview1.GetInstanceID().CompareTo( preview2.GetInstanceID() );
					if( result != 0 )
						return result;
				}

				preview1 = AssetPreview.GetMiniThumbnail( x );
				preview2 = AssetPreview.GetMiniThumbnail( y );

				// 2. Compare object thumbnails
				if( preview1 != null && preview2 != null )
				{
					result = preview1.GetInstanceID().CompareTo( preview2.GetInstanceID() );
					if( result != 0 )
						return result;
				}

				// 3. Compare Type names
				result = type1.Name.CompareTo( type2.Name );
				if( result != 0 )
					return result;

				// 4. Compare object names
				return EditorUtility.NaturalCompare( x.name, y.name );
			}
		}

		public enum SortType { None = 0, Name = 1, Type = 2 };

		private static readonly Color activeButtonColorLightSkin = new Color32( 245, 170, 10, 255 );
		private static readonly Color activeButtonColorDarkSkin = new Color32( 100, 65, 0, 255 );
		private static readonly Color nonFavoriteObjectIconColor = new Color32( 32, 32, 32, 255 );

		private GUIStyle buttonStyle;

		private List<Object> objects;
		private Object mainObject;
		internal HashSet<Object> favoriteObjects;

		private SortType sortType;

		private GUIContent addToFavoritesIcon, removeFromFavoritesIcon;
		private Vector2 scrollPosition;

		public delegate bool ObjectClickedDelegate( Object obj );
		private ObjectClickedDelegate onObjectLeftClicked;
		private ObjectClickedDelegate onObjectRightClicked;
		private ObjectClickedDelegate onObjectRemoved;

		public delegate void FavoriteStateChangedDelegate( Object obj, bool isFavorite );
		private FavoriteStateChangedDelegate onObjectFavoriteStateChanged;

		public delegate void WindowClosedDelegate( SortType sortType );
		private WindowClosedDelegate onWindowClosed;

		public void Initialize( List<Object> objects, HashSet<Object> favoriteObjects, Object mainObject, SortType sortType, ObjectClickedDelegate onObjectLeftClicked, ObjectClickedDelegate onObjectRightClicked, ObjectClickedDelegate onObjectRemoved, FavoriteStateChangedDelegate onObjectFavoriteStateChanged, WindowClosedDelegate onWindowClosed )
		{
			this.objects = objects;
			this.favoriteObjects = favoriteObjects;
			this.mainObject = mainObject;
			this.sortType = sortType;
			this.onObjectLeftClicked = onObjectLeftClicked;
			this.onObjectRightClicked = onObjectRightClicked;
			this.onObjectRemoved = onObjectRemoved;
			this.onObjectFavoriteStateChanged = onObjectFavoriteStateChanged;
			this.onWindowClosed = onWindowClosed;

			addToFavoritesIcon = new GUIContent( EditorGUIUtility.IconContent( "Favorite Icon" ).image, "Add to Favorites" );
			removeFromFavoritesIcon = new GUIContent( addToFavoritesIcon.image, "Remove from Favorites" );

			SortObjects();
		}

		private void OnDestroy()
		{
			try
			{
				if( onWindowClosed != null )
					onWindowClosed( sortType );
			}
			finally
			{
				objects = null;
				favoriteObjects = null;
				mainObject = null;
				buttonStyle = null;
				addToFavoritesIcon = null;
				removeFromFavoritesIcon = null;
				onObjectLeftClicked = null;
				onObjectRightClicked = null;
				onObjectRemoved = null;
				onObjectFavoriteStateChanged = null;
				onWindowClosed = null;
			}
		}

		private void OnGUI()
		{
			if( objects == null )
				return;

			if( buttonStyle == null )
				buttonStyle = new GUIStyle( EditorStyles.label ) { padding = new RectOffset( 0, 0, 0, 0 ) };

			Rect rect = new Rect( Vector2.zero, position.size );

			// Draw borders around the window
			if( Event.current.type == EventType.Repaint )
				EditorStyles.helpBox.Draw( rect, false, false, false, false );

			rect.height -= EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			scrollPosition = GUI.BeginScrollView( rect, scrollPosition, new Rect( 0f, 0f, rect.width, objects.Count * ( EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing ) + 5f ), false, false, GUIStyle.none, GUI.skin.verticalScrollbar );

			Rect favoritesIconRect = new Rect( EditorGUIUtility.standardVerticalSpacing, EditorGUIUtility.standardVerticalSpacing, EditorGUIUtility.singleLineHeight, EditorGUIUtility.singleLineHeight );
			Rect buttonRect = new Rect( favoritesIconRect.x + favoritesIconRect.width + 4f, favoritesIconRect.y, rect.width - 2f * EditorGUIUtility.standardVerticalSpacing - favoritesIconRect.width - 4f, favoritesIconRect.height );

			Color contentColor = GUI.contentColor;

			for( int i = 0; i < objects.Count; i++ )
			{
				if( ReferenceEquals( objects[i], null ) )
				{
					objects.RemoveAt( i );
					GUIUtility.ExitGUI();
				}

				if( objects[i] == mainObject )
				{
					Rect backgroundRect = buttonRect;
					backgroundRect.y -= EditorGUIUtility.standardVerticalSpacing * 0.5f;
					backgroundRect.height += EditorGUIUtility.standardVerticalSpacing;

					EditorGUI.DrawRect( backgroundRect, EditorGUIUtility.isProSkin ? activeButtonColorDarkSkin : activeButtonColorLightSkin );
				}

				bool isObjectFavorite = favoriteObjects.Contains( objects[i] );
				if( isObjectFavorite )
					GUI.contentColor = Color.white;
				else
					GUI.contentColor = nonFavoriteObjectIconColor;

				if( GUI.Button( favoritesIconRect, isObjectFavorite ? removeFromFavoritesIcon : addToFavoritesIcon, buttonStyle ) )
				{
					if( isObjectFavorite )
						favoriteObjects.Remove( objects[i] );
					else
						favoriteObjects.Add( objects[i] );

					if( onObjectFavoriteStateChanged != null )
						onObjectFavoriteStateChanged( objects[i], !isObjectFavorite );
				}

				GUI.contentColor = contentColor;

				if( GUI.Button( buttonRect, EditorGUIUtility.ObjectContent( objects[i], objects[i].GetType() ), buttonStyle ) )
				{
					if( Event.current.button == 0 )
					{
						if( onObjectLeftClicked == null || onObjectLeftClicked( objects[i] ) )
						{
							Close();
							GUIUtility.ExitGUI();
						}
					}
					else if( Event.current.button == 1 )
					{
						if( onObjectRightClicked != null )
							onObjectRightClicked( objects[i] );
					}
					else if( Event.current.button == 2 )
					{
						if( onObjectRemoved != null && onObjectRemoved( objects[i] ) )
						{
							RemoveObjectFromList( objects[i] );
							GUIUtility.ExitGUI();
						}
					}
				}

				favoritesIconRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
				buttonRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			GUI.EndScrollView();

			rect.y += rect.height;
			rect.height = EditorGUIUtility.singleLineHeight;

			rect.x += EditorGUIUtility.standardVerticalSpacing;
			rect.width -= 2f * EditorGUIUtility.standardVerticalSpacing;

			float originalLabelWidth = EditorGUIUtility.labelWidth;
			EditorGUIUtility.labelWidth = 75f;

			EditorGUI.BeginChangeCheck();

			sortType = (SortType) EditorGUI.EnumPopup( rect, "Sort by:", sortType );
			if( EditorGUI.EndChangeCheck() )
				SortObjects();

			EditorGUIUtility.labelWidth = originalLabelWidth;
		}

		public void RemoveObjectFromList( Object obj )
		{
			if( obj != null && objects != null && objects.Remove( obj ) )
			{
				if( objects.Count == 0 )
					Close();
				else
				{
					Vector2 size = position.size;
					size.y -= EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
					minSize = size;
					maxSize = size;
				}
			}
		}

		private void SortObjects()
		{
			if( objects == null )
				return;

			if( sortType == SortType.Name )
				objects.Sort( new NameComparer() );
			else if( sortType == SortType.Type )
				objects.Sort( new TypeComparer() );
		}
	}
}