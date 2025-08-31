const $ = (s)=>document.querySelector(s);
const statusEl = $('#status');
const sumCard = $('#summary-card');
const prevCard = $('#preview-card');
const tHead = $('#preview-table thead');
const tBody = $('#preview-table tbody');
const colsList = $('#cols-list');

let defaultColumns = [
  'InvoiceNumber','FechaEmision','Artista','Concepto','Instrumento',
  'FechaDesde','FechaHasta','HoraDesde','HoraHasta','Dias','Horas',
  'Unitario','Subtotal','SubtotalFactura','AporteContribucionOS','Jubilacion',
  'RecursoAdministrativo','Tasa','Transporte','TotalFactura','Programa'
];
let selectedColumns = [...defaultColumns];
let enabledCols = new Set(defaultColumns);

function renderColumns(){
  colsList.innerHTML = selectedColumns.map((c)=>
    `<label draggable="true" data-col="${c}"><input type="checkbox" data-col="${c}" ${enabledCols.has(c)?'checked':''}/> ${c}</label>`
  ).join('');
  // drag & drop handlers
  colsList.querySelectorAll('label').forEach(lbl=>{
    lbl.addEventListener('dragstart', (e)=>{
      e.dataTransfer.setData('text/plain', lbl.dataset.col);
      lbl.classList.add('dragging');
    });
    lbl.addEventListener('dragend', ()=> lbl.classList.remove('dragging'));
    lbl.addEventListener('dragover', (e)=> e.preventDefault());
    lbl.addEventListener('drop', (e)=>{
      e.preventDefault();
      const from = e.dataTransfer.getData('text/plain');
      const to = lbl.dataset.col;
      if(!from || !to || from===to) return;
      const i = selectedColumns.indexOf(from);
      const j = selectedColumns.indexOf(to);
      if(i<0 || j<0) return;
      selectedColumns.splice(i,1);
      selectedColumns.splice(j,0,from);
      renderColumns();
    });
  });
}

function getSelectedOrdered(){
  const checks = colsList.querySelectorAll('input[type="checkbox"]');
  const out = [];
  selectedColumns.forEach(c=>{
    const chk = Array.from(checks).find(x=>x.dataset.col===c);
    if(chk && chk.checked) out.push(c);
  });
  return out;
}

function setStatus(msg, type='info'){
  statusEl.textContent = msg || '';
  statusEl.style.color = type==='error' ? '#fecaca' : type==='warn' ? '#fbbf24' : '#94a3b8';
}

// Progreso visual (subida/procesamiento)
const progress = document.getElementById('progress');
const progressBar = document.getElementById('progress-bar');
const progressLabel = document.getElementById('progress-label');
function showProgress(pct, label){
  if(!progress) return;
  progress.style.display='block';
  progressBar.style.width = Math.min(100, Math.max(0, pct)) + '%';
  progressLabel.textContent = label ?? (Math.round(pct)+'%');
}
function hideProgress(){
  if(!progress) return;
  progress.style.display='none';
  progressBar.style.width='0%';
  progressLabel.textContent='0%';
}

function toFormData(){
  const f = new FormData();
  const file = $('#zip').files[0];
  if(!file) throw new Error('Selecciona un archivo .zip');
  f.append('zip', file, file.name);
  return f;
}

