window.ironmonToggleSpeed = {
 attach(root, initial, bridge, labels) {
  const editor=root.querySelector('.sl-editor');
  const abort=new AbortController();
  const copy=value=>JSON.parse(JSON.stringify(value));
  const label=lane=>lane==='battle'?labels.battle:labels.overworld;
  let sequence=0,touch=null,suppressClick=false,wireFrame,scrollFrame;
  const freshId=()=>`speed-${Date.now().toString(36)}-${sequence++}`;
  const state={config:copy(initial),saved:copy(initial),history:[copy(initial)],cursor:0,armed:null,current:initial.lanes.overworld[0].nodes[0].id,memory:Object.create(null),error:''};

  function locate(config,id) {
    for(const lane of ['overworld','battle']) for(let gi=0;gi<config.lanes[lane].length;gi++) {
      const group=config.lanes[lane][gi],ni=group.nodes.findIndex(node=>node.id===id);
      if(ni>=0) return {lane,group,gi,ni,node:group.nodes[ni]};
    }
    return null;
  }
  function flattened(config,lane) {return config.lanes[lane].flatMap(group=>group.nodes);}
  function locateGroup(config,id) {
    for(const lane of ['overworld','battle']) {
      const gi=config.lanes[lane].findIndex(group=>group.id===id);
      if(gi>=0) return {lane,gi,group:config.lanes[lane][gi]};
    }
    return null;
  }
  function connection(config,groupId) {
    return config.links.find(link=>['overworld','battle'].some(lane=>locate(config,link[lane])?.group.id===groupId));
  }
  function destination(config,memory,currentId) {
    const current=locate(config,currentId),other=current.lane==='overworld'?'battle':'overworld';
    if(current.node.value===1) return {id:config.lanes[other][0].nodes[0].id,reason:'Normal speed'};
    const link=connection(config,current.group.id);
    if(!link) return {id:null,reason:'Connect this group to preview its transition.'};
    const anchor=locate(config,link[other]),remembered=memory[anchor.group.id];
    if(anchor.group.nodes.some(node=>node.id===remembered)) return {id:remembered,reason:'Remembered speed'};
    return {id:anchor.node.id,reason:'Connected entry'};
  }
  function remember() {
    const current=locate(state.config,state.current);
    if(current && current.node.value!==1) state.memory[current.group.id]=current.node.id;
  }
  function normalize(config) {
    for(const lane of ['overworld','battle']) config.lanes[lane]=config.lanes[lane].filter(group=>group.nodes.length);
    const used=new Set(),kept=[];
    for(const link of [...config.links].reverse()) {
      const a=locate(config,link.overworld),b=locate(config,link.battle);
      if(!a||!b||a.lane!=='overworld'||b.lane!=='battle'||a.node.value===1||b.node.value===1||used.has(a.group.id)||used.has(b.group.id)) continue;
      used.add(a.group.id);used.add(b.group.id);kept.unshift(link);
    }
    config.links=kept;
    return config;
  }
  function valid(config) {
    if(!config?.lanes||!Array.isArray(config.links)) return false;
    const ids=new Set(),groups=new Set();
    for(const lane of ['overworld','battle']) {
      if(!Array.isArray(config.lanes[lane])||!config.lanes[lane].length||flattened(config,lane).length>128) return false;
      for(const [index,group] of config.lanes[lane].entries()) {
        if(typeof group.id!=='string'||!/^[a-zA-Z0-9_-]+$/.test(group.id)||group.id.length>64||groups.has(group.id)||!Array.isArray(group.nodes)||!group.nodes.length) return false;
        groups.add(group.id);
        for(const node of group.nodes) {
          if(typeof node.id!=='string'||!/^[a-zA-Z0-9_-]+$/.test(node.id)||node.id.length>64||ids.has(node.id)||!Number.isInteger(node.value)||node.value<1||node.value>10) return false;
          if((index===0)!==(node.value===1)) return false;
          ids.add(node.id);
        }
        if(index===0&&group.nodes.length!==1) return false;
      }
    }
    const wires=new Set();
    return config.links.length<128&&config.links.every(link=>typeof link.id==='string'&&link.id.length<=64&&/^[a-zA-Z0-9_-]+$/.test(link.id)&&!wires.has(link.id)&&wires.add(link.id))&&JSON.stringify(normalize(copy(config)).links)===JSON.stringify(config.links);
  }
  function missingConnections(config) {
    return ['overworld','battle'].flatMap(lane=>config.lanes[lane].slice(1).filter(group=>!connection(config,group.id)).map(group=>({lane,group})));
  }
  function dirty() {return JSON.stringify(state.config)!==JSON.stringify(state.saved);}
  function recordChange() {
    if(JSON.stringify(state.history[state.cursor])===JSON.stringify(state.config)) return;
    state.history=state.history.slice(0,state.cursor+1);state.history.push(copy(state.config));
    if(state.history.length>60) state.history.shift();
    state.cursor=state.history.length-1;
  }
  function changed(preservePreview=false) {
    normalize(state.config);
    if(!preservePreview) {state.current=state.config.lanes.overworld[0].nodes[0].id;state.memory=Object.create(null);}
    recordChange();state.armed=null;state.error='';
    render();
  }
  function travelHistory(direction) {
    const index=state.cursor+direction;
    if(index<0||index>=state.history.length) return;
    state.cursor=index;state.config=copy(state.history[index]);state.current=state.config.lanes.overworld[0].nodes[0].id;
    state.memory=Object.create(null);state.armed=null;state.error='';render();
  }
  let saving=false;
  async function save() {
    if(saving) return;
    if(!valid(state.config)||missingConnections(state.config).length) {
      state.error=labels.incomplete;render();return false;
    }
    saving=true;const profile=copy(state.config);render();
    try {
      const error=await bridge.invokeMethodAsync('SaveProfileAsync',profile);
      if(error) state.error=error;
      else {state.saved=profile;state.error='';}
    } catch {state.error=labels.saveFailed;}
    finally {saving=false;render();}
    return !state.error;
  }
  function detach(id) {
    const location=locate(state.config,id);
    if(!location||location.node.value===1) return null;
    location.group.nodes.splice(location.ni,1);
    return location.node;
  }
  function applyDrop(payload,target) {
    if(!payload||!target) return;
    if(payload.group) {
      const source=locateGroup(state.config,payload.group);
      if(!source||source.gi===0) return;
      if(target.kind==='remove') {
        state.config.lanes[source.lane].splice(source.gi,1);changed();return;
      }
      const at=target.node?locate(state.config,target.node):null;
      const lane=at?.lane||target.lane,before=at?.group.id||target.before;
      if(lane!==source.lane) {render();return;}
      if(before===source.group.id) return;
      const groups=state.config.lanes[lane];groups.splice(source.gi,1);
      const index=before?groups.findIndex(group=>group.id===before):groups.length;
      groups.splice(index<0?groups.length:Math.max(1,index),0,source.group);
      changed(true);return;
    }
    if(payload.value&&target.lane&&flattened(state.config,target.lane).length>=128) {state.error=labels.tooMany;render();return;}
    const source=payload.node?locate(state.config,payload.node):null;
    if(source?.node.value===1) return;
    if(target.kind==='remove') {
      if(payload.node) {detach(payload.node);changed();}
      return;
    }
    if((target.kind==='merge'||target.kind==='inside')&&target.node===payload.node) return;
    if(source&&target.kind==='gap'&&source.group.id===target.before&&source.group.nodes.length===1) return;
    const targetLocation=target.node?locate(state.config,target.node):null;
    if(payload.value&&targetLocation&&flattened(state.config,targetLocation.lane).length>=128) {state.error=labels.tooMany;render();return;}
    if(targetLocation?.node.value===1) return;
    const destinationLane=targetLocation?.lane||target.lane;
    if(source&&source.lane!==destinationLane&&flattened(state.config,destinationLane).length>=128) {state.error=labels.tooMany;render();return;}
    const node=payload.node?detach(payload.node):{id:freshId(),value:payload.value};
    if(!node) return;
    if(target.kind==='merge') {
      const into=locate(state.config,target.node);
      into.group.nodes.splice(into.ni+1,0,node);
      const keep=connection(state.config,into.group.id);
      if(keep) state.config.links=[...state.config.links.filter(link=>link.id!==keep.id),keep];
      changed();
    } else if(target.kind==='inside') {
      const into=locate(state.config,target.node);
      into.group.nodes.splice(into.ni,0,node);changed();
    } else {
      state.config.lanes[target.lane]=state.config.lanes[target.lane].filter(group=>group.nodes.length);
      const groups=state.config.lanes[target.lane];
      const index=target.before?groups.findIndex(group=>group.id===target.before):groups.length;
      groups.splice(index<0?groups.length:Math.max(1,index),0,{id:freshId(),nodes:[node]});
      changed();
    }
  }
  function connect(aId,bId) {
    const a=locate(state.config,aId),b=locate(state.config,bId);
    if(!a||!b||a.lane===b.lane||a.node.value===1||b.node.value===1) return;
    const link={id:freshId(),[a.lane]:aId,[b.lane]:bId};
    state.config.links=state.config.links.filter(existing=>{
      const ow=locate(state.config,existing.overworld),bt=locate(state.config,existing.battle);
      return ![a.group.id,b.group.id].includes(ow?.group.id)&&![a.group.id,b.group.id].includes(bt?.group.id);
    });
    state.config.links.push(link);
    changed();
  }
  function gap(lane,before='') {
    return `<div class="sl-gap ${before?'':'sl-tail'}" data-drop="gap" data-lane="${lane}" data-before="${before}" aria-hidden="true"></div>`;
  }
  function token(node,lane,missing=false) {
    const linked=state.config.links.some(link=>link[lane]===node.id);
    return `<button type="button" class="sl-token sl-node ${node.value===1?'sl-fixed':''}" data-node="${node.id}" data-linked="${linked}" data-missing="${missing}" data-current="${state.current===node.id}" data-armed="${state.armed===node.id}" ${node.value===1?'disabled aria-label="Fixed normal speed, 1 times"':`draggable="false" data-drop="merge" aria-label="${label(lane)} ${node.value} times. Click to connect or drag to arrange."`}>${node.value}×</button>`;
  }
  function render() {
    const active=root.contains(document.activeElement)?document.activeElement:null;
    const focus=active?.dataset.action?`[data-action="${active.dataset.action}"]`:active?.dataset.node?`button[data-node="${active.dataset.node}"]`:active?.dataset.palette?`[data-palette="${active.dataset.palette}"]`:null;
    const missing=missingConnections(state.config),missingIds=new Set(missing.map(item=>item.group.id));
    root.querySelector('.sl-palette').innerHTML=Array.from({length:9},(_,i)=>i+2).map(value=>`<button type="button" class="sl-token" draggable="false" data-palette="${value}" aria-label="Add ${value} times">${value}×</button>`).join('');
    root.querySelector('.sl-lanes').innerHTML=['overworld','battle'].map(lane=>{
      const groups=state.config.lanes[lane];
      return `<div class="sl-lane"><div class="sl-lane-name">${label(lane)}</div><div class="sl-track" data-drop="gap" data-lane="${lane}">${groups.map((group,gi)=>`${gi?gap(lane,group.id):''}<div class="sl-group ${group.nodes.length>1?'sl-bundle':''}" data-group="${group.id}" data-missing="${missingIds.has(group.id)}" role="group" aria-label="${missingIds.has(group.id)?'Missing connection. ':''}${group.nodes.length>1?'Group: ':''}${group.nodes.map(node=>node.value+' times').join(', ')}">${group.nodes.map((node,ni)=>`${ni?`<div class="sl-inner-gap" data-drop="inside" data-node="${node.id}" aria-hidden="true"></div>`:''}${token(node,lane,group.nodes.length===1&&missingIds.has(group.id))}`).join('')}</div>`).join('')}${gap(lane)}</div></div>`;
    }).join('');
    const current=locate(state.config,state.current),next=destination(state.config,state.memory,state.current),target=next.id?locate(state.config,next.id):null;
    root.querySelector('.sl-preview').innerHTML=`<div class="sl-preview-state" aria-live="polite"><small>${labels.preview}</small><strong>${label(current.lane)} · ${current.node.value}×</strong></div><div class="sl-preview-actions"><button type="button" class="sl-primary" data-action="toggle">${labels.pressKey}</button><button type="button" class="cursor-interaction" data-action="transition" ${target?'':'disabled'}>${current.lane==='overworld'?labels.enterBattle:labels.returnOverworld}${target?' · '+target.node.value+'×':''}</button></div>`;
    root.querySelector('.sl-edit-actions').innerHTML=`<div class="sl-history"><button type="button" class="cursor-interaction" data-action="undo" ${state.cursor===0?'disabled':''}>${labels.undo}</button><button type="button" class="cursor-interaction" data-action="redo" ${state.cursor===state.history.length-1?'disabled':''}>${labels.redo}</button></div><div class="sl-save-area"><span class="sl-draft-status" data-invalid="${missing.length>0}" role="status">${dirty()?labels.unsaved:labels.saved}${missing.length?' · '+missing.length+' missing connection'+(missing.length===1?'':'s'):''}</span><button type="button" class="sl-primary" data-action="save" ${saving?'disabled':''}>${saving?labels.saving:labels.save}</button></div>`;
    const error=root.querySelector('.sl-error');error.hidden=!state.error;error.textContent=state.error;
    if(focus) root.querySelector(focus)?.focus({preventScroll:true});
    cancelAnimationFrame(wireFrame);wireFrame=requestAnimationFrame(drawWires);
  }
  function drawWires() {
    const board=root.querySelector('.sl-board'),bounds=board.getBoundingClientRect(),svg=root.querySelector('.sl-wires');
    svg.setAttribute('viewBox',`0 0 ${Math.max(1,bounds.width)} ${Math.max(1,bounds.height)}`);
    function path(aId,bId,id,fixed=false) {
      const a=root.querySelector(`button[data-node="${aId}"]`),b=root.querySelector(`button[data-node="${bId}"]`);
      if(!a||!b) return '';
      const ar=a.getBoundingClientRect(),br=b.getBoundingClientRect(),x1=ar.left+ar.width/2-bounds.left,y1=ar.bottom-bounds.top+2,x2=br.left+br.width/2-bounds.left,y2=br.top-bounds.top-2;
      const middle=(y1+y2)/2,d=`M ${x1} ${y1} C ${x1} ${middle},${x2} ${middle},${x2} ${y2}`;
      return `${fixed?'':`<path class="sl-wire-hit" data-wire="${id}" d="${d}"></path>`}<path class="sl-wire ${fixed?'sl-wire-fixed':''}" d="${d}"></path>${fixed?'':`<circle class="sl-wire-dot" cx="${x1}" cy="${y1}" r="2.5"></circle><circle class="sl-wire-dot" cx="${x2}" cy="${y2}" r="2.5"></circle>`}`;
    }
    const ow=state.config.lanes.overworld[0].nodes[0].id,bt=state.config.lanes.battle[0].nodes[0].id;
    svg.innerHTML='<title>Explicit entry connections; returns remember the previous group member</title>'+path(ow,bt,'normal',true)+state.config.links.map(link=>path(link.overworld,link.battle,link.id)).join('');
  }
  function dropTarget(element) {
    const target=element?.closest('[data-drop]');
    if(!target||!root.contains(target)) return null;
    return {element:target,kind:target.dataset.drop,lane:target.dataset.lane,node:target.dataset.node,before:target.dataset.before};
  }
  function clearDrop() {root.querySelectorAll('[data-drop-active]').forEach(node=>node.removeAttribute('data-drop-active'));}
  function scrollDrag() {
    if(!touch?.active) return;
    const panel=root.closest('.detail-panel')||document.scrollingElement,bounds=root.closest('.detail-panel')?.getBoundingClientRect()||{top:0,bottom:innerHeight};
    if(bounds) {
      const edge=40,y=touch.pointerY;
      const step=y<bounds.top+edge?-Math.min(18,(bounds.top+edge-y)/2):y>bounds.bottom-edge?Math.min(18,(y-bounds.bottom+edge)/2):0;
      if(step) panel.scrollTop+=step;
    }
    scrollFrame=requestAnimationFrame(scrollDrag);
  }
  function payload(element) {
    const token=element.closest('button[data-node],button[data-palette]');
    if(token) return token.disabled?null:token.dataset.node?{node:token.dataset.node}:{value:Number(token.dataset.palette)};
    const bundle=element.closest('.sl-bundle');
    return bundle?{group:bundle.dataset.group}:null;
  }
  function init() {
    state.history=[copy(state.config)];state.cursor=0;
    root.addEventListener('click',event=>{
      if(suppressClick) {suppressClick=false;return;}
      const wire=event.target.closest('[data-wire]');
      if(wire) {state.config.links=state.config.links.filter(link=>link.id!==wire.dataset.wire);changed();return;}
      const button=event.target.closest('button');if(!button||button.disabled) return;
      if(button.dataset.palette) {state.armed=null;render();return;}
      if(button.dataset.node) {
        const chosen=locate(state.config,button.dataset.node),previous=state.armed?locate(state.config,state.armed):null;
        if(previous&&previous.lane!==chosen.lane) {connect(previous.node.id,chosen.node.id);return;}
        state.armed=state.armed===chosen.node.id?null:chosen.node.id;render();return;
      }
      const action=button.dataset.action;
      if(action==='undo') travelHistory(-1);
      if(action==='redo') travelHistory(1);
      if(action==='save') save();
      if(action==='toggle') {
        remember();const current=locate(state.config,state.current),nodes=flattened(state.config,current.lane),index=nodes.findIndex(node=>node.id===state.current);
        state.current=nodes[(index+1)%nodes.length].id;remember();state.armed=null;
        render();
      }
      if(action==='transition') {
        remember();const from=locate(state.config,state.current),next=destination(state.config,state.memory,state.current);
        if(next.id) {state.current=next.id;remember();const to=locate(state.config,state.current);state.armed=null;render();}
      }
    },{signal:abort.signal});
    root.addEventListener('keydown',event=>{
      if(event.key==='Escape'&&state.armed) {event.preventDefault();event.stopPropagation();state.armed=null;render();}
      if((event.ctrlKey||event.metaKey)&&event.key.toLowerCase()==='z') {event.preventDefault();travelHistory(event.shiftKey?1:-1);}
      if((event.ctrlKey||event.metaKey)&&event.key.toLowerCase()==='y') {event.preventDefault();travelHistory(1);}
    },{signal:abort.signal});
    root.addEventListener('pointerdown',event=>{
      suppressClick=false;
      if(event.button!==0) return;const current=payload(event.target);if(!current) return;
      touch={payload:current,x:event.clientX,y:event.clientY,active:false,element:event.target};
      event.target.setPointerCapture(event.pointerId);
    },{signal:abort.signal});
    root.addEventListener('pointermove',event=>{
      if(!touch) return;
      touch.pointerY=event.clientY;
      if(!touch.active&&Math.hypot(event.clientX-touch.x,event.clientY-touch.y)>8) {touch.active=true;scrollFrame=requestAnimationFrame(scrollDrag);}
      if(!touch.active) return;event.preventDefault();clearDrop();
      const target=dropTarget(document.elementFromPoint(event.clientX,event.clientY));if(target) target.element.dataset.dropActive='true';
      let ghost=editor.querySelector('.sl-drag-label');
      if(!ghost) {ghost=document.createElement('div');ghost.className='sl-drag-label';ghost.setAttribute('aria-hidden','true');editor.appendChild(ghost);}
      const value=touch.payload.value||locate(state.config,touch.payload.node)?.node.value,bounds=editor.getBoundingClientRect();
      ghost.textContent=touch.payload.group?locateGroup(state.config,touch.payload.group).group.nodes.map(node=>node.value+'×').join(' → '):`${value}×`;
      ghost.style.left=`${event.clientX-bounds.left+12}px`;ghost.style.top=`${event.clientY-bounds.top+12}px`;
    },{signal:abort.signal});
    root.addEventListener('pointerup',event=>{
      if(!touch) return;const previous=touch;touch=null;cancelAnimationFrame(scrollFrame);
      editor.querySelector('.sl-drag-label')?.remove();
      if(previous.active) {event.preventDefault();suppressClick=true;const target=dropTarget(document.elementFromPoint(event.clientX,event.clientY));clearDrop();applyDrop(previous.payload,target);}
    },{signal:abort.signal});
    root.addEventListener('pointercancel',()=>{touch=null;cancelAnimationFrame(scrollFrame);editor.querySelector('.sl-drag-label')?.remove();clearDrop();},{signal:abort.signal});
    observer=new ResizeObserver(()=>{cancelAnimationFrame(wireFrame);wireFrame=requestAnimationFrame(drawWires);});observer.observe(root.querySelector('.sl-board'));
    render();
  }
  let observer;
  init();
  root.toggleSpeedDispose=()=>{abort.abort();observer.disconnect();cancelAnimationFrame(wireFrame);cancelAnimationFrame(scrollFrame);};
 },
 dispose(root) {root.toggleSpeedDispose?.();}
};
