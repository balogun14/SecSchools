const { app, BrowserWindow } = require('electron');
const path = require('path');
const { spawn } = require('child_process');

let mainWindow;
let backendProcess;

// Determine backend executable path based on environment
function getBackendPath() {
    if (app.isPackaged) {
        // Production: backend is in resources folder
        return path.join(process.resourcesPath, 'backend', 'SchoolAI.Backend.exe');
    } else {
        // Development: backend is in sibling directory
        return path.join(__dirname, '..', 'SchoolAI.Backend', 'bin', 'Debug', 'net10.0', 'SchoolAI.Backend.exe');
    }
}

function startBackend() {
    const backendPath = getBackendPath();
    console.log('Starting backend from:', backendPath);

    try {
        backendProcess = spawn(backendPath, [], {
            stdio: 'pipe',
            windowsHide: true
        });

        backendProcess.stdout.on('data', (data) => {
            console.log(`Backend: ${data}`);
        });

        backendProcess.stderr.on('data', (data) => {
            console.error(`Backend Error: ${data}`);
        });

        backendProcess.on('error', (err) => {
            console.error('Failed to start backend:', err);
        });

        backendProcess.on('close', (code) => {
            console.log(`Backend exited with code ${code}`);
        });
    } catch (error) {
        console.error('Error spawning backend:', error);
    }
}

function stopBackend() {
    if (backendProcess) {
        console.log('Stopping backend...');
        
        // On Windows, we need to kill the process tree
        if (process.platform === 'win32') {
            spawn('taskkill', ['/pid', backendProcess.pid, '/f', '/t']);
        } else {
            backendProcess.kill('SIGTERM');
        }
        
        backendProcess = null;
    }
}

function createWindow() {
    mainWindow = new BrowserWindow({
        width: 1200,
        height: 800,
        minWidth: 800,
        minHeight: 600,
        show: false, // Don't show until backend is ready
        webPreferences: {
            nodeIntegration: false,
            contextIsolation: true
        },
        icon: path.join(__dirname, 'icon.png'),
        backgroundColor: '#1a1a2e'
    });

    mainWindow.loadFile('index.html');

    // Show window after delay to allow backend to initialize
    setTimeout(() => {
        mainWindow.show();
        console.log('Window shown - backend should be ready');
    }, 5000);

    mainWindow.on('closed', () => {
        mainWindow = null;
    });
}

app.whenReady().then(() => {
    startBackend();
    createWindow();

    app.on('activate', () => {
        if (BrowserWindow.getAllWindows().length === 0) {
            createWindow();
        }
    });
});

app.on('window-all-closed', () => {
    stopBackend();
    if (process.platform !== 'darwin') {
        app.quit();
    }
});

app.on('before-quit', () => {
    stopBackend();
});
