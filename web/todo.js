const board = document.querySelector('#board');
const panel = document.querySelector('#window');
const status = document.querySelector('#status');
const undoButton = document.querySelector('#undo');
const restoreButton = document.querySelector('#restore');
const storageKey = 'todo.browser.v1';
// Migrate earlier embedded demos without embedding a site owner's identity.
try {
  const legacyKey=Object.keys(localStorage).find(key=>key.endsWith('.todo-demo.v1'));
  if(legacyKey){
    for(const suffix of ['', '.settings']){
      if(!localStorage.getItem(storageKey+suffix)){
        const existing=localStorage.getItem(legacyKey+suffix);
        if(existing) localStorage.setItem(storageKey+suffix,existing);
      }
    }
  }
} catch {}
const palette = [
  ['#800020','#B33A53'], ['#EB7900','#FFCC91'], ['#E0C000','#FFED96'],
  ['#10AB48','#94E1AB'], ['#00ABB8','#8CE5EA'], ['#3885E8','#AACBFA'], ['#9C5DFF','#CAAEFA']
];
// Same position-based interpolation as native Theme.ColumnShade.
function shade(index, depth) {
  const [base,light]=palette[index % palette.length].map(hex=>[1,3,5].map(i=>parseInt(hex.slice(i,i+2),16)));
  const blend=1-Math.exp(-Math.max(0,depth)/3);
  return 'rgba('+base.map((c,i)=>Math.round(c+(light[i]-c)*blend)).join(',')+',0.9215686275)';
}
let groups = [{id:'watching',title:'Watching list',items:[]}];
try {
  const stored = JSON.parse(localStorage.getItem(storageKey));
  const previousFormat = Array.isArray(stored);
  const saved = previousFormat ? stored : stored?.groups;
  if (Array.isArray(saved) && saved[0]?.id === 'watching' && saved.every(group =>
    typeof group.id === 'string' && typeof group.title === 'string' && Array.isArray(group.items) &&
    group.items.every(item => typeof item.id === 'string' && typeof item.text === 'string')
  )) {
    groups = saved;

  }
} catch {}
const history = [];
let feedbackTimer;

