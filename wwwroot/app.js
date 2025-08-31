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
    const res = await fetch('/api/invoices/preview', { method:'POST', body: fd });
    if(!res.ok){
      let msg = 'Error al previsualizar.';
      try{ const j = await res.json(); msg = j.message || msg; }catch{ }
      setStatus(msg,'error');
      return;
    }
    const data = await res.json();
    $('#sum-total').textContent = data.totalPdfs ?? 0;
    $('#sum-dups').textContent = data.duplicates ?? 0;
    $('#sum-parsed').textContent = data.parsedPdfs ?? 0;
    $('#sum-nodata').textContent = data.noDataPdfs ?? 0;
    const errs = (data.errors||[]).map(e=>`<div>• ${e}</div>`).join('');
    $('#errors').innerHTML = errs;
    sumCard.style.display='block';
    setStatus(data.message || '');

    const rows = data.rows || [];
    // initialize columns UI if empty
    if(!colsList.hasChildNodes()) renderColumns();
    const order = getSelectedOrdered();
    if(rows.length){
      const cols = order.length ? order : Object.keys(rows[0]);
      tHead.innerHTML = `<tr>${cols.map(c=>`<th>${c}</th>`).join('')}</tr>`;
      tBody.innerHTML = rows.map(r=>`<tr>${cols.map(c=>`<td>${r[c] ?? ''}</td>`).join('')}</tr>`).join('');
      prevCard.style.display='block';
    }else{
      prevCard.style.display='none';
    }
  }catch(err){
    setStatus(err.message,'error');
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
    const res = await fetch('/api/invoices/upload', { method:'POST', body: fd });
    const ct = res.headers.get('content-type') || '';
    if(!res.ok){
      let msg = 'Error al generar Excel.';
      if(ct.includes('application/json')){ try{ const j = await res.json(); msg = j.message || msg; }catch{} }
      else{ try{ msg = await res.text(); }catch{} }
      setStatus(msg,'error');
      return;
    }
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'invoices.xls';
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
    setStatus('Excel descargado.');
  }catch(err){
    setStatus(err.message,'error');
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
