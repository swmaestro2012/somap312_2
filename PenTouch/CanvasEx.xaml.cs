#define NETWORK

using System;
using System.Diagnostics;
using System.Linq;

using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Input;
using Windows.UI.Input.Inking;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace PenTouch
{
	public sealed partial class CanvasEx : Grid
	{
		RectangleGeometry clipRect = new RectangleGeometry();

		InkManager				inkManager;
		InkDrawingAttributes	inkAttr;

		Polygon palmBlock;
		int palmSide;
		Line palmTempLine;

		enum ActionType
		{
			None, Drawing, Moving, Scaling, Palm
		}

		ActionType	actionType;
		uint		pointID;
		Point		prevPoint;
		int			zoomLevel;

		public CanvasEx()
		{
			this.InitializeComponent();

			palmBlock = new Polygon();
			rootCanvas.Children.Add(palmBlock);
			palmBlock.StrokeThickness = 3;
			palmBlock.FillRule = FillRule.Nonzero;
			palmBlock.Fill = new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0, 0));

			Clip = clipRect;
			zoomLevel = 0;
		}

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			inkAttr = new InkDrawingAttributes();
			inkAttr.IgnorePressure = false;
			inkAttr.PenTip = PenTipShape.Circle;
			inkAttr.Size = new Size(4, 4);
			inkAttr.Color = Colors.Black;
			ResetInkManager();

#if NETWORK
			Network.OnNetworkRecieved += OnNetworkRecieved;
			Network.connect();
