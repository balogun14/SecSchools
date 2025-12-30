using LiteDB;
using UglyToad.PdfPig;

namespace SchoolAI.Backend.Services;

public interface IVectorStore
{
    Task InitializeAsync();
    Task IngestPdfAsync(Stream pdfStream, string filename);
    Task<List<string>> SearchSimilarAsync(string query, int topK = 3);
}

public class VectorStore : IVectorStore
{
    private readonly string _dataDirectory;
    private readonly string _databasePath;
    private readonly string _embeddingsPath;
    private readonly IAiEngine _aiEngine;
    private readonly ILogger<VectorStore> _logger;
    
    private LiteDatabase? _database;
    private ILiteCollection<TextChunk>? _chunksCollection;
    private List<EmbeddingEntry> _embeddings = new();
    private readonly SemaphoreSlim _embeddingsLock = new(1, 1);

    private const int CHUNK_SIZE = 500;  // Characters per chunk
    private const int CHUNK_OVERLAP = 50; // Overlap between chunks

    public VectorStore(IAiEngine aiEngine, ILogger<VectorStore> logger)
    {
        _aiEngine = aiEngine;
        _logger = logger;
        
        // Use relative paths from application base directory
        _dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        _databasePath = Path.Combine(_dataDirectory, "content.db");
        _embeddingsPath = Path.Combine(_dataDirectory, "embeddings.json");
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing Vector Store...");

        // Ensure data directory exists
        if (!Directory.Exists(_dataDirectory))
        {
            Directory.CreateDirectory(_dataDirectory);
        }

        // Initialize LiteDB
        _database = new LiteDatabase(_databasePath);
        _chunksCollection = _database.GetCollection<TextChunk>("chunks");
        _chunksCollection.EnsureIndex(x => x.Id);

        // Load existing embeddings into memory
        await LoadEmbeddingsAsync();

        _logger.LogInformation("Vector Store initialized with {Count} embeddings", _embeddings.Count);
    }

