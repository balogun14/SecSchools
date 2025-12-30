using Microsoft.AspNetCore.Mvc;
using SchoolAI.Backend.Models;
using SchoolAI.Backend.Services;

namespace SchoolAI.Backend.Controllers;

[ApiController]
[Route("api")]
public class ApiController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<ApiController> _logger;

    public ApiController(IChatService chatService, IVectorStore vectorStore, ILogger<ApiController> logger)
    {
        _chatService = chatService;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    /// <summary>
    /// Upload a PDF document for ingestion
    /// </summary>
    [HttpPost("upload")]
    public async Task<ActionResult<UploadResponse>> UploadPdf(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new UploadResponse
                {
                    Success = false,
                    Error = "No file provided"
                });
            }

            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new UploadResponse
                {
                    Success = false,
                    Error = "Only PDF files are supported"
                });
            }

            _logger.LogInformation("Uploading PDF: {Filename}, Size: {Size} bytes", file.FileName, file.Length);

            using var stream = file.OpenReadStream();
            await _vectorStore.IngestPdfAsync(stream, file.FileName);

            return Ok(new UploadResponse
            {
                Success = true,
                Message = $"Successfully ingested: {file.FileName}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process PDF upload");
            return StatusCode(500, new UploadResponse
            {
                Success = false,
                Error = $"Failed to process PDF: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Generate AI response based on mode (teacher/student)
    /// </summary>
    [HttpPost("generate")]
    public async Task<ActionResult<GenerateResponse>> Generate([FromBody] GenerateRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BadRequest(new GenerateResponse
                {
                    Success = false,
                    Error = "Query is required"
                });
            }

            string content;

            if (request.IsTeacher)
            {
                // Validate and clamp question count
                var questionCount = Math.Clamp(request.QuestionCount, 1, 20);
                
                // Validate question format
                var validFormats = new[] { "mcq", "short_answer", "true_false", "essay", "mixed" };
                var format = validFormats.Contains(request.QuestionFormat.ToLowerInvariant()) 
                    ? request.QuestionFormat 
                    : "mixed";

                content = await _chatService.GenerateTeacherQuestionsAsync(
                    request.Query, 
                    questionCount, 
                    format);
            }
            else
            {
                content = await _chatService.AnswerStudentQuestionAsync(request.Query);
            }

            return Ok(new GenerateResponse
            {
                Success = true,
                Content = content
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate response");
            return StatusCode(500, new GenerateResponse
            {
                Success = false,
                Error = $"Failed to generate response: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
