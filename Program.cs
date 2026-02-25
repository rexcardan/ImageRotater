using System.Runtime.InteropServices;
using System.Windows.Forms;
using EvilDICOM.Core;
using EvilDICOM.Core.Helpers;
using itk.simple;

// ── Configuration ──────────────────────────────────────────────────
var dcmPath = @"J:\Varian\GBM_RTOG0825DICOM";
double rotationAngleDeg = 45; // degrees clockwise

// ── Find CT files ──────────────────────────────────────────────────
var ctFiles = Directory.GetFiles(dcmPath, "CT*.dcm");
Array.Sort(ctFiles);
Console.WriteLine($"Found {ctFiles.Length} CT files in {dcmPath}");

// ── First pass: read all slices ────────────────────────────────────
int imgRows = 0, imgColumns = 0;
List<double>? imgPixelSpacing = null;

// Read all DICOM files with their Z position
var sliceEntries = new List<(string filePath, DICOMObject dcm, double zPos, short[] pixels)>();

foreach (var filePath in ctFiles)
{
    var fileName = Path.GetFileName(filePath);
    Console.WriteLine($"Reading: {fileName}");

    var dcm = DICOMObject.Read(filePath);
    var sel = dcm.GetSelector();
    var rows = sel.Rows.Data;
    var columns = sel.Columns.Data;
    var pixelSpacing = sel.PixelSpacing.Data_;
    var imagePosition = sel.ImagePositionPatient.Data_;
    double zPos = imagePosition[2]; // Z component

    imgRows = rows;
    imgColumns = columns;
    imgPixelSpacing = pixelSpacing;

    var pixelBytes = dcm.Elements.FirstOrDefault(e => e.Tag == TagHelper.PixelData).DData_ as List<byte>;
    var shortCount = rows * columns;
    var shorts = new short[shortCount];
    Buffer.BlockCopy(pixelBytes.ToArray(), 0, shorts, 0, shortCount * 2);

    sliceEntries.Add((filePath, dcm, zPos, shorts));
}

// Sort by Z position (ascending)
sliceEntries.Sort((a, b) => a.zPos.CompareTo(b.zPos));

var beforeSlices = sliceEntries.Select(e => e.pixels).ToList();
var dcmObjects = sliceEntries.Select(e => e.dcm).ToList();
var sortedFilePaths = sliceEntries.Select(e => e.filePath).ToArray();

// ── Show BEFORE viewer ─────────────────────────────────────────────
Console.WriteLine("Showing BEFORE rotation viewer...");
Application.EnableVisualStyles();
var beforeViewer = new SliceViewer("BEFORE Rotation", beforeSlices, imgColumns, imgRows);
Application.Run(beforeViewer);

// ── Rotate all slices ──────────────────────────────────────────────
Console.WriteLine("Rotating slices...");
var afterSlices = new List<short[]>();

for (int i = 0; i < dcmObjects.Count; i++)
{
    var dcm = dcmObjects[i];
    var shortCount = imgRows * imgColumns;

    // Build SimpleITK 2D image (Int16 for CT)
    var image = new Image((uint)imgColumns, (uint)imgRows, PixelIDValueEnum.sitkInt16);
    image.SetSpacing(new VectorDouble(new[] { imgPixelSpacing![0], imgPixelSpacing[1] }));

    // Copy pixel bytes into the SimpleITK image buffer
    var pixelBuffer = image.GetBufferAsInt16();
    var srcBytes = new byte[shortCount * 2];
    Buffer.BlockCopy(beforeSlices[i], 0, srcBytes, 0, shortCount * 2);
    Marshal.Copy(srcBytes, 0, pixelBuffer, shortCount * 2);

    // Set up rotation: negative angle for clockwise in image coordinates
    double angleRad = -rotationAngleDeg * Math.PI / 180.0;
    var center = new VectorDouble(new[]
    {
        imgColumns * imgPixelSpacing[0] / 2.0,
        imgRows * imgPixelSpacing[1] / 2.0
    });

    var transform = new Euler2DTransform();
    transform.SetCenter(center);
    transform.SetAngle(angleRad);

    // Resample with linear interpolation, -1000 HU fill (air)
    var resampler = new ResampleImageFilter();
    resampler.SetReferenceImage(image);
    resampler.SetInterpolator(InterpolatorEnum.sitkLinear);
    resampler.SetDefaultPixelValue(-1000);
    resampler.SetTransform(transform);

    var rotated = resampler.Execute(image);

    // Extract rotated pixels
    var rotatedBuffer = rotated.GetBufferAsInt16();
    var rotatedBytes = new byte[shortCount * 2];
    Marshal.Copy(rotatedBuffer, rotatedBytes, 0, shortCount * 2);

    var rotatedShorts = new short[shortCount];
    Buffer.BlockCopy(rotatedBytes, 0, rotatedShorts, 0, shortCount * 2);
    afterSlices.Add(rotatedShorts);

    // Update DICOM pixel data and write
    dcm.Elements.FirstOrDefault(e => e.Tag == TagHelper.PixelData).DData_ = rotatedBytes.ToList();

    var fileName = Path.GetFileName(sortedFilePaths[i]);
    var outFileName = fileName.StartsWith("CT.", StringComparison.OrdinalIgnoreCase)
        ? "CT.ROT." + fileName.Substring(3)
        : "CT.ROT." + fileName;
    var outPath = Path.Combine(dcmPath, outFileName);
    dcm.Write(outPath);

    Console.WriteLine($"  {fileName} -> {outFileName}");

    rotated.Dispose();
    image.Dispose();
}

// ── Show AFTER viewer ──────────────────────────────────────────────
Console.WriteLine("Showing AFTER rotation viewer...");
var afterViewer = new SliceViewer("AFTER Rotation", afterSlices, imgColumns, imgRows);
Application.Run(afterViewer);

Console.WriteLine("Done.");
