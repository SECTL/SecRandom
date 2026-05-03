using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using SecRandom.Core.Models.Camera;
using SecRandom.Shared;

namespace SecRandom.Core.Services.Camera;

public partial class CameraDrawEngine
{
    private sealed record DetectedFace(float X, float Y, float Width, float Height, float Confidence);

    private readonly object detectorLock = new();
    private FaceDetectorYN? yunetDetector;
    private Net? ultralightNet;
    private IReadOnlyList<Rect2f>? ultralightPriors;
    private string? loadedDetectorModel;
    private string? lastDetectionErrorMessage;
    private Size detectorInputSize;

    private (IReadOnlyList<FaceBox> Faces, DetectionState State) detectFaces(Mat frameBgr)
    {
        lock (detectorLock)
        {
            try
            {
                ensureDetectorLoaded();

                IReadOnlyList<DetectedFace> faces = yunetDetector != null
                    ? detectFacesWithYunet(frameBgr)
                    : detectFacesWithUltralight(frameBgr);

                lastDetectionErrorMessage = null;
                var mergedFaces = mergeFaceBoxes(frameBgr.Width, frameBgr.Height, faces);
                return (mergedFaces, mergedFaces.Count == 0 ? DetectionState.NoFace : DetectionState.HasFaces);
            }
            catch (Exception ex)
            {
                if (lastDetectionErrorMessage != ex.Message)
                {
                    lastDetectionErrorMessage = ex.Message;
                    logger.LogDebug(ex, "Face detection failed.");
                }

                return ([], DetectionState.Error);
            }
        }
    }

    private void reloadDetector()
    {
        lock (detectorLock)
        {
            disposeDetector();
            loadedDetectorModel = null;
            lastDetectionErrorMessage = null;
        }
    }

    private void disposeDetector()
    {
        yunetDetector?.Dispose();
        yunetDetector = null;
        ultralightNet?.Dispose();
        ultralightNet = null;
        ultralightPriors = null;
    }

    private void ensureDetectorLoaded()
    {
        var modelName = string.IsNullOrWhiteSpace(detectorModel)
            ? "version-RFB-640.onnx"
            : detectorModel.Trim();
        var inputSize = resolveDetectorInputSize(modelName);

        if (loadedDetectorModel == modelName && detectorInputSize == inputSize &&
            (yunetDetector != null || ultralightNet != null))
            return;

        disposeDetector();

        var modelPath = resolveDetectorModelPath(modelName);
        ensureModelFileIsUsable(modelPath);

        if (modelName.Contains("yunet", StringComparison.OrdinalIgnoreCase))
        {
            yunetDetector = FaceDetectorYN.Create(
                modelPath,
                string.Empty,
                inputSize,
                0.85f,
                0.3f,
                5000,
                Backend.DEFAULT,
                Target.CPU);
        }
        else
        {
            ultralightNet = CvDnn.ReadNetFromOnnx(modelPath);
            ultralightPriors = generateUltralightPriors(inputSize.Width, inputSize.Height);
        }

        loadedDetectorModel = modelName;
        detectorInputSize = inputSize;
    }