async function preview(){
  try{
    setStatus('Procesando vista previa...');
    sumCard.style.display='none';
    prevCard.style.display='none';
    const fd = toFormData();
    const dup = document.getElementById('chk-dup');
    if(dup && dup.checked) fd.append('processDuplicates','true');
    // XHR para mostrar progreso de subida
    const xhr = new XMLHttpRequest();
    const resp = await new Promise((resolve,reject)=>{
      xhr.open('POST','/api/invoices/preview');
      xhr.upload.onprogress = (e)=>{ if(e.lengthComputable){ const pct=(e.loaded/e.total)*100; showProgress(pct, `Subiendo ${Math.round(pct)}%`);} };
      xhr.onload = ()=> resolve({ status:xhr.status, body:xhr.responseText, ct: xhr.getResponseHeader('content-type')||'' });
      xhr.onerror = ()=> reject(new Error('Error de red'));
      xhr.send(fd);
      showProgress(5,'Subiendo 5%');
    });
    showProgress(95,'Procesando...');
    if(resp.status < 200 || resp.status >= 300){
      let msg = 'Error al previsualizar.';
      try{ msg = resp.ct.includes('application/json') ? (JSON.parse(resp.body).message || msg) : (resp.body || msg); }catch{}
      setStatus(msg,'error'); hideProgress(); return;
    }
    const data = JSON.parse(resp.body);
    $('#sum-total').textContent = data.totalPdfs ?? 0;
    $('#sum-dups').textContent = data.duplicates ?? 0;
    $('#sum-parsed').textContent = data.parsedPdfs ?? 0;
    $('#sum-nodata').textContent = data.noDataPdfs ?? 0;
    const errs = (data.errors||[]).map(e=>`<div>• ${e}</div>`).join('');
    $('#errors').innerHTML = errs;
    sumCard.style.display='block';
    setStatus(data.message || '');

    // Mock: si no hay filas, mostrar algunas para que el usuario pruebe el reordenamiento
    const rows = (data.rows && data.rows.length ? data.rows : [
      {InvoiceNumber:'000001', FechaEmision:'17/02/2025', Artista:'NOMBRE A', Concepto:'REPETICION 30%', Instrumento:'Guitarra', FechaDesde:'05/01/25', FechaHasta:'05/01/25', HoraDesde:'00:00', HoraHasta:'02:00', Dias:0, Horas:2, Unitario:2700, Subtotal:5400, SubtotalFactura:113400, AporteContribucionOS:6804, Jubilacion:0, RecursoAdministrativo:0, Tasa:0, Transporte:0, TotalFactura:113400, Programa:'PROGRAMA X'},
      {InvoiceNumber:'000001', FechaEmision:'17/02/2025', Artista:'NOMBRE B', Concepto:'REPETICION 30%', Instrumento:'Bajo', FechaDesde:'05/01/25', FechaHasta:'05/01/25', HoraDesde:'00:00', HoraHasta:'02:00', Dias:0, Horas:2, Unitario:2700, Subtotal:5400, SubtotalFactura:113400, AporteContribucionOS:6804, Jubilacion:0, RecursoAdministrativo:0, Tasa:0, Transporte:0, TotalFactura:113400, Programa:'PROGRAMA X'},
      {InvoiceNumber:'000001', FechaEmision:'17/02/2025', Artista:'NOMBRE C', Concepto:'REPETICION 30%', Instrumento:'Batería', FechaDesde:'05/01/25', FechaHasta:'05/01/25', HoraDesde:'00:00', HoraHasta:'02:00', Dias:0, Horas:2, Unitario:2700, Subtotal:5400, SubtotalFactura:113400, AporteContribucionOS:6804, Jubilacion:0, RecursoAdministrativo:0, Tasa:0, Transporte:0, TotalFactura:113400, Programa:'PROGRAMA X'}
    ]);
    // initialize columns UI if empty
    if(!colsList.hasChildNodes()) renderColumns();
    const order = getSelectedOrdered();
    if(rows.length){
      const cols = order.length ? order : Object.keys(rows[0]);
      tHead.innerHTML = `<tr>${cols.map(c=>`<th>${c}</th>`).join('')}</tr>`;
      tBody.innerHTML = rows.map(r=>`<tr>${cols.map(c=>`<td>${r[c] ?? ''}</td>`).join('')}</tr>`).join('');
      prevCard.style.display='block';
    }else{
      prevCard.style.display='block';
    }
    showProgress(100,'Completado'); setTimeout(hideProgress, 600);
  }catch(err){
    setStatus(err.message,'error');
    hideProgress();
  }
}

