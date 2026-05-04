using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using SecRandom.Core.Models.Camera;
using SecRandom.Shared;
using System.Runtime.InteropServices;

namespace SecRandom.Core.Services.Camera;

public partial class CameraDrawEngine
{
    private const float YuNetConfidenceThreshold = 0.85f;
    private const float UltralightConfidenceThreshold = 0.70f;
    private const float UltralightNmsThreshold = 0.40f;
    private const float DamoyoloConfidenceThreshold = 0.30f;
    private const int DamoyoloInputWidth = 640;
    private const int DamoyoloInputHeight = 640;
    private const bool DamoyoloUseLetterbox = false;

    private sealed record DetectedFace(float X, float Y, float Width, float Height, float Confidence);

    private enum DetectorBackend
    {
        YuNet,
        Ultralight,
        Damoyolo
    }

    private sealed record TensorPreprocessResult(
        DenseTensor<float> Tensor,
        float ScaleX,
        float ScaleY,
        int PadX,
        int PadY,
        Size InputSize);

    private sealed record OrtOutput(string Name, int[] Dimensions, float[] Data)
    {
        public int LastDimension => Dimensions.Length == 0 ? 0 : Dimensions[^1];
    }

    private readonly object detectorLock = new();
    private FaceDetectorYN? yunetDetector;
    private InferenceSession? ultralightSession;
    private InferenceSession? damoyoloSession;
    private string? ultralightInputName;
    private string? damoyoloInputName;
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

