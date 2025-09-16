// DOM Elements
const fileInput = document.getElementById('zip');
const uploadArea = document.getElementById('upload-area');
const progressContainer = document.getElementById('progress-container');
const progressBar = document.getElementById('progress-bar');
const progressLabel = document.getElementById('progress-label');
const optionsSection = document.getElementById('options');
const duplicatesCheckbox = document.getElementById('chk-dup');
const previewButton = document.getElementById('btn-preview');
const downloadButton = document.getElementById('btn-download');
const statusMessage = document.getElementById('status');
const resultsSection = document.getElementById('results-section');
const summaryCard = document.getElementById('summary-card');
const previewCard = document.getElementById('preview-card');
const previewTable = document.getElementById('preview-table');
const errorsList = document.getElementById('errors');

// State
let currentFile = null;
let previewData = null;
let currentSessionId = null;
let progressInterval = null;

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    setupEventListeners();
});

function setupEventListeners() {
    // File input change
    fileInput.addEventListener('change', handleFileSelect);
    
    // Drag and drop
    uploadArea.addEventListener('dragover', handleDragOver);
    uploadArea.addEventListener('dragleave', handleDragLeave);
    uploadArea.addEventListener('drop', handleDrop);
    
    // Buttons
    previewButton?.addEventListener('click', handlePreview);
    downloadButton?.addEventListener('click', handleDownload);
    
    // Prevent default drag behaviors
    ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
        uploadArea.addEventListener(eventName, preventDefaults, false);
        document.body.addEventListener(eventName, preventDefaults, false);
    });
}

function preventDefaults(e) {
    e.preventDefault();
    e.stopPropagation();
}

function handleDragOver(e) {
    uploadArea.classList.add('dragging');
}

function handleDragLeave(e) {
    uploadArea.classList.remove('dragging');
}

function handleDrop(e) {
    uploadArea.classList.remove('dragging');
    const files = e.dataTransfer.files;
    if (files.length > 0 && files[0].name.endsWith('.zip')) {
        fileInput.files = files;
        handleFileSelect({ target: { files } });
    } else {
        showStatus('Por favor, selecciona un archivo ZIP válido', 'error');
    }
}

function handleFileSelect(e) {
    const file = e.target.files[0];
    if (!file) return;
    
    if (!file.name.endsWith('.zip')) {
        showStatus('Por favor, selecciona un archivo ZIP válido', 'error');
        fileInput.value = '';
        return;
    }
    
    currentFile = file;
    showStatus(`Archivo seleccionado: ${file.name}`, 'info');
    
    // Show options
    optionsSection.style.display = 'block';
    
    // Auto-preview
    handlePreview();
}

async function handlePreview() {
    if (!currentFile) {
        showStatus('Por favor, selecciona un archivo ZIP primero', 'error');
        return;
    }

    currentSessionId = generateSessionId();
    const formData = new FormData();
    formData.append('zip', currentFile);
    formData.append('processDuplicates', duplicatesCheckbox.checked);
    formData.append('sessionId', currentSessionId);

    try {
        showProgress(true, 'Analizando archivo...');
        previewButton.disabled = true;
        downloadButton.disabled = true;

        // Start progress monitoring
        startProgressMonitoring();

        const response = await fetch('/api/invoices/preview', {
            method: 'POST',
            body: formData
        });

        const data = await response.json();

        if (!response.ok) {
            throw new Error(data.message || 'Error al procesar el archivo');
        }

        previewData = data;
        displayResults(data);
        showStatus('Vista previa generada exitosamente', 'success');

    } catch (error) {
        console.error('Error:', error);
        showStatus(`Error: ${error.message}`, 'error');
        hideResults();
    } finally {
        stopProgressMonitoring();
        showProgress(false);
        previewButton.disabled = false;
        downloadButton.disabled = false;
    }
}

async function handleDownload() {
    if (!currentFile) {
        showStatus('Por favor, selecciona un archivo ZIP primero', 'error');
        return;
    }

    currentSessionId = generateSessionId();
    const formData = new FormData();
    formData.append('zip', currentFile);
    formData.append('processDuplicates', duplicatesCheckbox.checked);
    formData.append('sessionId', currentSessionId);

    try {
        showProgress(true, 'Iniciando procesamiento...');
        downloadButton.disabled = true;

        // Start progress monitoring
        startProgressMonitoring();

        const response = await fetch('/api/invoices/upload', {
            method: 'POST',
            body: formData
        });

        if (!response.ok) {
            const errorData = await response.json();
            throw new Error(errorData.message || 'Error al procesar el archivo');
        }

        // Download the file
        const blob = await response.blob();
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `facturas_${new Date().toISOString().slice(0, 10)}.xls`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        window.URL.revokeObjectURL(url);

        showStatus('Archivo Excel descargado exitosamente', 'success');

    } catch (error) {
        console.error('Error:', error);
        showStatus(`Error: ${error.message}`, 'error');
    } finally {
        stopProgressMonitoring();
        showProgress(false);
        downloadButton.disabled = false;
    }
}