async function download(){
  try{
    setStatus('Generando Excel...');
    const fd = toFormData();
    const order = getSelectedOrdered();
    if(order && order.length) fd.append('columns', JSON.stringify(order));
    const dup = document.getElementById('chk-dup');
    if(dup && dup.checked) fd.append('processDuplicates','true');
    // XHR para mostrar progreso de subida y feedback de procesamiento
    const xhr = new XMLHttpRequest();
    const resp = await new Promise((resolve,reject)=>{
      xhr.open('POST','/api/invoices/upload');
      xhr.responseType='blob';
      xhr.upload.onprogress=(e)=>{ if(e.lengthComputable){ const pct=(e.loaded/e.total)*100; showProgress(pct, `Subiendo ${Math.round(pct)}%`);} };
      xhr.onload=()=> resolve({ status:xhr.status, blob:xhr.response, ct: xhr.getResponseHeader('content-type')||'' });
      xhr.onerror=()=> reject(new Error('Error de red'));
      xhr.send(fd);
      showProgress(5,'Subiendo 5%');
    });
    showProgress(95,'Procesando...');
    if(resp.status<200||resp.status>=300){
      let msg='Error al generar Excel.'; try{ if(resp.ct.includes('application/json')){ const t = await resp.blob.text(); msg = JSON.parse(t).message || msg; } }catch{}
      setStatus(msg,'error'); hideProgress(); return;
    }
    const blob = resp.blob;
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'invoices.xls';
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
    setStatus('Excel descargado.');
    showProgress(100,'Completado'); setTimeout(hideProgress, 600);
  }catch(err){
    setStatus(err.message,'error');
    hideProgress();
  }
}

$('#btn-preview').addEventListener('click', preview);
$('#btn-download').addEventListener('click', download);

// Maintain enabled set when user toggles checks
colsList.addEventListener('change', (e)=>{
  const t = e.target;
  if(t && t.type==='checkbox'){
    if(t.checked) enabledCols.add(t.dataset.col); else enabledCols.delete(t.dataset.col);
  }
});
$('#col-reset').addEventListener('click', ()=>{ selectedColumns=[...defaultColumns]; enabledCols = new Set(defaultColumns); renderColumns(); });
const $ = (s)=>document.querySelector(s);
const statusEl = $('#status');
const sumCard = $('#summary-card');
const prevCard = $('#preview-card');
const tHead = $('#preview-table thead');
const tBody = $('#preview-table tbody');
const zoneEnabled = $('#cols-enabled');
const zoneDisabled = $('#cols-disabled');

const defaultColumns = [
  'InvoiceNumber','FechaEmision','Artista','Concepto','Instrumento',
  'FechaDesde','FechaHasta','HoraDesde','HoraHasta','Dias','Horas',
  'Unitario','Subtotal','SubtotalFactura','AporteContribucionOS','Jubilacion',
  'RecursoAdministrativo','Tasa','Transporte','TotalFactura','Programa'
];
let selectedColumns = [...defaultColumns];
let enabledCols = new Set(defaultColumns);
let currentRows = [];

function setStatus(msg, type='info'){
  statusEl.textContent = msg || '';
  statusEl.style.color = type==='error' ? '#fecaca' : type==='warn' ? '#fbbf24' : '#94a3b8';
}

// Progreso visual
const progress = document.getElementById('progress');
const progressBar = document.getElementById('progress-bar');
const progressLabel = document.getElementById('progress-label');
function showProgress(pct, label){
  if(!progress) return;
  progress.style.display='block';
  progressBar.style.width = Math.min(100, Math.max(0, pct)) + '%';
  progressLabel.textContent = label ?? (Math.round(pct)+'%');
}
function hideProgress(){
  if(!progress) return;
  progress.style.display='none';
  progressBar.style.width='0%';
  progressLabel.textContent='0%';
}

function makeChip(col){
  const el = document.createElement('div');
  el.className='chip'; el.draggable=true; el.dataset.col=col; el.textContent=col;
  const close=document.createElement('span'); close.textContent='×'; close.className='close'; close.title='Ocultar';
  close.addEventListener('click', (e)=>{ e.stopPropagation(); enabledCols.delete(col); renderColumns(); renderPreview(currentRows); });
  el.appendChild(close);
  el.addEventListener('dragstart', (e)=>{ e.dataTransfer.setData('text/col', col); el.classList.add('dragging'); });
  el.addEventListener('dragend', ()=> el.classList.remove('dragging'));
  return el;
}

