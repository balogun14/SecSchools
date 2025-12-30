// ===== Configuration =====
const API_BASE = 'http://localhost:5000/api';

// ===== DOM Elements =====
const elements = {
    // Tabs
    tabBtns: document.querySelectorAll('.tab-btn'),
    tabPanels: document.querySelectorAll('.tab-panel'),
    
    // Teacher - Upload
    uploadZone: document.getElementById('upload-zone'),
    fileInput: document.getElementById('file-input'),
    fileName: document.getElementById('file-name'),
    uploadBtn: document.getElementById('upload-btn'),
    uploadStatus: document.getElementById('upload-status'),
    
    // Teacher - Questions
    questionTopic: document.getElementById('question-topic'),
    questionCount: document.getElementById('question-count'),
    countDisplay: document.getElementById('count-display'),
    questionFormat: document.getElementById('question-format'),
    generateBtn: document.getElementById('generate-btn'),
    questionsOutput: document.getElementById('questions-output'),
    
    // Student - Chat
    chatMessages: document.getElementById('chat-messages'),
    chatInput: document.getElementById('chat-input'),
    sendBtn: document.getElementById('send-btn'),
    
    // Loading
    loadingOverlay: document.getElementById('loading-overlay'),
    loadingText: document.getElementById('loading-text')
};

// ===== State =====
let selectedFile = null;

// ===== Tab Switching =====
elements.tabBtns.forEach(btn => {
    btn.addEventListener('click', () => {
        const targetTab = btn.dataset.tab;
        
        // Update buttons
        elements.tabBtns.forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        
        // Update panels
        elements.tabPanels.forEach(panel => {
            panel.classList.remove('active');
            if (panel.id === `${targetTab}-tab`) {
                panel.classList.add('active');
            }
        });
    });
});

// ===== File Upload Zone =====
elements.uploadZone.addEventListener('click', () => {
    elements.fileInput.click();
});

elements.uploadZone.addEventListener('dragover', (e) => {
    e.preventDefault();
    elements.uploadZone.classList.add('drag-over');
});

elements.uploadZone.addEventListener('dragleave', () => {
    elements.uploadZone.classList.remove('drag-over');
});

elements.uploadZone.addEventListener('drop', (e) => {
    e.preventDefault();
    elements.uploadZone.classList.remove('drag-over');
    
    const files = e.dataTransfer.files;
    if (files.length > 0 && files[0].type === 'application/pdf') {
        handleFileSelect(files[0]);
    } else {
        showStatus('error', 'Please select a PDF file');
    }
});

elements.fileInput.addEventListener('change', (e) => {
    if (e.target.files.length > 0) {
        handleFileSelect(e.target.files[0]);
    }
});

function handleFileSelect(file) {
    selectedFile = file;
    elements.fileName.textContent = `Selected: ${file.name}`;
    elements.uploadBtn.disabled = false;
}

// ===== Upload Button =====
elements.uploadBtn.addEventListener('click', async () => {
    if (!selectedFile) return;
    
    showLoading('Uploading and processing PDF...');
    
    try {
        const formData = new FormData();
        formData.append('file', selectedFile);
        
        const response = await fetch(`${API_BASE}/upload`, {
            method: 'POST',
            body: formData
        });
        
        const data = await response.json();
        
        if (data.success) {
            showStatus('success', data.message);
            // Reset file selection
            selectedFile = null;
            elements.fileName.textContent = '';
            elements.uploadBtn.disabled = true;
            elements.fileInput.value = '';
        } else {
            showStatus('error', data.error || 'Upload failed');
        }
    } catch (error) {
        console.error('Upload error:', error);
        showStatus('error', 'Failed to connect to server. Is the backend running?');
    } finally {
        hideLoading();
    }
});

// ===== Question Count Slider =====
elements.questionCount.addEventListener('input', (e) => {
    elements.countDisplay.textContent = e.target.value;
});

// ===== Generate Questions Button =====
elements.generateBtn.addEventListener('click', async () => {
    const topic = elements.questionTopic.value.trim();
    
    if (!topic) {
        elements.questionTopic.focus();
        return;
    }
    
    showLoading('Generating questions... This may take a moment.');
    
    try {
        const response = await fetch(`${API_BASE}/generate`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                query: topic,
                isTeacher: true,
                questionCount: parseInt(elements.questionCount.value),
                questionFormat: elements.questionFormat.value
            })
        });
        
        const data = await response.json();
        
        if (data.success) {
            elements.questionsOutput.textContent = data.content;
            elements.questionsOutput.classList.add('visible');
        } else {
            elements.questionsOutput.textContent = `Error: ${data.error}`;
            elements.questionsOutput.classList.add('visible');
        }
    } catch (error) {
        console.error('Generate error:', error);
        elements.questionsOutput.textContent = 'Failed to connect to server. Is the backend running?';
        elements.questionsOutput.classList.add('visible');
    } finally {
        hideLoading();
    }
});

// ===== Chat Functionality =====
elements.sendBtn.addEventListener('click', sendMessage);
elements.chatInput.addEventListener('keypress', (e) => {
    if (e.key === 'Enter') {
        sendMessage();
    }
});

async function sendMessage() {
    const message = elements.chatInput.value.trim();
    if (!message) return;
    
    // Add user message to chat
    addMessage('user', message);
    elements.chatInput.value = '';
    
    showLoading('Thinking...');
    
    try {
        const response = await fetch(`${API_BASE}/generate`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                query: message,
                isTeacher: false
            })
        });
        
        const data = await response.json();
        
        if (data.success) {
            addMessage('ai', data.content);
        } else {
            addMessage('ai', `Sorry, I encountered an error: ${data.error}`);
        }
    } catch (error) {
        console.error('Chat error:', error);
        addMessage('ai', 'Failed to connect to the server. Please make sure the application is fully loaded.');
    } finally {
        hideLoading();
    }
}

function addMessage(type, content) {
    const messageDiv = document.createElement('div');
    messageDiv.className = `message ${type === 'user' ? 'user-message' : 'ai-message'}`;
    
    messageDiv.innerHTML = `
        <div class="message-avatar">${type === 'user' ? '👤' : '🤖'}</div>
        <div class="message-content">
            <p>${escapeHtml(content)}</p>
        </div>
    `;
    
    elements.chatMessages.appendChild(messageDiv);
    elements.chatMessages.scrollTop = elements.chatMessages.scrollHeight;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML.replace(/\n/g, '<br>');
}

// ===== Status Message =====
function showStatus(type, message) {
    elements.uploadStatus.textContent = message;
    elements.uploadStatus.className = `status-message ${type}`;
    
    // Auto-hide after 5 seconds
    setTimeout(() => {
        elements.uploadStatus.className = 'status-message';
    }, 5000);
}

// ===== Loading Overlay =====
function showLoading(text = 'Processing...') {
    elements.loadingText.textContent = text;
    elements.loadingOverlay.classList.add('visible');
}

function hideLoading() {
    elements.loadingOverlay.classList.remove('visible');
}

// ===== Initialize =====
console.log('SchoolAI Frontend loaded');