    private async Task LoadEmbeddingsAsync()
    {
        if (File.Exists(_embeddingsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_embeddingsPath);
                _embeddings = System.Text.Json.JsonSerializer.Deserialize<List<EmbeddingEntry>>(json) ?? new List<EmbeddingEntry>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load embeddings, starting fresh");
                _embeddings = new List<EmbeddingEntry>();
            }
        }
    }

    private async Task SaveEmbeddingsAsync()
    {
        await _embeddingsLock.WaitAsync();
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_embeddings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_embeddingsPath, json);
        }
        finally
        {
            _embeddingsLock.Release();
        }
    }

    public async Task IngestPdfAsync(Stream pdfStream, string filename)
    {
        _logger.LogInformation("Ingesting PDF: {Filename}", filename);
        _logger.LogInformation("Memory before PDF extraction: {Memory} MB", 
            GC.GetTotalMemory(false) / 1024 / 1024);

        // Extract text from PDF
        string fullText;
        using (var pdf = PdfDocument.Open(pdfStream))
        {
            fullText = string.Join("\n", pdf.GetPages().Select(p => p.Text));
        }
        
        _logger.LogInformation("Memory after PDF extraction: {Memory} MB", 
            GC.GetTotalMemory(false) / 1024 / 1024);

        if (string.IsNullOrWhiteSpace(fullText))
        {
            throw new InvalidOperationException("PDF contains no extractable text");
        }

        // Chunk the text
        var chunks = ChunkText(fullText, filename);
        _logger.LogInformation("Created {Count} chunks from PDF", chunks.Count);
        _logger.LogInformation("Memory after chunking: {Memory} MB", 
            GC.GetTotalMemory(false) / 1024 / 1024);

        // Store chunks and generate embeddings
        var totalChunks = chunks.Count;
        var processedChunks = 0;
        
        foreach (var chunk in chunks)
        {
            processedChunks++;
            if (processedChunks % 10 == 0 || processedChunks == 1)
            {
                _logger.LogInformation("Processing chunk {Current}/{Total} - Memory: {Memory} MB", 
                    processedChunks, totalChunks, GC.GetTotalMemory(false) / 1024 / 1024);
            }
            
            // Store text in LiteDB
            _chunksCollection!.Insert(chunk);

            // Generate embedding (now using lightweight hash-based approach)
            var embedding = await _aiEngine.GenerateEmbeddingAsync(chunk.Content);

            // Add to in-memory embeddings
            _embeddings.Add(new EmbeddingEntry
            {
                ChunkId = chunk.Id,
                Embedding = embedding
            });
        }

        // Persist embeddings to JSON
        await SaveEmbeddingsAsync();
        
        _logger.LogInformation("Memory after ingestion complete: {Memory} MB", 
            GC.GetTotalMemory(false) / 1024 / 1024);

        _logger.LogInformation("Successfully ingested PDF: {Filename}", filename);
    }

    private List<TextChunk> ChunkText(string text, string sourceFile)
    {
        var chunks = new List<TextChunk>();
        var cleanText = text.Replace("\r\n", "\n").Replace("\r", "\n");
        
        int position = 0;
        int chunkIndex = 0;
        int lastPosition = -1; // Track to prevent infinite loop

        while (position < cleanText.Length)
        {
            // Safety check: if position hasn't advanced, force it forward
            if (position == lastPosition)
            {
                position++;
                continue;
            }
            lastPosition = position;
            
            int endPosition = Math.Min(position + CHUNK_SIZE, cleanText.Length);
            
            // Try to break at a sentence or paragraph boundary
            if (endPosition < cleanText.Length)
            {
                int searchLength = Math.Min(100, endPosition - position);
                if (searchLength > 0)
                {
                    var breakPoint = cleanText.LastIndexOfAny(new[] { '.', '\n', '!', '?' }, endPosition - 1, searchLength);
                    if (breakPoint > position)
                    {
                        endPosition = breakPoint + 1;
                    }
                }
            }

            var chunkContent = cleanText.Substring(position, endPosition - position).Trim();
            
            if (!string.IsNullOrWhiteSpace(chunkContent))
            {
                chunks.Add(new TextChunk
                {
                    Id = ObjectId.NewObjectId(),
                    Content = chunkContent,
                    SourceFile = sourceFile,
                    ChunkIndex = chunkIndex++,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Move position forward - simple approach, no overlap needed for basic retrieval
            position = endPosition;
        }

        return chunks;
    }

    public async Task<List<string>> SearchSimilarAsync(string query, int topK = 3)
    {
        if (_embeddings.Count == 0)
        {
            return new List<string>();
        }

        // Generate query embedding
        var queryEmbedding = await _aiEngine.GenerateEmbeddingAsync(query);

        // Calculate cosine similarity for all embeddings
        var similarities = _embeddings
            .Select(e => new { e.ChunkId, Similarity = CosineSimilarity(queryEmbedding, e.Embedding) })
            .OrderByDescending(x => x.Similarity)
            .Take(topK)
            .ToList();

        // Fetch actual text content from LiteDB
        var results = new List<string>();
        foreach (var similarity in similarities)
        {
            var chunk = _chunksCollection!.FindById(similarity.ChunkId);
            if (chunk != null)
            {
                results.Add(chunk.Content);
            }
        }

        return results;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0f;
        }

        float dotProduct = 0f;
        float normA = 0f;
        float normB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        if (denominator == 0)
        {
            return 0f;
        }

        return (float)(dotProduct / denominator);
    }
}

// Data models
public class TextChunk
{
    public required ObjectId Id { get; set; }
    public required string Content { get; set; } = string.Empty;
    public required string SourceFile { get; set; } = string.Empty;
    public required int ChunkIndex { get; set; }
    public required DateTime CreatedAt { get; set; }
}

public class EmbeddingEntry
{
    public required ObjectId ChunkId { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
}