function renderColumns(){
  zoneEnabled.innerHTML=''; zoneDisabled.innerHTML='';
  selectedColumns.forEach(c=>{ if(enabledCols.has(c)) zoneEnabled.appendChild(makeChip(c)); });
  defaultColumns.forEach(c=>{ if(!enabledCols.has(c)) zoneDisabled.appendChild(makeChip(c)); });
}

function enableDnDZones(){
  [zoneEnabled, zoneDisabled].forEach(zone=>{
    zone.addEventListener('dragover', (e)=> e.preventDefault());
    zone.addEventListener('drop', (e)=>{
      e.preventDefault();
      const col = e.dataTransfer.getData('text/col'); if(!col) return;
      if(zone===zoneEnabled){
        enabledCols.add(col);
        const target = e.target.closest('.chip');
        if(target){
          const to = target.dataset.col;
          const i = selectedColumns.indexOf(col);
          const j = selectedColumns.indexOf(to);
          if(i>=0) selectedColumns.splice(i,1);
          if(j>=0) selectedColumns.splice(j,0,col);
        } else if(!selectedColumns.includes(col)){
          selectedColumns.push(col);
        }
      } else {
        enabledCols.delete(col);
      }
      renderColumns();
      renderPreview(currentRows);
    });
  });
}

function getSelectedOrdered(){
  return selectedColumns.filter(c=> enabledCols.has(c));
}

function toFormData(){
  const f = new FormData();
  const file = document.getElementById('zip').files[0];
  if(!file) throw new Error('Selecciona un archivo .zip');
  f.append('zip', file, file.name);
  return f;
}

function renderPreview(rows){
  currentRows = rows;
  const cols = getSelectedOrdered();
  if(!rows || rows.length===0){ tHead.innerHTML=''; tBody.innerHTML=''; return; }
  tHead.innerHTML = `<tr>${cols.map(c=>`<th>${c}</th>`).join('')}</tr>`;
  tBody.innerHTML = rows.map(r=>`<tr>${cols.map(c=>`<td>${r[c] ?? ''}</td>`).join('')}</tr>`).join('');
}

