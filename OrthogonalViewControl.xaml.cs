using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ImageRotater;

public enum ViewType { Axial, Sagittal, Coronal }

public partial class OrthogonalViewControl : UserControl
{
    private CtVolume? _volume;
    private ViewType _viewType;
    private bool _isDragging;
    private double _centerX, _centerY, _radius;
    private int _sliceWidthPixels, _sliceHeightPixels;
    private double _sliceWidthSpacingMm, _sliceHeightSpacingMm;

    private const double WindowWidth = 2000;
    private const double WindowLevel = 0;
    private const double GridSpacingMm = 50.0;
    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromArgb(65, 255, 255, 255));

    public double RotationAngle { get; private set; }

    public OrthogonalViewControl()
    {
        InitializeComponent();
    }

    public void Initialize(ViewType viewType, string title)
    {
        _viewType = viewType;
        TitleText.Text = title;
    }

    public void SetVolume(CtVolume volume)
    {
        _volume = volume;
        RotationAngle = 0;

        int maxSlice = _viewType switch
        {
            ViewType.Axial => volume.SliceCount - 1,
            ViewType.Sagittal => volume.Columns - 1,
            ViewType.Coronal => volume.Rows - 1,
            _ => 0
        };

        SliceSlider.Maximum = maxSlice;
        SliceSlider.Value = maxSlice / 2;
        ShowSlice((int)SliceSlider.Value);
    }

    private void ShowSlice(int index)
    {
        if (_volume == null) return;

        short[] pixels;
        int width, height;
        double widthSpacing, heightSpacing;

        switch (_viewType)
        {
            case ViewType.Axial:
                pixels = _volume.GetAxialSlice(index);
                width = _volume.Columns;
                height = _volume.Rows;
                widthSpacing = _volume.PixelSpacingX;
                heightSpacing = _volume.PixelSpacingY;
                break;
            case ViewType.Sagittal:
                pixels = _volume.GetSagittalSlice(index);
                width = _volume.Rows;
                height = _volume.SliceCount;
                widthSpacing = _volume.PixelSpacingY;
                heightSpacing = _volume.SliceSpacing;
                break;
            case ViewType.Coronal:
                pixels = _volume.GetCoronalSlice(index);
                width = _volume.Columns;
                height = _volume.SliceCount;
                widthSpacing = _volume.PixelSpacingX;
                heightSpacing = _volume.SliceSpacing;
                break;
            default:
                return;
        }

        SliceImage.Source = CreateBitmap(pixels, width, height, widthSpacing, heightSpacing);
        SliceImage.RenderTransform = new RotateTransform(RotationAngle);
        _sliceWidthPixels = width;
        _sliceHeightPixels = height;
        _sliceWidthSpacingMm = widthSpacing;
        _sliceHeightSpacingMm = heightSpacing;
        UpdateInfo(index);
        UpdateOverlay();
    }

    private static WriteableBitmap CreateBitmap(short[] pixels, int width, int height, double widthSpacing, double heightSpacing)
    {
        const double baseDpi = 96.0;
        const double minDpi = 1.0;
        const double maxDpi = 9600.0;

        double safeWidthSpacing = widthSpacing > 0 ? widthSpacing : 1.0;
        double safeHeightSpacing = heightSpacing > 0 ? heightSpacing : 1.0;
        double dpiX = baseDpi;
        double dpiY = baseDpi * safeWidthSpacing / safeHeightSpacing;
        dpiY = Math.Clamp(dpiY, minDpi, maxDpi);

        var bmp = new WriteableBitmap(width, height, dpiX, dpiY, PixelFormats.Gray8, null);
        var buffer = new byte[width * height];

        double wMin = WindowLevel - WindowWidth / 2.0;
        double wRange = WindowWidth;

        int count = Math.Min(pixels.Length, buffer.Length);
        for (int i = 0; i < count; i++)
        {
            double val = (pixels[i] - wMin) / wRange * 255.0;
            if (val < 0) val = 0;
            if (val > 255) val = 255;
            buffer[i] = (byte)val;
        }

        bmp.WritePixels(new Int32Rect(0, 0, width, height), buffer, width, 0);
        return bmp;
    }

    private void UpdateInfo(int index)
    {
        string viewName = _viewType.ToString();
        int total = (int)SliceSlider.Maximum + 1;
        InfoLabel.Text = $"{viewName} | Slice {index + 1}/{total} | Rotation: {RotationAngle:F1}\u00b0";
    }

    private void CalculateOverlayGeometry()
    {
        double canvasW = OverlayCanvas.ActualWidth;
        double canvasH = OverlayCanvas.ActualHeight;
        if (canvasW <= 0 || canvasH <= 0)
        {
            _radius = 0;
            return;
        }

        _centerX = canvasW / 2.0;
        _centerY = canvasH / 2.0;
        _radius = Math.Min(canvasW, canvasH) * 0.35;
    }

    private void UpdateOverlay()
    {
        OverlayCanvas.Children.Clear();
        if (_volume == null) return;

        DrawGridOverlay();

        CalculateOverlayGeometry();
        if (_radius <= 0) return;

        // Main circle
        var circle = new Ellipse
        {
            Width = _radius * 2,
            Height = _radius * 2,
            Stroke = new SolidColorBrush(Color.FromArgb(180, 0, 200, 255)),
            StrokeThickness = 2,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(circle, _centerX - _radius);
        Canvas.SetTop(circle, _centerY - _radius);
        OverlayCanvas.Children.Add(circle);

        // Reference line (12 o'clock, dimmed dashed)
        var refLine = new Line
        {
            X1 = _centerX, Y1 = _centerY,
            X2 = _centerX, Y2 = _centerY - _radius,
            Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 4 }
        };
        OverlayCanvas.Children.Add(refLine);

        // Current angle indicator line
        double rad = RotationAngle * Math.PI / 180.0;
        double endX = _centerX + _radius * Math.Sin(rad);
        double endY = _centerY - _radius * Math.Cos(rad);

        var angleLine = new Line
        {
            X1 = _centerX, Y1 = _centerY,
            X2 = endX, Y2 = endY,
            Stroke = new SolidColorBrush(Color.FromArgb(220, 255, 255, 0)),
            StrokeThickness = 2
        };
        OverlayCanvas.Children.Add(angleLine);

        // Handle dot on the circle edge
        var handle = new Ellipse
        {
            Width = 14, Height = 14,
            Fill = new SolidColorBrush(Color.FromArgb(220, 255, 255, 0)),
            Stroke = Brushes.White,
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(handle, endX - 7);
        Canvas.SetTop(handle, endY - 7);
        OverlayCanvas.Children.Add(handle);

        // Center dot
        var centerDot = new Ellipse
        {
            Width = 6, Height = 6,
            Fill = new SolidColorBrush(Color.FromArgb(180, 0, 200, 255))
        };
        Canvas.SetLeft(centerDot, _centerX - 3);
        Canvas.SetTop(centerDot, _centerY - 3);
        OverlayCanvas.Children.Add(centerDot);
    }

    private void DrawGridOverlay()
    {
        if (_sliceWidthPixels <= 0 || _sliceHeightPixels <= 0) return;
        if (_sliceWidthSpacingMm <= 0 || _sliceHeightSpacingMm <= 0) return;

        Rect imageRect = GetDisplayedImageRect();
        if (imageRect.Width <= 0 || imageRect.Height <= 0) return;

        double xPeriodPixels = GridSpacingMm / _sliceWidthSpacingMm;
        double yPeriodPixels = GridSpacingMm / _sliceHeightSpacingMm;
        if (xPeriodPixels < 1.0 || yPeriodPixels < 1.0) return;

        double xScale = imageRect.Width / _sliceWidthPixels;
        double yScale = imageRect.Height / _sliceHeightPixels;

        for (double x = 0; x <= _sliceWidthPixels; x += xPeriodPixels)
        {
            double drawX = imageRect.Left + x * xScale;
            var line = new Line
            {
                X1 = drawX,
                Y1 = imageRect.Top,
                X2 = drawX,
                Y2 = imageRect.Bottom,
                Stroke = GridBrush,
                StrokeThickness = 1
            };
            OverlayCanvas.Children.Add(line);
        }

        for (double y = 0; y <= _sliceHeightPixels; y += yPeriodPixels)
        {
            double drawY = imageRect.Top + y * yScale;
            var line = new Line
            {
                X1 = imageRect.Left,
                Y1 = drawY,
                X2 = imageRect.Right,
                Y2 = drawY,
                Stroke = GridBrush,
                StrokeThickness = 1
            };
            OverlayCanvas.Children.Add(line);
        }
    }

    private Rect GetDisplayedImageRect()
    {
        if (SliceImage.Source is not BitmapSource source) return Rect.Empty;

        double canvasW = OverlayCanvas.ActualWidth;
        double canvasH = OverlayCanvas.ActualHeight;
        if (canvasW <= 0 || canvasH <= 0) return Rect.Empty;

        double srcW = source.PixelWidth * 96.0 / source.DpiX;
        double srcH = source.PixelHeight * 96.0 / source.DpiY;
        if (srcW <= 0 || srcH <= 0) return Rect.Empty;

        double scale = Math.Min(canvasW / srcW, canvasH / srcH);
        double drawW = srcW * scale;
        double drawH = srcH * scale;
        double left = (canvasW - drawW) / 2.0;
        double top = (canvasH - drawH) / 2.0;
        return new Rect(left, top, drawW, drawH);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_volume == null) return;
        _isDragging = true;
        OverlayCanvas.CaptureMouse();
        UpdateAngleFromMouse(e.GetPosition(OverlayCanvas));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        UpdateAngleFromMouse(e.GetPosition(OverlayCanvas));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        OverlayCanvas.ReleaseMouseCapture();
    }

    private void UpdateAngleFromMouse(Point pos)
    {
        double dx = pos.X - _centerX;
        double dy = _centerY - pos.Y;
        RotationAngle = Math.Atan2(dx, dy) * 180.0 / Math.PI;

        SliceImage.RenderTransform = new RotateTransform(RotationAngle);
        UpdateInfo((int)SliceSlider.Value);
        UpdateOverlay();
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_volume != null)
            ShowSlice((int)SliceSlider.Value);
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateOverlay();
    }
}
