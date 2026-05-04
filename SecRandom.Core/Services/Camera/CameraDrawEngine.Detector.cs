using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using SecRandom.Core.Models.Camera;
using SecRandom.Shared;

namespace SecRandom.Core.Services.Camera;

public partial class CameraDrawEngine
{
    private sealed record DetectedFace(float X, float Y, float Width, float Height, float Confidence);

    private sealed record OnnxDetectorOutput(float[] Data, int[] Dimensions);

    private enum DetectorBackend { YuNet, Ultralight, Damoyolo }

    private readonly object detectorLock = new();
    private FaceDetectorYN? yunetDetector;
    private InferenceSession? ultralightSession;
    private InferenceSession? damoyoloSession;
    private string? ultralightInputName;
    private string? damoyoloInputName;
    private IReadOnlyList<string> ultralightOutputNames = [];
    private IReadOnlyList<string> damoyoloOutputNames = [];
    private DetectorBackend activeBackend;
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

                IReadOnlyList<DetectedFace> faces = activeBackend switch
                {
                    DetectorBackend.YuNet => detectFacesWithYunet(frameBgr),
                    DetectorBackend.Damoyolo => detectFacesWithDamoyolo(frameBgr),
                    DetectorBackend.Ultralight => detectFacesWithUltralight(frameBgr),
                    _ => Array.Empty<DetectedFace>()
                };

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
        ultralightSession?.Dispose();
        ultralightSession = null;
        ultralightInputName = null;
        ultralightOutputNames = [];
        damoyoloSession?.Dispose();
        damoyoloSession = null;
        damoyoloInputName = null;
        damoyoloOutputNames = [];
    }

    private void ensureDetectorLoaded()
    {
        var modelName = string.IsNullOrWhiteSpace(detectorModel)
            ? "version-RFB-640.onnx"
            : detectorModel.Trim();
        var backend = modelName.Contains("yunet", StringComparison.OrdinalIgnoreCase)
            ? DetectorBackend.YuNet
            : modelName.Contains("damoyolo", StringComparison.OrdinalIgnoreCase)
                ? DetectorBackend.Damoyolo
                : DetectorBackend.Ultralight;
        var inputSize = backend == DetectorBackend.Damoyolo
            ? new Size(640, 640)
            : resolveDetectorInputSize(modelName);

        if (loadedDetectorModel == modelName && detectorInputSize == inputSize &&
            (yunetDetector != null || ultralightSession != null || damoyoloSession != null))
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
            activeBackend = DetectorBackend.YuNet;
        }
        else if (modelName.Contains("damoyolo", StringComparison.OrdinalIgnoreCase))
        {
            damoyoloSession = createOnnxSession(modelPath);
            damoyoloInputName = damoyoloSession.InputMetadata.First().Key;
            damoyoloOutputNames = damoyoloSession.OutputMetadata.Keys.ToList();
            activeBackend = DetectorBackend.Damoyolo;
            detectorInputSize = new Size(640, 640);
        }
        else
        {
            ultralightSession = createOnnxSession(modelPath);
            ultralightInputName = ultralightSession.InputMetadata.First().Key;
            ultralightOutputNames = ultralightSession.OutputMetadata.Keys.ToList();
            activeBackend = DetectorBackend.Ultralight;
        }

        loadedDetectorModel = modelName;
        detectorInputSize = inputSize;
    }

    private static InferenceSession createOnnxSession(string modelPath)
    {
        using var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };
        return new InferenceSession(modelPath, sessionOptions);
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
        if (modelName.Contains("640", StringComparison.OrdinalIgnoreCase))
            return new Size(640, 480);

        if (modelName.Contains("320", StringComparison.OrdinalIgnoreCase))
            return new Size(320, 240);

        var width = targetWidth > 0 ? targetWidth : 0;
        var height = targetHeight > 0 ? targetHeight : 0;
        if (width > 0 && height > 0)
            return new Size(width, height);

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
        if (ultralightSession == null || string.IsNullOrWhiteSpace(ultralightInputName))
            return [];

        using var resized = resizeForDetector(frameBgr, detectorInputSize);
        var inputTensor = createTensorFromMat(resized, 1.0f / 128.0f, 127.0f, true);
        var outputs = runOnnxDetector(ultralightSession, ultralightInputName, ultralightOutputNames, inputTensor);
        return parseUltralightOutputs(outputs, frameBgr.Width, frameBgr.Height);
    }

    private IReadOnlyList<DetectedFace> detectFacesWithDamoyolo(Mat frameBgr)
    {
        if (damoyoloSession == null || string.IsNullOrWhiteSpace(damoyoloInputName))
            return [];

        var scale = Math.Min(640f / frameBgr.Width, 640f / frameBgr.Height);
        var newWidth = Math.Max(1, (int)(frameBgr.Width * scale));
        var newHeight = Math.Max(1, (int)(frameBgr.Height * scale));
        var padX = (640 - newWidth) / 2;
        var padY = (640 - newHeight) / 2;

        using var resizedFrame = new Mat();
        Cv2.Resize(frameBgr, resizedFrame, new Size(newWidth, newHeight), 0, 0, InterpolationFlags.Linear);
        using var resized = new Mat(new Size(640, 640), frameBgr.Type(), new Scalar(0, 0, 0));
        using (var roi = new Mat(resized, new Rect(padX, padY, newWidth, newHeight)))
        {
            resizedFrame.CopyTo(roi);
        }

        if (damoyoloOutputNames.Count == 0)
            throw new InvalidOperationException(cameraText("DetectorOutputUnsupported"));

        logger.LogDebug($"DAMOYOLO outputs: {string.Join(", ", damoyoloOutputNames)}");

        var inputTensor = createTensorFromMat(resized, 1.0f / 255.0f, 0f, true);
        var outputs = runOnnxDetector(damoyoloSession, damoyoloInputName, damoyoloOutputNames, inputTensor);
        return parseDamoyoloOutput(outputs, frameBgr.Width, frameBgr.Height);
    }

    private static Mat resizeForDetector(Mat frameBgr, Size inputSize)
    {
        if (frameBgr.Width == inputSize.Width && frameBgr.Height == inputSize.Height)
            return frameBgr.Clone();

        var resized = new Mat();
        Cv2.Resize(frameBgr, resized, inputSize, 0, 0, InterpolationFlags.Linear);
        return resized;
    }

    private static DenseTensor<float> createTensorFromMat(Mat mat, float scale, float mean, bool swapRB = true)
    {
        var height = mat.Height;
        var width = mat.Width;
        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
        var indexer = mat.GetGenericIndexer<Vec3b>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = indexer[y, x];
                var b = pixel.Item0;
                var g = pixel.Item1;
                var r = pixel.Item2;

                if (swapRB)
                {
                    tensor[0, 0, y, x] = (r - mean) * scale;
                    tensor[0, 1, y, x] = (g - mean) * scale;
                    tensor[0, 2, y, x] = (b - mean) * scale;
                }
                else
                {
                    tensor[0, 0, y, x] = (b - mean) * scale;
                    tensor[0, 1, y, x] = (g - mean) * scale;
                    tensor[0, 2, y, x] = (r - mean) * scale;
                }
            }
        }

        return tensor;
    }

    private static IReadOnlyList<OnnxDetectorOutput> runOnnxDetector(
        InferenceSession session,
        string inputName,
        IReadOnlyList<string> outputNames,
        DenseTensor<float> inputTensor)
    {
        if (outputNames.Count == 0)
            throw new InvalidOperationException(cameraText("DetectorOutputUnsupported"));

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
        };
        using var results = session.Run(inputs, outputNames);
        return results
            .Select(result =>
            {
                var tensor = result.AsTensor<float>();
                return new OnnxDetectorOutput(tensor.ToArray(), tensor.Dimensions.ToArray());
            })
            .ToList();
    }

    private IReadOnlyList<DetectedFace> parseUltralightOutputs(
        IReadOnlyList<OnnxDetectorOutput> outputs,
        int frameWidth,
        int frameHeight)
    {
        float[]? scores = null;
        float[]? boxes = null;

        foreach (var output in outputs)
        {
            var data = output.Data;
            if (data.Length == 0)
                continue;

            var columns = inferOutputColumns(output);
            if (columns == 2)
                scores = data;
            else if (columns == 4)
                boxes = data;
        }

        if (scores == null || boxes == null)
            throw new InvalidOperationException(cameraText("DetectorOutputUnsupported"));

        var count = Math.Min(scores.Length / 2, boxes.Length / 4);
        if (count <= 0)
            return [];

        List<Rect> nmsBoxes = [];
        List<float> nmsScores = [];
        List<DetectedFace> candidates = [];

        for (var i = 0; i < count; i++)
        {
            var confidence = scores[i * 2 + 1];
            if (confidence <= 0.7f)
                continue;

            var boxIndex = i * 4;
            var x1 = boxes[boxIndex] * frameWidth;
            var y1 = boxes[boxIndex + 1] * frameHeight;
            var x2 = boxes[boxIndex + 2] * frameWidth;
            var y2 = boxes[boxIndex + 3] * frameHeight;
            if (x2 <= x1 || y2 <= y1)
                continue;

            var candidate = clampFace(x1, y1, x2 - x1, y2 - y1, confidence, frameWidth, frameHeight);
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

    private static IReadOnlyList<DetectedFace> parseDamoyoloOutput(
        IReadOnlyList<OnnxDetectorOutput> outputs,
        int frameWidth,
        int frameHeight)
    {
        var outputData = outputs.Select(output => output.Data).ToList();

        if (tryParseDamoyoloFourTensorOutputs(outputs, outputData, frameWidth, frameHeight, out var fourTensorFaces))
            return fourTensorFaces;

        for (var outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
        {
            var data = outputData[outputIndex];
            if (data.Length == 0 || inferDamoyoloOutputColumns(outputs[outputIndex]) != 6)
                continue;

            List<DetectedFace> results = [];
            var count = data.Length / 6;
            for (var i = 0; i < count; i++)
            {
                var offset = i * 6;
                var confidence = data[offset + 4];
                if (confidence <= 0.7f)
                    continue;

                var candidate = mapDamoyoloFace(
                    data[offset],
                    data[offset + 1],
                    data[offset + 2],
                    data[offset + 3],
                    confidence,
                    frameWidth,
                    frameHeight);
                if (candidate != null)
                {
                    results.Add(candidate);
                    continue;
                }

                candidate = mapDamoyoloFace(
                    data[offset + 2],
                    data[offset + 3],
                    data[offset + 4],
                    data[offset + 5],
                    data[offset + 1],
                    frameWidth,
                    frameHeight);
                if (candidate != null)
                    results.Add(candidate);
            }

            return results;
        }

        throw new InvalidOperationException(cameraText("DetectorOutputUnsupported"));
    }

    private static bool tryParseDamoyoloFourTensorOutputs(
        IReadOnlyList<OnnxDetectorOutput> outputs,
        IReadOnlyList<float[]> outputData,
        int frameWidth,
        int frameHeight,
        out IReadOnlyList<DetectedFace> faces)
    {
        faces = [];
        if (outputs.Count < 4 || outputData.Count < 4)
            return false;

        var numDets = outputData[0].Length > 0 ? Math.Max(0, (int)MathF.Round(outputData[0][0])) : 0;
        var boxes = outputData[1];
        var scores = outputData[2];
        if (inferDamoyoloOutputColumns(outputs[1]) != 4 || scores.Length == 0)
        {
            var boxesIndex = -1;
            var numIndex = -1;
            for (var i = 0; i < outputs.Count; i++)
            {
                if (numIndex < 0 && outputData[i].Length == 1)
                    numIndex = i;
                if (boxesIndex < 0 && inferDamoyoloOutputColumns(outputs[i]) == 4)
                    boxesIndex = i;
            }

            if (boxesIndex < 0)
                return false;

            boxes = outputData[boxesIndex];
            if (numIndex >= 0)
                numDets = Math.Max(0, (int)MathF.Round(outputData[numIndex][0]));

            var scoreIndex = Enumerable.Range(0, outputs.Count)
                .FirstOrDefault(index => index != boxesIndex && index != numIndex && outputData[index].Length >= boxes.Length / 4);
            if (scoreIndex == boxesIndex || scoreIndex == numIndex || outputData[scoreIndex].Length < boxes.Length / 4)
                return false;

            scores = outputData[scoreIndex];
        }

        var count = numDets > 0
            ? Math.Min(numDets, Math.Min(boxes.Length / 4, scores.Length))
            : Math.Min(boxes.Length / 4, scores.Length);
        if (count <= 0)
        {
            faces = [];
            return true;
        }

        List<DetectedFace> results = [];
        for (var i = 0; i < count; i++)
        {
            var confidence = scores[i];
            if (confidence <= 0.7f)
                continue;

            var boxIndex = i * 4;
            var candidate = mapDamoyoloFace(
                boxes[boxIndex],
                boxes[boxIndex + 1],
                boxes[boxIndex + 2],
                boxes[boxIndex + 3],
                confidence,
                frameWidth,
                frameHeight);
            if (candidate != null)
                results.Add(candidate);
        }

        faces = results;
        return true;
    }

    private static DetectedFace? mapDamoyoloFace(
        float x1,
        float y1,
        float x2,
        float y2,
        float confidence,
        int frameWidth,
        int frameHeight)
    {
        if (confidence <= 0.7f || x2 <= x1 || y2 <= y1)
            return null;

        if (Math.Max(Math.Max(x1, y1), Math.Max(x2, y2)) <= 1.5f)
        {
            x1 *= 640f;
            y1 *= 640f;
            x2 *= 640f;
            y2 *= 640f;
        }

        float scale = Math.Min(640f / frameWidth, 640f / frameHeight);
        int newWidth = (int)(frameWidth * scale);
        int newHeight = (int)(frameHeight * scale);
        int padX = (640 - newWidth) / 2;
        int padY = (640 - newHeight) / 2;
        float x1Orig = (x1 - padX) / scale;
        float y1Orig = (y1 - padY) / scale;
        float x2Orig = (x2 - padX) / scale;
        float y2Orig = (y2 - padY) / scale;

        return clampFace(x1Orig, y1Orig, x2Orig - x1Orig, y2Orig - y1Orig, confidence, frameWidth, frameHeight);
    }

    private static int inferDamoyoloOutputColumns(OnnxDetectorOutput output)
    {
        var dataLength = output.Data.Length;
        if (output.Dimensions.Length > 0)
        {
            var last = output.Dimensions[^1];
            if (last == 4 || last == 6)
                return last;
        }

        if (dataLength % 6 == 0)
            return 6;

        if (dataLength % 4 == 0)
            return 4;

        return 0;
    }

    private static int inferOutputColumns(OnnxDetectorOutput output)
    {
        var dataLength = output.Data.Length;
        if (output.Dimensions.Length > 0)
        {
            var last = output.Dimensions[^1];
            if (last == 2 || last == 4)
                return last;
        }

        if (dataLength % 4 == 0)
            return 4;

        if (dataLength % 2 == 0)
            return 2;

        return 0;
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
