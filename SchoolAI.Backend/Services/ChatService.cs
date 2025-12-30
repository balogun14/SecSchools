namespace SchoolAI.Backend.Services;

public interface IChatService
{
    Task<string> GenerateTeacherQuestionsAsync(string topic, int questionCount, string questionFormat);
    Task<string> AnswerStudentQuestionAsync(string question);
}

public class ChatService : IChatService
{
    private readonly IAiEngine _aiEngine;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<ChatService> _logger;

    public ChatService(IAiEngine aiEngine, IVectorStore vectorStore, ILogger<ChatService> logger)
    {
        _aiEngine = aiEngine;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    public async Task<string> GenerateTeacherQuestionsAsync(string topic, int questionCount, string questionFormat)
    {
        _logger.LogInformation("Generating {Count} {Format} questions for topic: {Topic}", 
            questionCount, questionFormat, topic);

        // Retrieve relevant context from the uploaded documents
        var contextChunks = await _vectorStore.SearchSimilarAsync(topic);
        var context = string.Join("\n\n", contextChunks);

        if (string.IsNullOrWhiteSpace(context))
        {
            return "No lesson content has been uploaded yet. Please upload a PDF document first.";
        }

        var formatInstructions = GetFormatInstructions(questionFormat);

        var systemPrompt = $@"You are an expert teacher assistant. Your task is to generate assessment questions based on the provided lesson content.

INSTRUCTIONS:
1. Generate exactly {questionCount} questions based on the lesson content below.
2. {formatInstructions}
3. Make sure questions test understanding, not just memorization.
4. Questions should be clear, concise, and appropriate for students.
5. Format each question with a number (1., 2., etc.)

LESSON CONTENT:
{context}";

        var userMessage = $"Generate {questionCount} {questionFormat} questions about: {topic}";

        return await _aiEngine.GenerateChatResponseAsync(systemPrompt, userMessage);
    }

    public async Task<string> AnswerStudentQuestionAsync(string question)
    {
        _logger.LogInformation("Answering student question: {Question}", question);

        // Retrieve relevant context from the uploaded documents
        var contextChunks = await _vectorStore.SearchSimilarAsync(question);
        var context = string.Join("\n\n", contextChunks);

        if (string.IsNullOrWhiteSpace(context))
        {
            return "I don't have any lesson content to reference. Please ask your teacher to upload the lesson notes first.";
        }

        var systemPrompt = $@"You are a helpful teaching assistant helping students understand their lesson content.

INSTRUCTIONS:
1. Answer the student's question based ONLY on the lesson content provided below.
2. If the answer is not in the lesson content, say so politely and suggest they ask their teacher.
3. Explain concepts in a clear, student-friendly way.
4. Use examples from the lesson content when helpful.
5. Keep your response focused and educational.

LESSON CONTENT:
{context}";

        return await _aiEngine.GenerateChatResponseAsync(systemPrompt, question);
    }

    private static string GetFormatInstructions(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "mcq" => "Generate Multiple Choice Questions. Each question should have 4 options (A, B, C, D) with one correct answer clearly indicated.",
            "short_answer" => "Generate Short Answer Questions. Each question should require a brief 1-3 sentence response.",
            "true_false" => "Generate True/False Questions. After each statement, indicate the correct answer (True or False).",
            "essay" => "Generate Essay Questions. These should be open-ended questions requiring longer, detailed responses.",
            "mixed" or _ => "Generate a mix of question types: include some Multiple Choice, Short Answer, and True/False questions."
        };
    }
}