function save() {
  try { localStorage.setItem(storageKey, JSON.stringify({ groups })); } catch {}
}
function remember() {
  history.push(structuredClone(groups));
  if (history.length > 60) history.shift();
  undoButton.disabled = false;
}
function change(action, focusId, message, snapshot) {
  remember();
  if (snapshot) history[history.length-1]=snapshot;
  action();
  save();
  render(focusId);
  if (message === 'Deleted' || message === 'Completed') {
    status.textContent=message; document.querySelector('#feedback').hidden=false;
    clearTimeout(feedbackTimer);
    feedbackTimer=setTimeout(()=>{document.querySelector('#feedback').hidden=true;},6500);
  }
}
function undo() {
  if (!history.length) return;
  const focused = document.activeElement.dataset.focusId;
  groups = history.pop();
  save();
  render(focused);
  document.querySelector('#feedback').hidden=true;
}
function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text) node.textContent = text;
  return node;
}
function button(className, text, label, action) {
  const node = element('button', className, text);
  node.type = 'button';
  node.setAttribute('aria-label', label);
  node.title = label;
  node.addEventListener('click', action);
  return node;
}
function edit(field, model, property) {
  let recorded = false;
  field.value = model[property];
  field.addEventListener('focus', () => { recorded = false; field.editSnapshot=structuredClone(groups); });
  field.addEventListener('input', () => {
    if (!recorded) { remember(); recorded = true; }
    model[property] = field.value;
    save();
  });
}
function addItem(group, index = group.items.length) {
  const item = { id: crypto.randomUUID(), text: '' };
  change(() => group.items.splice(index, 0, item), item.id, 'Item added.');
}
function moveItem(from, item, to, index) {
  const old = from.items.indexOf(item);
  if (from === to && index > old) index--;
  if (from === to && index === old) return;
  change(() => {
    from.items.splice(old, 1);
    to.items.splice(index, 0, item);
  }, item.id, 'Item moved.');
}
function safeLink(value) {
  try {
    const url = new URL(value);
    return ['http:', 'https:'].includes(url.protocol) ? url.href : '';
  } catch { return ''; }
}
function attachDrag(handle, card, group, item) {
  let drag;
  function clearTarget() {
    board.querySelectorAll('.is-target').forEach(node => node.classList.remove('is-target'));
  }
  handle.addEventListener('pointerdown', event => {
    if (event.button !== 0 || !event.isPrimary || event.target.closest('button,input,textarea,a')) return;
    drag = { id: event.pointerId, x: event.clientX, y: event.clientY };
    handle.setPointerCapture(event.pointerId);
  });
  function dropAt(event) {
    const node = document.elementFromPoint(event.clientX, event.clientY);
    const column = node?.closest('.column');
    if (!column || column.dataset.groupId !== group.id) return null;
    const target = groups.find(entry => entry.id === column.dataset.groupId);
    const targetCard = node.closest('.card');
    const index = target.items.findIndex(entry => entry.id === targetCard?.dataset.itemId);
    const box = targetCard?.getBoundingClientRect();
    const insert = index < 0 ? target.items.length : index + Number(event.clientY > box.top + box.height / 2);
    return { target, insert, indicator: targetCard || column };
  }
  handle.addEventListener('pointermove', event => {
    if (drag?.id !== event.pointerId) return;
    if (Math.hypot(event.clientX - drag.x, event.clientY - drag.y) < 5 && !drag.moved) return;
    drag.moved = true;
    const list=card.closest('.items'), bounds=list.getBoundingClientRect();
    if(event.clientY<bounds.top+30) list.scrollTop-=12;
    if(event.clientY>bounds.bottom-30) list.scrollTop+=12;
    card.classList.add('is-moving');
    clearTarget();
    dropAt(event)?.indicator.classList.add('is-target');
  });
  function finish(event) {
    if (drag?.id !== event.pointerId) return;
    const drop = event.type === 'pointerup' && drag.moved && dropAt(event);
    drag = null;
    clearTarget();
    card.classList.remove('is-moving');
    if (drop) moveItem(group, item, drop.target, drop.insert);
  }
  handle.addEventListener('pointerup', finish);
  handle.addEventListener('pointercancel', finish);
  handle.addEventListener('lostpointercapture', finish);
  handle.addEventListener('keydown', event => {
    if (!event.altKey || !['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key)) return;
    event.preventDefault();
    const row = group.items.indexOf(item);
    if (event.key === 'ArrowUp' && row > 0) moveItem(group, item, group, row - 1);
    if (event.key === 'ArrowDown' && row + 1 < group.items.length) moveItem(group, item, group, row + 2);
  });
}
function renderCard(group, item, index) {
  const card = element('div', 'card');
  card.dataset.itemId = item.id;
  const colorIndex=groups.indexOf(group);
  card.style.background='linear-gradient('+shade(colorIndex,index)+','+shade(colorIndex,index+1)+')';
  card.tabIndex=0; card.setAttribute('aria-label','Move item with Alt and arrow keys');
  const complete = button('done', '', 'Complete item', () => {
    const adjacent = group.items[index + 1] || group.items[index - 1];
    complete.classList.add('completed');
    complete.disabled=true;
    setTimeout(()=>{const current=group.items.indexOf(item); if(current>=0) change(() => group.items.splice(current, 1), adjacent?.id || group.id, 'Completed');},160);
  });
  const field = element('textarea');
  field.rows = 1;
  field.maxLength=400;
  field.dataset.focusId = item.id;
  field.setAttribute('aria-label', 'Item in ' + (group.title || 'unnamed topic'));
  edit(field, item, 'text');
  field.addEventListener('keydown', event => {
    if (event.isComposing || event.keyCode === 229) return;
    if (event.key === 'Enter') {
      event.preventDefault();
      addItem(group, index + 1);
    }
    if (['Backspace', 'Delete'].includes(event.key) && !event.repeat && !event.ctrlKey && !event.metaKey && !field.value && !item.url) {
      event.preventDefault();
      const adjacent = group.items[index - 1] || group.items[index + 1];
      change(() => group.items.splice(index, 1), adjacent?.id || group.id, 'Deleted', field.editSnapshot);
    }
  });
  const actions = element('div', 'actions');
  const link = element('a', 'item-link', '↗');
  link.target = '_blank';
  link.rel = 'noopener noreferrer';
  link.setAttribute('aria-label', 'Open saved link');
  function updateLink() {
    const href = safeLink(item.text.trim()) || safeLink(item.url);
    link.hidden = !href;
    if (href) link.href = href;
    else link.removeAttribute('href');
  }
  updateLink();
  field.addEventListener('input', updateLink);
  const content=element('div','card-content'); content.append(field);
  if(group.id==='watching' || item.url) {
    item.url ||= '';
    const urlField=element('textarea','url'); urlField.rows=1; urlField.maxLength=2000;
    urlField.dataset.focusId=item.id+'-url'; urlField.setAttribute('aria-label','Item link');
    edit(urlField,item,'url'); urlField.addEventListener('input',updateLink);
    urlField.addEventListener('keydown',event=>{
      if(event.isComposing || event.keyCode===229) return;
      if(event.key==='Enter'){event.preventDefault(); addItem(group,index+1);}
      if(['Backspace','Delete'].includes(event.key)&&!event.repeat&&!item.text&&!urlField.value){
        event.preventDefault(); change(()=>group.items.splice(index,1),group.items[index-1]?.id||group.id,'Deleted',urlField.editSnapshot);
      }
    });
    content.append(urlField);
  }
  attachDrag(card, card, group, item);
  actions.append(link);
  card.append(complete, content, actions);
  return card;
}
function renderColumn(group, index) {
  const column = element('section', index === 0 ? 'column watching' : 'column');
  column.dataset.groupId = group.id;
  const [tone, lift] = palette[index % palette.length];
  const ink=index%palette.length===0?'#fff7f5':'#090c0d';
  column.style.setProperty('--tone', tone);
  column.style.setProperty('--lift', lift);
  column.style.setProperty('--card-ink', ink);
  const head = element('div', 'column-head');
  const title = element('input');
  title.maxLength=60;
  title.dataset.focusId = group.id;
  title.setAttribute('aria-label', index === 0 ? 'Watching list name' : 'Topic name');
  edit(title, group, 'title');
  title.addEventListener('keydown', event => {
    if (event.isComposing || event.keyCode === 229) return;
    if (event.key === 'Enter') { event.preventDefault(); addItem(group); }
    if (index && ['Backspace', 'Delete'].includes(event.key) && !event.repeat && !title.value) {
      event.preventDefault();
      change(() => groups.splice(index, 1), groups[index - 1].id, 'Deleted', title.editSnapshot);
    }
  });
  head.append(title, button('add', '+', 'Add item to ' + (group.title || 'topic'), () => addItem(group)));
  const items = element('div', 'items');
  items.append(...group.items.map((item, row) => renderCard(group, item, row)));
  column.append(head, items);
  return column;
}
function render(focusId) {
  const left = board.querySelector('.topics')?.scrollLeft || 0;
  const offsets = new Map([...board.querySelectorAll('.column')].map(column =>
    [column.dataset.groupId, column.querySelector('.items').scrollTop]
  ));
  const [watching, ...columns] = groups.map(renderColumn);
  const topics = element('div', 'topics');
  topics.append(...columns);
  const divider=element('div','divider'); divider.tabIndex=0; divider.setAttribute('role','separator'); divider.setAttribute('aria-label','Watching list width'); divider.setAttribute('aria-orientation','vertical'); attachDivider(divider);
  board.replaceChildren(watching, divider, topics);
  topics.addEventListener('wheel',e=>{if(e.shiftKey && !e.deltaX){topics.scrollLeft+=e.deltaY; e.preventDefault();}},{passive:false});
  topics.scrollLeft = left;
  for (const column of [watching, ...columns]) column.querySelector('.items').scrollTop = offsets.get(column.dataset.groupId) || 0;
  undoButton.disabled = !history.length;
  if (focusId) board.querySelector('[data-focus-id="' + CSS.escape(focusId) + '"]')?.focus();
}

document.querySelector('#new-topic').addEventListener('click', () => {
  const group = { id: crypto.randomUUID(), title: '', items: [] };
  change(() => groups.push(group), group.id, 'Topic added.');
});
undoButton.addEventListener('click', undo);
document.addEventListener('keydown', event => {
  if (!event.isComposing && (event.ctrlKey || event.metaKey) && !event.shiftKey && event.key.toLowerCase() === 'z') {
    event.preventDefault();
    undo();
  }
});
const collapseButton = document.querySelector('#collapse');
collapseButton.addEventListener('click', () => {
  panel.hidden = true;
  restoreButton.hidden = false;
  restoreButton.style.left='auto'; restoreButton.style.right='12px'; restoreButton.style.top='12px'; document.querySelector('#expand').focus();
});
document.querySelector('#expand').addEventListener('click', () => {
  if (restoreButton.dragged) { restoreButton.dragged=false; return; }
  const position=restoreButton.getBoundingClientRect();
  restoreButton.hidden = true;
  panel.hidden = false;
  placePanel(position.left,position.top);
  collapseButton.focus();
});
let preferences={transparency:45,watchingWidth:250};
try { Object.assign(preferences,JSON.parse(localStorage.getItem(storageKey+'.settings'))||{}); } catch {}
function savePreferences(){try{localStorage.setItem(storageKey+'.settings',JSON.stringify(preferences));}catch{}}
function applyPreferences(){
  preferences.transparency=Math.max(35,Math.min(95,Number(preferences.transparency)||45));
  document.documentElement.style.setProperty('--glass',1-preferences.transparency/100);
  document.documentElement.style.setProperty('--watch-width',Math.max(120,Number(preferences.watchingWidth)||250)+'px');
  document.querySelector('#transparency').value=preferences.transparency;
  document.querySelector('#transparency-value').textContent=preferences.transparency+'%';
}
function attachDivider(divider){
  let start;
  divider.addEventListener('pointerdown',e=>{if(e.button!==0)return;start={x:e.clientX,width:board.querySelector('.watching').getBoundingClientRect().width/(Number(panel.style.zoom)||1)};divider.setPointerCapture(e.pointerId);});
  divider.addEventListener('pointermove',e=>{if(!start)return;preferences.watchingWidth=Math.max(120,Math.min(board.clientWidth*.45,start.width+(e.clientX-start.x)/(Number(panel.style.zoom)||1)));applyPreferences();});
  const finish=()=>{start=null;savePreferences();};
  divider.addEventListener('pointerup',finish);divider.addEventListener('pointercancel',finish);
  divider.addEventListener('keydown',e=>{if(!['ArrowLeft','ArrowRight'].includes(e.key))return;e.preventDefault();preferences.watchingWidth=Math.max(120,Math.min(board.clientWidth*.45,preferences.watchingWidth+(e.key==='ArrowLeft'?-16:16)));applyPreferences();savePreferences();});
}
const menu=document.querySelector('#settings'),more=document.querySelector('#more');
more.addEventListener('click',()=>{menu.hidden=!menu.hidden;more.setAttribute('aria-expanded',String(!menu.hidden));});
document.addEventListener('pointerdown',e=>{if(!menu.contains(e.target)&&!more.contains(e.target)){menu.hidden=true;more.setAttribute('aria-expanded','false');}});
document.querySelector('#transparency').addEventListener('input',e=>{preferences.transparency=Number(e.target.value);applyPreferences();savePreferences();});
function placePanel(x,y){
  const r=panel.getBoundingClientRect();
  const zoom=Number(panel.style.zoom)||1;
  if(panel.style.position!=='fixed'){panel.style.width=r.width/zoom+'px';panel.style.height=r.height/zoom+'px';}
  panel.style.position='fixed';panel.style.left=Math.max(0,Math.min(innerWidth-r.width,x))/zoom+'px';panel.style.top=Math.max(0,Math.min(innerHeight-r.height,y))/zoom+'px';
}
function dragSurface(handle,target,exclude){
  let start;
  handle.addEventListener('pointerdown',e=>{if(e.button!==0||e.target.closest(exclude))return;const r=target.getBoundingClientRect();start={x:e.clientX,y:e.clientY,left:r.left,top:r.top};target.dragged=false;});
  handle.addEventListener('pointermove',e=>{if(!start)return;const dx=e.clientX-start.x,dy=e.clientY-start.y;if(Math.hypot(dx,dy)<5&&!target.dragged)return;target.dragged=true;handle.setPointerCapture(e.pointerId);
    if(target===panel)placePanel(start.left+dx,start.top+dy);else{target.style.right='auto';target.style.left=Math.max(0,Math.min(innerWidth-62,start.left+dx))+'px';target.style.top=Math.max(7,Math.min(innerHeight-62,start.top+dy))+'px';}
  });
  handle.addEventListener('pointerup',()=>{if(start&&target===panel){const r=panel.getBoundingClientRect();preferences.position={x:r.x,y:r.y};savePreferences();}start=null;});
  handle.addEventListener('pointercancel',()=>{start=null;});
}
dragSurface(document.querySelector('header'),panel,'button');
dragSurface(restoreButton,restoreButton,'.bubble-close');
document.querySelector('#close').addEventListener('click',()=>{panel.hidden=true;restoreButton.hidden=true;});
document.querySelector('.bubble-close').addEventListener('click',e=>{e.stopPropagation();restoreButton.hidden=true;});
// Corners scale the whole interface, matching native corner scaling.
for(const corner of document.querySelectorAll('.resize')){
  let start;
  corner.addEventListener('pointerdown',e=>{if(e.button!==0)return;const r=panel.getBoundingClientRect();start={x:e.clientX,y:e.clientY,width:r.width,height:r.height,left:r.left,top:r.top,zoom:Number(panel.style.zoom)||1};corner.setPointerCapture(e.pointerId);});
  corner.addEventListener('pointermove',e=>{if(!start)return;const west=corner.classList.contains('nw')||corner.classList.contains('sw'),north=corner.classList.contains('nw')||corner.classList.contains('ne');const dx=(e.clientX-start.x)*(west?-1:1),dy=(e.clientY-start.y)*(north?-1:1);const ratio=Math.max(.55,Math.min(1.6,1+(dx/start.width+dy/start.height)/2));const zoom=Math.max(.4,Math.min(2,start.zoom*ratio));
    panel.style.width=start.width/start.zoom+'px';panel.style.height=start.height/start.zoom+'px';panel.style.zoom=zoom;placePanel(start.left+(west?start.width*(1-zoom/start.zoom):0),start.top+(north?start.height*(1-zoom/start.zoom):0));
  });
  corner.addEventListener('pointerup',()=>{if(start){const r=panel.getBoundingClientRect();preferences.geometry={width:parseFloat(panel.style.width),height:parseFloat(panel.style.height),zoom:Number(panel.style.zoom)};preferences.position={x:r.x,y:r.y};savePreferences();}start=null;});
  corner.addEventListener('pointercancel',()=>{start=null;});
}
applyPreferences();
if(preferences.geometry){const g=preferences.geometry;panel.style.width=Math.max(300,g.width)+'px';panel.style.height=Math.max(200,g.height)+'px';panel.style.zoom=Math.max(.4,Math.min(2,g.zoom||1));}
if(preferences.position)placePanel(preferences.position.x,preferences.position.y);
render();