#endif
		}

		private void ResetInkManager()
		{
			inkManager = new InkManager();
			inkManager.SetDefaultDrawingAttributes(inkAttr);
		}

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

		private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (actionType != ActionType.None &&
				actionType != ActionType.Palm)
				return;

			PointerPoint pt = e.GetCurrentPoint(canvas);
			PointerDeviceType pointerDevType = e.Pointer.PointerDeviceType;

			PointerPoint check = e.GetCurrentPoint(palmBlock);
			if (PointInPalmBlock(check.Position))
				return;
		
			if (actionType == ActionType.Palm)
			{
				pt = e.GetCurrentPoint(rootCanvas);
				PointerPoint palmPt = e.GetCurrentPoint(palmBlock);

				palmBlock.Points.Clear();
				palmBlock.Points.Add(new Point(0, 0));
				palmBlock.Points.Add(palmPt.Position);

				palmTempLine = new Line();
				palmTempLine.StrokeDashArray.Add(5);
				palmTempLine.StrokeDashArray.Add(5);
				palmTempLine.Stroke = new SolidColorBrush(Colors.Black);
				rootCanvas.Children.Add(palmTempLine);

				palmSide = 0;
			}
			else if (pointerDevType == PointerDeviceType.Pen ||
					pointerDevType == PointerDeviceType.Mouse &&
					pt.Properties.IsLeftButtonPressed)
			{
				LiveRenderBegin(pt);
				inkManager.ProcessPointerDown(pt);

				e.Handled = true;
			}
			else if (pointerDevType == PointerDeviceType.Touch ||
					pt.Properties.IsRightButtonPressed)
			{
				pointID = pt.PointerId;
				actionType = ActionType.Moving;
				prevPoint = pt.Position;

				e.Handled = true;
			}
		}

		private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
		{
			PointerPoint pt = e.GetCurrentPoint(canvas);

			if (actionType == ActionType.Palm && palmTempLine != null)
			{
				pt = e.GetCurrentPoint(rootCanvas);
				PointerPoint palmPt = e.GetCurrentPoint(palmBlock);

				bool angle = Angle(new Point(0, 0), palmBlock.Points[palmBlock.Points.Count - 1]) > Angle(new Point(0, 0), palmPt.Position);

				if (angle && palmSide >= 0)
				{
					if (palmSide == 0)
					{
						palmSide = 1;
						palmBlock.Points.Insert(1, new Point(palmPt.Position.X, palmBlock.Margin.Top));
					}

					//CW
					palmTempLine.X1 = pt.Position.X;
					palmTempLine.Y1 = pt.Position.Y;
					palmTempLine.X2 = palmBlock.Margin.Left;
					palmTempLine.Y2 = pt.Position.Y;

					palmBlock.Points.Add(palmPt.Position);
				}
				else if (!angle && palmSide <= 0)
				{
					if (palmSide == 0)
					{
						palmSide = -1;
						palmBlock.Points.Insert(1, new Point(palmBlock.Margin.Left, palmPt.Position.Y));
					}

					//CCW
					palmTempLine.X1 = pt.Position.X;
					palmTempLine.Y1 = pt.Position.Y;
					palmTempLine.X2 = pt.Position.X;
					palmTempLine.Y2 = palmBlock.Margin.Top;

					palmBlock.Points.Add(palmPt.Position);
				}
			}
			else if (actionType == ActionType.Drawing && e.Pointer.PointerId == pointID)
			{
				LiveRenderUpdate(pt);
				inkManager.ProcessPointerUpdate(pt);

				e.Handled = true;
			}
			else if (actionType == ActionType.Moving && e.Pointer.PointerId == pointID)
			{
				translate.X += (pt.Position.X - prevPoint.X) * scale.ScaleX;
				translate.Y += (pt.Position.Y - prevPoint.Y) * scale.ScaleY;
				e.Handled = true;
			}
		}

		private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			PointerPoint pt = e.GetCurrentPoint(canvas);

			if (actionType == ActionType.Palm)
			{
				if (palmTempLine != null && palmSide != 0)
				{
					pt = e.GetCurrentPoint(rootCanvas);
					PointerPoint palmPt = e.GetCurrentPoint(palmBlock);

					rootCanvas.Children.Remove(palmTempLine);
					/*
					if (palmSide > 0)
						palmBlock.Points.Add(new Point(palmBlock.Margin.Left, palmPt.Position.Y));
					if (palmSide < 0)
						palmBlock.Points.Add(new Point(palmPt.Position.X, palmBlock.Margin.Top));
					*/
					palmBlock.Points.Add(new Point(palmTempLine.X2 - palmBlock.Margin.Left, palmTempLine.Y2 - palmBlock.Margin.Top));
				}
			
				palmTempLine = null;
				actionType = ActionType.None;

				e.Handled = true;
			}
			else if (actionType == ActionType.Drawing && e.Pointer.PointerId == pointID)
			{
				inkManager.ProcessPointerUp(pt);
				BezierRender();
				actionType = ActionType.None;

				e.Handled = true;
			}
			else if (actionType == ActionType.Moving && e.Pointer.PointerId == pointID)
			{
				actionType = ActionType.None;

				e.Handled = true;
			}
		}
		
		private void LiveRenderBegin(PointerPoint pt)
		{
			actionType = ActionType.Drawing;

			pointID = pt.PointerId;
			prevPoint = pt.Position;
		}

		private void LiveRenderUpdate(PointerPoint pt)
		{
			Point nowPoint = pt.Position;

			Line line = new Line()
			{
				X1 = prevPoint.X,
				Y1 = prevPoint.Y,
				X2 = nowPoint.X,
				Y2 = nowPoint.Y,
				StrokeThickness = pt.Properties.Pressure * inkAttr.Size.Width * 2,
				Stroke = new SolidColorBrush(inkAttr.Color),
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				StrokeLineJoin = PenLineJoin.Round,
			};

			liveRender.Children.Add(line);
			
			prevPoint = nowPoint;
		}

		private void LiveRenderEnd()
		{
			liveRender.Children.Clear();
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
		
		private void BezierRender()
		{
			foreach (var stroke in inkManager.GetStrokes())
			{
				var strokeCanvas = new Canvas();

				var segs = stroke.GetRenderingSegments().GetEnumerator();
				segs.MoveNext();

				Point org = segs.Current.Position;

				while (segs.MoveNext())
				{
					var path = CreateBezierPath(segs.Current.BezierControlPoint1, segs.Current.BezierControlPoint2, segs.Current.Position, org, segs.Current.Pressure);
#if NETWORK
					Network.sendData(segs.Current.BezierControlPoint1, segs.Current.BezierControlPoint2, segs.Current.Position, org, segs.Current.Pressure);
#endif
					strokeCanvas.Children.Add(path);
					Windows.UI.Xaml.Controls.Canvas.SetZIndex(path, 1);

					org = segs.Current.Position;
				}

				bezierRender.Children.Add(strokeCanvas);
			}

			ResetInkManager();
			LiveRenderEnd();
		}

		private void OnNetworkRecieved(Point p1, Point p2, Point p3, Point p4, float pressure)
		{
			var path = CreateBezierPath(p1, p2, p3, p4, pressure);
			bezierRender.Children.Add(path);
		}
		
		public Windows.UI.Xaml.Shapes.Path CreateBezierPath(Point p1, Point p2, Point p3, Point org, float pressure)
		{
			var figure = new PathFigure();
			figure.StartPoint = org;
			
			var bezier = new BezierSegment();
			bezier.Point1 = p1;
			bezier.Point2 = p2;
			bezier.Point3 = p3;
			figure.Segments.Add(bezier);

			var geometry = new PathGeometry();
			geometry.Figures.Add(figure);

			var path = new Windows.UI.Xaml.Shapes.Path();
			path.Stroke = new SolidColorBrush(inkAttr.Color);
			path.StrokeThickness = pressure * inkAttr.Size.Width * 2;
			path.StrokeStartLineCap = PenLineCap.Round;
			path.StrokeEndLineCap = PenLineCap.Round;
			path.StrokeLineJoin = PenLineJoin.Round;
			path.Data = geometry;

			return path;
		}

		private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
		{
			if (PointInPalmBlock(e.GetCurrentPoint(palmBlock).Position))
				return;

			PointerPoint pt = e.GetCurrentPoint(canvas);
			Debug.WriteLine(pt.Properties.MouseWheelDelta);
			Scaling(pt.Position, pt.Properties.MouseWheelDelta > 0);
			e.Handled = true;
		}

		private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
		{
			if (PointInPalmBlock(new Point(e.Position.X - palmBlock.Margin.Left, e.Position.Y - palmBlock.Margin.Top)))
				return;

			Scaling(e.Position, e.Delta.Scale > 0);
			e.Handled = true;
		}

		private void Scaling(Point p, bool zoomin)
		{
			if (actionType == ActionType.Scaling)
				return;

			double value = 1.5;
			if (zoomin)
			{
				if (zoomLevel > 1)
					return;

				++zoomLevel;
			}
			else
			{
				if (zoomLevel < -1)
					return;
				
				--zoomLevel;
				value = 1 / value;
			}

			actionType = ActionType.Scaling;

			Point t = new Point(p.X * scale.ScaleX + translate.X, p.Y * scale.ScaleX + translate.Y);

			t.X -= t.X * (1 / value);
			t.Y -= t.Y * (1 / value);
			/*
			scale.ScaleX *= value;
			scale.ScaleY *= value;

			translate.X -= t.X;
			translate.Y -= t.Y;

			translate.X *= value;
			translate.Y *= value;
			/*/
			animTranslateX.To = (translate.X - t.X) * value;
			animTranslateY.To = (translate.Y - t.Y) * value;

			animScaleX.To = scale.ScaleX * value;
			animScaleY.To = scale.ScaleY * value;
			
			storyboard.Begin();
			//*/
		}

		private void OnAnimCompleted(object sender, object e)
		{
			actionType = ActionType.None;
		}

		public void Clear()
		{
			liveRender.Children.Clear();
			bezierRender.Children.Clear();

			translate.X = 0;
			translate.Y = 0;

			scale.ScaleX = 1;
			scale.ScaleY = 1;

			ResetInkManager();
		}

		public void Undo()
		{
			if (bezierRender.Children.Count > 0)
				bezierRender.Children.Remove(bezierRender.Children.Last());
		}

		public void ChangePenColor(Color color)
		{
			if (inkAttr == null)
				return; 

			inkAttr.Color = color;
			ResetInkManager();
		}

		public void ChangePenThickness(double value)
		{
			if (inkAttr == null)
				return;

			inkAttr.Size = new Size(value, value);
			ResetInkManager();
		}

		public void StartPalmBlockSelect()
		{
			actionType = ActionType.Palm;
			palmBlock.Points.Clear();
		}
	}
}
