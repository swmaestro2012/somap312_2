using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;

using Windows.Devices.Input;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.UI;
using Windows.UI.Input;
using Windows.UI.Input.Inking;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Shapes;

/*
 * 12 11 05~06
 * 퍼포먼스를 해결해야 합니다.
 * 리팩토링
 * 
 * 12 11 07
 * 라인 두께, 길이 제한 설정
 * 터치 캔슬 펜 설정
 * isDoubleTapEnable = false
 * 
 * 12 11 08~11
 * 버티지 못하고 Direct X 적용 ㅜㅜ
 * 그리고 개고생 ㅜㅜ
 * 아무튼 지옥과도 같던 퍼포먼스의 활로를 뚫었습니다.
 * 지금부터는 베지어가 아니라 라인으로 그려짐
 * Manipulation 을 사용할지도 생각이 필요..
 * 실시간 렌더링은 부하를 충분히 견디지 못함
 * 일정 라인수가 되거나 일정 크기를 넘겨버리면 렌더링하는 알고리즘이 필요할듯.
 */

namespace PenTouch
{
	public sealed partial class CanvasEx : Grid
	{
		#region Field

		RectangleGeometry clipRect = new RectangleGeometry();

		Slider strokeThickness;
		Button strokeColor;

		private double Thickness
		{
			get { return strokeThickness.Value; }
		}

		private Brush Color
		{
			get { return strokeColor.Foreground ?? new SolidColorBrush(Colors.Black); }
		}

		ActionType actionType;

		Polygon palmBlock;
		int		palmSide;
		Line	palmTempLine;

		bool	doubleTouch;
		uint	pointID, point2ID;
		Point	pointPrev;

		SharpDX.Direct2D1.Device d2dDevice;
		SharpDX.Direct2D1.Factory1 d2dFactory;
		SharpDX.Direct2D1.DeviceContext d2dContext;
		
		SharpDX.Direct3D11.Device1 d3dDevice;
		SharpDX.Direct3D11.DeviceContext1 d3dContext;

		SharpDX.DXGI.Device2 dxgiDevice;
		
		Panel liveRender;

		#endregion Field

		#region Initialize

		public CanvasEx()
		{
			this.InitializeComponent();

			palmBlock = new Polygon();
			rootCanvas.Children.Add(palmBlock);
			palmBlock.StrokeThickness = 3;
			palmBlock.FillRule = FillRule.Nonzero;
			palmBlock.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0xff, 0, 0));

            //Network added start
            Network.connect();
            //Network added end

			Network.OnNetworkRecieved += Render;

