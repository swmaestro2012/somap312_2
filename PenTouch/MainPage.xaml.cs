using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;

using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// 빈 페이지 항목 템플릿에 대한 설명은 http://go.microsoft.com/fwlink/?LinkId=234238에 나와 있습니다.

namespace PenTouch
{
	/// <summary>
	/// 자체에서 사용하거나 프레임 내에서 탐색할 수 있는 빈 페이지입니다.
	/// </summary>
	public sealed partial class MainPage : Page
	{
		public MainPage()
		{
			this.InitializeComponent();
		}

		/// <summary>
		/// 이 페이지가 프레임에 표시되려고 할 때 호출됩니다.
		/// </summary>
		/// <param name="e">페이지에 도달한 방법을 설명하는 이벤트 데이터입니다. Parameter
		/// 속성은 일반적으로 페이지를 구성하는 데 사용됩니다.</param>
		protected override void OnNavigatedTo(NavigationEventArgs e)
		{
			mainCanvas.SetPenColor(colorSelectButton);
			mainCanvas.SetPenThickness(thickSelectSlider);

			colorSelectButton.Foreground = null;
		}

		private void ClearButtonClicked(object sender, RoutedEventArgs e)
		{
			mainCanvas.Clear();
		}

		private void UndoButtonClicked(object sender, RoutedEventArgs e)
		{
			mainCanvas.Undo();
		}

		private void ColorSelectButtonClicked(object sender, RoutedEventArgs e)
		{
			colorSelectPopup.IsOpen = true;
		}

		private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (colorSelectPopup != null)
				colorSelectPopup.IsOpen = false;
		}

		private void ThickSelectButtonClicked(object sender, RoutedEventArgs e)
		{
			thickSelectPopup.IsOpen = true;
		}

		private void ThicknessChanged(object sender, RangeBaseValueChangedEventArgs e)
		{

		}

		private void PalmBlockButtonClicked(object sender, RoutedEventArgs e)
		{
			mainCanvas.PalmBlockSelect();
			bottomAppBar.IsOpen = false;
		}
	}
}
 