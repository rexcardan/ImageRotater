using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using EvilDICOM.Core.Helpers;
using itk.simple;
using Microsoft.Win32;

namespace ImageRotater;

public partial class MainWindow : Window
{
    private CtVolume? _volume;

    public MainWindow()
    {
        InitializeComponent();

        AxialView.Initialize(ViewType.Axial, "Axial");
        SagittalView.Initialize(ViewType.Sagittal, "Sagittal");
        CoronalView.Initialize(ViewType.Coronal, "Coronal");
    }

    private void OnLoadClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select CT DICOM Folder"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            Cursor = Cursors.Wait;
            StatusText.Text = "Loading CT files...";

            // Force UI update before blocking load
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

            _volume = CtVolume.LoadFromDirectory(dlg.FolderName);

            AxialView.SetVolume(_volume);
            SagittalView.SetVolume(_volume);
            CoronalView.SetVolume(_volume);

            FinalizeButton.IsEnabled = true;
            StatusText.Text = $"Loaded {_volume.SliceCount} slices " +
                              $"({_volume.Columns}\u00d7{_volume.Rows}, " +
                              $"spacing {_volume.PixelSpacingX:F2}\u00d7{_volume.PixelSpacingY:F2}\u00d7{_volume.SliceSpacing:F2} mm) " +
                              $"from {dlg.FolderName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading CT files:\n{ex.Message}",
                "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Load failed.";
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    private void OnFinalizeClick(object sender, RoutedEventArgs e)
    {
        if (_volume == null) return;

        var dlg = new OpenFolderDialog
        {
            Title = "Select Output Folder for Rotated CT Files"
        };

        if (dlg.ShowDialog() != true) return;

        string outputFolder = dlg.FolderName;

        double axialAngle = AxialView.RotationAngle;
        double sagittalAngle = SagittalView.RotationAngle;
        double coronalAngle = CoronalView.RotationAngle;

        try
        {
            Cursor = Cursors.Wait;
            StatusText.Text = $"Applying 3D rotation (Ax:{axialAngle:F1}\u00b0 Sag:{sagittalAngle:F1}\u00b0 Cor:{coronalAngle:F1}\u00b0) and saving...";
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

            uint sizeX = (uint)_volume.Columns;
            uint sizeY = (uint)_volume.Rows;
            uint sizeZ = (uint)_volume.SliceCount;
            int slicePixels = _volume.Columns * _volume.Rows;
            int sliceByteCount = slicePixels * 2;

            // Build 3D SimpleITK volume
            var image3d = new Image(sizeX, sizeY, sizeZ, PixelIDValueEnum.sitkInt16);
            image3d.SetSpacing(new VectorDouble(new[]
            {
                _volume.PixelSpacingX, _volume.PixelSpacingY, _volume.SliceSpacing
            }));

            // Copy all axial slices into the 3D volume buffer
            var volumeBuffer = image3d.GetBufferAsInt16();
            for (int z = 0; z < _volume.SliceCount; z++)
            {
                var slice = _volume.AxialSlices[z];
                var bytes = new byte[sliceByteCount];
                Buffer.BlockCopy(slice, 0, bytes, 0, bytes.Length);
                Marshal.Copy(bytes, 0, IntPtr.Add(volumeBuffer, z * sliceByteCount), bytes.Length);
            }

            // Set up 3D Euler rotation centered on the volume
            var transform = new Euler3DTransform();
            var center = new VectorDouble(new[]
            {
                sizeX * _volume.PixelSpacingX / 2.0,
                sizeY * _volume.PixelSpacingY / 2.0,
                sizeZ * _volume.SliceSpacing / 2.0
            });
            transform.SetCenter(center);

            // Negate angles: user drags clockwise = positive, SimpleITK uses CCW convention
            double axialRad = -axialAngle * Math.PI / 180.0;
            double sagittalRad = -sagittalAngle * Math.PI / 180.0;
            double coronalRad = -coronalAngle * Math.PI / 180.0;
            transform.SetRotation(sagittalRad, coronalRad, axialRad);

            // Resample with linear interpolation, air fill
            var resampler = new ResampleImageFilter();
            resampler.SetReferenceImage(image3d);
            resampler.SetInterpolator(InterpolatorEnum.sitkLinear);
            resampler.SetDefaultPixelValue(-1000);
            resampler.SetTransform(transform);

            var rotated = resampler.Execute(image3d);

            // Extract rotated slices and write DICOM files
            var rotBuffer = rotated.GetBufferAsInt16();

            for (int z = 0; z < _volume.SliceCount; z++)
            {
                var sliceBytes = new byte[sliceByteCount];
                Marshal.Copy(IntPtr.Add(rotBuffer, z * sliceByteCount), sliceBytes, 0, sliceByteCount);

                var dcm = _volume.DicomObjects[z];
                dcm.Elements.First(el => el.Tag == TagHelper.PixelData).DData_ = sliceBytes.ToList();

                var fileName = Path.GetFileName(_volume.FilePaths[z]);
                var outFileName = fileName.StartsWith("CT.", StringComparison.OrdinalIgnoreCase)
                    ? "CT.ROT." + fileName.Substring(3)
                    : "CT.ROT." + fileName;

                dcm.Write(Path.Combine(outputFolder, outFileName));
            }

            rotated.Dispose();
            image3d.Dispose();

            StatusText.Text = $"Saved {_volume.SliceCount} rotated files to {outputFolder} " +
                              $"(Axial: {axialAngle:F1}\u00b0, Sagittal: {sagittalAngle:F1}\u00b0, Coronal: {coronalAngle:F1}\u00b0)";

            MessageBox.Show(
                $"Successfully saved {_volume.SliceCount} rotated CT files to:\n{outputFolder}",
                "Finalize Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error during finalization:\n{ex.Message}",
                "Finalize Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Finalize failed.";
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }
}
