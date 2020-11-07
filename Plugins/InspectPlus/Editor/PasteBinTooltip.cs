using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace InspectPlusNamespace
{
	public class PasteBinTooltip : EditorWindow
	{
		private static PasteBinTooltip mainWindow;
		private static string tooltip;

		private static GUIStyle m_style;
		internal static GUIStyle Style
		{
			get
			{
				if( m_style == null )
					m_style = (GUIStyle) typeof( EditorStyles ).GetProperty( "tooltip", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static ).GetValue( null, null );

				return m_style;
			}
		}

		public static void Show( Rect sourcePosition, string tooltip )
		{
			Vector2 preferredSize = Style.CalcSize( new GUIContent( tooltip ) ) + Style.contentOffset + new Vector2( Style.padding.horizontal + Style.margin.horizontal, Style.padding.vertical + Style.margin.vertical );
			Rect preferredPosition;

			Rect positionLeft = new Rect( sourcePosition.position - new Vector2( preferredSize.x, 0f ), preferredSize );
			Rect screenFittedPositionLeft = Utilities.GetScreenFittedRect( positionLeft );

			Vector2 positionOffset = positionLeft.position - screenFittedPositionLeft.position;
			Vector2 sizeOffset = positionLeft.size - screenFittedPositionLeft.size;
			if( positionOffset.sqrMagnitude <= 400f && sizeOffset.sqrMagnitude <= 400f )
				preferredPosition = screenFittedPositionLeft;
			else
			{
				Rect positionRight = new Rect( sourcePosition.position + new Vector2( sourcePosition.width, 0f ), preferredSize );
				Rect screenFittedPositionRight = Utilities.GetScreenFittedRect( positionRight );

				Vector2 positionOffset2 = positionRight.position - screenFittedPositionRight.position;
				Vector2 sizeOffset2 = positionRight.size - screenFittedPositionRight.size;
				if( positionOffset2.magnitude + sizeOffset2.magnitude < positionOffset.magnitude + sizeOffset.magnitude )
					preferredPosition = screenFittedPositionRight;
				else
					preferredPosition = screenFittedPositionLeft;
			}

			// Don't lose focus to the previous window (in this case, PasteBinContextWindow which automatically closes when it loses focus)
			EditorWindow prevFocusedWindow = focusedWindow;

			if( !mainWindow )
			{
				mainWindow = CreateInstance<PasteBinTooltip>();
				mainWindow.ShowPopup();
			}

			mainWindow.position = preferredPosition;
			PasteBinTooltip.tooltip = tooltip;

			prevFocusedWindow.Focus();
		}

		public static void Hide()
		{
			if( mainWindow )
			{
				mainWindow.Close();
				mainWindow = null;
			}
		}

		private void OnGUI()
		{
			GUI.Label( new Rect( Vector2.zero, position.size ), tooltip, Style );
		}
	}
}