function displayResults(data) {
    // Show results section
    resultsSection.style.display = 'block';
    
    // Update summary
    document.getElementById('sum-total').textContent = data.totalPdfs || 0;
    document.getElementById('sum-parsed').textContent = data.parsedPdfs || 0;
    document.getElementById('sum-dups').textContent = data.duplicates || 0;
    document.getElementById('sum-nodata').textContent = data.noDataPdfs || 0;

    // Update current file display if available
    updateCurrentFileDisplay(data.currentFile, data.processingProgress);

    // Show preview limit message if applicable
    if (data.isPreview && data.previewLimit) {
        const previewInfo = document.createElement('div');
        previewInfo.className = 'preview-info';
        previewInfo.innerHTML = `<small><strong>Vista Previa:</strong> Se muestran máximo ${data.previewLimit} filas. Use "Descargar Excel" para obtener todos los datos.</small>`;
        previewCard.insertBefore(previewInfo, previewCard.firstChild);
    }
    
    // Display errors if any
    if (data.errors && data.errors.length > 0) {
        errorsList.innerHTML = `
            <h4>Errores encontrados:</h4>
            <ul>
                ${data.errors.map(err => `<li>${escapeHtml(err)}</li>`).join('')}
            </ul>
        `;
    } else {
        errorsList.innerHTML = '';
    }
    
    // Display preview table
    if (data.rows && data.rows.length > 0) {
        displayPreviewTable(data.rows);
        previewCard.style.display = 'block';
    } else {
        previewCard.style.display = 'none';
    }
}

function displayPreviewTable(rows) {
    if (!rows || rows.length === 0) {
        previewTable.innerHTML = '<tr><td>No hay datos para mostrar</td></tr>';
        return;
    }
    
    // Get columns from first row
    const columns = Object.keys(rows[0]);
    
    // Build header
    const thead = previewTable.querySelector('thead');
    thead.innerHTML = `
        <tr>
            ${columns.map(col => `<th>${escapeHtml(formatColumnName(col))}</th>`).join('')}
        </tr>
    `;
    
    // Build body (show max 50 rows for preview)
    const tbody = previewTable.querySelector('tbody');
    const previewRows = rows.slice(0, 50);
    
    tbody.innerHTML = previewRows.map(row => `
        <tr>
            ${columns.map(col => `<td>${escapeHtml(formatCellValue(row[col]))}</td>`).join('')}
        </tr>
    `).join('');
    
    if (rows.length > 50) {
        tbody.innerHTML += `
            <tr>
                <td colspan="${columns.length}" style="text-align: center; font-style: italic;">
                    ... y ${rows.length - 50} filas más
                </td>
            </tr>
        `;
    }
}

function formatColumnName(name) {
    // Convert camelCase to Title Case
    return name
        .replace(/([A-Z])/g, ' $1')
        .replace(/^./, str => str.toUpperCase())
        .trim();
}

function formatCellValue(value) {
    if (value == null) return '';
    if (typeof value === 'boolean') return value ? 'Sí' : 'No';
    if (typeof value === 'number') return value.toLocaleString('es-AR');
    if (typeof value === 'string' && value.match(/^\d{4}-\d{2}-\d{2}/)) {
        return new Date(value).toLocaleDateString('es-AR');
    }
    return String(value);
}

function hideResults() {
    resultsSection.style.display = 'none';
    previewCard.style.display = 'none';
}

function showProgress(show, message = 'Procesando...') {
    if (show) {
        progressContainer.style.display = 'block';
        progressLabel.textContent = message;
        // Don't start fake animation - real progress will be updated via monitoring
        progressBar.style.width = '0%';
    } else {
        progressContainer.style.display = 'none';
        document.getElementById('current-file').style.display = 'none';
        progressBar.style.width = '0%';
    }
}

function showStatus(message, type = 'info') {
    statusMessage.textContent = message;
    statusMessage.className = `status-message ${type}`;
    statusMessage.style.display = 'block';
    
    if (type === 'success' || type === 'info') {
        setTimeout(() => {
            statusMessage.style.display = 'none';
        }, 5000);
    }
}

function updateCurrentFileDisplay(currentFile, progress) {
    const currentFileDiv = document.getElementById('current-file');
    const currentFileNameSpan = document.getElementById('current-file-name');

    if (currentFile) {
        currentFileNameSpan.textContent = currentFile;
        currentFileDiv.style.display = 'block';

        // Update progress if provided
        if (typeof progress === 'number') {
            progressBar.style.width = `${Math.min(100, Math.max(0, progress))}%`;
        }
    } else {
        currentFileDiv.style.display = 'none';
    }
}

function generateSessionId() {
    return Date.now().toString(36) + Math.random().toString(36).substr(2);
}

function startProgressMonitoring() {
    if (!currentSessionId) return;

    progressInterval = setInterval(async () => {
        try {
            const response = await fetch(`/api/invoices/progress/${currentSessionId}`);
            if (response.ok) {
                const progress = await response.json();
                updateProgressFromServer(progress);
            }
        } catch (error) {
            console.error('Error fetching progress:', error);
        }
    }, 500); // Check every 500ms
}

function stopProgressMonitoring() {
    if (progressInterval) {
        clearInterval(progressInterval);
        progressInterval = null;
    }
}

function updateProgressFromServer(progress) {
    if (!progress) return;

    // Update progress bar
    progressBar.style.width = `${progress.progress}%`;

    // Update current file
    if (progress.currentFile) {
        updateCurrentFileDisplay(progress.currentFile, progress.progress);
    }

    // Update message
    if (progress.message) {
        progressLabel.textContent = progress.message;
    }

    // If completed, stop monitoring
    if (progress.isCompleted) {
        stopProgressMonitoring();
    }
}

function escapeHtml(text) {
    const map = {
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        '"': '&quot;',
        "'": '&#039;'
    };
    return String(text).replace(/[&<>"']/g, m => map[m]);
}