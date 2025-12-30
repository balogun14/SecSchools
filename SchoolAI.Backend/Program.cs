using SchoolAI.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on port 5000
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5000);
});

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "SchoolAI API", Version = "v1" });
});

// Configure CORS for Electron app
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Register application services as singletons (they manage their own state)
builder.Services.AddSingleton<IAiEngine, AiEngine>();
builder.Services.AddSingleton<IVectorStore, VectorStore>();
builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseCors();

// Enable Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SchoolAI API v1");
    c.RoutePrefix = string.Empty; // Serve Swagger UI at root
});

app.MapControllers();

// Initialize AI Engine and Vector Store on startup
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(async () =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    
    try
    {
        logger.LogInformation("Initializing AI services...");
        
        var aiEngine = app.Services.GetRequiredService<IAiEngine>();
        await aiEngine.InitializeAsync();
        
        var vectorStore = app.Services.GetRequiredService<IVectorStore>();
        await vectorStore.InitializeAsync();
        
        logger.LogInformation("All services initialized successfully!");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize services");
    }
});

app.Run();
