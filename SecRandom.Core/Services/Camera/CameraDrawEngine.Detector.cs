using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using SecRandom.Core.Models.Camera;
using SecRandom.Shared;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

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

    private readonly object _detectorLock = new();
    private DetectorBackend _activeBackend;
    private string? _damoyoloInputName;
    private InferenceSession? _damoyoloSession;
    private Size _detectorInputSize;
    private string? _lastDetectionErrorMessage;
    private string? _loadedDetectorModel;
    private string? _ultralightInputName;
    private InferenceSession? _ultralightSession;
    private FaceDetectorYN? _yunetDetector;

    private (IReadOnlyList<FaceBox> Faces, DetectionState State) DetectFaces(Mat frameBgr)
    {
        lock (_detectorLock)
        {
            try
            {
                EnsureDetectorLoaded();

                var faces = _activeBackend switch
                {
                    DetectorBackend.YuNet => DetectFacesWithYunet(frameBgr),
                    DetectorBackend.Damoyolo => DetectFacesWithDamoyolo(frameBgr),
                    DetectorBackend.Ultralight => DetectFacesWithUltralight(frameBgr),
                    _ => Array.Empty<DetectedFace>()
                };

                _lastDetectionErrorMessage = null;
                var mergedFaces = MergeFaceBoxes(frameBgr.Width, frameBgr.Height, faces);
                return (mergedFaces, mergedFaces.Count == 0 ? DetectionState.NoFace : DetectionState.HasFaces);
            }
            catch (Exception ex)
            {
                if (_lastDetectionErrorMessage != ex.Message)
                {
                    _lastDetectionErrorMessage = ex.Message;
                    _logger.LogDebug(ex, "Face detection failed.");
                }

                return ([], DetectionState.Error);
            }
        }
    }

    private void _ReloadDetector()
    {
        lock (_detectorLock)
        {
            DisposeDetector();
            _loadedDetectorModel = null;
            _lastDetectionErrorMessage = null;
        }
    }

    private void DisposeDetector()
    {
        _yunetDetector?.Dispose();
        _yunetDetector = null;

        _ultralightSession?.Dispose();
        _ultralightSession = null;
        _ultralightInputName = null;

        _damoyoloSession?.Dispose();
        _damoyoloSession = null;
        _damoyoloInputName = null;
    }

    private void EnsureDetectorLoaded()
    {
        var modelName = string.IsNullOrWhiteSpace(DetectorModel)
            ? "version-RFB-640.onnx"
            : DetectorModel.Trim();

        var backend = ResolveDetectorBackend(modelName);
        var inputSize = backend == DetectorBackend.Damoyolo
            ? new Size(DamoyoloInputWidth, DamoyoloInputHeight)
            : ResolveDetectorInputSize(modelName);

        if (_loadedDetectorModel == modelName && _detectorInputSize == inputSize && IsDetectorLoaded(backend))
            return;

        DisposeDetector();

        var modelPath = ResolveDetectorModelPath(modelName);
        EnsureModelFileIsUsable(modelPath);

        switch (backend)
        {
            case DetectorBackend.YuNet:
                _yunetDetector = FaceDetectorYN.Create(
                    modelPath,
                    string.Empty,
                    inputSize,
                    YuNetConfidenceThreshold);
                break;

            case DetectorBackend.Damoyolo:
                _damoyoloSession = new InferenceSession(modelPath);
                _damoyoloInputName = GetSingleInputName(_damoyoloSession, modelName);
                LogOrtMetadata("DAMOYOLO", _damoyoloSession);
                break;

            case DetectorBackend.Ultralight:
                _ultralightSession = new InferenceSession(modelPath);
                _ultralightInputName = GetSingleInputName(_ultralightSession, modelName);
                LogOrtMetadata("Ultralight", _ultralightSession);
                break;
        }

        _activeBackend = backend;
        _loadedDetectorModel = modelName;
        _detectorInputSize = inputSize;
    }

    private bool IsDetectorLoaded(DetectorBackend backend)
    {
        return backend switch
        {
            DetectorBackend.YuNet => _yunetDetector != null,
            DetectorBackend.Ultralight => _ultralightSession != null,
            DetectorBackend.Damoyolo => _damoyoloSession != null,
            _ => false
        };
    }

    private static DetectorBackend ResolveDetectorBackend(string modelName)
    {
        if (modelName.Contains("yunet", StringComparison.OrdinalIgnoreCase))
            return DetectorBackend.YuNet;

        if (modelName.Contains("yolo", StringComparison.OrdinalIgnoreCase))
            return DetectorBackend.Damoyolo;

        return DetectorBackend.Ultralight;
    }

    private static string ResolveDetectorModelPath(string modelName)
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

        throw new FileNotFoundException(CameraText("DetectorModelNotFound", modelName), outputPath);
    }

    private static void EnsureModelFileIsUsable(string modelPath)
    {
        var info = new FileInfo(modelPath);
        if (!info.Exists || info.Length <= 0)
            throw new FileNotFoundException(CameraText("DetectorModelNotFound", Path.GetFileName(modelPath)),
                modelPath);

        if (info.Length <= 512)
        {
            var header = File.ReadAllText(modelPath);
            if (header.StartsWith("version https://git-lfs.github.com/spec", StringComparison.Ordinal))
                throw new InvalidOperationException(CameraText("DetectorModelIsLfsPointer", modelPath));
        }
    }

    private Size ResolveDetectorInputSize(string modelName)
    {
        if (modelName.Contains("640", StringComparison.OrdinalIgnoreCase))
            return new Size(640, 480);

        if (modelName.Contains("320", StringComparison.OrdinalIgnoreCase))
            return new Size(320, 240);

        var width = TargetWidth > 0 ? TargetWidth : 0;
        var height = TargetHeight > 0 ? TargetHeight : 0;
        if (width > 0 && height > 0)
            return new Size(width, height);

        return new Size(320, 240);
    }

    private IReadOnlyList<DetectedFace> DetectFacesWithYunet(Mat frameBgr)
    {
        if (_yunetDetector == null)
            return [];

        using var resized = ResizeForDetector(frameBgr, _detectorInputSize);

        using var faces = new Mat();
        var count = _yunetDetector.Detect(resized, faces);
        if (count <= 0 || faces.Empty())
            return [];

        var scaleX = (float)frameBgr.Width / _detectorInputSize.Width;
        var scaleY = (float)frameBgr.Height / _detectorInputSize.Height;
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

    private IReadOnlyList<DetectedFace> DetectFacesWithUltralight(Mat frameBgr)
    {
        if (_ultralightSession == null || string.IsNullOrWhiteSpace(_ultralightInputName))
            return [];

        using var resized = ResizeForDetector(frameBgr, _detectorInputSize);
        var tensor = CreateTensorFromMat(
            resized,
            _detectorInputSize,
            1.0f / 128.0f,
            127.0f,
            127.0f,
            127.0f,
            true);

        var outputs = RunOrt(_ultralightSession, _ultralightInputName, tensor);
        return ParseUltralightOutputs(outputs, frameBgr.Width, frameBgr.Height);
    }

    private IReadOnlyList<DetectedFace> DetectFacesWithDamoyolo(Mat frameBgr)
    {
        if (_damoyoloSession == null || string.IsNullOrWhiteSpace(_damoyoloInputName))
            return [];

        var inputSize = new Size(DamoyoloInputWidth, DamoyoloInputHeight);
        var preprocess = DamoyoloUseLetterbox
            ? CreateLetterboxTensorFromMat(
                frameBgr,
                inputSize,
                1.0f / 255.0f,
                0.0f,
                0.0f,
                0.0f,
                true)
            : CreateDirectResizeTensorFromMat(
                frameBgr,
                inputSize,
                1.0f / 255.0f,
                0.0f,
                0.0f,
                0.0f,
                true);

        var outputs = RunOrt(_damoyoloSession, _damoyoloInputName, preprocess.Tensor);
        LogDamoyoloOutputSummary(outputs);
        return ParseDamoyoloOutputs(outputs, frameBgr.Width, frameBgr.Height, preprocess);
    }

    private static Mat ResizeForDetector(Mat frameBgr, Size inputSize)
    {
        if (frameBgr.Width == inputSize.Width && frameBgr.Height == inputSize.Height)
            return frameBgr.Clone();

        var resized = new Mat();
        Cv2.Resize(frameBgr, resized, inputSize);
        return resized;
    }

    private static DenseTensor<float> CreateTensorFromMat(
        Mat frameBgr,
        Size inputSize,
        float scaleFactor,
        float mean0,
        float mean1,
        float mean2,
        bool swapRb)
    {
        using var source = EnsureBgr8(frameBgr);
        using var resized = ResizeForDetector(source, inputSize);
        return CreateNchwTensorFromBgr8(resized, scaleFactor, mean0, mean1, mean2, swapRb);
    }

    private static TensorPreprocessResult CreateDirectResizeTensorFromMat(
        Mat frameBgr,
        Size inputSize,
        float scaleFactor,
        float mean0,
        float mean1,
        float mean2,
        bool swapRb)
    {
        using var source = EnsureBgr8(frameBgr);
        using var resized = ResizeForDetector(source, inputSize);
        var tensor = CreateNchwTensorFromBgr8(resized, scaleFactor, mean0, mean1, mean2, swapRb);

        var scaleX = (float)inputSize.Width / source.Width;
        var scaleY = (float)inputSize.Height / source.Height;
        return new TensorPreprocessResult(tensor, scaleX, scaleY, 0, 0, inputSize);
    }

    private static TensorPreprocessResult CreateLetterboxTensorFromMat(
        Mat frameBgr,
        Size inputSize,
        float scaleFactor,
        float mean0,
        float mean1,
        float mean2,
        bool swapRb)
    {
        using var source = EnsureBgr8(frameBgr);

        var scale = Math.Min((float)inputSize.Width / source.Width, (float)inputSize.Height / source.Height);
        var newWidth = Math.Max(1, (int)(source.Width * scale));
        var newHeight = Math.Max(1, (int)(source.Height * scale));
        var padX = (inputSize.Width - newWidth) / 2;
        var padY = (inputSize.Height - newHeight) / 2;

        using var resizedFrame = new Mat();
        Cv2.Resize(source, resizedFrame, new Size(newWidth, newHeight));

        using var padded = new Mat(inputSize, MatType.CV_8UC3, new Scalar(0, 0, 0));
        using (var roi = new Mat(padded, new Rect(padX, padY, newWidth, newHeight)))
        {
            resizedFrame.CopyTo(roi);
        }

        var tensor = CreateNchwTensorFromBgr8(padded, scaleFactor, mean0, mean1, mean2, swapRb);
        return new TensorPreprocessResult(tensor, scale, scale, padX, padY, inputSize);
    }

    private static Mat EnsureBgr8(Mat frame)
    {
        if (frame.Empty())
            throw new InvalidOperationException(CameraText("DetectorOutputUnsupported"));

        if (frame.Type() == MatType.CV_8UC3)
            return frame.Clone();

        var converted = new Mat();
        if (frame.Channels() == 4)
            Cv2.CvtColor(frame, converted, ColorConversionCodes.BGRA2BGR);
        else if (frame.Channels() == 1)
            Cv2.CvtColor(frame, converted, ColorConversionCodes.GRAY2BGR);
        else if (frame.Channels() == 3)
            frame.ConvertTo(converted, MatType.CV_8UC3);
        else
            throw new NotSupportedException(
                $"Unsupported detector frame format: channels={frame.Channels()}, type={frame.Type()}.");

        return converted;
    }

    private static DenseTensor<float> CreateNchwTensorFromBgr8(
        Mat imageBgr,
        float scaleFactor,
        float mean0,
        float mean1,
        float mean2,
        bool swapRb)
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

                    if (swapRb)
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

    private IReadOnlyList<OrtOutput> RunOrt(InferenceSession session, string inputName, DenseTensor<float> inputTensor)
    {
        var inputs = new[]
        {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
        };

        using var results = session.Run(inputs);
        return results.Select(ReadOrtOutput).ToList();
    }

    private static OrtOutput ReadOrtOutput(DisposableNamedOnnxValue output)
    {
        return output.Value switch
        {
            Tensor<float> tensor => CreateOrtOutput(output.Name, tensor, static value => value),
            Tensor<double> tensor => CreateOrtOutput(output.Name, tensor, static value => (float)value),
            Tensor<int> tensor => CreateOrtOutput(output.Name, tensor, static value => value),
            Tensor<long> tensor => CreateOrtOutput(output.Name, tensor, static value => value),
            Tensor<short> tensor => CreateOrtOutput(output.Name, tensor, static value => value),
            Tensor<byte> tensor => CreateOrtOutput(output.Name, tensor, static value => value),
            _ => throw new NotSupportedException(
                $"Unsupported ONNX output type for '{output.Name}': {output.Value?.GetType().FullName ?? "null"}.")
        };
    }

    private static OrtOutput CreateOrtOutput<T>(string name, Tensor<T> tensor, Func<T, float> convert)
    {
        var raw = tensor.ToArray();
        var data = new float[raw.Length];
        for (var i = 0; i < raw.Length; i++)
            data[i] = convert(raw[i]);

        return new OrtOutput(name, tensor.Dimensions.ToArray(), data);
    }

    private static string GetSingleInputName(InferenceSession session, string modelName)
    {
        var inputName = session.InputMetadata.Keys.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(inputName))
            throw new InvalidOperationException($"Detector model '{modelName}' has no ONNX input.");

        return inputName;
    }

    private void LogOrtMetadata(string backendName, InferenceSession session)
    {
        foreach (var (name, metadata) in session.InputMetadata)
            _logger.LogDebug(
                "{Backend} ONNX input: {Name}, type={Type}, shape=[{Shape}]",
                backendName,
                name,
                metadata.ElementType,
                string.Join(", ", metadata.Dimensions.Select(FormatOrtDimension)));

        foreach (var (name, metadata) in session.OutputMetadata)
            _logger.LogDebug(
                "{Backend} ONNX output: {Name}, type={Type}, shape=[{Shape}]",
                backendName,
                name,
                metadata.ElementType,
                string.Join(", ", metadata.Dimensions.Select(FormatOrtDimension)));
    }

    private static string FormatOrtDimension(int dimension)
    {
        return dimension <= 0 ? "?" : dimension.ToString();
    }

    private void LogDamoyoloOutputSummary(IReadOnlyList<OrtOutput> outputs)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
            return;

        var numDets = FindOutputByName(outputs, "num_dets")?.Data.FirstOrDefault();
        var scores = FindOutputByName(outputs, "det_scores")?.Data ?? [];
        var maxScore = scores.Length == 0 ? 0.0f : scores.Max();
        var topScores = scores
            .OrderByDescending(score => score)
            .Take(5)
            .Select(score => score.ToString("0.000"));

        _logger.LogDebug(
            "DAMOYOLO summary: num_dets={NumDets}, max_score={MaxScore:0.000}, top_scores=[{TopScores}]",
            numDets,
            maxScore,
            string.Join(", ", topScores));
    }

    private static IReadOnlyList<DetectedFace> ParseUltralightOutputs(
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

            var columns = InferOutputColumns(output);
            if (columns == 2)
                scores = output.Data;
            else if (columns == 4)
                boxes = output.Data;
        }

        if (scores == null || boxes == null)
            throw new InvalidOperationException(CameraText("DetectorOutputUnsupported"));

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

            var candidate = ClampFace(x1, y1, x2 - x1, y2 - y1, confidence, frameWidth, frameHeight);
            if (candidate != null)
                candidates.Add(candidate);
        }

        return ApplyNms(candidates, UltralightConfidenceThreshold, UltralightNmsThreshold);
    }

    private static IReadOnlyList<DetectedFace> ParseDamoyoloOutputs(
        IReadOnlyList<OrtOutput> outputs,
        int frameWidth,
        int frameHeight,
        TensorPreprocessResult preprocess)
    {
        var numDetsOutput = FindOutputByName(outputs, "num_dets") ??
                            outputs.FirstOrDefault(output => output.Data.Length == 1 &&
                                                             output.Name.Contains("num",
                                                                 StringComparison.OrdinalIgnoreCase));
        var boxesOutput = FindOutputByName(outputs, "det_boxes") ??
                          outputs.FirstOrDefault(output =>
                              output.Name.Contains("box", StringComparison.OrdinalIgnoreCase) &&
                              InferOutputColumns(output) == 4) ??
                          outputs.FirstOrDefault(output => InferOutputColumns(output) == 4);
        var scoresOutput = FindOutputByName(outputs, "det_scores") ??
                           outputs.FirstOrDefault(output =>
                               output.Name.Contains("score", StringComparison.OrdinalIgnoreCase));

        if (boxesOutput != null && scoresOutput != null)
            return ParseDamoyoloEndToEndOutput(
                numDetsOutput,
                boxesOutput,
                scoresOutput,
                frameWidth,
                frameHeight,
                preprocess);

        var sixColumnOutput = outputs.FirstOrDefault(output => InferOutputColumns(output) == 6);
        if (sixColumnOutput != null)
            return ParseDamoyoloSixColumnOutput(sixColumnOutput, frameWidth, frameHeight, preprocess);

        throw new InvalidOperationException(CameraText("DetectorOutputUnsupported"));
    }

    private static IReadOnlyList<DetectedFace> ParseDamoyoloEndToEndOutput(
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
            var candidate = MapDamoyoloFace(
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

    private static IReadOnlyList<DetectedFace> ParseDamoyoloSixColumnOutput(
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

            var candidate = MapDamoyoloFace(
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

            candidate = MapDamoyoloFace(
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

    private static DetectedFace? MapDamoyoloFace(
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

        return ClampFace(x1Orig, y1Orig, x2Orig - x1Orig, y2Orig - y1Orig, confidence, frameWidth, frameHeight);
    }

    private static OrtOutput? FindOutputByName(IReadOnlyList<OrtOutput> outputs, string expectedName)
    {
        return outputs.FirstOrDefault(output =>
                   string.Equals(output.Name, expectedName, StringComparison.OrdinalIgnoreCase)) ??
               outputs.FirstOrDefault(output =>
                   output.Name.EndsWith($"/{expectedName}", StringComparison.OrdinalIgnoreCase)) ??
               outputs.FirstOrDefault(output => output.Name.Contains(expectedName, StringComparison.OrdinalIgnoreCase));
    }

    private static int InferOutputColumns(OrtOutput output)
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

    private static IReadOnlyList<DetectedFace> ApplyNms(
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
                if (IntersectionOverUnion(candidates[current], candidates[other]) >= iouThreshold)
                    order.RemoveAt(i);
            }
        }

        return picked.Select(index => candidates[index]).ToList();
    }

    private static IReadOnlyList<FaceBox> MergeFaceBoxes(int frameWidth, int frameHeight,
        IReadOnlyList<DetectedFace> faces)
    {
        var cleaned = faces
            .Select(face =>
                ClampFace(face.X, face.Y, face.Width, face.Height, face.Confidence, frameWidth, frameHeight))
            .Where(face => face != null)
            .Select(face => face!)
            .ToList();

        if (cleaned.Count <= 1)
            return cleaned.Select(ToFaceBox).ToList();

        var frameArea = frameWidth * frameHeight;
        var averageArea = cleaned.Average(face => Math.Max(1.0f, face.Width) * Math.Max(1.0f, face.Height));
        var treatAsParts = cleaned.Count >= 4 && averageArea < frameArea * 0.01f;
        var parent = Enumerable.Range(0, cleaned.Count).ToArray();

        for (var i = 0; i < cleaned.Count; i++)
        for (var j = i + 1; j < cleaned.Count; j++)
            if (IntersectionOverUnion(cleaned[i], cleaned[j]) >= 0.2f ||
                (treatAsParts && ExpandedIntersects(cleaned[i], cleaned[j])))
                Union(parent, i, j);

        var clusters = new Dictionary<int, List<DetectedFace>>();
        for (var i = 0; i < cleaned.Count; i++)
        {
            var root = Find(parent, i);
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
                return ToFaceBox(new DetectedFace(x1, y1, x2 - x1, y2 - y1, confidence));
            })
            .OrderBy(face => face.X1)
            .ThenBy(face => face.Y1)
            .ThenBy(face => (face.X2 - face.X1) * (face.Y2 - face.Y1))
            .ToList();
    }

    private static DetectedFace? ClampFace(float x, float y, float width, float height, float confidence,
        int frameWidth, int frameHeight)
    {
        var x1 = Math.Clamp(x, 0, Math.Max(0, frameWidth - 1));
        var y1 = Math.Clamp(y, 0, Math.Max(0, frameHeight - 1));
        var x2 = Math.Clamp(x + width, 0, frameWidth);
        var y2 = Math.Clamp(y + height, 0, frameHeight);
        if (x2 <= x1 || y2 <= y1)
            return null;

        return new DetectedFace(x1, y1, x2 - x1, y2 - y1, confidence);
    }

    private static FaceBox ToFaceBox(DetectedFace face)
    {
        return new FaceBox(face.X, face.Y, face.X + face.Width, face.Y + face.Height, face.Confidence);
    }

    private static float IntersectionOverUnion(DetectedFace a, DetectedFace b)
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

    private static bool ExpandedIntersects(DetectedFace a, DetectedFace b)
    {
        var margin = 0.6f * Math.Min(Math.Max(a.Width, a.Height), Math.Max(b.Width, b.Height));
        return Math.Min(a.X + a.Width + margin, b.X + b.Width + margin) - Math.Max(a.X - margin, b.X - margin) > 0 &&
               Math.Min(a.Y + a.Height + margin, b.Y + b.Height + margin) - Math.Max(a.Y - margin, b.Y - margin) > 0;
    }

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }

        return parent[index];
    }

    private static void Union(int[] parent, int left, int right)
    {
        var leftRoot = Find(parent, left);
        var rightRoot = Find(parent, right);
        if (leftRoot != rightRoot)
            parent[rightRoot] = leftRoot;
    }

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
}