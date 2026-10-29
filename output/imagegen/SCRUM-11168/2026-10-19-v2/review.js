"use strict";
const data=window.REVIEW_DATA;
const pageById=new Map(data.screens.map(s=>[s.id,s]));
let current="home",selectedScenario="",navigation=[],actual=false;
let whiteChoice="",whiteReason="",enlargementAccepted=false;
const $=id=>document.getElementById(id);
function makeButton(text,onClick,primary=false){const b=document.createElement("button");b.type="button";b.className="button"+(primary?" primary":"");b.textContent=text;b.addEventListener("click",onClick);return b;}
function renderPages(){
 const q=$("search").value.trim().toLowerCase(),host=$("pages");host.replaceChildren();
 const groups=[...new Set(data.screens.map(s=>s.group))];let count=0;
 for(const group of groups){
  const matches=data.screens.filter(s=>s.group===group&&(!q||(s.title+" "+s.intent+" "+s.id).toLowerCase().includes(q)));if(!matches.length)continue;
  const h=document.createElement("h2");h.textContent=group;host.append(h);
  for(const s of matches){const b=document.createElement("button");b.type="button";b.className="page";b.dataset.page=s.id;b.setAttribute("aria-current",s.id===current?"page":"false");const n=document.createElement("small");n.textContent=String(data.screens.indexOf(s)+1).padStart(2,"0");b.append(n,document.createTextNode(s.title));b.addEventListener("click",()=>show(s.id));host.append(b);count++;}
 }if(!count){const p=document.createElement("p");p.className="no-results";p.textContent="没有匹配的页面";host.append(p);}
}
function renderScenario(){
 const scenario=data.scenarios.find(s=>s.id===selectedScenario);$("scenario-nav").replaceChildren();
 $("scenario-note").textContent=scenario?(scenario.note||"按节点查看这组流程；下方情景操作可查看分支。"):"从左侧选择页面，或选择一条路线查看先后与分支。";
 if(!scenario)return;scenario.nodes.forEach((id,i)=>{const b=document.createElement("button");b.type="button";b.textContent=(i+1)+" "+pageById.get(id).title;b.setAttribute("aria-current",id===current?"step":"false");b.addEventListener("click",()=>show(id));$("scenario-nav").append(b);});
}
function toast(message){$("toast").textContent=message;}
function renderInput(){
 const host=$("input-demo");host.replaceChildren();
 if(current==="white-ink"){
  const box=document.createElement("div");box.className="input-demo";const note=document.createElement("p");note.textContent="下方演示输入对应图中的未选择状态；请选择类型并写明理由。";
  const label=document.createElement("label");label.textContent="白墨分支";const select=document.createElement("select");select.id="white-choice";select.setAttribute("aria-label","演示白墨分支");
  [["","请选择，未设默认值"],["0","精细图案 · 0 px"],["1","普通图案 · 1 px"],["2","大块实底 · 2 px"]].forEach(([v,t])=>{const o=document.createElement("option");o.value=v;o.textContent=t;select.append(o);});select.value=whiteChoice;
  const reasonLabel=document.createElement("label");reasonLabel.textContent="选择理由";const reason=document.createElement("textarea");reason.id="white-reason";reason.setAttribute("aria-label","演示白墨选择理由");reason.placeholder="例如：保留细小花叶边缘";reason.value=whiteReason;
  select.addEventListener("change",()=>{whiteChoice=select.value;updateGuards();});reason.addEventListener("input",()=>{whiteReason=reason.value;updateGuards();});label.append(select);reasonLabel.append(reason);box.append(note,label,reasonLabel);host.append(box);
 }
 if(current==="enlargement"){const box=document.createElement("div");box.className="input-demo";const label=document.createElement("label"),check=document.createElement("input");check.type="checkbox";check.id="enlargement-consent";check.checked=enlargementAccepted;check.addEventListener("change",()=>{enlargementAccepted=check.checked;updateGuards();});label.append(check,document.createTextNode(" 我了解当前 150 × 200 mm 示例尺寸的放大风险（仅本次演示）"));box.append(label);host.append(box);}
}
function updateGuards(){document.querySelectorAll("[data-guard]").forEach(b=>{b.disabled=b.dataset.guard==="white-input"?!(whiteChoice!==""&&whiteReason.trim()):b.dataset.guard==="enlargement"?!enlargementAccepted:false;});}
function renderActions(){
 const host=$("actions");host.replaceChildren();const edges=data.edges[current]||[];
 edges.forEach(([label,target,guard],i)=>{const b=makeButton(label,()=>{
  if(b.disabled)return;
  if(guard==="enlargement"){show(target);toast("已接受本次示例尺寸风险，请另行确认尺寸。");return;}
  if(current==="return-confirm"&&target==="crop"){whiteChoice="";whiteReason="";enlargementAccepted=false;}
  if(current==="collision"&&target==="save"){show(target);toast("示例：已采用新名称；尚未保存。");return;}
  if(current==="png-saved"&&target==="home"){show(target);toast("示例：PNG处理任务已完成。");return;}
  if(current==="save-uncertain"&&target==="save-uncertain"){toast("示例：仍未核实；保留当前记录，不重复写入。");return;}show(target);
 },i===0);if(guard)b.dataset.guard=guard;host.append(b);});updateGuards();
}
function show(id,remember=true){
 if(!pageById.has(id))return;if(remember&&id!==current)navigation.push(current);current=id;const s=pageById.get(id);
 $("page-title").textContent=s.title;$("page-intent").textContent=s.intent;$("screen").src=s.file;$("screen").alt=s.title+"："+s.intent;$("original").href=s.file;$("category").textContent=s.group;$("primary-label").textContent="画面主操作："+s.primary;$("hidden").textContent=s.hidden||"仅显示当前情景需要的内容。";
 $("back").disabled=!navigation.length;toast("");renderPages();renderScenario();renderInput();renderActions();try{history.replaceState(null,"","#"+id);}catch{}$("page-title").focus({preventScroll:true});
}
$("search").addEventListener("input",renderPages);$("back").addEventListener("click",()=>{if(navigation.length)show(navigation.pop(),false);});
$("fit").addEventListener("click",()=>{actual=!actual;$("canvas").classList.toggle("actual",actual);$("fit").textContent=actual?"适合页面宽度":"查看原尺寸";});
data.scenarios.forEach(s=>{const o=document.createElement("option");o.value=s.id;o.textContent=s.name;$("scenario").append(o);});
$("scenario").addEventListener("change",()=>{selectedScenario=$("scenario").value;const s=data.scenarios.find(x=>x.id===selectedScenario);whiteChoice="";whiteReason="";enlargementAccepted=false;navigation=[];if(s)show(s.nodes[0],false);else renderScenario();});
window.addEventListener("hashchange",()=>{const id=location.hash.slice(1);if(pageById.has(id)&&id!==current)show(id);});const initial=location.hash.slice(1);show(pageById.has(initial)?initial:"home",false);
window.prototypeReview={navigate:show,getCurrent:()=>current,getScenario:()=>selectedScenario};

