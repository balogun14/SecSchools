# SchoolAI - Local-First Desktop Application

A desktop application that enables teachers to upload PDF lesson notes and students to chat with AI about those notes. Runs completely offline using local AI inference.

## Features

- **Offline Operation**: No internet required after initial setup
- **PDF Ingestion**: Upload lesson materials in PDF format
- **AI-Powered Q&A**: Students can ask questions about uploaded content
- **Question Generation**: Teachers can generate assessment questions with customizable format and count
- **Low-End Device Support**: Optimized for CPU-only operation on constrained hardware

## Technology Stack

| Component | Technology |
|-----------|------------|
| Backend | C# ASP.NET Core Web API (.NET 8) |
| Frontend | Electron.js |
| AI Engine | LLamaSharp (Qwen 2.5 1.5B GGUF) |
| Text Database | LiteDB |
| Vector Storage | JSON file with in-memory cosine similarity |
| PDF Extraction | UglyToad.PdfPig |

## Project Structure

```
SecSchools/
├── SchoolAI.Backend/
│   ├── Controllers/
│   │   └── ApiController.cs
│   ├── Models/
│   │   └── GenerateRequest.cs
│   ├── Services/
│   │   ├── AiEngine.cs
│   │   ├── ChatService.cs
│   │   └── VectorStore.cs
│   ├── Program.cs
│   └── SchoolAI.Backend.csproj
└── SchoolAI.Frontend/
    ├── main.js
    ├── index.html
    ├── renderer.js
    ├── styles.css
    └── package.json
```

## Prerequisites

- .NET 8 SDK
- Node.js (v18 or higher)
- 4GB+ RAM recommended
- 2GB free disk space (for model download)

## Installation

### Backend Setup

```powershell
cd SchoolAI.Backend
dotnet restore
dotnet build
```

### Frontend Setup

```powershell
cd SchoolAI.Frontend
npm install
```

## Running the Application

### Development Mode

1. Start the backend (Terminal 1):
```powershell
cd SchoolAI.Backend
dotnet run
```

2. Start the frontend (Terminal 2):
```powershell
cd SchoolAI.Frontend
npm start
```

Note: On first run, the application will automatically download the Qwen 2.5 1.5B model (~1GB) from HuggingFace.

### Production Build

1. Publish the backend:
```powershell
cd SchoolAI.Backend
dotnet publish -c Release -r win-x64 --self-contained
```

2. Build the Electron app:
```powershell
cd SchoolAI.Frontend
npm run build
```

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/upload` | POST | Upload PDF file for ingestion |
| `/api/generate` | POST | Generate AI response (teacher or student mode) |
| `/api/health` | GET | Health check endpoint |

### Generate Request Body

```json
{
  "query": "string",
  "isTeacher": true,
  "questionCount": 5,
  "questionFormat": "mixed"
}
```

**Question Formats**: `mcq`, `short_answer`, `true_false`, `essay`, `mixed`

## Configuration

The application uses the following defaults optimized for low-end devices:

| Setting | Value |
|---------|-------|
| Context Size | 2048 tokens |
| Batch Size | 128 |
| GPU Layers | 0 (CPU only) |
| Model | qwen2.5-1.5b-instruct-q4_k_m.gguf |

## Data Storage

- **Text Content**: Stored in LiteDB at `data/content.db`
- **Embeddings**: Stored in JSON at `data/embeddings.json`
- **Model File**: Downloaded to `models/` directory

## License

MIT License