			//Clip = clipRect;
		}

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY | ManipulationModes.TranslateInertia | ManipulationModes.Scale;

			tileBackground.ImageSource = "Assets/bgline.JPG";
			
			onePointWait.Visibility = Visibility.Collapsed;
			twoPointWait.Visibility = Visibility.Collapsed;

			double val = -Math.PI / 2;
			foreach (UIElement child in colorSelect.Children)
			{
				child.SetValue(Canvas.LeftProperty, Math.Cos(val) * 70);
				child.SetValue(Canvas.TopProperty, Math.Sin(val) * 70);
				val += Math.PI / 5;
			}

            var debugLevel = SharpDX.Direct2D1.DebugLevel.None;
            d2dFactory = new SharpDX.Direct2D1.Factory1(SharpDX.Direct2D1.FactoryType.SingleThreaded, debugLevel);

            var creationFlags = SharpDX.Direct3D11.DeviceCreationFlags.BgraSupport;

			using (var defaultDevice = new SharpDX.Direct3D11.Device(SharpDX.Direct3D.DriverType.Hardware, creationFlags))
				d3dDevice = defaultDevice.QueryInterface<SharpDX.Direct3D11.Device1>();

			d3dContext = d3dDevice.ImmediateContext.QueryInterface<SharpDX.Direct3D11.DeviceContext1>();

			using (dxgiDevice = d3dDevice.QueryInterface<SharpDX.DXGI.Device2>())
				d2dDevice = new SharpDX.Direct2D1.Device(d2dFactory, dxgiDevice);

			d2dContext = new SharpDX.Direct2D1.DeviceContext(d2dDevice, SharpDX.Direct2D1.DeviceContextOptions.None);

			DisplayProperties.LogicalDpiChanged += LogicalDpiChanged;
		}

		#endregion Initialize

		#region Util

		private double Distance(Point p1, Point p2)
		{
			return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
		}

		private double Angle(Point p1, Point p2)
		{
			return Math.Atan2(p2.X - p1.X, p2.Y - p1.Y);
		}

		private bool PointInPalmBlock(Point p)
		{
			if (palmBlock.Visibility == Visibility.Collapsed)
				return false;

			int i, j = palmBlock.Points.Count - 1;
			bool oddNodes = false;

			for (i = 0; i < palmBlock.Points.Count; i++)
			{
				if ((palmBlock.Points[i].Y < p.Y && palmBlock.Points[j].Y >= p.Y
				|| palmBlock.Points[j].Y < p.Y && palmBlock.Points[i].Y >= p.Y)
				&& (palmBlock.Points[i].X <= p.X || palmBlock.Points[j].X <= p.X))
				{
					oddNodes ^= (palmBlock.Points[i].X + (p.Y - palmBlock.Points[i].Y) / (palmBlock.Points[j].Y - palmBlock.Points[i].Y) * (palmBlock.Points[j].X - palmBlock.Points[i].X) < p.X);
				}

				j = i;
			}

			return oddNodes;
		}

		#endregion

		#region Action

		enum ActionType
		{
			None, Wait, Draw, Move, Palm, PenTouch
		}

		#region Palm Block

		public void PalmBlockSelect()
		{
			actionType = ActionType.Palm;
			palmBlock.Points.Clear();
		}

		//pt = e.GetCurrentPoint(palmBlock)
		private bool PalmBlockStart(PointerPoint pt)
		{
			if (actionType != ActionType.Palm)
				return false;

			palmBlock.Points.Clear();
			palmBlock.Points.Add(new Point(0, 0));
			palmBlock.Points.Add(pt.Position);

			palmTempLine = new Line();
			palmTempLine.StrokeDashArray.Add(5);
			palmTempLine.StrokeDashArray.Add(5);
			palmTempLine.Stroke = new SolidColorBrush(Colors.Black);
			palmTempLine.Margin = palmBlock.Margin;
			rootCanvas.Children.Add(palmTempLine);

			pointID = pt.PointerId;
			pointPrev = pt.Position;
			palmSide = 0;

			return true;
		}
		
		//pt = e.GetCurrentPoint(palmBlock)
		private bool PalmBlockMove(PointerPoint pt)
		{
			if (actionType != ActionType.Palm ||
				pointID != pt.PointerId ||
				palmTempLine == null)
				return false;

			if (palmSide == 0 && Distance(pointPrev, pt.Position) < 5)
				return true;

			bool angle = Angle(new Point(0, 0), palmBlock.Points[palmBlock.Points.Count - 1]) > Angle(new Point(0, 0), pt.Position);

			if (angle && palmSide >= 0)
			{
				if (palmSide == 0)
				{
					palmSide = 1;
					palmBlock.Points.Insert(1, new Point(pt.Position.X, 0));
				}

				//CW
				palmTempLine.X1 = pt.Position.X;
				palmTempLine.Y1 = pt.Position.Y;
				palmTempLine.X2 = 0;
				palmTempLine.Y2 = pt.Position.Y;

				palmBlock.Points.Add(pt.Position);
			}
			else if (!angle && palmSide <= 0)
			{
				if (palmSide == 0)
				{
					palmSide = -1;
					palmBlock.Points.Insert(1, new Point(0, pt.Position.Y));
				}

				//CCW
				palmTempLine.X1 = pt.Position.X;
				palmTempLine.Y1 = pt.Position.Y;
				palmTempLine.X2 = pt.Position.X;
				palmTempLine.Y2 = 0;

				palmBlock.Points.Add(pt.Position);
			}

			return true;
		}

		//pt = e.GetCurrentPoint(palmBlock)
		private bool PalmBlockEnd(PointerPoint pt)
		{
			if (actionType != ActionType.Palm ||
				pointID != pt.PointerId ||
				palmTempLine == null)
				return false;
			
			if (palmSide != 0)
			{
				rootCanvas.Children.Remove(palmTempLine);
				palmBlock.Points.Add(new Point(palmTempLine.X2, palmTempLine.Y2));
			}

			actionType = ActionType.None;
			palmTempLine = null;
			
			return true;
		}

		#endregion

		#region Drawing

		float pressurePrev;

		//pt = e.GetCurruntPoint(canvas);
		private bool DrawStart(PointerPoint pt)
		{
			if (actionType != ActionType.None && actionType != ActionType.Move && actionType != ActionType.PenTouch ||
				pt.PointerDevice.PointerDeviceType != PointerDeviceType.Pen && 
				(pt.PointerDevice.PointerDeviceType != PointerDeviceType.Mouse || !pt.Properties.IsLeftButtonPressed))
				return false;

			PenTouchEnd();

			actionType = ActionType.Draw;

			pointID = pt.PointerId;
			pointPrev = pt.Position;

			pressurePrev = pt.Properties.Pressure;

			RenderStart();

			return true;
		}

		//pt = e.GetCurruntPoint(canvas);
		private bool DrawMove(PointerPoint pt)
		{
			if (actionType != ActionType.Draw ||
				pt.PointerId != pointID ||
				liveRender == null)
				return false;

			if (Distance(pointPrev, pt.Position) >= 2)
			{
				liveRender.Children.Add(MakeLine(pointPrev, pt.Position, pt.Properties.Pressure * Thickness, Color));

				pointPrev = pt.Position;
				pressurePrev = pt.Properties.Pressure;
			}
		
			return true;
		}

		//pt = e.GetCurruntPoint(canvas);
		private bool DrawEnd(PointerPoint pt)
		{
			if (actionType != ActionType.Draw ||
				pointID != pt.PointerId ||
				liveRender == null)
				return false;

			actionType = ActionType.None;

			RenderEnd();

			return true;
		}

		private void RenderStart()
		{
			liveRender = new Canvas();
			canvas.Children.Add(liveRender);
		}

		private Line MakeLine(Point p1, Point p2, double thick, Brush color)
		{
			Line l = new Line()
			{
				X1 = p1.X,
				Y1 = p1.Y,
				X2 = p2.X,
				Y2 = p2.Y,
				StrokeThickness = thick,
				Stroke = color,
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				StrokeLineJoin = PenLineJoin.Round,
			};

			return l;
		}

		private void Render(List<UIElement> points)
		{
			List<UIElement> directXPoints = new List<UIElement>();

			Line prev = null;

			foreach (var pt in points)
			{
				if (pt is Line == false)
					continue;

				Line now = pt as Line;

				if (prev == null)
					prev = now;

				int n = 1;
				double pressure = now.StrokeThickness - prev.StrokeThickness;
				Point pos = new Point(now.X2 - now.X1, now.Y2 - now.Y1);

				while (Math.Abs(pressure / n) >= 0.5)
					++n;

				for (int i = 1; i <= n; ++i)
				{
					Line l = MakeLine(
						new Point(now.X1 + pos.X * (i - 1) / n, now.Y1 + pos.Y * (i - 1) / n),
						new Point(now.X1 + pos.X * i / n, now.Y1 + pos.Y * i / n),
						prev.StrokeThickness + pressure * i / n, now.Stroke);
					
					directXPoints.Add(l);
				}

				prev = now;
			}

			DirectXRender(directXPoints);
		}

		private void DirectXRender(List<UIElement> points)
		{
			if (points.Count <= 0)
				return;

			Line initLine = points[0] as Line;

			Point
				p1 = new Point
				(
					Math.Min(initLine.X1 - initLine.StrokeThickness / 2, initLine.X2 - initLine.StrokeThickness / 2),
					Math.Min(initLine.Y1 - initLine.StrokeThickness / 2, initLine.Y2 - initLine.StrokeThickness / 2)
				),
				p2 = new Point
				(
					Math.Max(initLine.X1 + initLine.StrokeThickness / 2, initLine.X2 + initLine.StrokeThickness / 2),
					Math.Max(initLine.Y1 + initLine.StrokeThickness / 2, initLine.Y2 + initLine.StrokeThickness / 2)
				);

			foreach (var child in points)
			{
				var line = child as Line;

				if (line == null)
					continue;

				if (p1.X > line.X1 - line.StrokeThickness / 2)
					p1.X = line.X1 - line.StrokeThickness / 2;
				if (p1.X > line.X2 - line.StrokeThickness / 2)
					p1.X = line.X2 - line.StrokeThickness / 2;

				if (p2.X < line.X1 + line.StrokeThickness / 2)
					p2.X = line.X1 + line.StrokeThickness / 2;
				if (p2.X < line.X2 + line.StrokeThickness / 2)
					p2.X = line.X2 + line.StrokeThickness / 2;

				if (p1.Y > line.Y1 - line.StrokeThickness / 2)
					p1.Y = line.Y1 - line.StrokeThickness / 2;
				if (p1.Y > line.Y2 - line.StrokeThickness / 2)
					p1.Y = line.Y2 - line.StrokeThickness / 2;

				if (p2.Y < line.Y1 + line.StrokeThickness / 2)
					p2.Y = line.Y1 + line.StrokeThickness / 2;
				if (p2.Y < line.Y2 + line.StrokeThickness / 2)
					p2.Y = line.Y2 + line.StrokeThickness / 2;
			}

			var bndRect = new Rect(p1, p2);

			var dxTarget = new SurfaceImageSource
			(
				(int)(bndRect.Width * DisplayProperties.LogicalDpi / 96.0 + 1),
				(int)(bndRect.Height * DisplayProperties.LogicalDpi / 96.0 + 1)
			);

			SharpDX.DXGI.ISurfaceImageSourceNative dxTargetNative = SharpDX.ComObject.As<SharpDX.DXGI.ISurfaceImageSourceNative>(dxTarget);
			dxTargetNative.Device = d3dDevice.QueryInterface<SharpDX.DXGI.Device>();

			/*
			 * Draw Logic
			 */
			SharpDX.DrawingPoint drawingPoint;
			var surface = dxTargetNative.BeginDraw(new SharpDX.Rectangle(0, 0,
				(int)(bndRect.Width * DisplayProperties.LogicalDpi / 96.0 + 1),
				(int)(bndRect.Height * DisplayProperties.LogicalDpi / 96.0 + 1)),
				out drawingPoint);

			var dxRenderTarget = new SharpDX.Direct2D1.RenderTarget(d2dFactory, surface, new SharpDX.Direct2D1.RenderTargetProperties()
			{
				DpiX = DisplayProperties.LogicalDpi,
				DpiY = DisplayProperties.LogicalDpi,
				PixelFormat = new SharpDX.Direct2D1.PixelFormat(SharpDX.DXGI.Format.B8G8R8A8_UNorm, SharpDX.Direct2D1.AlphaMode.Premultiplied),
				Type = SharpDX.Direct2D1.RenderTargetType.Default,
				Usage = SharpDX.Direct2D1.RenderTargetUsage.None
			});

			dxRenderTarget.BeginDraw();
			dxRenderTarget.Clear(SharpDX.Color.Transparent);

			foreach (var child in points)
			{
				var line = child as Line;

				if (line == null)
					continue;

				Color c = (line.Stroke as SolidColorBrush).Color;
				var brush = new SharpDX.Direct2D1.SolidColorBrush(dxRenderTarget, new SharpDX.Color(c.R, c.G, c.B, c.A));

				var style = new SharpDX.Direct2D1.StrokeStyleProperties();
				style.LineJoin = SharpDX.Direct2D1.LineJoin.Round;
				style.StartCap = SharpDX.Direct2D1.CapStyle.Round;
				style.EndCap = SharpDX.Direct2D1.CapStyle.Round;
				var stroke = new SharpDX.Direct2D1.StrokeStyle(d2dFactory, style);

				dxRenderTarget.DrawLine(
					new SharpDX.DrawingPointF((float)(line.X1 - bndRect.Left), (float)(line.Y1 - bndRect.Top)),
					new SharpDX.DrawingPointF((float)(line.X2 - bndRect.Left), (float)(line.Y2 - bndRect.Top)),
					brush, (float)line.StrokeThickness, stroke);
			}

			dxRenderTarget.EndDraw();
			dxTargetNative.EndDraw();

			var dxImage = new Image();
			dxImage.Source = dxTarget;
			canvas.Children.Add(dxImage);
			Canvas.SetLeft(dxImage, bndRect.X);
			Canvas.SetTop(dxImage, bndRect.Y);
		}

		private void RenderEnd()
		{
			Render(liveRender.Children.ToList());
			Network.sendData(liveRender.Children.ToList());

			liveRender.Children.Clear();
			canvas.Children.Remove(liveRender);
			liveRender = null;
		}

		#endregion Drawing

		#region Waiting, Moving and Scaling

		//pt = e.GetCurruntPoint(rootCanvas);
		private bool WaitStart(PointerPoint pt)
		{
			if (actionType != ActionType.None && actionType != ActionType.Wait && actionType != ActionType.PenTouch ||
				pt.PointerDevice.PointerDeviceType != PointerDeviceType.Touch &&
				(pt.PointerDevice.PointerDeviceType != PointerDeviceType.Mouse || !pt.Properties.IsRightButtonPressed) ||
				doubleTouch)
				return false;

			if (PointInPalmBlock(new Point(pt.Position.X - palmBlock.Margin.Left, pt.Position.Y - palmBlock.Margin.Top)))
				return false;

			if (actionType == ActionType.Wait)
			{
				doubleTouch = true;
				
				point2ID = pt.PointerId;

				twoPointWait.Visibility = Visibility.Visible;
				onePointWait.Visibility = Visibility.Collapsed;
				twoPointWait.Opacity = 0.5;
				twoPointWait.Margin = new Thickness((pointPrev.X + pt.Position.X) / 2, (pointPrev.Y + pt.Position.Y) / 2, 0, 0);

				strokeSelect.Width = strokeSelect.Height = 40;
				Canvas.SetLeft(strokeSelect, -strokeSelect.Width / 2);
				Canvas.SetTop(strokeSelect, -strokeSelect.Height / 2); 
				twoPointButtons.Visibility = Visibility.Visible;

				return true;
			}
			else
			{
				doubleTouch = false;

				actionType = ActionType.Wait;
				pointID = pt.PointerId;
				pointPrev = pt.Position;

				twoPointWait.Visibility = Visibility.Collapsed;
				onePointWait.Visibility = Visibility.Visible;
				onePointWait.Opacity = 0.5;
				onePointWait.Margin = new Thickness(pt.Position.X, pt.Position.Y, 0, 0);

				colorSelect.Visibility = Visibility.Collapsed;
				drawShape.Visibility = Visibility.Collapsed;
				onePointButtons.Visibility = Visibility.Visible;
			}

			return true;
		}

		private bool MovingMove(ManipulationDeltaRoutedEventArgs e)
		{
			if (actionType != ActionType.Wait && actionType != ActionType.Move)
				return false;
			
			if (actionType == ActionType.Wait)
				if (Math.Sqrt(Math.Pow(e.Cumulative.Translation.X, 2) + Math.Pow(e.Cumulative.Translation.Y, 2)) >= 5)
				{
					onePointWait.Visibility = Visibility.Collapsed;
					twoPointWait.Visibility = Visibility.Collapsed;
					actionType = ActionType.Move;
				}

			Point delta = e.Delta.Translation;

			translate.X += delta.X;
			translate.Y += delta.Y;
		
			return true;
		}

		private bool ScalingMove(ManipulationDeltaRoutedEventArgs e)
		{
			if (!doubleTouch)
				return false;

			if (actionType == ActionType.Wait)
				if (Math.Abs(e.Cumulative.Scale - 1) >= 0.1)
				{
					onePointWait.Visibility = Visibility.Collapsed;
					twoPointWait.Visibility = Visibility.Collapsed;
					actionType = ActionType.Move;
				}

			Point t = e.Position;
			double delta = e.Delta.Scale;

			if (delta * scale.ScaleX > 1.5)
				delta = 1.5 / scale.ScaleX;
			if (delta * scale.ScaleX < 1)
				delta = 1 / scale.ScaleX;

			t.X -= t.X * (1 / delta);
			t.Y -= t.Y * (1 / delta);
			
			translate.X -= t.X;
			translate.Y -= t.Y;

			translate.X *= delta;
			translate.Y *= delta;

			scale.ScaleX *= delta;
			scale.ScaleY *= delta;

			return true;
		}
		
		//pt = e.GetCurruntPoint(rootCanvas);
		private bool WaitEnd(PointerPoint pt)
		{
			if (actionType != ActionType.Wait)
				return false;

			if (doubleTouch)
			{
				doubleTouch = false;

				if (point2ID == pt.PointerId)
					pointID = pt.PointerId;

				return true;
			}

			if (pt.PointerId != pointID)
				return false;

			actionType = ActionType.None;

			onePointWait.Visibility = Visibility.Collapsed;
			twoPointWait.Visibility = Visibility.Collapsed;
			
			return true;
		}

		//pt = e.GetCurruntPoint(rootCanvas);
		private bool PenTouchStart(PointerPoint pt)
		{
			if (actionType != ActionType.Wait ||
				pt.PointerId != pointID)
				return false;

			actionType = ActionType.PenTouch;

			onePointWait.Opacity = 1;
			twoPointWait.Opacity = 1;

			return true;
		}

		private void PenTouchEnd(object sender = null, object e = null)
		{
			if (actionType == ActionType.PenTouch)
				actionType = ActionType.None;

			doubleTouch = false;

			twoPointWait.Visibility = Visibility.Collapsed;
			onePointWait.Visibility = Visibility.Collapsed;
		}

		private void OnColorSelectPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (e.Pointer.PointerDeviceType != PointerDeviceType.Pen)
				return;

			e.Handled = true;

			onePointButtons.Visibility = Visibility.Collapsed;
			colorSelect.Visibility = Visibility.Visible;
			//penTouchType = PenTouchType.ColorSelect;
		}

		private void OnColorSelectPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			if (e.Pointer.PointerDeviceType != PointerDeviceType.Pen)
				return;

			Shape s = sender as Shape;

			if (s == null)
				return;

			e.Handled = true;

			strokeColor.Foreground = s.Fill;

			PenTouchEnd();
		}

		private void OnDrawShapePointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (e.Pointer.PointerDeviceType != PointerDeviceType.Pen)
				return;

			e.Handled = true;

			onePointButtons.Visibility = Visibility.Collapsed;
			drawShape.Visibility = Visibility.Visible;

			drawShape.CapturePointer(e.Pointer);

			Line line = new Line()
			{
				X1 = 0,
				Y1 = 0,
				X2 = 0,
				Y2 = 0,
				StrokeThickness = Thickness * 0.5 * scale.ScaleX,
				Stroke = Color,
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				StrokeLineJoin = PenLineJoin.Round,
				StrokeDashArray = { 2, 2 },
			};

			drawShape.Children.Add(line);
		}

		private void OnDrawShapePointerMoved(object sender, PointerRoutedEventArgs e)
		{
			e.Handled = true;

			if (drawShape.PointerCaptures != null && e.Pointer.IsInContact && drawShape.Children.Count > 0)
			{
				var shape = drawShape.Children.Last();

				if (shape is Line)
				{
					var line = shape as Line;
					var pt = e.GetCurrentPoint(drawShape);

					line.X2 = pt.Position.X;
					line.Y2 = pt.Position.Y;
				}
			}
		}

		private void OnDrawShapePointerReleased(object sender, PointerRoutedEventArgs e)
		{
			e.Handled = true;

			List<UIElement> list = new List<UIElement>();

			foreach (var child in drawShape.Children)
				if (child is Line)
				{
					Point point;
					var line = child as Line;

					point = canvas.RenderTransform.Inverse.TransformPoint(drawShape.RenderTransform.TransformPoint(new Point(line.X1, line.Y1)));
					line.X1 = point.X + (onePointWait.Margin.Left + Canvas.GetLeft(drawShape)) / scale.ScaleX;
					line.Y1 = point.Y + (onePointWait.Margin.Top + Canvas.GetTop(drawShape)) / scale.ScaleY;

					point = canvas.RenderTransform.Inverse.TransformPoint(drawShape.RenderTransform.TransformPoint(new Point(line.X2, line.Y2)));
					line.X2 = point.X + (onePointWait.Margin.Left + Canvas.GetLeft(drawShape)) / scale.ScaleX;
					line.Y2 = point.Y + (onePointWait.Margin.Top + Canvas.GetTop(drawShape)) / scale.ScaleY;

					list.Add(line);
				}

			Render(list);
			Network.sendData(list);

			drawShape.Children.Clear();
			drawShape.ReleasePointerCaptures();

			PenTouchEnd();
		}

		private void OnThicknessSelectPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (e.Pointer.PointerDeviceType != PointerDeviceType.Pen)
				return;

			e.Handled = true;

			strokeSelect.CapturePointer(e.Pointer);
			pointPrev = e.GetCurrentPoint(rootCanvas).Position;
		}

		private void OnThicknessSelectPointerMoved(object sender, PointerRoutedEventArgs e)
		{
			e.Handled = true;

			if (strokeSelect.PointerCaptures != null && e.Pointer.IsInContact)
			{
				double value = Thickness + (e.GetCurrentPoint(rootCanvas).Position.Y - pointPrev.Y) / 10;

				if (value > 40)
					value = 40;
				if (value < 1)
					value = 1;

				strokeSelect.Width = strokeSelect.Height = value;
				Canvas.SetLeft(strokeSelect, -strokeSelect.Width / 2);
				Canvas.SetTop(strokeSelect, -strokeSelect.Height / 2);
			}
		}

		private void OnThicknessSelectPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			e.Handled = true;

			strokeSelect.ReleasePointerCaptures();

			strokeThickness.Value = (int)strokeSelect.Width;

			PenTouchEnd();
		}

		#endregion Wating, Moving and Scaling

		#endregion Action

		#region Event

		private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			e.Handled = true;

			if (PalmBlockStart(e.GetCurrentPoint(palmBlock)))
				return;

			if (DrawStart(e.GetCurrentPoint(canvas)))
				return;
			
			if (WaitStart(e.GetCurrentPoint(rootCanvas)))
				return;
		}

		private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
		{
			e.Handled = true;

			if (PalmBlockMove(e.GetCurrentPoint(palmBlock)))
				return;

			if (DrawMove(e.GetCurrentPoint(canvas)))
				return;
		}

		private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			e.Handled = true;

			if (PalmBlockEnd(e.GetCurrentPoint(palmBlock)))
				return;

			if (DrawEnd(e.GetCurrentPoint(canvas)))
				return;

			if (WaitEnd(e.GetCurrentPoint(rootCanvas)))
				return;
			
			if (e.Pointer.PointerDeviceType == PointerDeviceType.Pen)
				PenTouchEnd();
		}

		private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
		{
			e.Handled = true;

			if (PalmBlockEnd(e.GetCurrentPoint(palmBlock)))
				return;

			if (DrawEnd(e.GetCurrentPoint(canvas)))
				return;

			if (PenTouchStart(e.GetCurrentPoint(rootCanvas)))
				return;

			if (e.Pointer.PointerDeviceType == PointerDeviceType.Pen)
				PenTouchEnd();
		}

		private void OnManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
		{
			e.Handled = true;

			if (e.PointerDeviceType != PointerDeviceType.Touch &&
				e.PointerDeviceType != PointerDeviceType.Mouse ||
				PointInPalmBlock(new Point(e.Position.X - palmBlock.Margin.Left, e.Position.Y - palmBlock.Margin.Top)))
				e.Complete();
		}

		private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
		{
			e.Handled = true;
			
			MovingMove(e);
			ScalingMove(e);
		}

		private void OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
		{
			e.Handled = true;

			if (actionType == ActionType.Move)
			{
				doubleTouch = false;
				actionType = ActionType.None;
			}
		}

		private void OnSizeChanged(object sender, SizeChangedEventArgs e)
		{
			palmBlock.Margin = new Thickness(e.NewSize.Width, e.NewSize.Height, 0, 0);
			clipRect.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);

			if (ApplicationView.Value == ApplicationViewState.Snapped)
				palmBlock.Visibility = Visibility.Collapsed;
			else
				palmBlock.Visibility = Visibility.Visible;
		}

		void LogicalDpiChanged(object sender)
		{
			d2dContext.DotsPerInch = new SharpDX.DrawingSizeF(DisplayProperties.LogicalDpi, DisplayProperties.LogicalDpi);
		}

		public void Clear()
		{
			canvas.Children.Clear();
		}

		public void Undo()
		{
			if (canvas.Children.Count > 0)
				canvas.Children.Remove(canvas.Children.Last());
		}

		public void SetPenColor(Button value)
		{
			strokeColor = value;
		}

		public void SetPenThickness(Slider value)
		{
			strokeThickness = value;
		}

		#endregion Event
	}
}