async function preview(){
  try{
    setStatus('Procesando vista previa...');
    sumCard.style.display='none';
    const fd = toFormData();
    const dup = document.getElementById('chk-dup');
    if(dup && dup.checked) fd.append('processDuplicates','true');
    const xhr = new XMLHttpRequest();
    const resp = await new Promise((resolve,reject)=>{
      xhr.open('POST','/api/invoices/preview');
      xhr.upload.onprogress=(e)=>{ if(e.lengthComputable){ const pct=(e.loaded/e.total)*100; showProgress(pct, `Subiendo ${Math.round(pct)}%`);} };
      xhr.onload=()=> resolve({ status:xhr.status, body:xhr.responseText, ct:xhr.getResponseHeader('content-type')||'' });
      xhr.onerror=()=> reject(new Error('Error de red'));
      xhr.send(fd); showProgress(5,'Subiendo 5%');
    });
    showProgress(95,'Procesando...');
    if(resp.status<200 || resp.status>=300){
      let msg='Error al previsualizar.'; try{ msg = resp.ct.includes('application/json') ? (JSON.parse(resp.body).message || msg) : (resp.body || msg);}catch{}
      setStatus(msg,'error'); hideProgress(); return;
    }
    const data = JSON.parse(resp.body);
    $('#sum-total').textContent = data.totalPdfs ?? 0;
    $('#sum-dups').textContent = data.duplicates ?? 0;
    $('#sum-parsed').textContent = data.parsedPdfs ?? 0;
    $('#sum-nodata').textContent = data.noDataPdfs ?? 0;
    $('#errors').innerHTML = (data.errors||[]).map(e=>`<div>• ${e}</div>`).join('');
    sumCard.style.display='block';
    setStatus(data.message || '');

    const rows = (data.rows && data.rows.length ? data.rows : [
      {InvoiceNumber:'000001', FechaEmision:'17/02/2025', Artista:'NOMBRE A', Concepto:'REPETICION 30%', Instrumento:'Guitarra', FechaDesde:'05/01/25', FechaHasta:'05/01/25', HoraDesde:'00:00', HoraHasta:'02:00', Dias:0, Horas:2, Unitario:2700, Subtotal:5400, SubtotalFactura:113400, AporteContribucionOS:6804, Jubilacion:0, RecursoAdministrativo:0, Tasa:0, Transporte:0, TotalFactura:113400, Programa:'PROGRAMA X'},
      {InvoiceNumber:'000001', FechaEmision:'17/02/2025', Artista:'NOMBRE B', Concepto:'REPETICION 30%', Instrumento:'Bajo', FechaDesde:'05/01/25', FechaHasta:'05/01/25', HoraDesde:'00:00', HoraHasta:'02:00', Dias:0, Horas:2, Unitario:2700, Subtotal:5400, SubtotalFactura:113400, AporteContribucionOS:6804, Jubilacion:0, RecursoAdministrativo:0, Tasa:0, Transporte:0, TotalFactura:113400, Programa:'PROGRAMA X'},
      {InvoiceNumber:'000001', FechaEmision:'17/02/2025', Artista:'NOMBRE C', Concepto:'REPETICION 30%', Instrumento:'Batería', FechaDesde:'05/01/25', FechaHasta:'05/01/25', HoraDesde:'00:00', HoraHasta:'02:00', Dias:0, Horas:2, Unitario:2700, Subtotal:5400, SubtotalFactura:113400, AporteContribucionOS:6804, Jubilacion:0, RecursoAdministrativo:0, Tasa:0, Transporte:0, TotalFactura:113400, Programa:'PROGRAMA X'}
    ]);
    if(!zoneEnabled.hasChildNodes() && !zoneDisabled.hasChildNodes()){ renderColumns(); enableDnDZones(); }
    renderPreview(rows); prevCard.style.display='block';
    showProgress(100,'Completado'); setTimeout(hideProgress, 600);
  }catch(err){ setStatus(err.message,'error'); hideProgress(); }
}

async function download(){
  try{
    setStatus('Generando Excel...');
    const fd = toFormData();
    const order = getSelectedOrdered();
    if(order && order.length) fd.append('columns', JSON.stringify(order));
    const dup = document.getElementById('chk-dup');
    if(dup && dup.checked) fd.append('processDuplicates','true');
    const xhr = new XMLHttpRequest();
    const resp = await new Promise((resolve,reject)=>{
      xhr.open('POST','/api/invoices/upload');
      xhr.responseType='blob';
      xhr.upload.onprogress=(e)=>{ if(e.lengthComputable){ const pct=(e.loaded/e.total)*100; showProgress(pct, `Subiendo ${Math.round(pct)}%`);} };
      xhr.onload=()=> resolve({ status:xhr.status, blob:xhr.response, ct:xhr.getResponseHeader('content-type')||'' });
      xhr.onerror=()=> reject(new Error('Error de red'));
      xhr.send(fd); showProgress(5,'Subiendo 5%');
    });
    showProgress(95,'Procesando...');
    if(resp.status<200||resp.status>=300){ let msg='Error al generar Excel.'; setStatus(msg,'error'); hideProgress(); return; }
    const blob = resp.blob; const url = URL.createObjectURL(blob); const a=document.createElement('a'); a.href=url; a.download='invoices.xls'; document.body.appendChild(a); a.click(); a.remove(); URL.revokeObjectURL(url);
    setStatus('Excel descargado.'); showProgress(100,'Completado'); setTimeout(hideProgress, 600);
  }catch(err){ setStatus(err.message,'error'); hideProgress(); }
}

document.getElementById('zip').addEventListener('change', ()=> preview());
document.getElementById('btn-download').addEventListener('click', download);
document.getElementById('col-reset').addEventListener('click', ()=>{ selectedColumns=[...defaultColumns]; enabledCols=new Set(defaultColumns); renderColumns(); renderPreview(currentRows); });
