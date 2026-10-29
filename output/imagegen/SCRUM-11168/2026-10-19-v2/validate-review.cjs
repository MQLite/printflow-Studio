"use strict";
const fs=require("fs"),path=require("path"),vm=require("vm"),crypto=require("crypto");
const root=__dirname,m=JSON.parse(fs.readFileSync(path.join(root,"prototype-manifest.json"),"utf8"));
const assets=[],errors=[];
function hash(p){return crypto.createHash("sha256").update(fs.readFileSync(p)).digest("hex").toUpperCase();}
const ids=new Set(),files=new Set();
for(const s of m.screens){
 if(ids.has(s.id)||files.has(s.file))errors.push("duplicate "+s.id);ids.add(s.id);files.add(s.file);
 const p=path.join(root,s.file);if(!fs.existsSync(p)){errors.push("missing "+s.file);continue;}
 const b=fs.readFileSync(p);if(b.subarray(0,8).toString("hex")!=="89504e470d0a1a0a")errors.push("not PNG "+s.file);
 const targetHash=hash(p);if(targetHash!==hash(s.source))errors.push("source copy drift "+s.file);
 assets.push({id:s.id,file:s.file,width:b.readUInt32BE(16),height:b.readUInt32BE(20),bytes:b.length,sha256:targetHash,sourceCopyVerified:true});
}
for(const s of m.scenarios)for(const id of s.nodes)if(!ids.has(id))errors.push("broken scenario "+s.id+"->"+id);
let edgeCount=0;for(const [from,edges] of Object.entries(m.edges)){if(!ids.has(from))errors.push("unknown edge source "+from);for(const [label,to,guard]of edges){edgeCount++;if(!ids.has(to))errors.push("broken edge "+from+"->"+to);if(guard&&!["white-input","enlargement"].includes(guard))errors.push("unknown guard");if(!label)errors.push("empty action");}}
for(const s of m.screens)if(!(s.prompt||s.inherited))errors.push("missing prompt "+s.id);
const documentNodes=new Map();
class Element{
 constructor(tag){this.tagName=tag;this.children=[];this.events={};this.attrs={};this.dataset={};this.value="";this.textContent="";this.disabled=false;this.checked=false;this.className="";this.classList={toggle:()=>{}};}
 set id(v){this._id=v;documentNodes.set(v,this);}get id(){return this._id;}
 append(...items){for(const item of items){if(item&&typeof item==="object")item.parent=this;this.children.push(item);}}
 replaceChildren(...items){for(const x of this.children)if(x&&typeof x==="object")x.parent=null;this.children=[];this.append(...items);}
 setAttribute(k,v){this.attrs[k]=v;}addEventListener(k,f){this.events[k]=f;}focus(){focused=this.id;}getAttribute(k){return this.attrs[k];}
}
let focused="";
const rootElement=new Element("root");
const requiredIds=["search","pages","scenario","scenario-nav","scenario-note","page-title","page-intent","screen","original","category","primary-label","hidden","back","fit","canvas","input-demo","actions","toast"];
for(const id of requiredIds){const e=new Element("element");e.id=id;rootElement.append(e);}
function descendants(e){return [e,...e.children.filter(x=>x&&typeof x==="object").flatMap(descendants)];}
const document={createElement:t=>new Element(t),createTextNode:t=>({tagName:"#text",textContent:t,children:[]}),getElementById:id=>documentNodes.get(id),querySelectorAll:q=>q==="[data-guard]"?descendants(rootElement).filter(e=>e.dataset?.guard):[]};
const context={document,window:{addEventListener:()=>{}},location:{hash:"#home"},history:{replaceState:()=>{}},console,Map,Set};
vm.createContext(context);
vm.runInContext(fs.readFileSync(path.join(root,"review-data.js"),"utf8"),context);
const browserJs=fs.readFileSync(path.join(root,"review.js"),"utf8");new vm.Script(browserJs);vm.runInContext(browserJs,context);
const api=context.window.prototypeReview,$=id=>documentNodes.get(id);
const checks=[];
function check(name,condition){checks.push({name,result:condition?"PASS":"FAIL"});if(!condition)errors.push(name);}
function clickText(text){const b=$("actions").children.find(x=>x.textContent===text);if(!b)throw Error("Missing button "+text);if(!b.disabled)b.events.click();return b;}
check("Initial home and 45 visible page choices",api.getCurrent()==="home"&&$("pages").children.filter(x=>x.dataset?.page).length===45);
check("Neutral focus, no auto approval",focused==="page-title");
for(const s of m.screens){api.navigate(s.id);check("View "+s.id,api.getCurrent()===s.id&&$("screen").src===s.file&&$("page-title").textContent===s.title);}
api.navigate("white-ink");
check("White branch has no default",$("white-choice").value==="");
check("White confirmation disabled without inputs",$("actions").children[0].disabled);
$("white-choice").value="0";$("white-choice").events.change();check("Reason remains mandatory",$("actions").children[0].disabled);
$("white-reason").value="   ";$("white-reason").events.input();check("Whitespace reason refused",$("actions").children[0].disabled);
$("white-reason").value="保留细小花叶";$("white-reason").events.input();check("Explicit zero-pixel choice and reason accepted",!$("actions").children[0].disabled);
clickText("确认白墨选择");check("White confirmation goes to preflight",api.getCurrent()==="preflight");
api.navigate("enlargement");check("Enlargement consent unchecked and authorization disabled",!$("enlargement-consent").checked&&$("actions").children[1].disabled);
$("enlargement-consent").checked=true;$("enlargement-consent").events.change();clickText("允许此次放大，回到尺寸确认");check("Enlargement returns to size confirmation, no auto-generation",api.getCurrent()==="dimensions"&&$("toast").textContent.includes("另行确认尺寸"));
api.navigate("return-confirm");clickText("确认返回并重新裁切");api.navigate("white-ink");check("Upstream return clears old white choice",$("white-choice").value===""&&$("actions").children[0].disabled);
api.navigate("enlargement");check("Upstream return clears enlargement consent",!$("enlargement-consent").checked);
api.navigate("return-confirm");clickText("取消，保留原状态");check("Cancel return goes to dimensions",api.getCurrent()==="dimensions");
api.navigate("draft-leave");clickText("继续编辑");check("Cancel draft exit returns to dimensions",api.getCurrent()==="dimensions");
api.navigate("takeover");clickText("继续自动处理（取消）");check("Cancel takeover returns to Photoshop running",api.getCurrent()==="tiff-running");
api.navigate("abandon");clickText("继续当前任务");check("Cancel abandon returns to current review",api.getCurrent()==="review");
api.navigate("save-uncertain");clickText("模拟仍未核实");check("Uncertain save stays unresolved without duplicate write",api.getCurrent()==="save-uncertain"&&$("toast").textContent.includes("不重复写入"));
api.navigate("collision");clickText("使用建议名称后确认保存");check("Rename does not imply saved",api.getCurrent()==="save"&&$("toast").textContent.includes("尚未保存"));
api.navigate("png-final");clickText("仅通过，进入PNG保存");check("Asset approval leads to PNG-only save",api.getCurrent()==="png-save");
clickText("模拟保存成功");check("Asset save leads to PNG-only success",api.getCurrent()==="png-saved");
$("scenario").value="asset";$("scenario").events.change();check("Selecting route scenario resets preview to first step",api.getScenario()==="asset"&&api.getCurrent()==="home");
check("Asset route does not contain print dimensions, white-ink or TIFF states",!m.scenarios.find(x=>x.id==="asset").nodes.some(x=>["dimensions","white-ink","preflight","tiff-running","tiff-review","save","saved"].includes(x)));
check("Direct TIFF scenario has no Meitu or trim",!m.scenarios.find(x=>x.id==="tiff").nodes.some(x=>["enhance-ready","enhance-running","background-ready","background-running","crop","crop-review"].includes(x)));
$("search").value="保存待核实";$("search").events.input();check("Search safely returns empty without error",$("pages").children.some(x=>x.textContent==="没有匹配的页面"));
$("search").value="保存结果待核实";$("search").events.input();check("Search locates specific state",$("pages").children.some(x=>x.dataset?.page==="save-uncertain"));
const oldRoot=path.resolve(root,"../2026-10-19-v1"),oldVerification=JSON.parse(fs.readFileSync(path.join(oldRoot,"verification.json"),"utf8"));
const v1preserved=oldVerification.files.every(x=>hash(path.join(oldRoot,x.file))===x.sha256);check("Original six v1 assets preserved",v1preserved);
const localFiles=["index.html","review.css","review.js","review-data.js","design-spec.html","prototype-manifest.json","flow-map.json"];for(const f of localFiles)if(!fs.existsSync(path.join(root,f)))errors.push("missing supporting file "+f);
const record={status:errors.length?"FAIL":"LOCAL_ASSET_AND_FLOW_CHECKS_PASS",date:"2026-10-19",imageCount:assets.length,newImageCount:m.coverage.newImageScreens,flowScenarios:m.scenarios.length,flowEdges:edgeCount,checkCount:checks.length,passed:checks.filter(x=>x.result==="PASS").length,errors,checks,assets,
browserExecution:"NOT RUN - in-app browser security policy blocks file protocol; no server or alternate browser workaround attempted",
validationMethod:"PNG signatures and source hashes; reference integrity; JavaScript syntax and deterministic isolated mock-DOM behavior checks; generated images visually inspected",
physicalWindowDpi:"NOT RUN",productBuildTests:"NOT RUN - product code unchanged",productionAutomation:"NOT RUN",noviceAcceptance:"NOT RUN",styleApproval:"APPROVED_BY_USER",expandedPrototypeApproval:m.styleApproval.fullPrototypeApproved?"APPROVED_BY_USER":"PENDING",v1AssetsPreserved:v1preserved};
const reportName=process.argv[2]||"verification.json";
if(path.basename(reportName)!==reportName||!reportName.endsWith(".json"))throw new Error("Report must be a local JSON filename");
fs.writeFileSync(path.join(root,reportName),JSON.stringify(record,null,2)+"\n");
fs.writeFileSync(path.join(root,"asset-integrity.json"),JSON.stringify(assets,null,2)+"\n");
console.log(JSON.stringify({status:record.status,images:assets.length,scenarios:m.scenarios.length,edges:edgeCount,passed:record.passed,checks:record.checkCount,errors}));if(errors.length)process.exitCode=1;
