using System.IO;
using EvilDICOM.Core;
using EvilDICOM.Core.Helpers;

namespace ImageRotater;

public class CtVolume
{
    public List<short[]> AxialSlices { get; }
    public List<DICOMObject> DicomObjects { get; }
    public List<string> FilePaths { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int SliceCount => AxialSlices.Count;
    public double PixelSpacingX { get; }
    public double PixelSpacingY { get; }
    public double SliceSpacing { get; }

    public CtVolume(
        List<short[]> axialSlices,
        List<DICOMObject> dicomObjects,
        List<string> filePaths,
        int columns, int rows,
        double pixelSpacingX, double pixelSpacingY,
        double sliceSpacing)
    {
        AxialSlices = axialSlices;
        DicomObjects = dicomObjects;
        FilePaths = filePaths;
        Columns = columns;
        Rows = rows;
        PixelSpacingX = pixelSpacingX;
        PixelSpacingY = pixelSpacingY;
        SliceSpacing = sliceSpacing;
    }

    public short[] GetAxialSlice(int z) => AxialSlices[z];

    public short[] GetSagittalSlice(int x)
    {
        // YZ plane: width = Rows (Y axis), height = SliceCount (Z axis)
        var slice = new short[Rows * SliceCount];
        for (int z = 0; z < SliceCount; z++)
        {
            // Display convention: +Z at top, -Z at bottom.
            int outZ = SliceCount - 1 - z;
            for (int y = 0; y < Rows; y++)
                slice[outZ * Rows + y] = AxialSlices[z][y * Columns + x];
        }
        return slice;
    }

    public short[] GetCoronalSlice(int y)
    {
        // XZ plane: width = Columns (X axis), height = SliceCount (Z axis)
        var slice = new short[Columns * SliceCount];
        for (int z = 0; z < SliceCount; z++)
        {
            // Display convention: +Z at top, -Z at bottom.
            int outZ = SliceCount - 1 - z;
            for (int x = 0; x < Columns; x++)
                slice[outZ * Columns + x] = AxialSlices[z][y * Columns + x];
        }
        return slice;
    }

    public static CtVolume LoadFromDirectory(string path)
    {
        var ctFiles = Directory.GetFiles(path, "CT*.dcm");
        if (ctFiles.Length == 0)
            throw new FileNotFoundException($"No CT*.dcm files found in {path}");

        Array.Sort(ctFiles);

        int rows = 0, cols = 0;
        List<double>? pixelSpacing = null;
        var entries = new List<(string filePath, DICOMObject dcm, double zPos, short[] pixels)>();

        foreach (string file in ctFiles)
        {
            DICOMObject dcm = DICOMObject.Read(file);
            var sel = dcm.GetSelector();
            rows = sel.Rows.Data;
            cols = sel.Columns.Data;
            pixelSpacing = sel.PixelSpacing.Data_;
            var imagePosition = sel.ImagePositionPatient.Data_;
            double zPos = imagePosition[2];

            var pixelBytes = dcm.Elements.First(e => e.Tag == TagHelper.PixelData).DData_ as List<byte>;
            var shortCount = rows * cols;
            var shorts = new short[shortCount];
            Buffer.BlockCopy(pixelBytes!.ToArray(), 0, shorts, 0, shortCount * 2);

            entries.Add((file, dcm, zPos, shorts));
        }

        entries.Sort((a, b) => a.zPos.CompareTo(b.zPos));

        double sliceSpacing = entries.Count > 1
            ? Math.Abs(entries[1].zPos - entries[0].zPos)
            : 1.0;

        return new CtVolume(
            entries.Select(e => e.pixels).ToList(),
            entries.Select(e => e.dcm).ToList(),
            entries.Select(e => e.filePath).ToList(),
            cols, rows,
            // DICOM PixelSpacing is [row spacing (Y), column spacing (X)] in mm.
            pixelSpacing![1], pixelSpacing[0],
            sliceSpacing);
    }
}
