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
    private LLamaWeights? _embedderWeights;
    private LLamaEmbedder? _embedder;
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
            _modelParams = new ModelParams(_modelPath)
            {
                ContextSize = 2048,      // Reduced for low-end devices
                GpuLayerCount = 0,       // CPU only - CRITICAL
                BatchSize = 128,         // Smaller batch for memory efficiency
                Threads = Math.Max(1, Environment.ProcessorCount / 2), // Use half CPU cores
            };

            _logger.LogInformation("Loading model from {ModelPath}...", _modelPath);
            _model = await LLamaWeights.LoadFromFileAsync(_modelParams);
            
            // Create embedder for vector search
            var embedderParams = new ModelParams(_modelPath)
            {
                ContextSize = 512,       // Smaller context for embeddings
                GpuLayerCount = 0,
                BatchSize = 512,         // Must equal UBatchSize for embeddings
                UBatchSize = 512,        // Required for non-causal models
                Threads = Math.Max(1, Environment.ProcessorCount / 2),
                Embeddings = true        // Enable embedding mode
            };
            
            _embedderWeights = await LLamaWeights.LoadFromFileAsync(embedderParams);
            _embedder = new LLamaEmbedder(_embedderWeights, embedderParams);

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

        using var context = _model.CreateContext(_modelParams);
        var executor = new InteractiveExecutor(context);

        var chatHistory = new ChatHistory();
        chatHistory.AddMessage(AuthorRole.System, systemPrompt);
        chatHistory.AddMessage(AuthorRole.User, userMessage);

        var session = new ChatSession(executor, chatHistory);

        var inferenceParams = new InferenceParams
        {
            MaxTokens = 512,           // Limit response length for low-end devices
            Temperature = 0.7f,
            TopP = 0.9f,
            AntiPrompts = new List<string> { "<|im_end|>", "<|end|>" }
        };

        var response = new System.Text.StringBuilder();
        await foreach (var text in session.ChatAsync(chatHistory, inferenceParams))
        {
            response.Append(text);
        }

        return response.ToString().Trim();
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        if (!_isInitialized || _embedder == null)
        {
            throw new InvalidOperationException("AI Engine not initialized. Call InitializeAsync first.");
        }

        // Generate embedding for the text
        var embeddings = await _embedder.GetEmbeddings(text);
        
        // Return the first embedding (there's typically only one for single text input)
        return embeddings.SelectMany(x => x).ToArray();
    }

    public void Dispose()
    {
        _model?.Dispose();
        _embedderWeights?.Dispose();
        _embedder?.Dispose();
        _initLock.Dispose();
    }
}
