const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const {pathToFileURL}=require('node:url');
const {chromium}=require('playwright');
const project=path.resolve(__dirname,'../..'),assets=path.join(project,'tracker/src/Tracker/Ironmon.Tracker.App/wwwroot');
const source=fs.readFileSync(path.join(assets,'toggle-speed.js'),'utf8');
const assert=(condition,message)=>{if(!condition)throw Error(message);};
const clone=value=>JSON.parse(JSON.stringify(value));
const initial={lanes:{overworld:[],battle:[]},links:[]};
for(const [lane,prefix] of [['overworld','ow'],['battle','bt']]) initial.lanes[lane]=[1,2,3].map(value=>({id:`${prefix}-group-${value}`,nodes:[{id:`${prefix}-${value}`,value}]}));
initial.links=[2,3].map(value=>({id:`matching-${value}`,overworld:`ow-${value}`,battle:`bt-${value}`}));
const strings=fs.readFileSync(path.join(project,'tracker/src/Tracker/Ironmon.Tracker.App/Resources/Localization/TrackerResources.resx'),'utf8');
const labels={};
for(const match of strings.matchAll(/<data name="Settings.ToggleSpeed.([^"]+)"[^>]*><value>(.*?)<\/value><\/data>/g)) labels[match[1][0].toLowerCase()+match[1].slice(1)]=match[2];
assert(labels.save==='Save changes','Production localization labels loaded');
const context={window:{},AbortController};vm.createContext(context);
vm.runInContext(source.replace('  init();','render=()=>{};globalThis.model={state,valid,missingConnections,destination,locate,flattened,applyDrop,connect,travelHistory,save};'),context);
context.window.ironmonToggleSpeed.attach({querySelector:()=>({})},initial,{invokeMethodAsync:async()=>null},labels);
const m=context.model;
let seed=761,mutations=0;
const random=max=>{seed^=seed<<13;seed^=seed>>>17;seed^=seed<<5;return(seed>>>0)%max;};
for(let run=0;run<80;run++) {
 m.state.config=clone(initial);m.state.saved=clone(initial);m.state.history=[clone(initial)];m.state.cursor=0;
 for(let step=0;step<24;step++) {
  const lane=random(2)?'overworld':'battle',nodes=m.flattened(m.state.config,lane).filter(node=>node.value!==1),choice=random(5);
  if(choice===0||!nodes.length)m.applyDrop({value:2+random(9)},{kind:'gap',lane});
  else {
   const node=nodes[random(nodes.length)];
   if(choice===1)m.applyDrop({node:node.id},{kind:'remove'});
   if(choice===2)m.applyDrop({node:node.id},{kind:'merge',node:nodes[random(nodes.length)].id});
   if(choice===3)m.applyDrop({node:node.id},{kind:'gap',lane,before:m.state.config.lanes[lane][random(m.state.config.lanes[lane].length)].id});
   if(choice===4)m.applyDrop({group:m.locate(m.state.config,node.id).group.id},{kind:'gap',lane,before:m.state.config.lanes[lane][random(m.state.config.lanes[lane].length)].id});
  }
  assert(m.valid(m.state.config),'Drag mutation preserves the graph structure');mutations++;
  if(!m.missingConnections(m.state.config).length)for(const name of ['overworld','battle'])for(const stage of m.flattened(m.state.config,name)) {
   const target=m.destination(m.state.config,{},stage.id);
   assert(target.id&& (stage.value===1||m.locate(m.state.config,target.id).node.value!==1),'Every complete accelerated position has an accelerated destination');
  }
 }
}
(async()=>{
 let browser;
 try {
  browser=await chromium.launch({headless:true,...(process.env.IRONMON_BROWSER?{executablePath:process.env.IRONMON_BROWSER}:{})});
  const page=await browser.newPage({viewport:{width:500,height:809}}),errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  await page.goto(pathToFileURL(path.join(project,'data/run-management-speed-tab-preview.html')).href);
  await page.addScriptTag({content:source});
  await page.evaluate(({initial,labels})=>{window.saves=[];window.rejectSave=false;window.ironmonToggleSpeed.attach(document.querySelector('.toggle-speed-root'),initial,{invokeMethodAsync:async(method,profile)=>{window.saves.push(profile);return window.rejectSave?'Connection rejected the profile':null;}},labels);},{initial,labels});
  fs.mkdirSync(path.join(project,'data/speed-up-validation'),{recursive:true});
  const root=page.locator('.toggle-speed-root'),ow=root.locator('.sl-track[data-lane="overworld"]');
  assert((await ow.locator('button.sl-node').allTextContents()).join(',')==='1×,2×,3×','Actual editor default values');
  const bounds=await page.locator('.toggle-speed-settings').boundingBox();
  assert(bounds.width<=500&&bounds.x>=0&&bounds.x+bounds.width<=500,'Speed settings card fits its 500px window');
  await page.screenshot({path:path.join(project,'data/speed-up-validation/editor-default.png')});
  await root.locator('[data-palette="6"]').dragTo(ow.locator('.sl-tail'));
  const six=ow.locator('button[data-node]').filter({hasText:'6×'});
  assert(await six.count()===1,'Native pointer drag adds a multiplier');
  assert(await six.getAttribute('data-missing')==='true','Unconnected stage visibly invalid');
  await root.locator('[data-action="save"]').click();
  assert(await page.evaluate(()=>window.saves.length)===0,'Incomplete draft never reaches persistence');
  assert(await root.locator('.sl-error').isVisible(),'Incomplete save explains the error');
  await six.dragTo(root.locator('[data-node="ow-2"]'));
  assert(await ow.locator('.sl-bundle').count()===1,'Dropping onto a multiplier groups it');
  assert(await ow.locator('.sl-bundle').getAttribute('data-missing')==='false','Existing group connection covers new member');
  await page.evaluate(()=>window.rejectSave=true);
  await root.locator('[data-action="save"]').click();
  await page.waitForFunction(()=>document.querySelector('.sl-error').textContent.includes('Connection rejected'));
  assert((await root.locator('.sl-draft-status').textContent()).includes('Unsaved'),'Rejected transport preserves draft');
  await page.evaluate(()=>window.rejectSave=false);
  await root.locator('[data-action="save"]').click();
  await page.waitForFunction(()=>document.querySelector('.sl-draft-status').textContent==='Saved');
  await root.locator('[data-action="toggle"]').click();
  await root.locator('[data-action="toggle"]').click();
  assert((await root.locator('[data-current="true"]').textContent())==='6×','Preview advances through group members');
  await root.locator('[data-action="transition"]').click();
  assert((await root.locator('[data-current="true"]').textContent())==='2×','Preview uses explicit battle entry');
  await root.locator('[data-action="transition"]').click();
  assert((await root.locator('[data-current="true"]').textContent())==='6×','Preview restores exact overworld member');
  const groupId=await ow.locator('.sl-bundle').getAttribute('data-group');
  await ow.locator('.sl-bundle').dragTo(ow.locator('.sl-tail'),{sourcePosition:{x:6,y:6}});
  assert((await ow.locator('button[data-node]').allTextContents()).join(',')==='1×,3×,2×,6×','Whole group moves in configured order: '+(await ow.locator('button[data-node]').allTextContents()).join(','));
  assert(await ow.locator('.sl-bundle').getAttribute('data-group')===groupId,'Whole group keeps identity');
  await root.locator('[data-action="undo"]').click();
  assert((await ow.locator('button[data-node]').allTextContents()).join(',')==='1×,2×,6×,3×','Undo restores the whole group');
  await root.locator('[data-action="redo"]').click();
  assert((await ow.locator('button[data-node]').allTextContents()).join(',')==='1×,3×,2×,6×','Redo reapplies group order');
  await root.locator('[data-palette="2"]').dragTo(ow.locator('.sl-tail'));
  assert(await ow.locator('button[data-node]').filter({hasText:'2×'}).count()===2,'Duplicate multipliers allowed');
  await ow.locator('button[data-node]').filter({hasText:'6×'}).dragTo(ow.locator('.sl-tail'));
  assert(await ow.locator('.sl-bundle').count()===0,'Member drags out of group');
  await ow.locator('button[data-node]').filter({hasText:'6×'}).dragTo(root.locator('.sl-palette'));
  assert(await ow.locator('button[data-node]').filter({hasText:'6×'}).count()===0,'Palette removes dragged members');
  const extra=ow.locator('button[data-node]').filter({hasText:'2×'}).last();
  await extra.click();await root.locator('[data-node="bt-3"]').click();
  assert(await extra.getAttribute('data-linked')==='true','Clicking opposite multipliers creates entry connection');
  assert(await root.locator('[data-node="ow-3"]').getAttribute('data-missing')==='true','Rewiring reveals abandoned group');
  await root.locator('path.sl-wire-hit').last().click();
  assert(await extra.getAttribute('data-missing')==='true','Clicking the wire removes the connection');
  for(const width of [360,420,500,700]) {
   await page.setViewportSize({width,height:809});
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'No horizontal overflow at '+width);
  }
  await page.setViewportSize({width:500,height:809});
  await page.screenshot({path:path.join(project,'data/speed-up-validation/editor-edited.png')});
  await page.evaluate(()=>window.ironmonToggleSpeed.dispose(document.querySelector('.toggle-speed-root')));
  assert(!errors.length,errors.join('\n'));
  console.log(JSON.stringify({status:'PASS',modelMutations:mutations,viewports:[360,420,500,700],productionPointerDrag:true,saveRejectionAndHistory:true}));
 }finally{await browser?.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
