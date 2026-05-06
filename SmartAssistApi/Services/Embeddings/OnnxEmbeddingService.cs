using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SmartAssistApi.Configuration;

namespace SmartAssistApi.Services.Embeddings;

public sealed class OnnxEmbeddingService : IEmbeddingService, IDisposable
{
    private static readonly Regex TokenRegex = new(@"[a-z0-9]+|[^\s]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly ILogger<OnnxEmbeddingService> _logger;
    private readonly EmbeddingOptions _options;
    private readonly Lazy<InferenceSession> _session;
    private readonly ConcurrentDictionary<string, int> _vocab = new(StringComparer.Ordinal);
    private readonly int _unkId;
    private readonly int _clsId;
    private readonly int _sepId;
    private readonly int _padId;
    private readonly bool _isReady;
    private readonly string? _initError;

    public int Dimension => _options.Dimension;

    public OnnxEmbeddingService(
        IWebHostEnvironment env,
        IOptions<EmbeddingOptions> options,
        ILogger<OnnxEmbeddingService> logger)
    {
        _logger = logger;
        _options = options.Value;

        var vocabPath = ResolvePath(env.ContentRootPath, _options.VocabPath);
        if (!File.Exists(vocabPath))
        {
            _isReady = false;
            _initError = $"Embedding vocab file not found: {vocabPath}";
            _logger.LogError(
                "ONNX embeddings disabled. {Error} Configure EMBEDDINGS__VOCABPATH or deploy Models/* files.",
                _initError);
            _unkId = 100;
            _clsId = 101;
            _sepId = 102;
            _padId = 0;
            _session = new Lazy<InferenceSession>(() => throw new InvalidOperationException(_initError));
            return;
        }

        try
        {
            LoadVocab(vocabPath);
            _unkId = ResolveTokenId("[UNK]", 100);
            _clsId = ResolveTokenId("[CLS]", 101);
            _sepId = ResolveTokenId("[SEP]", 102);
            _padId = ResolveTokenId("[PAD]", 0);
            _isReady = true;
        }
        catch (Exception ex)
        {
            _isReady = false;
            _initError = $"Failed to initialize ONNX embeddings from vocab '{vocabPath}': {ex.Message}";
            _logger.LogError(ex, "ONNX embeddings disabled during initialization.");
            _unkId = 100;
            _clsId = 101;
            _sepId = 102;
            _padId = 0;
            _session = new Lazy<InferenceSession>(() => throw new InvalidOperationException(_initError));
            return;
        }

        _session = new Lazy<InferenceSession>(() =>
        {
            var modelPath = ResolvePath(env.ContentRootPath, _options.ModelPath);
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"ONNX embedding model not found: {modelPath}");

            var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Clamp(_options.IntraOpThreads, 1, 8),
                InterOpNumThreads = Math.Clamp(_options.InterOpThreads, 1, 8),
            };

            var session = new InferenceSession(modelPath, sessionOptions);
            _logger.LogInformation("ONNX embedding model loaded from {ModelPath}.", modelPath);
            return session;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        EnsureReady();
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Embedding input text must not be empty.", nameof(text));
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(EmbedSingle(text));
    }

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        EnsureReady();
        if (texts.Count == 0)
            return Task.FromResult(Array.Empty<float[]>());
        ct.ThrowIfCancellationRequested();

        var output = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(texts[i]))
                throw new ArgumentException("Embedding batch contains empty text.", nameof(texts));
            output[i] = EmbedSingle(texts[i]);
        }

        return Task.FromResult(output);
    }

    private float[] EmbedSingle(string text)
    {
        EnsureReady();
        var session = _session.Value;
        var tokenIds = TokenizeWordPiece(text);
        var seqLen = tokenIds.Count;

        var inputIds = new DenseTensor<long>(new[] { 1, seqLen });
        var attentionMask = new DenseTensor<long>(new[] { 1, seqLen });
        var tokenTypeIds = new DenseTensor<long>(new[] { 1, seqLen });

        for (var i = 0; i < seqLen; i++)
        {
            inputIds[0, i] = tokenIds[i];
            attentionMask[0, i] = tokenIds[i] == _padId ? 0 : 1;
            tokenTypeIds[0, i] = 0;
        }

        var inputNames = session.InputMetadata.Keys.ToHashSet(StringComparer.Ordinal);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
        };
        if (inputNames.Contains("token_type_ids"))
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds));

        using var results = session.Run(inputs);
        var outputTensor = results.First().AsTensor<float>();
        var pooled = MeanPool(outputTensor, attentionMask, seqLen, Dimension);
        NormalizeL2(pooled);
        return pooled;
    }

    private List<int> TokenizeWordPiece(string text)
    {
        var maxSeqLen = Math.Clamp(_options.MaxSequenceLength, 16, 256);
        var budget = Math.Max(2, maxSeqLen - 2);
        var tokens = TokenRegex.Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Take(512)
            .ToList();

        var ids = new List<int>(maxSeqLen) { _clsId };
        foreach (var token in tokens)
        {
            if (ids.Count - 1 >= budget)
                break;

            if (_vocab.TryGetValue(token, out var known))
            {
                ids.Add(known);
                continue;
            }

            var pieces = WordPiece(token);
            if (pieces.Count == 0)
                ids.Add(_unkId);
            else
                ids.AddRange(pieces);

            if (ids.Count - 1 >= budget)
                break;
        }

        ids.Add(_sepId);
        while (ids.Count < maxSeqLen)
            ids.Add(_padId);
        return ids;
    }

    private List<int> WordPiece(string token)
    {
        var outIds = new List<int>();
        var start = 0;
        const int maxChars = 100;
        if (token.Length > maxChars)
            return outIds;

        while (start < token.Length)
        {
            var end = token.Length;
            int matchedId = -1;
            int matchedEnd = -1;

            while (start < end)
            {
                var piece = token[start..end];
                if (start > 0)
                    piece = $"##{piece}";
                if (_vocab.TryGetValue(piece, out var id))
                {
                    matchedId = id;
                    matchedEnd = end;
                    break;
                }
                end--;
            }

            if (matchedId < 0)
                return [];

            outIds.Add(matchedId);
            start = matchedEnd;
        }

        return outIds;
    }

    private void LoadVocab(string vocabPath)
    {
        var index = 0;
        foreach (var rawLine in File.ReadLines(vocabPath))
        {
            var token = rawLine.Trim();
            if (token.Length == 0)
                continue;
            _vocab.TryAdd(token, index);
            index++;
        }

        if (_vocab.Count == 0)
            throw new InvalidOperationException("Embedding vocab is empty.");
    }

    private int ResolveTokenId(string token, int fallback)
    {
        if (_vocab.TryGetValue(token, out var value))
            return value;
        return fallback;
    }

    private void EnsureReady()
    {
        if (_isReady)
            return;
        throw new InvalidOperationException(_initError ?? "ONNX embeddings are not initialized.");
    }

    private static float[] MeanPool(Tensor<float> output, Tensor<long> mask, int seqLen, int dim)
    {
        var pooled = new float[dim];
        long maskSum = 0;

        for (var t = 0; t < seqLen; t++)
        {
            if (mask[0, t] == 0)
                continue;
            maskSum++;
            for (var d = 0; d < dim; d++)
                pooled[d] += output[0, t, d];
        }

        if (maskSum == 0)
            return pooled;

        var inv = 1f / maskSum;
        for (var i = 0; i < pooled.Length; i++)
            pooled[i] *= inv;

        return pooled;
    }

    private static void NormalizeL2(float[] vector)
    {
        var sum = 0f;
        for (var i = 0; i < vector.Length; i++)
            sum += vector[i] * vector[i];
        if (sum <= 0f)
            return;

        var inv = 1f / MathF.Sqrt(sum);
        for (var i = 0; i < vector.Length; i++)
            vector[i] *= inv;
    }

    private static string ResolvePath(string rootPath, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;
        return Path.Combine(rootPath, configuredPath);
    }

    public void Dispose()
    {
        if (_session.IsValueCreated)
            _session.Value.Dispose();
    }
}