    private static string resolveDetectorModelPath(string modelName)
    {
        var outputPath = Utils.GetFilePath("cv_models", modelName);
        if (File.Exists(outputPath))
            return outputPath;

        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var current = baseDirectory; current != null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "data", "cv_models", modelName);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(cameraText("DetectorModelNotFound", modelName), outputPath);
    }

    private static void ensureModelFileIsUsable(string modelPath)
    {
        var info = new FileInfo(modelPath);
        if (!info.Exists || info.Length <= 0)
            throw new FileNotFoundException(cameraText("DetectorModelNotFound", Path.GetFileName(modelPath)), modelPath);

        if (info.Length <= 512)
        {
            var header = File.ReadAllText(modelPath);
            if (header.StartsWith("version https://git-lfs.github.com/spec", StringComparison.Ordinal))
                throw new InvalidOperationException(cameraText("DetectorModelIsLfsPointer", modelPath));
        }
    }

    private Size resolveDetectorInputSize(string modelName)
    {
        var width = targetWidth > 0 ? targetWidth : 0;
        var height = targetHeight > 0 ? targetHeight : 0;
        if (width > 0 && height > 0)
            return new Size(width, height);

        if (modelName.Contains("640", StringComparison.OrdinalIgnoreCase))
            return new Size(640, 480);

        return new Size(320, 240);
    }

    private IReadOnlyList<DetectedFace> detectFacesWithYunet(Mat frameBgr)
    {
        if (yunetDetector == null)
            return [];

        using var resized = resizeForDetector(frameBgr, detectorInputSize);

        using var faces = new Mat();
        var count = yunetDetector.Detect(resized, faces);
        if (count <= 0 || faces.Empty())
            return [];

        var scaleX = (float)frameBgr.Width / detectorInputSize.Width;
        var scaleY = (float)frameBgr.Height / detectorInputSize.Height;
        List<DetectedFace> results = [];
        for (var i = 0; i < faces.Rows; i++)
        {
            var confidence = faces.At<float>(i, 14);
            if (confidence < 0.85f)
                continue;

            results.Add(new DetectedFace(
                faces.At<float>(i, 0) * scaleX,
                faces.At<float>(i, 1) * scaleY,
                faces.At<float>(i, 2) * scaleX,
                faces.At<float>(i, 3) * scaleY,
                confidence));
        }

        return results;
    }

    private IReadOnlyList<DetectedFace> detectFacesWithUltralight(Mat frameBgr)
    {
        if (ultralightNet == null || ultralightPriors == null)
            return [];

        using var resized = resizeForDetector(frameBgr, detectorInputSize);
        using var blob = CvDnn.BlobFromImage(
            resized,
            1.0 / 128.0,
            detectorInputSize,
            new Scalar(127.0, 127.0, 127.0),
            true,
            false);

        ultralightNet.SetInput(blob, string.Empty);

        var outputNames = ultralightNet.GetUnconnectedOutLayersNames()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
        List<Mat> outputs = [];
        try
        {
            ultralightNet.Forward(outputs, outputNames);
            return parseUltralightOutputs(outputs, ultralightPriors, frameBgr.Width, frameBgr.Height);
        }
        finally
        {
            foreach (var output in outputs)
                output.Dispose();
        }
    }

    private static Mat resizeForDetector(Mat frameBgr, Size inputSize)
    {
        if (frameBgr.Width == inputSize.Width && frameBgr.Height == inputSize.Height)
            return frameBgr.Clone();

        var resized = new Mat();
        Cv2.Resize(frameBgr, resized, inputSize, 0, 0, InterpolationFlags.Linear);
        return resized;
    }

    private IReadOnlyList<DetectedFace> parseUltralightOutputs(
        IReadOnlyList<Mat> outputs,
        IReadOnlyList<Rect2f> priors,
        int frameWidth,
        int frameHeight)
    {
        float[]? scores = null;
        float[]? boxes = null;

        foreach (var output in outputs)
        {
            if (!output.GetArray(out float[] data) || data.Length == 0)
                continue;

            var columns = inferOutputColumns(output, data.Length);
            if (columns == 2)
                scores = data;
            else if (columns == 4)
                boxes = data;
        }

        if (scores == null || boxes == null)
            throw new InvalidOperationException(cameraText("DetectorOutputUnsupported"));

        var count = Math.Min(Math.Min(scores.Length / 2, boxes.Length / 4), priors.Count);
        if (count <= 0)
            return [];

        var scaleX = (float)frameWidth / detectorInputSize.Width;
        var scaleY = (float)frameHeight / detectorInputSize.Height;
        List<Rect> nmsBoxes = [];
        List<float> nmsScores = [];
        List<DetectedFace> candidates = [];

        for (var i = 0; i < count; i++)
        {
            var confidence = scores[i * 2 + 1];
            if (confidence <= 0.7f)
                continue;

            var prior = priors[i];
            var boxIndex = i * 4;
            var centerX = prior.X + boxes[boxIndex] * 0.1f * prior.Width;
            var centerY = prior.Y + boxes[boxIndex + 1] * 0.1f * prior.Height;
            var width = prior.Width * MathF.Exp(boxes[boxIndex + 2] * 0.2f);
            var height = prior.Height * MathF.Exp(boxes[boxIndex + 3] * 0.2f);

            var x = (centerX - width / 2.0f) * detectorInputSize.Width * scaleX;
            var y = (centerY - height / 2.0f) * detectorInputSize.Height * scaleY;
            var w = width * detectorInputSize.Width * scaleX;
            var h = height * detectorInputSize.Height * scaleY;
            if (w <= 0 || h <= 0)
                continue;

            var candidate = clampFace(x, y, w, h, confidence, frameWidth, frameHeight);
            if (candidate == null)
                continue;

            var rect = new Rect(
                (int)MathF.Round(candidate.X),
                (int)MathF.Round(candidate.Y),
                Math.Max(1, (int)MathF.Round(candidate.Width)),
                Math.Max(1, (int)MathF.Round(candidate.Height)));
            candidates.Add(candidate);
            nmsBoxes.Add(rect);
            nmsScores.Add(confidence);
        }

        if (candidates.Count == 0)
            return [];

        CvDnn.NMSBoxes(nmsBoxes, nmsScores, 0.7f, 0.4f, out var picked, 1.0f, 0);
        return picked.Length == 0 ? [] : picked.Select(index => candidates[index]).ToList();
    }

    private static int inferOutputColumns(Mat output, int dataLength)
    {
        if (output.Dims > 0)
        {
            var last = output.Size(output.Dims - 1);
            if (last == 2 || last == 4)
                return last;
        }

        if (output.Cols == 2 || output.Cols == 4)
            return output.Cols;

        if (output.Channels() == 2 || output.Channels() == 4)
            return output.Channels();

        if (dataLength % 4 == 0)
            return 4;

        if (dataLength % 2 == 0)
            return 2;

        return 0;
    }

    private static IReadOnlyList<Rect2f> generateUltralightPriors(int inputWidth, int inputHeight)
    {
        float[][] minBoxes =
        [
            [10.0f, 16.0f, 24.0f],
            [32.0f, 48.0f],
            [64.0f, 96.0f],
            [128.0f, 192.0f, 256.0f]
        ];
        int[] strides = [8, 16, 32, 64];
        List<Rect2f> priors = [];

        for (var level = 0; level < strides.Length; level++)
        {
            var stride = strides[level];
            var featureMapWidth = (int)Math.Ceiling(inputWidth / (double)stride);
            var featureMapHeight = (int)Math.Ceiling(inputHeight / (double)stride);
            for (var y = 0; y < featureMapHeight; y++)
            {
                for (var x = 0; x < featureMapWidth; x++)
                {
                    var centerX = (x + 0.5f) * stride / inputWidth;
                    var centerY = (y + 0.5f) * stride / inputHeight;
                    foreach (var box in minBoxes[level])
                        priors.Add(new Rect2f(centerX, centerY, box / inputWidth, box / inputHeight));
                }
            }
        }

        return priors;
    }

    private static IReadOnlyList<FaceBox> mergeFaceBoxes(int frameWidth, int frameHeight, IReadOnlyList<DetectedFace> faces)
    {
        var cleaned = faces
            .Select(face => clampFace(face.X, face.Y, face.Width, face.Height, face.Confidence, frameWidth, frameHeight))
            .Where(face => face != null)
            .Select(face => face!)
            .ToList();

        if (cleaned.Count <= 1)
            return cleaned.Select(toFaceBox).ToList();

        var frameArea = frameWidth * frameHeight;
        var averageArea = cleaned.Average(face => Math.Max(1.0f, face.Width) * Math.Max(1.0f, face.Height));
        var treatAsParts = cleaned.Count >= 4 && averageArea < frameArea * 0.01f;
        var parent = Enumerable.Range(0, cleaned.Count).ToArray();

        for (var i = 0; i < cleaned.Count; i++)
        {
            for (var j = i + 1; j < cleaned.Count; j++)
            {
                if (intersectionOverUnion(cleaned[i], cleaned[j]) >= 0.2f ||
                    treatAsParts && expandedIntersects(cleaned[i], cleaned[j]))
                    union(parent, i, j);
            }
        }

        var clusters = new Dictionary<int, List<DetectedFace>>();
        for (var i = 0; i < cleaned.Count; i++)
        {
            var root = find(parent, i);
            if (!clusters.TryGetValue(root, out var cluster))
            {
                cluster = [];
                clusters[root] = cluster;
            }

            cluster.Add(cleaned[i]);
        }

        return clusters.Values
            .Select(cluster =>
            {
                var x1 = cluster.Min(face => face.X);
                var y1 = cluster.Min(face => face.Y);
                var x2 = cluster.Max(face => face.X + face.Width);
                var y2 = cluster.Max(face => face.Y + face.Height);
                var confidence = cluster.Max(face => face.Confidence);
                return toFaceBox(new DetectedFace(x1, y1, x2 - x1, y2 - y1, confidence));
            })
            .OrderBy(face => face.X1)
            .ThenBy(face => face.Y1)
            .ThenBy(face => (face.X2 - face.X1) * (face.Y2 - face.Y1))
            .ToList();
    }

    private static DetectedFace? clampFace(float x, float y, float width, float height, float confidence, int frameWidth, int frameHeight)
    {
        var x1 = Math.Clamp(x, 0, Math.Max(0, frameWidth - 1));
        var y1 = Math.Clamp(y, 0, Math.Max(0, frameHeight - 1));
        var x2 = Math.Clamp(x + width, 0, frameWidth);
        var y2 = Math.Clamp(y + height, 0, frameHeight);
        if (x2 <= x1 || y2 <= y1)
            return null;

        return new DetectedFace(x1, y1, x2 - x1, y2 - y1, confidence);
    }

    private static FaceBox toFaceBox(DetectedFace face)
    {
        return new FaceBox(face.X, face.Y, face.X + face.Width, face.Y + face.Height, face.Confidence);
    }

    private static float intersectionOverUnion(DetectedFace a, DetectedFace b)
    {
        var x1 = Math.Max(a.X, b.X);
        var y1 = Math.Max(a.Y, b.Y);
        var x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        var intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        if (intersection <= 0)
            return 0;

        var areaA = Math.Max(1, a.Width * a.Height);
        var areaB = Math.Max(1, b.Width * b.Height);
        return intersection / (areaA + areaB - intersection);
    }

    private static bool expandedIntersects(DetectedFace a, DetectedFace b)
    {
        var margin = 0.6f * Math.Min(Math.Max(a.Width, a.Height), Math.Max(b.Width, b.Height));
        return Math.Min(a.X + a.Width + margin, b.X + b.Width + margin) - Math.Max(a.X - margin, b.X - margin) > 0 &&
               Math.Min(a.Y + a.Height + margin, b.Y + b.Height + margin) - Math.Max(a.Y - margin, b.Y - margin) > 0;
    }

    private static int find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }

        return index;
    }

    private static void union(int[] parent, int left, int right)
    {
        var leftRoot = find(parent, left);
        var rightRoot = find(parent, right);
        if (leftRoot != rightRoot)
            parent[rightRoot] = leftRoot;
    }
}
