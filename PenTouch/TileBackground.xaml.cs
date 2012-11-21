using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// 사용자 정의 컨트롤 항목 템플릿에 대한 설명은 http://go.microsoft.com/fwlink/?LinkId=234236에 나와 있습니다.

namespace PenTouch
{
	public sealed partial class TileBackground : UserControl
	{
		Image[,] images;

		public TileBackground()
		{
			this.InitializeComponent();

			SizeChanged += OnSizeChanged;
		}
		
		public static readonly DependencyProperty ImageSourceProperty =
			DependencyProperty.Register("ImageSource", typeof(string), typeof(TileBackground), new PropertyMetadata(null, OnImageSourceChanged));

		public string ImageSource
		{
			get { return GetValue(ImageSourceProperty) as string; }
			set { SetValue(ImageSourceProperty, value); }
		}

		private static void OnImageSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
		{
			(sender as TileBackground).Refresh();
		}

		private void OnSizeChanged(object sender, SizeChangedEventArgs e)
		{
			Refresh();
		}

		async private void Refresh()
		{
			screen.Children.Clear();

			if (ImageSource == null)
				return;

			BitmapImage bmp = new BitmapImage();
			bmp.SetSource(await (await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///" + ImageSource))).OpenAsync(FileAccessMode.Read));
			
			int col = (int)ActualWidth / bmp.PixelWidth + 1, row = (int)ActualHeight / bmp.PixelHeight + 1;
			images = new Image[col, row];

			for (int i = 0; i < col; ++i)
				for (int j = 0; j < row; ++j)
				{
					images[i, j] = new Image();
					images[i, j].Source = bmp;

					screen.Children.Add(images[i, j]);
					Canvas.SetLeft(images[i, j], i * bmp.PixelWidth);
					Canvas.SetTop(images[i, j], j * bmp.PixelHeight);
				}
		}
	}
}
