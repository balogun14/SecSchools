namespace SchoolAI.Backend.Models;

public class GenerateRequest
{
    public string Query { get; set; } = string.Empty;
    public bool IsTeacher { get; set; }
    
    /// <summary>
    /// Number of questions to generate (teacher mode only). Default: 5, Range: 1-20
    /// </summary>
    public int QuestionCount { get; set; } = 5;
    
    /// <summary>
    /// Question format (teacher mode only). 
    /// Options: "mcq", "short_answer", "true_false", "essay", "mixed"
    /// </summary>
    public string QuestionFormat { get; set; } = "mixed";
}

public class GenerateResponse
{
    public bool Success { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public class UploadResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}