        damoyoloSession?.Dispose();
        damoyoloSession = null;
        damoyoloInputName = null;
    }

    private void ensureDetectorLoaded()
    {
        var modelName = string.IsNullOrWhiteSpace(detectorModel)
            ? "version-RFB-640.onnx"
            : detectorModel.Trim();

        var backend = resolveDetectorBackend(modelName);
        var inputSize = backend == DetectorBackend.Damoyolo
            ? new Size(DamoyoloInputWidth, DamoyoloInputHeight)
            : resolveDetectorInputSize(modelName);

        if (loadedDetectorModel == modelName && detectorInputSize == inputSize && isDetectorLoaded(backend))
            return;

        disposeDetector();

        var modelPath = resolveDetectorModelPath(modelName);
        ensureModelFileIsUsable(modelPath);

        switch (backend)
        {
            case DetectorBackend.YuNet:
                yunetDetector = FaceDetectorYN.Create(
                    modelPath,
                    string.Empty,
                    inputSize,
                    YuNetConfidenceThreshold,
                    0.3f,
                    5000,
                    Backend.DEFAULT,
                    Target.CPU);
                break;

            case DetectorBackend.Damoyolo:
                damoyoloSession = new InferenceSession(modelPath);
                damoyoloInputName = getSingleInputName(damoyoloSession, modelName);
                logOrtMetadata("DAMOYOLO", damoyoloSession);
                break;

            case DetectorBackend.Ultralight:
                ultralightSession = new InferenceSession(modelPath);
                ultralightInputName = getSingleInputName(ultralightSession, modelName);
                logOrtMetadata("Ultralight", ultralightSession);
                break;
        }

        activeBackend = backend;
        loadedDetectorModel = modelName;
        detectorInputSize = inputSize;
    }

    private bool isDetectorLoaded(DetectorBackend backend)
    {
        return backend switch
        {
            DetectorBackend.YuNet => yunetDetector != null,
            DetectorBackend.Ultralight => ultralightSession != null,
            DetectorBackend.Damoyolo => damoyoloSession != null,
            _ => false
        };
    }

    private static DetectorBackend resolveDetectorBackend(string modelName)
    {
        if (modelName.Contains("yunet", StringComparison.OrdinalIgnoreCase))
            return DetectorBackend.YuNet;

        if (modelName.Contains("yolo", StringComparison.OrdinalIgnoreCase))
            return DetectorBackend.Damoyolo;

        return DetectorBackend.Ultralight;
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
            if (confidence < YuNetConfidenceThreshold)
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
        var tensor = createTensorFromMat(
            resized,
            detectorInputSize,
            scaleFactor: 1.0f / 128.0f,
            mean0: 127.0f,
            mean1: 127.0f,
            mean2: 127.0f,
            swapRB: true);

        var outputs = runOrt(ultralightSession, ultralightInputName, tensor);
        return parseUltralightOutputs(outputs, frameBgr.Width, frameBgr.Height);
    }

    private IReadOnlyList<DetectedFace> detectFacesWithDamoyolo(Mat frameBgr)
    {
        if (damoyoloSession == null || string.IsNullOrWhiteSpace(damoyoloInputName))
            return [];

        var inputSize = new Size(DamoyoloInputWidth, DamoyoloInputHeight);
        var preprocess = DamoyoloUseLetterbox
            ? createLetterboxTensorFromMat(
                frameBgr,
                inputSize,
                scaleFactor: 1.0f / 255.0f,
                mean0: 0.0f,
                mean1: 0.0f,
                mean2: 0.0f,
                swapRB: true)
            : createDirectResizeTensorFromMat(
                frameBgr,
                inputSize,
                scaleFactor: 1.0f / 255.0f,
                mean0: 0.0f,
                mean1: 0.0f,
                mean2: 0.0f,
                swapRB: true);

        var outputs = runOrt(damoyoloSession, damoyoloInputName, preprocess.Tensor);
        logDamoyoloOutputSummary(outputs);
        return parseDamoyoloOutputs(outputs, frameBgr.Width, frameBgr.Height, preprocess);
    }

    private static Mat resizeForDetector(Mat frameBgr, Size inputSize)
    {
        if (frameBgr.Width == inputSize.Width && frameBgr.Height == inputSize.Height)
            return frameBgr.Clone();

        var resized = new Mat();
        Cv2.Resize(frameBgr, resized, inputSize, 0, 0, InterpolationFlags.Linear);
        return resized;
    }

    private static DenseTensor<float> createTensorFromMat(
        Mat frameBgr,
        Size inputSize,
        float scaleFactor,
        float mean0,
        float mean1,
        float mean2,
        bool swapRB)
    {
        using var source = ensureBgr8(frameBgr);
        using var resized = resizeForDetector(source, inputSize);
        return createNchwTensorFromBgr8(resized, scaleFactor, mean0, mean1, mean2, swapRB);
    }

    private static TensorPreprocessResult createDirectResizeTensorFromMat(
        Mat frameBgr,
        Size inputSize,
        float scaleFactor,
        float mean0,
        float mean1,
        float mean2,
        bool swapRB)
    {
        using var source = ensureBgr8(frameBgr);
        using var resized = resizeForDetector(source, inputSize);
        var tensor = createNchwTensorFromBgr8(resized, scaleFactor, mean0, mean1, mean2, swapRB);

        var scaleX = (float)inputSize.Width / source.Width;
        var scaleY = (float)inputSize.Height / source.Height;
        return new TensorPreprocessResult(tensor, scaleX, scaleY, 0, 0, inputSize);
    }

    private static TensorPreprocessResult createLetterboxTensorFromMat(
        Mat frameBgr,
        Size inputSize,
        float scaleFactor,
        float mean0,
        float mean1,
        float mean2,
        bool swapRB)
    {
        using var source = ensureBgr8(frameBgr);

        var scale = Math.Min((float)inputSize.Width / source.Width, (float)inputSize.Height / source.Height);
        var newWidth = Math.Max(1, (int)(source.Width * scale));
        var newHeight = Math.Max(1, (int)(source.Height * scale));
        var padX = (inputSize.Width - newWidth) / 2;
        var padY = (inputSize.Height - newHeight) / 2;

        using var resizedFrame = new Mat();
        Cv2.Resize(source, resizedFrame, new Size(newWidth, newHeight), 0, 0, InterpolationFlags.Linear);

        using var padded = new Mat(inputSize, MatType.CV_8UC3, new Scalar(0, 0, 0));
        using (var roi = new Mat(padded, new Rect(padX, padY, newWidth, newHeight)))
        {
            resizedFrame.CopyTo(roi);
        }

        var tensor = createNchwTensorFromBgr8(padded, scaleFactor, mean0, mean1, mean2, swapRB);
        return new TensorPreprocessResult(tensor, scale, scale, padX, padY, inputSize);
    }

    private static Mat ensureBgr8(Mat frame)
    {
        if (frame.Empty())
            throw new InvalidOperationException(cameraText("DetectorOutputUnsupported"));

        if (frame.Type() == MatType.CV_8UC3)
            return frame.Clone();

        var converted = new Mat();
        if (frame.Channels() == 4)
        {
            Cv2.CvtColor(frame, converted, ColorConversionCodes.BGRA2BGR);
        }
        else if (frame.Channels() == 1)
        {
            Cv2.CvtColor(frame, converted, ColorConversionCodes.GRAY2BGR);
        }
        else if (frame.Channels() == 3)
        {
            frame.ConvertTo(converted, MatType.CV_8UC3);
        }
        else
        {
            throw new NotSupportedException($"Unsupported detector frame format: channels={frame.Channels()}, type={frame.Type()}.");
        }

        return converted;
    }

    private static DenseTensor<float> createNchwTensorFromBgr8(
        Mat imageBgr,
        float scaleFactor,
        float mean0,
        float mean1,
        float mean2,
        bool swapRB)
    {
        if (imageBgr.Type() != MatType.CV_8UC3)
            throw new NotSupportedException($"Detector tensor input must be CV_8UC3, got {imageBgr.Type()}.");

        var height = imageBgr.Rows;
        var width = imageBgr.Cols;
        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

        Mat? contiguousClone = null;
        var input = imageBgr;
        if (!input.IsContinuous())
        {
            contiguousClone = input.Clone();
            input = contiguousClone;
        }

        try
        {
            var bytes = new byte[checked(width * height * 3)];
            Marshal.Copy(input.Data, bytes, 0, bytes.Length);

            for (var y = 0; y < height; y++)
            {
                var rowOffset = y * width * 3;
                for (var x = 0; x < width; x++)
                {
                    var offset = rowOffset + x * 3;
                    var b = bytes[offset];
                    var g = bytes[offset + 1];
                    var r = bytes[offset + 2];

                    if (swapRB)
                    {
                        tensor[0, 0, y, x] = (r - mean0) * scaleFactor;
                        tensor[0, 1, y, x] = (g - mean1) * scaleFactor;
                        tensor[0, 2, y, x] = (b - mean2) * scaleFactor;
                    }
                    else
                    {
                        tensor[0, 0, y, x] = (b - mean0) * scaleFactor;
                        tensor[0, 1, y, x] = (g - mean1) * scaleFactor;
                        tensor[0, 2, y, x] = (r - mean2) * scaleFactor;
                    }
                }
            }

            return tensor;
        }
        finally
        {
            contiguousClone?.Dispose();
        }
    }

    private IReadOnlyList<OrtOutput> runOrt(InferenceSession session, string inputName, DenseTensor<float> inputTensor)
    {
        var inputs = new[]
        {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
        };

        using var results = session.Run(inputs);
        return results.Select(readOrtOutput).ToList();
    }

    private static OrtOutput readOrtOutput(DisposableNamedOnnxValue output)
    {
        return output.Value switch
        {
            Tensor<float> tensor => createOrtOutput(output.Name, tensor, static value => value),
            Tensor<double> tensor => createOrtOutput(output.Name, tensor, static value => (float)value),
            Tensor<int> tensor => createOrtOutput(output.Name, tensor, static value => value),
            Tensor<long> tensor => createOrtOutput(output.Name, tensor, static value => value),
            Tensor<short> tensor => createOrtOutput(output.Name, tensor, static value => value),
            Tensor<byte> tensor => createOrtOutput(output.Name, tensor, static value => value),
            _ => throw new NotSupportedException($"Unsupported ONNX output type for '{output.Name}': {output.Value?.GetType().FullName ?? "null"}.")
        };
    }

    private static OrtOutput createOrtOutput<T>(string name, Tensor<T> tensor, Func<T, float> convert)
    {
        var raw = tensor.ToArray();
        var data = new float[raw.Length];
        for (var i = 0; i < raw.Length; i++)
            data[i] = convert(raw[i]);

        return new OrtOutput(name, tensor.Dimensions.ToArray(), data);
    }

    private static string getSingleInputName(InferenceSession session, string modelName)
    {
        var inputName = session.InputMetadata.Keys.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(inputName))
            throw new InvalidOperationException($"Detector model '{modelName}' has no ONNX input.");

        return inputName;
    }

    private void logOrtMetadata(string backendName, InferenceSession session)
    {
        foreach (var (name, metadata) in session.InputMetadata)
        {
            logger.LogDebug(
                "{Backend} ONNX input: {Name}, type={Type}, shape=[{Shape}]",
                backendName,
                name,
                metadata.ElementType,
                string.Join(", ", metadata.Dimensions.Select(formatOrtDimension)));
        }

        foreach (var (name, metadata) in session.OutputMetadata)
        {
            logger.LogDebug(
                "{Backend} ONNX output: {Name}, type={Type}, shape=[{Shape}]",
                backendName,
                name,
                metadata.ElementType,
                string.Join(", ", metadata.Dimensions.Select(formatOrtDimension)));
        }
    }

    private static string formatOrtDimension(int dimension)
    {
        return dimension <= 0 ? "?" : dimension.ToString();
    }

    private void logDamoyoloOutputSummary(IReadOnlyList<OrtOutput> outputs)
    {
        if (!logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
            return;

        var numDets = findOutputByName(outputs, "num_dets")?.Data.FirstOrDefault();
        var scores = findOutputByName(outputs, "det_scores")?.Data ?? [];
        var maxScore = scores.Length == 0 ? 0.0f : scores.Max();
        var topScores = scores
            .OrderByDescending(score => score)
            .Take(5)
            .Select(score => score.ToString("0.000"));

        logger.LogDebug(
            "DAMOYOLO summary: num_dets={NumDets}, max_score={MaxScore:0.000}, top_scores=[{TopScores}]",
            numDets,
            maxScore,
            string.Join(", ", topScores));
    }

    private static IReadOnlyList<DetectedFace> parseUltralightOutputs(
        IReadOnlyList<OrtOutput> outputs,
        int frameWidth,
        int frameHeight)
    {
        float[]? scores = null;
        float[]? boxes = null;

        foreach (var output in outputs)
        {
            if (output.Data.Length == 0)
                continue;

            var columns = inferOutputColumns(output);
            if (columns == 2)
                scores = output.Data;
            else if (columns == 4)
                boxes = output.Data;
        }

        if (scores == null || boxes == null)
            throw new InvalidOperationException(cameraText("DetectorOutputUnsupported"));

        var count = Math.Min(scores.Length / 2, boxes.Length / 4);
        if (count <= 0)
            return [];

        List<DetectedFace> candidates = [];
        for (var i = 0; i < count; i++)
        {
            var confidence = scores[i * 2 + 1];
            if (confidence <= UltralightConfidenceThreshold)
                continue;

            var boxIndex = i * 4;
            var x1 = boxes[boxIndex] * frameWidth;
            var y1 = boxes[boxIndex + 1] * frameHeight;
            var x2 = boxes[boxIndex + 2] * frameWidth;
            var y2 = boxes[boxIndex + 3] * frameHeight;
            if (x2 <= x1 || y2 <= y1)
                continue;

            var candidate = clampFace(x1, y1, x2 - x1, y2 - y1, confidence, frameWidth, frameHeight);
            if (candidate != null)
                candidates.Add(candidate);
        }

        return applyNms(candidates, UltralightConfidenceThreshold, UltralightNmsThreshold);
    }

    private static IReadOnlyList<DetectedFace> parseDamoyoloOutputs(
        IReadOnlyList<OrtOutput> outputs,
        int frameWidth,
        int frameHeight,
        TensorPreprocessResult preprocess)
    {
        var numDetsOutput = findOutputByName(outputs, "num_dets") ??
                            outputs.FirstOrDefault(output => output.Data.Length == 1 &&
                                                             output.Name.Contains("num", StringComparison.OrdinalIgnoreCase));
        var boxesOutput = findOutputByName(outputs, "det_boxes") ??
                          outputs.FirstOrDefault(output => output.Name.Contains("box", StringComparison.OrdinalIgnoreCase) &&
                                                           inferOutputColumns(output) == 4) ??
                          outputs.FirstOrDefault(output => inferOutputColumns(output) == 4);
        var scoresOutput = findOutputByName(outputs, "det_scores") ??
                           outputs.FirstOrDefault(output => output.Name.Contains("score", StringComparison.OrdinalIgnoreCase));

        if (boxesOutput != null && scoresOutput != null)
        {
            return parseDamoyoloEndToEndOutput(
                numDetsOutput,
                boxesOutput,
                scoresOutput,
                frameWidth,
                frameHeight,
                preprocess);
        }

        var sixColumnOutput = outputs.FirstOrDefault(output => inferOutputColumns(output) == 6);
        if (sixColumnOutput != null)
            return parseDamoyoloSixColumnOutput(sixColumnOutput, frameWidth, frameHeight, preprocess);

        throw new InvalidOperationException(cameraText("DetectorOutputUnsupported"));
    }

    private static IReadOnlyList<DetectedFace> parseDamoyoloEndToEndOutput(
        OrtOutput? numDetsOutput,
        OrtOutput boxesOutput,
        OrtOutput scoresOutput,
        int frameWidth,
        int frameHeight,
        TensorPreprocessResult preprocess)
    {
        var boxes = boxesOutput.Data;
        var scores = scoresOutput.Data;
        var numDets = numDetsOutput?.Data.Length > 0
            ? Math.Max(0, (int)MathF.Round(numDetsOutput.Data[0]))
            : 0;

        var count = numDets > 0
            ? Math.Min(numDets, Math.Min(boxes.Length / 4, scores.Length))
            : Math.Min(boxes.Length / 4, scores.Length);

        if (count <= 0)
            return [];

        List<DetectedFace> results = [];
        for (var i = 0; i < count; i++)
        {
            var confidence = scores[i];
            if (confidence <= DamoyoloConfidenceThreshold)
                continue;

            var boxIndex = i * 4;
            var candidate = mapDamoyoloFace(
                boxes[boxIndex],
                boxes[boxIndex + 1],
                boxes[boxIndex + 2],
                boxes[boxIndex + 3],
                confidence,
                frameWidth,
                frameHeight,
                preprocess);
            if (candidate != null)
                results.Add(candidate);
        }

        return results;
    }

    private static IReadOnlyList<DetectedFace> parseDamoyoloSixColumnOutput(
        OrtOutput output,
        int frameWidth,
        int frameHeight,
        TensorPreprocessResult preprocess)
    {
        var data = output.Data;
        if (data.Length == 0 || data.Length % 6 != 0)
            return [];

        List<DetectedFace> results = [];
        var count = data.Length / 6;
        for (var i = 0; i < count; i++)
        {
            var offset = i * 6;
            var confidence = data[offset + 4];
            if (confidence <= DamoyoloConfidenceThreshold)
                continue;

            var candidate = mapDamoyoloFace(
                data[offset],
                data[offset + 1],
                data[offset + 2],
                data[offset + 3],
                confidence,
                frameWidth,
                frameHeight,
                preprocess);
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
                frameHeight,
                preprocess);
            if (candidate != null)
                results.Add(candidate);
        }

        return results;
    }

    private static DetectedFace? mapDamoyoloFace(
        float x1,
        float y1,
        float x2,
        float y2,
        float confidence,
        int frameWidth,
        int frameHeight,
        TensorPreprocessResult preprocess)
    {
        if (confidence <= DamoyoloConfidenceThreshold || x2 <= x1 || y2 <= y1)
            return null;

        if (Math.Max(Math.Max(x1, y1), Math.Max(x2, y2)) <= 1.5f)
        {
            x1 *= preprocess.InputSize.Width;
            y1 *= preprocess.InputSize.Height;
            x2 *= preprocess.InputSize.Width;
            y2 *= preprocess.InputSize.Height;
        }

        var x1Orig = (x1 - preprocess.PadX) / preprocess.ScaleX;
        var y1Orig = (y1 - preprocess.PadY) / preprocess.ScaleY;
        var x2Orig = (x2 - preprocess.PadX) / preprocess.ScaleX;
        var y2Orig = (y2 - preprocess.PadY) / preprocess.ScaleY;

        return clampFace(x1Orig, y1Orig, x2Orig - x1Orig, y2Orig - y1Orig, confidence, frameWidth, frameHeight);
    }

    private static OrtOutput? findOutputByName(IReadOnlyList<OrtOutput> outputs, string expectedName)
    {
        return outputs.FirstOrDefault(output => string.Equals(output.Name, expectedName, StringComparison.OrdinalIgnoreCase)) ??
               outputs.FirstOrDefault(output => output.Name.EndsWith($"/{expectedName}", StringComparison.OrdinalIgnoreCase)) ??
               outputs.FirstOrDefault(output => output.Name.Contains(expectedName, StringComparison.OrdinalIgnoreCase));
    }

    private static int inferOutputColumns(OrtOutput output)
    {
        if (output.LastDimension is 2 or 4 or 6)
            return output.LastDimension;

        if (output.Data.Length % 6 == 0)
            return 6;

        if (output.Data.Length % 4 == 0)
            return 4;

        if (output.Data.Length % 2 == 0)
            return 2;

        return 0;
    }

    private static IReadOnlyList<DetectedFace> applyNms(
        IReadOnlyList<DetectedFace> candidates,
        float scoreThreshold,
        float iouThreshold)
    {
        if (candidates.Count <= 1)
            return candidates.Where(candidate => candidate.Confidence >= scoreThreshold).ToList();

        var order = candidates
            .Select((candidate, index) => (candidate, index))
            .Where(item => item.candidate.Confidence >= scoreThreshold)
            .OrderByDescending(item => item.candidate.Confidence)
            .Select(item => item.index)
            .ToList();

        List<int> picked = [];
        while (order.Count > 0)
        {
            var current = order[0];
            picked.Add(current);
            order.RemoveAt(0);

            for (var i = order.Count - 1; i >= 0; i--)
            {
                var other = order[i];
                if (intersectionOverUnion(candidates[current], candidates[other]) >= iouThreshold)
                    order.RemoveAt(i);
            }
        }

        return picked.Select(index => candidates[index]).ToList();
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

        return parent[index];
    }

    private static void union(int[] parent, int left, int right)
    {
        var leftRoot = find(parent, left);
        var rightRoot = find(parent, right);
        if (leftRoot != rightRoot)
            parent[rightRoot] = leftRoot;
    }
}
