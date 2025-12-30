using LLama;
using LLama.Common;

namespace SchoolAI.Backend.Services;

public interface IAiEngine : IDisposable
{
    Task InitializeAsync();
    Task<string> GenerateChatResponseAsync(string systemPrompt, string userMessage);
    Task<float[]> GenerateEmbeddingAsync(string text);
}

public class AiEngine : IAiEngine
{
    private readonly string _modelDirectory;
    private readonly string _modelPath;
    private const string MODEL_FILENAME = "qwen2.5-1.5b-instruct-q4_k_m.gguf";
    private const string MODEL_URL = "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf";
    
    private LLamaWeights? _model;
    private readonly ILogger<AiEngine> _logger;
    private bool _isInitialized = false;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private ModelParams? _modelParams;

    public AiEngine(ILogger<AiEngine> logger)
    {
        _logger = logger;
        // Use relative path from application base directory
        _modelDirectory = Path.Combine(AppContext.BaseDirectory, "models");
        _modelPath = Path.Combine(_modelDirectory, MODEL_FILENAME);
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            _logger.LogInformation("Initializing AI Engine...");

            // Ensure model directory exists
            if (!Directory.Exists(_modelDirectory))
            {
                Directory.CreateDirectory(_modelDirectory);
            }

            // Download model if not present
            if (!File.Exists(_modelPath))
            {
                await DownloadModelAsync();
            }

            // Configure for CPU-only, low-end device optimization
            // MINIMAL memory configuration for low-RAM systems
            _modelParams = new ModelParams(_modelPath)
            {
                ContextSize = 1024,      // Minimal context for low memory
                GpuLayerCount = 0,       // CPU only - CRITICAL
                BatchSize = 64,          // Minimal batch size
                Threads = Math.Max(1, Environment.ProcessorCount / 2),
            };

            _logger.LogInformation("Loading model from {ModelPath}...", _modelPath);
            _logger.LogInformation("Current process memory before model load: {Memory} MB", 
                GC.GetTotalMemory(false) / 1024 / 1024);
            
            _model = await LLamaWeights.LoadFromFileAsync(_modelParams);
            
            _logger.LogInformation("Current process memory after model load: {Memory} MB", 
                GC.GetTotalMemory(false) / 1024 / 1024);
            
            // Note: Embeddings are now generated using lightweight hash-based approach
            // instead of LLM embeddings to save memory on low-end devices

            _isInitialized = true;
            _logger.LogInformation("AI Engine initialized successfully.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task DownloadModelAsync()
    {
        _logger.LogInformation("Model not found. Downloading from HuggingFace...");
        _logger.LogInformation("This may take several minutes (~1GB download)...");

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(30); // Long timeout for large file

        try
        {
            using var response = await httpClient.GetAsync(MODEL_URL, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;
            var lastLoggedPercent = 0;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(_modelPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var percent = (int)((downloadedBytes * 100) / totalBytes);
                    if (percent >= lastLoggedPercent + 10)
                    {
                        lastLoggedPercent = percent;
                        _logger.LogInformation("Download progress: {Percent}%", percent);
                    }
                }
            }

            _logger.LogInformation("Model download complete!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download model");
            
            // Clean up partial download
            if (File.Exists(_modelPath))
            {
                File.Delete(_modelPath);
            }
            
            throw new InvalidOperationException($"Failed to download model from {MODEL_URL}", ex);
        }
    }

    public async Task<string> GenerateChatResponseAsync(string systemPrompt, string userMessage)
    {
        if (!_isInitialized || _model == null || _modelParams == null)
        {
            throw new InvalidOperationException("AI Engine not initialized. Call InitializeAsync first.");
        }

        // Format prompt using Qwen's chat template
        var prompt = $"""
            <|im_start|>system
            {systemPrompt}<|im_end|>
            <|im_start|>user
            {userMessage}<|im_end|>
            <|im_start|>assistant
            """;

        var inferenceParams = new InferenceParams
        {
            MaxTokens = 512,           // Limit response length for low-end devices
            Temperature = 0.7f,
            TopP = 0.9f,
            AntiPrompts = new List<string> { "<|im_end|>", "<|end|>" }
        };

        // Use StatelessExecutor - simpler and doesn't require context state management
        using var context = _model.CreateContext(_modelParams);
        var executor = new StatelessExecutor(_model, _modelParams);

        // Generate response
        var response = new System.Text.StringBuilder();
        await foreach (var text in executor.InferAsync(prompt, inferenceParams))
        {
            response.Append(text);
        }

        return response.ToString().Trim();
    }

    public Task<float[]> GenerateEmbeddingAsync(string text)
    {
        // Use lightweight hash-based embeddings instead of LLM-based embeddings
        // This is MUCH more memory efficient for CPU-only devices
        // Uses a simple bag-of-words with hashing trick approach
        
        const int embeddingDimension = 256; // Small embedding size for efficiency
        var embedding = new float[embeddingDimension];
        
        // Tokenize: simple whitespace + lowercase
        var tokens = text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}' }, 
                   StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var token in tokens)
        {
            // Hash each token to a bucket using a simple hash
            var hash = GetStableHash(token);
            var bucket = Math.Abs(hash) % embeddingDimension;
            
            // Use sign of secondary hash for direction (feature hashing trick)
            var sign = (GetStableHash(token + "_sign") % 2 == 0) ? 1f : -1f;
            embedding[bucket] += sign;
            
            // Also add bigram features for better context
            if (token.Length > 2)
            {
                for (int i = 0; i < token.Length - 1; i++)
                {
                    var bigram = token.Substring(i, 2);
                    var bigramHash = GetStableHash(bigram);
                    var bigramBucket = Math.Abs(bigramHash) % embeddingDimension;
                    var bigramSign = (GetStableHash(bigram + "_sign") % 2 == 0) ? 0.5f : -0.5f;
                    embedding[bigramBucket] += bigramSign;
                }
            }
        }
        
        // L2 normalize the embedding
        var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
        }
        
        return Task.FromResult(embedding);
    }
    
    private static int GetStableHash(string str)
    {
        // Simple stable hash that doesn't change between runs
        unchecked
        {
            int hash = 17;
            foreach (char c in str)
            {
                hash = hash * 31 + c;
            }
            return hash;
        }
    }

    public void Dispose()
    {
        _model?.Dispose();
        _initLock.Dispose();
    }
}
