'use strict';
const $ = id => document.getElementById(id);
let snapshot, selected, working = false, view = 'library', pcPreferences;
let setupPending = false, setupInitialized = false, setupControllerDetected = false, setupScreenSignature = '', setupReview = false, offline = false;
let capturePrefix, captureTimer, controllerKind, controllerName = "";
let swapEdited=false, controllerSourceChanged=false, controllerRefresh=false;
const bindings = new Map(), editedBindings = new Set();
let pickerRequest;
let catalogChoice = null, catalogTimer, catalogRequest, catalogRevision = 0, catalogLoading = false;
let addWallpaper = null, wallpaperTarget, wallpaperCandidate, wallpaperRequest, wallpaperRevision = 0, wallpaperPage = 0, wallpaperImages = [], wallpaperApplying = false, wallpaperLastQuery = '';
function cancelWallpaperSearch() { wallpaperRequest?.abort(); wallpaperRequest = null; wallpaperRevision++; }
function resetAddWallpaper() { addWallpaper = null; $('addWallpaperNote').textContent = ''; $('addWallpaperPreview').hidden = true; $('clearAddWallpaper').hidden = true; }
function wallpaperUrl(url) { return typeof url === 'string' && /^\/api\/hub\/wallpapers\/image\/wp-[a-f0-9]{32}\/(thumbnail|full)$/.test(url); }
function wallpaperCaption(item) { return [item.width + ' × ' + item.height, item.source, item.artist, item.license].filter(Boolean).join(' · '); }
function showWallpaper(item) {
  wallpaperCandidate = item; $('wallpaperLarge').src = item.preview; $('wallpaperCaption').textContent = wallpaperCaption(item);
  const source = $('wallpaperSource'); source.hidden = !/^https:\/\/(wallhaven\.cc|commons\.wikimedia\.org|store\.steampowered\.com)\//.test(item.sourceUrl || '');
  if (!source.hidden) source.href = item.sourceUrl;
  $('wallpaperPreview').hidden = false; $('wallpaperUse').disabled = false;
  $('wallpaperPreview').scrollIntoView({behavior:'smooth',block:'nearest'});
}
function renderWallpapers() {
  $('wallpaperGrid').replaceChildren(...wallpaperImages.filter(item => wallpaperUrl(item.thumbnail) && wallpaperUrl(item.preview)).map(item => {
    const tile = el('button','wallpaper-tile'); tile.type = 'button'; tile.setAttribute('aria-label', 'Ver fundo ' + item.width + ' por ' + item.height + ' · ' + item.source);
    const image = el('img'); image.src = item.thumbnail; image.alt = ''; image.loading = 'lazy'; image.onerror = () => { image.hidden = true; };
    const size = item.width >= 3840 && item.height >= 2160 ? '4K · ' : '';
    tile.append(image, el('strong','',size + item.width + ' × ' + item.height), el('span','muted',item.source)); tile.onclick = () => showWallpaper(item); return tile;
  }));
}
async function searchWallpapers(more = false) {
  const query = $('wallpaperQuery').value.trim(); cancelWallpaperSearch(); const revision = wallpaperRevision;
  if (more && query !== wallpaperLastQuery) more = false;
  if (query.length < 2) { $('wallpaperStatus').textContent = 'Digite pelo menos duas letras do nome do jogo.'; return; }
  if (!more) { wallpaperImages = []; wallpaperPage = 0; wallpaperCandidate = null; $('wallpaperGrid').replaceChildren(); $('wallpaperPreview').hidden = true; }
  const page = wallpaperPage + 1, request = new AbortController(); wallpaperRequest = request;
  $('wallpaperStatus').textContent = 'Buscando fundos em alta resolução…'; $('wallpaperMore').disabled = true;
  const target = wallpaperTarget;
  const suffix = target?.gameId ? '&gameId=' + encodeURIComponent(target.gameId) : catalogChoice ? '&catalogId=' + encodeURIComponent(catalogChoice.id) : '';
  try {
    const result = await api('/api/hub/wallpapers/search?query=' + encodeURIComponent(query) + '&page=' + page + suffix, undefined, {signal:request.signal});
    if (revision !== wallpaperRevision) return;
    const known = new Set(wallpaperImages.map(i=>i.id)); wallpaperImages.push(...result.images.filter(i=>!known.has(i.id))); wallpaperPage = page; wallpaperLastQuery = query;
    renderWallpapers(); $('wallpaperMore').hidden = !result.hasMore;
    $('wallpaperStatus').textContent = [wallpaperImages.length ? 'Escolha uma imagem para ver a prévia e usar como fundo.' : 'Nenhum fundo encontrado. Tente outro nome ou escolha uma imagem local.', ...(result.warnings || []), result.cached ? 'Resultados guardados neste PC.' : ''].filter(Boolean).join(' ');
  } catch(error) { if (!request.signal.aborted && revision === wallpaperRevision) $('wallpaperStatus').textContent = error.message; }
  finally { if (wallpaperRequest === request) wallpaperRequest = null; if (revision === wallpaperRevision) $('wallpaperMore').disabled = false; }
}
function openWallpapers(game = null) {
  if (wallpaperApplying) return;
  cancelWallpaperSearch(); wallpaperTarget = game ? {gameId:game.id} : {add:true}; wallpaperImages = []; wallpaperPage = 0; wallpaperCandidate = null;
  $('wallpaperQuery').value = (game?.metadata?.title || game?.name || catalogChoice?.metadata.title || $('addTitle').value).trim();
  $('wallpaperGrid').replaceChildren(); $('wallpaperPreview').hidden = true; $('wallpaperMore').hidden = true;
  if (!$('wallpaperDialog').open) $('wallpaperDialog').showModal();
  return searchWallpapers();
}
async function useWallpaper() {
  if (!wallpaperCandidate || wallpaperApplying) return;
  const item = wallpaperCandidate, target = wallpaperTarget; wallpaperApplying = true; $('wallpaperUse').disabled = true;
  $('wallpaperStatus').textContent = target.add ? 'Fundo escolhido.' : 'Baixando e salvando o fundo…';
  try {
    if (target.add) {
      addWallpaper = item; $('addBackdrop').value = ''; $('addWallpaperPreview').src = item.thumbnail; $('addWallpaperPreview').hidden = false;
      $('addWallpaperNote').textContent = 'Fundo escolhido · ' + wallpaperCaption(item); $('clearAddWallpaper').hidden = false;
    } else {
      snapshot = await api('/api/hub/games/' + encodeURIComponent(target.gameId) + '/wallpaper', {wallpaperId:item.id}); render(); openGame(target.gameId);
    }
    $('wallpaperDialog').close(); notice(target.add ? 'Fundo escolhido. Ele será salvo ao adicionar o jogo.' : 'Fundo atualizado.');
  } catch(error) { $('wallpaperStatus').textContent = error.message; }
  finally { wallpaperApplying = false; $('wallpaperUse').disabled = false; }
}
const catalogFields = ['Description','Year','Platforms','Developers','Publishers','Genres'];
function cancelCatalog() { clearTimeout(catalogTimer); catalogRequest?.abort(); catalogRequest = null; catalogLoading = false; catalogRevision++; }
function clearCatalog(clearFields = false) {
  cancelCatalog(); catalogChoice = null; $('catalogClear').hidden = true;
  $('catalogResults').replaceChildren(); $('catalogPreview').replaceChildren(); $('catalogPreview').hidden = true;
  if (clearFields) for (const field of catalogFields) $('add' + field).value = '';
  if (clearFields) resetAddWallpaper();
}
function catalogImage(url, className = '') {
  // The catalog only returns authenticated, same-origin image routes.
  if (typeof url !== 'string' || !/^\/api\/hub\/catalog\/art\/(steam-\d+|wiki-Q\d+)\/(cover|backdrop)$/.test(url)) return null;
  const image = el('img', className); image.src = url; image.alt = ''; image.loading = 'lazy';
  image.onerror = () => { image.hidden = true; }; return image;
}
function catalogCard(entry, preview = false) {
  const m = entry.metadata, card = el(preview ? 'div' : 'button', 'catalog-card');
  if (!preview) { card.type = 'button'; card.setAttribute('aria-label', 'Escolher ' + m.title + (m.year ? ', ' + m.year : '')); }
  const backdrop = catalogImage(entry.backdrop, 'catalog-backdrop'); if (backdrop) card.append(backdrop);
  const cover = catalogImage(entry.cover, 'catalog-cover'); if (cover) card.append(cover); else card.append(el('div','catalog-cover catalog-placeholder',m.title.charAt(0)));
  const body = el('div','catalog-card-body'), title = el('div','catalog-title'); title.append(el('strong','',m.title));
  if (m.year) title.append(el('span','catalog-year',String(m.year))); body.append(title);
  body.append(el('span','muted',(m.platforms || []).join(' · ') || 'Plataforma não informada'));
  if (m.developers?.length) body.append(el('span','muted',m.developers.join(' · ')));
  if (m.description) body.append(el('p','catalog-description',m.description));
  if (preview) {
    if (m.genres?.length) body.append(el('span','muted',m.genres.join(' · ')));
    if (/^https:\/\/(store\.steampowered\.com|(?:en|pt)\.wikipedia\.org)\//.test(m.sourceUrl || '')) {
      const link = el('a','catalog-source',m.source === 'steam' ? 'Fonte: Steam' : 'Fonte: Wikipédia / Wikidata'); link.href = m.sourceUrl; link.target = '_blank'; link.rel = 'noopener noreferrer'; body.append(link);
    }
  } else body.append(el('span','catalog-source',m.source === 'steam' ? 'Steam' : 'Wikipédia / Wikidata'));
  card.append(body);
  if (!preview) card.onclick = () => chooseCatalog(entry);
  return card;
}
async function searchCatalog() {
  const query = $('addTitle').value.trim(); cancelCatalog(); const revision = catalogRevision;
  if (query.length < 2) { $('catalogStatus').textContent = 'Digite pelo menos duas letras do nome do jogo.'; return; }
  const request = new AbortController(); catalogRequest = request;
  $('catalogStatus').textContent = 'Buscando fichas…'; $('catalogResults').replaceChildren();
  try {
    const result = await api('/api/hub/catalog/search?query=' + encodeURIComponent(query), undefined, {signal: request.signal});
    if (revision !== catalogRevision) return;
    $('catalogResults').replaceChildren(...result.matches.map(entry => catalogCard(entry)));
    const status = result.matches.length ? 'Escolha a edição do seu jogo para preencher a ficha.' : 'Nenhuma ficha encontrada. Você pode cadastrar o jogo e preencher os dados manualmente.';
    $('catalogStatus').textContent = [status, ...(result.warnings || []), result.cached ? 'Resultados guardados neste PC.' : ''].filter(Boolean).join(' ');
  } catch (error) { if (!request.signal.aborted && revision === catalogRevision) $('catalogStatus').textContent = error.message + ' O cadastro manual continua disponível.'; }
  finally { if (catalogRequest === request) catalogRequest = null; }
}
async function chooseCatalog(entry) {
  cancelCatalog(); const revision = catalogRevision, request = new AbortController(); catalogRequest = request; catalogLoading = true;
  $('catalogStatus').textContent = 'Carregando informações e imagens…';
  $('catalogClear').hidden = false;
  try {
    const detailed = await api('/api/hub/catalog/details?id=' + encodeURIComponent(entry.id), undefined, {signal:request.signal});
    if (revision !== catalogRevision) return;
    const m = detailed.metadata; catalogChoice = detailed; catalogLoading = false;
    resetAddWallpaper();
    $('addTitle').value = m.title;
    for (const field of catalogFields) { const value = m[field.charAt(0).toLowerCase() + field.slice(1)]; $('add' + field).value = Array.isArray(value) ? value.join('; ') : value ?? ''; }
    $('catalogResults').replaceChildren(); $('catalogPreview').replaceChildren(catalogCard(detailed, true)); $('catalogPreview').hidden = false; $('catalogClear').hidden = false;
    $('catalogStatus').textContent = 'Ficha selecionada. Você pode ajustar o nome e as informações antes de adicionar.' + (!detailed.backdrop ? ' Esta fonte não oferece um fundo adequado; você pode escolher uma imagem local.' : '');
  } catch (error) { if (!request.signal.aborted && revision === catalogRevision) $('catalogStatus').textContent = error.message + ' Tente novamente ou use o cadastro manual.'; }
  finally { if (catalogRequest === request) { catalogRequest = null; catalogLoading = false; } }
}
function catalogTitleChanged() {
  if (catalogChoice) {
    cancelCatalog();
    // A custom display name does not discard an explicitly chosen identity.
    $('catalogStatus').textContent = 'Ficha: ' + catalogChoice.metadata.title + '. O nome na biblioteca será o que você digitou.'; return;
  }
  cancelCatalog(); $('catalogResults').replaceChildren();
  if ($('addTitle').value.trim().length < 2) { $('catalogStatus').textContent = 'Digite o nome para buscar capas e informações.'; return; }
  catalogTimer = setTimeout(searchCatalog, 450);
}
function pickerCancelButton() { return $($('gameDialog').open ? 'cancelDialogPick' : 'cancelPick'); }
async function refreshSetup() {
  const info = await api('/api/hub/setup'), p = info.preferences;
  setupPending = setupReview || !p.setupCompleted; $('setupPanel').hidden = !setupPending;
  controllerKind = info.controller.kind; rememberReopen(info.reopenAvailable);
  $('samsungHint').hidden = setupPending || !info.samsungHint;
  $('scan').hidden = setupPending;
  if (!setupPending) return;
  document.querySelectorAll('.view').forEach(n => n.hidden = true);
  $('pageTitle').textContent = 'Primeiro uso'; $('pageNote').textContent = 'Deixe o essencial pronto para jogar.';
  const chosen = setupInitialized ? $('setupDisplay').value : info.displayPath || '';
  const signature = JSON.stringify(info.screens);
  if (signature !== setupScreenSignature) {
    $('setupDisplay').replaceChildren(option('','Escolha a tela de jogo'),...info.screens.filter(s=>s.available).map((s,i)=>option(s.devicePath,(i+1)+' · '+s.name+' · '+s.width+' × '+s.height)));
    if (chosen && !info.screens.some(s=>s.available && s.devicePath===chosen)) $('setupDisplay').append(option(chosen,'Tela escolhida indisponível · reconecte ou escolha outra'));
    $('setupDisplay').value = chosen; setupScreenSignature = signature;
  }
  setupControllerDetected = info.controller.connected; controllerName = info.controller.connected ? info.controller.name : '';
  chip(info.controller);
  $('setupControllerNote').textContent = info.controller.kind === 'dualsense' && !info.controller.microphone ? info.controller.note : '';
  $('setupControllerChoice').hidden = info.controller.connected;
  if (!setupInitialized) {
    $('setupControllerSource').value = p.controllerSource;
    const controls = info.controllerOptions || p.controller || p.controls; setupMouseOptions = controls;
    showShortcut('setupLauncherShortcut',controls.launcher || controls.launcherBinding);
    $('setupAudio').replaceChildren(option('','Áudio da tela de jogo · automático'),option('keep','Manter a saída atual'),...info.audio.map(d=>option(d.id,d.name)));
    $('setupAudio').value = p.audioDeviceId || '';
    $('setupOnlyDisplay').checked = p.displayPath ? info.onlyGameDisplay : false;
    setupInitialized = true;
  }
  if (!p.controller && !editedBindings.has('setupLauncherShortcut')) showShortcut('setupLauncherShortcut',info.controllerOptions.launcher || info.controllerOptions.launcherBinding);
  if (!p.controller && info.controller.connected) setupMouseOptions = info.controllerOptions;
  renderSetupMouse();
}
let setupMouseOptions, chipState = '';
function chip(controller) {
  const state = controller.connected ? 'on:' + controller.name : 'off';
  if (state === chipState) return;
  const node = $('setupController'), arrived = chipState && controller.connected; chipState = state;
  node.textContent = controller.connected ? controller.name + ' conectado' : 'Conecte o controle por USB ou Bluetooth';
  node.classList.toggle('connected', controller.connected);
  if (arrived) { node.classList.remove('arrived'); void node.offsetWidth; node.classList.add('arrived'); }
  for (const prefix of bindings.keys()) updateShortcut(prefix);
}
const padButtons = [[64,'Select / View / Create / −'],[128,'Start / Menu / Options / +'],[256,'Clique do analógico esquerdo'],[512,'Clique do analógico direito'],[16,'Superior esquerdo · LB / L1 / L'],[32,'Superior direito · RB / R1 / R'],[1024,'Gatilho esquerdo'],[2048,'Gatilho direito'],[1,'Botão inferior · A / X / B'],[2,'Botão da direita · B / círculo / A'],[4,'Botão da esquerda · X / quadrado / Y'],[8,'Botão superior · Y / triângulo / X'],[4096,'Microfone · DualSense'],[8192,'Touchpad · DualSense']];
function option(value, text) { const node = el('option', '', text); node.value = value; return node; }
function buttonSelect(id, multiple = false) { const node = el('select'); node.id = id; node.multiple = multiple; if (multiple) node.size = 4; node.append(...padButtons.map(([v,t]) => option(v,t))); return node; }
function shortcutField(prefix, title, purpose) {
  const group = el('fieldset','shortcut-card'), legend = el('legend','',title); group.append(legend); group.dataset.purpose = purpose;
  const art = el('div','pad-stage'); art.id = prefix + 'Art'; group.append(art);
  const sentence = el('p','shortcut-sentence'); sentence.id = prefix + 'Sentence'; group.append(sentence);
  const current = el('p','shortcut-value'); current.id = prefix + 'Value'; group.append(current);
  const actions = el('div','actions'), record = button('Mudar botão', () => beginCapture(prefix)); record.id=prefix+'Record';record.classList.add('change-button');actions.append(record); group.append(actions);
  const hint = el('p','capture-hint'); hint.id = prefix + 'Hint'; hint.setAttribute('role','status'); group.append(hint);
  const tune = el('details','tune'), summary = el('summary','','Ajustar gesto'); tune.append(summary);
  const gesture = el('label','','Gesto'), select = el('select'); select.id = prefix + 'Gesture'; select.append(option('hold','Segurar'),option('double-tap','Duplo toque')); gesture.append(select); tune.append(gesture);
  const hold = el('label','','Tempo ao segurar'), time = el('select'); hold.id = prefix + 'HoldLabel'; time.id = prefix + 'Hold'; for (const ms of [500,1000,1500,2000,3000]) time.append(option(ms,(ms/1000).toLocaleString('pt-BR') + ' s')); hold.append(time); tune.append(hold);
  group.append(tune);
  select.onchange = () => { editedBindings.add(prefix); if(bindings.has(prefix))bindings.get(prefix).gesture=select.value; updateShortcut(prefix); };
  time.onchange = () => { editedBindings.add(prefix); if(bindings.has(prefix))bindings.get(prefix).holdMs=Number(time.value); updateShortcut(prefix); };
  return group;
}
// Família do desenho: controle detectado; senão, a escolha manual; senão, silhueta aguardando.
function artFamily() {
  if (controllerKind) return LGControllerArt.family(controllerKind, controllerName);
  const choice = setupPending ? $('setupControllerSource').value : $('controllerSource').value;
  return choice === 'auto' ? 'generic' : LGControllerArt.family(choice);
}
function gestureText(binding) {
  const names = buttonNames(binding.buttons) || 'o botão escolhido', seconds = (binding.holdMs/1000).toLocaleString('pt-BR');
  return binding.gesture === 'double-tap' ? ['Toque duas vezes em ', names] : ['Segure ', names, ' por ' + seconds + ' s'];
}
function sentenceInto(node, binding, purpose) {
  const parts = gestureText(binding), action = purpose === 'mouse' ? ' para ligar ou desligar o mouse.' : ' para abrir ou ocultar o launcher.';
  node.replaceChildren(parts[0], el('strong','',parts[1]), parts[2] || '', action);
}
$('shortcutFields').append(shortcutField('launcherShortcut','Abrir / ocultar launcher','launcher'),shortcutField('mouseShortcut','Ativar / desativar mouse','mouse'));
$('setupLauncherFields').append(shortcutField('setupLauncherShortcut','Abrir e ocultar o launcher','launcher'));
for (const id of ['leftClick','rightClick']) $(id).append(...padButtons.map(([v,t]) => option(v,t)));
function shortcutValue(prefix) { return { ...bindings.get(prefix), gesture: $(prefix+'Gesture').value, holdMs: Number($(prefix+'Hold').value) }; }
function buttonNames(value) {
  const names = controllerKind === 'dualsense' ? {64:'Create',128:'Options',256:'L3',512:'R3',16:'L1',32:'R1',1024:'L2',2048:'R2',1:'✕',2:'○',4:'□',8:'△',4096:'Microfone',8192:'Touchpad'} : controllerKind === 'switch-pro' ? {64:'−',128:'+',256:'L3',512:'R3',16:'L',32:'R',1024:'ZL',2048:'ZR',1:'B',2:'A',4:'Y',8:'X'} : {64:'View / Select',128:'Menu / Start',256:'L3',512:'R3',16:'LB',32:'RB',1024:'LT',2048:'RT',1:'A',2:'B',4:'X',8:'Y',4096:'Microfone',8192:'Touchpad'};
  return padButtons.filter(([v]) => (v & value) !== 0).map(([v]) => names[v] || (v === 1024 ? 'Gatilho esquerdo' : 'Gatilho direito')).join(' + ');
}
function updateShortcut(prefix) {
  const binding = bindings.get(prefix); if(!binding)return;
  $(prefix+'HoldLabel').hidden = $(prefix+'Gesture').value !== 'hold';
  const name=controllerName || (controllerKind==='dualsense'?'DualSense':controllerKind==='switch-pro'?'Nintendo':controllerKind==='xinput'?'Xbox / 8BitDo':'');
  const current = { ...binding, gesture: $(prefix+'Gesture').value, holdMs: Number($(prefix+'Hold').value) };
  $(prefix+'Value').textContent = name ? name + ' · ' + buttonNames(binding.buttons) : '';
  const family = artFamily(), card = $(prefix+'Art').closest('fieldset');
  sentenceInto($(prefix+'Sentence'), current, card.dataset.purpose);
  LGControllerArt.render($(prefix+'Art'), { family, buttons: current.buttons, gesture: current.gesture, holdMs: current.holdMs, waiting: family === 'generic', listening: capturePrefix === prefix });
  if (prefix === 'setupLauncherShortcut') renderSetupMouse();
}
// Cartão somente leitura do mouse no primeiro uso (padrão do controle detectado).
function renderSetupMouse() {
  const options = setupMouseOptions, binding = options && (options.mouse || options.mouseBinding), family = artFamily();
  $('setupMouseCard').hidden = !binding || family === 'generic';
  if ($('setupMouseCard').hidden) return;
  sentenceInto($('setupMouseSentence'), binding, 'mouse');
  LGControllerArt.render($('setupMouseArt'), { family, buttons: binding.buttons, gesture: binding.gesture, holdMs: binding.holdMs });
  const dualsense = family === 'dualsense';
  $('setupTouchDemo').hidden = !dualsense; if (dualsense) LGControllerArt.touchpadDemo($('setupTouchDemo'));
  $('setupMouseMoves').textContent = dualsense ? 'Deslize no touchpad para mover · pressione para clicar · pressione e deslize para arrastar.' : 'O analógico direito move o cursor; ' + buttonNames(options.rightClick ?? 32) + ' clica e ' + buttonNames(options.leftClick ?? 16) + ' faz o clique direito.';
}
function showShortcut(prefix, binding) { bindings.set(prefix,{...binding}); $(prefix+'Gesture').value = binding.gesture; $(prefix+'Hold').value = binding.holdMs; updateShortcut(prefix); }
async function beginCapture(prefix) {
  if(capturePrefix === prefix) { await api('/api/hub/controller-capture/cancel',{}); $(prefix+'Hint').textContent='';endCapture();return; }
  if(capturePrefix) await api('/api/hub/controller-capture/cancel',{});
  endCapture(); await api('/api/hub/controller-capture',{}); capturePrefix = prefix; $(prefix+'Record').textContent='Cancelar'; updateShortcut(prefix);
  document.querySelectorAll('#initialSetup button[type=submit],#pcSettings button[type=submit]').forEach(n=>n.disabled=true);
  async function poll() {
    try {
      const state = await api('/api/hub/controller-capture');
      if(capturePrefix !== prefix)return;
      if(state.phase === 'done') { const previous=shortcutValue(prefix);showShortcut(prefix,state.singlePress ? {...previous,buttons:state.binding.buttons} : state.binding); editedBindings.add(prefix); endCapture(); $(prefix+'Hint').textContent = 'Pronto! ' + buttonNames(state.binding.buttons) + ' escolhido.'; updateShortcut(prefix); $(prefix+'Art').classList.remove('picked'); void $(prefix+'Art').offsetWidth; $(prefix+'Art').classList.add('picked'); return; }
      if(state.phase === 'timeout' || state.phase === 'idle') { endCapture(); $(prefix+'Hint').textContent='Não percebemos nenhum botão. Tente de novo quando quiser.'; updateShortcut(prefix); return; }
      $(prefix+'Hint').textContent = state.phase === 'release' ? 'Solte os botões do controle…' : 'Aperte no controle o botão que você quer usar';
      captureTimer=setTimeout(poll,120);
    } catch(error) { endCapture(); $(prefix+'Hint').textContent=error.message; }
  }
  await poll();
}
function endCapture() {
  clearTimeout(captureTimer);capturePrefix=null;
  for(const prefix of ['setupLauncherShortcut','launcherShortcut','mouseShortcut']){$(prefix+'Record').textContent='Mudar botão';if(bindings.has(prefix))updateShortcut(prefix);}
  document.querySelectorAll('#initialSetup button[type=submit],#pcSettings button[type=submit]').forEach(n=>n.disabled=false);
}
function pointerLabels() { $('mouseSpeedValue').textContent = $('mouseSpeed').value; $('mouseDeadzoneValue').textContent = $('mouseDeadzone').value + '%'; $('mouseCurveValue').textContent = $('mouseCurve').value; }
for (const id of ['mouseSpeed','mouseDeadzone','mouseCurve']) $(id).oninput = pointerLabels;
const titles = { library: ['Biblioteca', 'Confira os jogos e gerencie o que aparece no launcher.'], folders: ['Pastas e busca', 'Defina onde encontrar jogos e quando atualizar a biblioteca.'], add: ['Adicionar jogo', 'Traga uma instalação local para o seu launcher.'], system: ['Configurações', 'Ajuste as opções do LazyGaming neste PC.'] };
function notice(text, error = false) { LGNotices.show($('notice'), text); $('notice').hidden = !text; $('notice').classList.toggle('error', error); LGNotices.show($('dialogNotice'), $('gameDialog').open ? text : ''); }
async function api(path, body, options = {}) {
  const controller = new AbortController();
  const cancel = () => controller.abort();
  const timeout = setTimeout(cancel, path === '/api/hub/pick' ? 600000 : 30000);
  options.signal?.addEventListener('abort', cancel, { once: true });
  if (options.signal?.aborted) cancel();
  let response;
  try {
  try { response = await fetch(path, { method: body === undefined ? 'GET' : 'POST', headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body), signal: controller.signal }); }
  catch (error) {
    if (controller.signal.aborted) {
      if (options.signal?.aborted) throw error;
      throw Error('O LazyGaming demorou para responder. Tente novamente.');
    }
    showOffline(false); throw Error('Abra o LazyGaming para continuar.');
  }
  if(response.status === 401) { showOffline(true); throw Error('Reabra a Central para continuar.'); }
  let result;
  try { result = await response.json(); }
  catch (error) {
    if (options.signal?.aborted) throw error;
    throw Error(controller.signal.aborted ? 'O LazyGaming demorou para responder. Tente novamente.' : 'O LazyGaming enviou uma resposta incompleta. Tente novamente.');
  }
  if (!response.ok) throw Object.assign(Error(result.error || 'Não foi possível concluir a operação.'), { status: response.status }); return result;
  } finally { clearTimeout(timeout); options.signal?.removeEventListener('abort', cancel); }
}
async function pick(kind) {
  if (pickerRequest) { pickerCancelButton().focus(); return null; }
  if (working) { notice('Aguarde a operação em andamento antes de escolher um arquivo.'); return null; }
  const request = new AbortController(); pickerRequest = request;
  const status = $($('gameDialog').open ? 'dialogPickerStatus' : 'pickerStatus');
  status.hidden = false;
  const text = kind === 'folder' ? 'Escolha uma pasta na janela do Windows.' : 'Escolha um arquivo na janela do Windows.';
  $('pickerNote').textContent = text; $('dialogPickerNote').textContent = text;
  try { return (await api('/api/hub/pick', { kind }, { signal: request.signal })).path; }
  catch (error) {
    if (request.signal.aborted) notice('Seleção cancelada.');
    else { if (typeof LGDiagnostics !== 'undefined') LGDiagnostics.report('hub-picker-failed', error); if (!offline) notice(error.message, true); }
    return null;
  } finally { pickerRequest = null; status.hidden = true; }
}
$('cancelPick').onclick = () => pickerRequest?.abort();
$('cancelDialogPick').onclick = () => pickerRequest?.abort();
$('gameDialog').addEventListener('close', () => pickerRequest?.abort());
window.addEventListener('pagehide', () => { pickerRequest?.abort(); cancelCatalog(); cancelWallpaperSearch(); });
function rememberReopen(available) { if(available)sessionStorage.setItem('lg-reopen','yes'); $('reopenClient').hidden = sessionStorage.getItem('lg-reopen') !== 'yes'; }
function showOffline(expired) {
  offline=true; endCapture(); $('offlinePanel').hidden=false; $('notice').hidden=true;
  $('offlineTitle').textContent=expired ? 'Reabra a Central' : 'LazyGaming fechado';
  $('offlineText').textContent=expired ? 'A conexão desta janela expirou.' : 'Abra o aplicativo para continuar.';
  $('connection').textContent='Fechado';document.body.classList.add('offline');
  $('content').inert=true;document.querySelector('nav').inert=true;$('scan').disabled=true;
  rememberReopen(false);
  window.scrollTo({top:0,left:0,behavior:'instant'});
  $('offlinePanel').scrollIntoView({block:'start',behavior:'instant'});
  $('offlineTitle').focus({preventScroll:true});
}
function controllerStatus(info) {
  const c=info.controller;controllerKind=c.kind;controllerName=c.connected?c.name:'';
  const text=c.connected ? c.name+' detectado' : 'Conecte seu controle';
  $('detectedController').textContent=text;
  $('controllerCapability').textContent=c.kind==='dualsense' && !c.microphone ? c.note : '';
  $('legacyMicLabel').hidden=c.kind!=='dualsense' && $('controllerSource').value!=='dualsense';$('bluetoothFeaturesLabel').hidden=$('legacyMicLabel').hidden;
  chip(c);
  if(setupPending){setupControllerDetected=c.connected;$('setupControllerChoice').hidden=c.connected;}
  if(!swapEdited && !controllerSourceChanged)$('swapButtons').checked=info.controllerOptions.swapConfirmBack;
  for(const prefix of bindings.keys())updateShortcut(prefix);
  if(!setupPending && pcPreferences && !capturePrefix) {
    for(const [prefix,key] of [['launcherShortcut','launcher'],['mouseShortcut','mouse']])
      if(!editedBindings.has(prefix))showShortcut(prefix,info.controllerOptions[key] || info.controllerOptions[key+'Binding']);
  }
  for(const dt of $('systemStatus').querySelectorAll('dt'))if(dt.textContent==='Controle')dt.nextElementSibling.textContent=text;
}
async function refreshController() {
  if(offline || controllerRefresh || document.visibilityState!=='visible')return;
  controllerRefresh=true;
  try{controllerStatus(await api('/api/hub/controller'));}catch(_){}finally{controllerRefresh=false;}
}
async function run(action, message) {
  if (working) { notice('Uma operação está em andamento. Aguarde a conclusão.'); return; }
  if (pickerRequest) { notice('Conclua ou cancele a seleção de arquivo antes de continuar.'); pickerCancelButton().focus(); return; }
  working = true; $('scan').disabled = true;
  try { await action(); if (message) notice(message); }
  catch (error) { if (typeof LGDiagnostics !== 'undefined') LGDiagnostics.report('hub-action-failed', error); if(!offline)notice(error.message, true); }
  finally { working = false; $('scan').disabled = offline; }
}
function el(tag, className, text) { const node = document.createElement(tag); if (className) node.className = className; if (text !== undefined) node.textContent = text; return node; }
function button(text, action) { const node = el('button', '', text); node.type = 'button'; node.onclick = () => run(action); return node; }
function time(value) { return value ? new Date(value).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : 'Ainda não realizada'; }
function rows() {
  const rows = [...snapshot.games];
  for (const registration of snapshot.registrations) if (!rows.some(g => g.id === registration.id)) rows.push({ ...registration, origin: 'Cadastro local', enabled: !(snapshot.preferences.excluded || []).includes(registration.id) });
  return rows;
}
function render() {
  const games = rows(); $('total').textContent = games.filter(g => g.enabled).length; $('navCount').textContent = games.length;
  $('available').textContent = games.filter(g => g.enabled && g.available).length; $('attention').textContent = games.filter(g => !g.available).length; $('lastScan').textContent = time(snapshot.lastScan);
  $('steam').checked = snapshot.preferences.steam; $('automatic').checked = snapshot.preferences.automatic; $('interval').value = snapshot.preferences.intervalMinutes;
  $('nextScan').textContent = snapshot.preferences.automatic ? 'Próxima busca: ' + (snapshot.nextScan ? time(snapshot.nextScan) : 'agendada pelo auxiliar') : 'Busca automática pausada.';
  $('folderList').replaceChildren(...snapshot.folders.map(folder => {
    const row = el('div', 'folder-row'), info = el('div'); info.append(el('div', 'folder-path', folder.path), el('span', 'badge ' + (folder.available ? '' : 'warning'), folder.available ? 'Disponível' : 'Pasta indisponível'));
    const label = el('label', 'toggle-line'), toggle = el('input'); toggle.type = 'checkbox'; toggle.checked = folder.enabled; toggle.setAttribute('aria-label', 'Buscar em ' + folder.path);
    toggle.onchange = () => run(async () => { await saveSettings({ folders: snapshot.preferences.folders.map(f => f.path === folder.path ? { ...f, enabled: toggle.checked } : f) }); }, 'Pasta atualizada.').finally(() => render()); label.append(toggle);
    row.append(info, label, button('Remover', async () => { await saveSettings({ folders: snapshot.preferences.folders.filter(f => f.path !== folder.path) }); notice('Pasta removida da busca. Os arquivos foram preservados.'); })); return row;
  }));
  $('scanNotes').replaceChildren(...(snapshot.notes.length ? snapshot.notes : ['Nenhum aviso na última busca.']).map(text => el('li', '', text)));
  renderGames();
}
function renderGames() {
  if (!snapshot) return;
  const query = $('search').value.trim().toLocaleLowerCase('pt-BR'), filter = $('filter').value;
  const games = rows().filter(g => g.name.toLocaleLowerCase('pt-BR').includes(query) && (filter !== 'attention' || !g.available) && (filter !== 'local' || g.origin !== 'Steam') && (filter !== 'hidden' || !g.enabled));
  $('empty').hidden = games.length > 0;
  $('gameList').replaceChildren(...games.map(game => {
    const row = el('article', 'game-row');
    const cover = game.cover ? el('img') : el('div', 'cover-placeholder', game.name.charAt(0)); if (game.cover) { cover.src = game.cover; cover.alt = ''; cover.loading = 'lazy'; }
    const title = el('div'); title.append(el('div', 'game-name', game.name), el('div', 'game-source', game.origin));
    const status = el('span', 'badge ' + (!game.available ? 'warning' : !game.enabled ? 'off' : ''), !game.available ? 'Indisponível' : !game.enabled ? 'Oculto' : 'Disponível');
    row.append(cover, title, status, el('span', 'game-source', game.origin), button('Gerenciar', async () => openGame(game.id))); return row;
  }));
}
function paths(target, values) { target.replaceChildren(); for (const [label, value] of values) if (value) target.append(el('dt', '', label), el('dd', '', value)); }
function openGame(id) {
  selected = rows().find(g => g.id === id); if (!selected) return;
  const registration = snapshot.registrations.find(r => r.id === id);
  $('detailOrigin').textContent = selected.origin; $('detailTitle').textContent = selected.name; $('detailName').value = selected.name;
  $('detailStatus').textContent = selected.available ? (selected.enabled ? 'Disponível na biblioteca.' : 'Oculto no launcher.') : registration?.reason || 'Instalação indisponível. Confira a pasta e o executável.';
  $('detailDescription').textContent = selected.metadata?.description || '';
  $('detailBackdrop').hidden = !selected.backdrop; if (selected.backdrop) $('detailBackdrop').src = selected.backdrop;
  $('detailWallpaperNote').textContent = selected.wallpaper ? wallpaperCaption(selected.wallpaper) : '';
  $('resetGameBackdrop').hidden = !selected.wallpaper;
  paths($('detailPaths'), [['Pasta', selected.folder], ['Executável', selected.executable], ['Conteúdo', selected.contentPath]]);
  $('rename').disabled = !snapshot.games.some(g => g.id === id); $('toggleGame').disabled = $('rename').disabled;
  $('toggleGame').textContent = selected.enabled ? 'Ocultar no launcher' : 'Mostrar no launcher';
  $('repairFields').hidden = !registration; $('repairExecutable').value = selected.executable || ''; $('repairContent').value = selected.contentPath || '';
  $('repairContentLabel').hidden = !selected.contentPath && !/eglantine\.exe$/i.test(selected.executable || '');
  $('dialogNotice').textContent = ''; if (!$('gameDialog').open) $('gameDialog').showModal();
}
async function reload() {
  snapshot = await api('/api/hub'); $('content').hidden = false; $('locked').hidden = true; $('connection').textContent = 'Pronto'; render();
}
async function saveSettings(change) { snapshot = await api('/api/hub/settings', { ...snapshot.preferences, ...change }); render(); }
async function addFolder(path) {
  if (!path) return; await saveSettings({ folders: [...snapshot.preferences.folders, { path, enabled: true }] }); $('folderPath').value = ''; notice('Pasta adicionada e biblioteca atualizada.');
}
async function system() {
  const [state, pc] = await Promise.all([api('/api/status'), api('/api/hub/pc-settings')]);
  const audio = pc.audio; pcPreferences = pc.preferences;
  const p = pcPreferences, c = pc.controllerOptions || p.controller || p.controls; controllerKind=pc.controller.kind; rememberReopen(pc.reopenAvailable);
  controllerStatus(pc);swapEdited=false;controllerSourceChanged=false;
  $('legacyMicLabel').hidden = pc.controller.kind !== 'dualsense' && p.controllerSource !== 'dualsense';
  $('bluetoothFeaturesLabel').hidden=$('legacyMicLabel').hidden;$('bluetoothFeatures').checked=p.dualSenseBluetoothFeatures!==false;
  $('tvIntegration').checked = p.tvIntegrationEnabled;
  $('tvCode').disabled = !pc.tvSetup.lanEnabled;
  $('tvIntegrationNote').textContent = (pc.tvSetup.lanEnabled ? 'Acesso pela rede local ativo. ' : 'Acesso à TV desativado neste momento. ') +
    (p.tvIntegrationEnabled !== pc.tvSetup.lanEnabled ? 'Salve e reinicie o auxiliar para aplicar. ' : '') +
    (pc.tvSetup.commandLineLan ? 'O atalho atual também ativa o acesso pela rede. ' : '') +
    (pc.tvSetup.paired ? 'Já existe um vínculo com TV; preserve-o para reconectar. ' : 'Nenhuma TV pareada. ') + pc.tvSetup.addresses.join(' · ');
  $('gameDisplay').replaceChildren(option('','Automático · tela única ou único HDMI'),...pc.screens.filter(s => s.available).map((s,i) => option(s.devicePath, (i+1) + ' · ' + s.name + ' · ' + s.width + ' × ' + s.height + ' · ' + Math.round(s.hz) + ' Hz')));
  if (p.displayPath && !pc.screens.some(s => s.available && s.devicePath === p.displayPath)) $('gameDisplay').append(option(p.displayPath,'Tela salva indisponível · reconecte ou escolha outra'));
  $('gameDisplay').value = p.displayPath || ''; $('restoreTv').checked = p.restoreTvState;
  $('sessionAudio').replaceChildren(option('','Automático · áudio da tela de jogo'),option('keep','Manter saída atual do Windows'),...audio.map(d => option(d.id,d.name)));
  if (p.audioDeviceId && p.audioDeviceId !== 'keep' && !audio.some(d => d.id === p.audioDeviceId)) $('sessionAudio').append(option(p.audioDeviceId,'Saída salva indisponível'));
  $('sessionAudio').value = p.audioDeviceId || '';
  $('controllerSource').value = p.controllerSource; $('swapButtons').checked = c.swapConfirmBack; $('legacyMic').checked = c.legacyMicrophone;
  showShortcut('launcherShortcut',c.launcher || c.launcherBinding); showShortcut('mouseShortcut',c.mouse || c.mouseBinding);
  $('mouseSpeed').value = c.sensitivity; $('mouseDeadzone').value = Math.round(c.deadzone*100); $('mouseCurve').value = c.curve;
  $('leftClick').value = c.leftClick; $('rightClick').value = c.rightClick; pointerLabels();
  $('monitors').checked = state.displays.monitorsEnabled;
  $('displayNote').textContent = state.displays.note || '';
  $('audio').replaceChildren(...audio.map(device => button(device.name + (device.current ? ' · Em uso' : ''), async () => { await api('/api/audio', { id: device.id }); await system(); notice('Saída de áudio atualizada.'); })));
  if (!audio.length) $('audio').append(el('p', 'muted', 'Nenhuma saída disponível nesta sessão.'));
  const controller = state.controller || {};
  $('activity').checked = state.enabled;
  paths($('systemStatus'), [['Aplicativo', state.enabled ? 'Ativo neste PC' : 'Pausado'], ['Versão', state.version], ['Biblioteca', state.state.games.length + ' jogos'], ['Controle', controller.connected ? 'Conectado' : 'Confira a conexão USB ou Bluetooth'], ['Busca automática', snapshot.preferences.automatic && state.enabled ? 'Ativada' : 'Pausada']]);
}
document.querySelectorAll('[data-view]').forEach(node => node.onclick = () => {
  if (setupPending) { $('setupPanel').scrollIntoView({ behavior: 'smooth' }); return; }
  view = node.dataset.view; document.querySelectorAll('[data-view]').forEach(n => n.toggleAttribute('aria-current', false)); node.setAttribute('aria-current', 'page');
  document.querySelectorAll('.view').forEach(n => n.hidden = n.id !== 'view-' + view);
  $('pageTitle').textContent = titles[view][0]; $('pageNote').textContent = titles[view][1]; if (view === 'system') run(system);
});
$('controllerSource').onchange=()=>{controllerSourceChanged=true;swapEdited=false;$('swapButtons').checked=$('controllerSource').value==='switch-pro' || $('controllerSource').value==='auto' && controllerKind==='switch-pro';};
$('swapButtons').onchange=()=>{swapEdited=true;};
$('search').oninput = renderGames; $('filter').onchange = renderGames;
$('scan').onclick = () => run(async () => { notice('Buscando jogos instalados…'); snapshot = await api('/api/hub/scan', {}); render(); notice('Busca concluída. ' + snapshot.games.filter(g => g.enabled).length + ' jogos na biblioteca.'); });
$('steam').onchange = () => run(() => saveSettings({ steam: $('steam').checked }), 'Busca Steam atualizada.').finally(render);
$('automatic').onchange = () => run(() => saveSettings({ automatic: $('automatic').checked }), 'Busca automática atualizada.').finally(render);
$('interval').onchange = () => run(() => saveSettings({ intervalMinutes: Number($('interval').value) }), 'Intervalo atualizado.').finally(render);
$('addFolder').onclick = async () => { const path = await pick('folder'); if (path) await run(() => addFolder(path)); };
$('saveFolder').onclick = () => run(() => addFolder($('folderPath').value.trim()));
document.querySelectorAll('[data-pick]').forEach(node => node.onclick = async () => { const path = await pick(node.dataset.pick); if (path) { const target = $(node.dataset.target); target.value = path; target.dispatchEvent(new Event('input', { bubbles: true })); } });
$('addGame').onsubmit = event => { event.preventDefault(); run(async () => {
  if (catalogLoading) { notice('Aguarde a ficha carregar ou escolha usar o cadastro manual.'); return; }
  const year = $('addYear').value.trim();
  snapshot = await api('/api/hub/import', { title: $('addTitle').value.trim(), executable: $('addExecutable').value.trim(), contentPath: $('addContent').value.trim(), cover: $('addCover').value.trim(), backdrop: $('addBackdrop').value.trim(),
    catalogId: catalogChoice?.id || null, wallpaperId: addWallpaper?.id || null, description: $('addDescription').value.trim(), year: year ? Number(year) : null, platforms: $('addPlatforms').value.trim(), developers: $('addDevelopers').value.trim(), publishers: $('addPublishers').value.trim(), genres: $('addGenres').value.trim() });
  render(); $('addGame').reset(); clearCatalog(true); $('catalogStatus').textContent = 'Digite o nome para buscar capas e informações.'; notice(snapshot.notice?.startsWith('Jogo adicionado.') ? snapshot.notice : 'Jogo adicionado à biblioteca.');
}); };
$('addTitle').oninput = catalogTitleChanged;
$('addTitle').onkeydown = event => { if (event.key === 'Enter' && !catalogChoice) { event.preventDefault(); searchCatalog(); } };
$('catalogSearch').onclick = () => { clearCatalog(true); return searchCatalog(); };
$('catalogClear').onclick = () => { clearCatalog(true); $('catalogStatus').textContent = 'Cadastro manual. Preencha as informações e escolha imagens, se quiser.'; };
$('findAddBackdrop').onclick = () => openWallpapers();
$('findGameBackdrop').onclick = () => openWallpapers(selected);
$('clearAddWallpaper').onclick = resetAddWallpaper;
$('addBackdrop').addEventListener('input', resetAddWallpaper);
$('wallpaperSearchForm').onsubmit = event => { event.preventDefault(); return searchWallpapers(); };
$('wallpaperMore').onclick = () => searchWallpapers(true);
$('wallpaperUse').onclick = useWallpaper;
$('wallpaperDialog').addEventListener('close', cancelWallpaperSearch);
$('resetGameBackdrop').onclick = () => run(async () => { const id = selected.id; snapshot = await api('/api/hub/games/' + encodeURIComponent(id) + '/wallpaper', {wallpaperId:null}); render(); openGame(id); notice('Fundo automático restaurado.'); });
$('importManifest').onclick = async () => { const path = await pick('manifest'); if (path) await run(async () => { snapshot = await api('/api/hub/import', { manifest: path }); render(); notice('Ficha importada.'); }); };
$('rename').onclick = () => run(async () => { await api('/api/games/' + selected.id + '/settings', { name: $('detailName').value.trim() }); await reload(); openGame(selected.id); notice('Nome salvo.'); });
$('toggleGame').onclick = () => run(async () => { snapshot = await api('/api/hub/games/' + selected.id + '/enabled', { enabled: !selected.enabled }); render(); openGame(selected.id); notice('Biblioteca atualizada.'); });
$('checkGame').onclick = () => run(async () => { const id = selected.id; snapshot = await api('/api/hub/scan', {}); render(); openGame(id); notice(selected.available ? 'Executável encontrado. Nenhum jogo foi aberto.' : 'Instalação indisponível. Confira o caminho abaixo.'); });
$('repair').onclick = () => run(async () => { const id = selected.id; snapshot = await api('/api/hub/repair', { id, executable: $('repairExecutable').value.trim(), contentPath: $('repairContent').value.trim() }); render(); openGame(id); notice('Instalação atualizada; nome e ficha preservados.'); });
$('monitors').onchange = () => run(async () => { await api('/api/displays', { monitorsEnabled: $('monitors').checked }); await system(); }, 'Preferência de monitores salva.');
$('activity').onchange = () => run(async () => { await api('/api/hub/activity', { enabled: $('activity').checked }); await system(); }, 'Funcionamento atualizado.');
$('pcSettings').onsubmit = event => { event.preventDefault(); run(async () => {
  await api('/api/hub/pc-settings', { ...pcPreferences, tvIntegrationEnabled: $('tvIntegration').checked, displayPath: $('gameDisplay').value || null, restoreTvState: $('restoreTv').checked, audioDeviceId: $('sessionAudio').value || null, controllerSource: $('controllerSource').value,
    confirmBackOverride:swapEdited ? $('swapButtons').checked : controllerSourceChanged ? null : pcPreferences.confirmBackOverride,dualSenseBluetoothFeatures:$('bluetoothFeatures').checked,
    controller: { launcher: editedBindings.has('launcherShortcut') ? shortcutValue('launcherShortcut') : pcPreferences.controller?.launcher ?? null, mouse: editedBindings.has('mouseShortcut') ? shortcutValue('mouseShortcut') : pcPreferences.controller?.mouse ?? null, sensitivity: Number($('mouseSpeed').value), deadzone: Number($('mouseDeadzone').value)/100, curve: Number($('mouseCurve').value), leftClick: Number($('leftClick').value), rightClick: Number($('rightClick').value), swapConfirmBack: $('swapButtons').checked, legacyMicrophone: $('legacyMic').checked } });
  await system(); notice('Configuração do PC salva.');
}); };
async function start() {
  try {
    const token = location.hash.slice(1); history.replaceState(null, '', location.pathname);
    if (token) {
      let response;try { response = await fetch('/hub-session', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token }) }); }catch(_){showOffline(false);return;}
      if (!response.ok) {showOffline(true);return;}
    }
    await refreshSetup(); await reload(); offline=false; document.body.classList.remove('offline'); $('offlinePanel').hidden=true;$('content').inert=false;document.querySelector('nav').inert=false;$('scan').disabled=false;
  } catch (error) { if(!offline){$('locked').hidden = false; $('content').hidden = true; $('connection').textContent = 'Aguardando acesso'; notice(error.message, true);} }
}
$('retry').onclick = start;
let finishing = false;
$('setupControllerSource').addEventListener('change', () => { for (const prefix of bindings.keys()) updateShortcut(prefix); renderSetupMouse(); });
$('initialSetup').onsubmit = event => {
  event.preventDefault(); if (finishing) return;
  $('setupError').textContent = '';
  if (!$('setupDisplay').value) { $('setupError').textContent = 'Escolha a tela em que você vai jogar.'; $('setupDisplay').focus(); return; }
  if (capturePrefix) { $('setupError').textContent = 'Conclua ou cancele a escolha do botão.'; return; }
  finishing = true; const submit = $('setupSubmit'); submit.disabled = true; submit.classList.add('busy'); submit.firstChild.textContent = 'Salvando…';
  run(async () => {
    try {
      const launcher = shortcutValue('setupLauncherShortcut');
      await api('/api/hub/setup', { displayPath: $('setupDisplay').value, controllerSource: setupControllerDetected ? 'auto' : $('setupControllerSource').value, audioDeviceId: $('setupAudio').value || null, onlyGameDisplay: $('setupOnlyDisplay').checked, controller: { ...setupMouseOptions,launcher } });
      const done = $('setupDone'); sentenceInto($('setupDoneText'), launcher, 'launcher');
      $('initialSetup').classList.add('leaving'); setTimeout(() => { if (finishing) { $('initialSetup').hidden = true; done.hidden = false; } }, 280);
      await new Promise(resolve => setTimeout(resolve, matchMedia('(prefers-reduced-motion: reduce)').matches ? 300 : 1700));
      setupReview = false;
      // A configuração já foi salva: falhas ao atualizar a tela não a desfazem.
      try { await refreshSetup(); await reload(); } catch (_) { }
      done.hidden = true; $('initialSetup').hidden = false; $('initialSetup').classList.remove('leaving');
      document.querySelector('[data-view="library"]').click(); notice('Tudo pronto. Bons jogos!');
    } catch (error) { $('setupError').textContent = error.message; throw error; }
    finally { finishing = false; submit.disabled = false; submit.classList.remove('busy'); submit.firstChild.textContent = 'Concluir'; }
  });
};
$('samsungLearn').onclick = () => { document.querySelector('[data-view="system"]').click(); setTimeout(() => $('tvIntegration').scrollIntoView({behavior:'smooth',block:'center'}),100); };
$('samsungDismiss').onclick = () => run(async () => { await api('/api/hub/samsung-hint/dismiss',{}); $('samsungHint').hidden = true; });
$('tvCode').onclick = () => run(async () => {
  const pc = await api('/api/hub/pc-settings');
  if (pc.tvSetup.paired && !confirm('Parear uma nova TV substitui o vínculo atual. Para a mesma TV, prefira Recuperar conexão no app. Gerar código?')) return;
  const result = await api('/api/local-pairing', {}); $('tvPairCode').textContent = 'Código: ' + result.code + ' · válido por 5 minutos';
  setTimeout(() => { $('tvPairCode').textContent = ''; }, result.expiresIn * 1000);
});
$('quitHelper').onclick = () => run(async () => {
  await api('/api/hub/quit', {}); showOffline(false);
});
$('reviewSetup').onclick = () => run(async () => { setupReview=true;setupInitialized=false;await refreshSetup(); });
start();
setInterval(() => { if (!offline && !working && snapshot && !$('gameDialog').open && document.visibilityState === 'visible') reload().catch(error => { if(!offline)notice(error.message, true); }); }, 15000);
setInterval(() => { if (!offline && setupPending && !working && !finishing && document.visibilityState === 'visible') refreshSetup().catch(error => { if(!offline)$('setupError').textContent = error.message; }); },2000);
setInterval(refreshController,750);
