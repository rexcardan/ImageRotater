using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

public class SliceViewer : Form
{
    private readonly List<short[]> _slices;
    private readonly int _columns;
    private readonly int _rows;
    private readonly TrackBar _slider;
    private readonly PictureBox _pictureBox;
    private readonly Label _label;

    // CT window: W=2000, L=0 (wide window to see everything)
    private const double WindowWidth = 2000;
    private const double WindowLevel = 0;

    public SliceViewer(string title, List<short[]> slices, int columns, int rows)
    {
        _slices = slices;
        _columns = columns;
        _rows = rows;

        Text = title;
        Width = Math.Max(columns + 40, 400);
        Height = rows + 120;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;

        _label = new Label
        {
            Text = $"Slice 1 / {slices.Count}",
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.MiddleCenter,
            Height = 24,
            Font = new Font("Segoe UI", 10)
        };

        _pictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black
        };

        _slider = new TrackBar
        {
            Dock = DockStyle.Bottom,
            Minimum = 0,
            Maximum = Math.Max(slices.Count - 1, 0),
            Value = 0,
            TickFrequency = Math.Max(slices.Count / 20, 1),
            LargeChange = Math.Max(slices.Count / 10, 1),
            SmallChange = 1,
            Height = 45
        };
        _slider.ValueChanged += (s, e) => ShowSlice(_slider.Value);

        Controls.Add(_pictureBox);
        Controls.Add(_slider);
        Controls.Add(_label);

        if (slices.Count > 0)
            ShowSlice(0);
    }

    private void ShowSlice(int index)
    {
        _label.Text = $"Slice {index + 1} / {_slices.Count}";
        _pictureBox.Image?.Dispose();
        _pictureBox.Image = SliceToBitmap(_slices[index]);
    }

    private Bitmap SliceToBitmap(short[] pixels)
    {
        var bmp = new Bitmap(_columns, _rows, PixelFormat.Format8bppIndexed);

        // Set grayscale palette
        var palette = bmp.Palette;
        for (int i = 0; i < 256; i++)
            palette.Entries[i] = Color.FromArgb(i, i, i);
        bmp.Palette = palette;

        // Window/level to 8-bit
        double wMin = WindowLevel - WindowWidth / 2.0;
        double wMax = WindowLevel + WindowWidth / 2.0;

        var bits = bmp.LockBits(new Rectangle(0, 0, _columns, _rows),
            ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);

        unsafe
        {
            for (int y = 0; y < _rows; y++)
            {
                byte* row = (byte*)bits.Scan0 + y * bits.Stride;
                for (int x = 0; x < _columns; x++)
                {
                    double val = pixels[y * _columns + x];
                    val = (val - wMin) / (wMax - wMin) * 255.0;
                    if (val < 0) val = 0;
                    if (val > 255) val = 255;
                    row[x] = (byte)val;
                }
            }
        }

        bmp.UnlockBits(bits);
        return bmp;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _pictureBox.Image?.Dispose();
        base.OnFormClosed(e);
    }
}
