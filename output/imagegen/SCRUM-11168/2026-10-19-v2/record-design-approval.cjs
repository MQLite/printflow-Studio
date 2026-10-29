const fs=require('node:fs');
const path=require('node:path');
const crypto=require('node:crypto');
const root=__dirname;
const file=n=>path.join(root,n);
const read=n=>fs.readFileSync(file(n),'utf8');
const write=(n,s)=>fs.writeFileSync(file(n),s,'utf8');
const hash=p=>crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
const approval=JSON.parse(read('design-approval.json'));
if(approval.recorded) throw new Error('Approval already recorded; do not replay.');
approval.hashesBefore={manifest:hash(file('prototype-manifest.json')),spec:hash(file('design-spec.html')),verification:hash(file('verification.json'))};
const manifest=JSON.parse(read('prototype-manifest.json'));
manifest.status='DESIGN_APPROVED_IMPLEMENTATION_PLAN_READY_FOR_REVIEW';
manifest.styleApproval.fullPrototypeApproved=true;
manifest.styleApproval.fullPrototypeSource='用户：通过。请继续。';
manifest.styleApproval.productImplementationApproved=false;
manifest.styleApproval.implementationNextStep='审阅SCRUM-11177具体实施计划并选择执行方式';
manifest.designApprovalRecord='design-approval.json';
for(const issue of manifest.issues){
  if(['SCRUM-11174','SCRUM-11175','SCRUM-11176'].includes(issue.key)){
    issue.status='DESIGN_ACCEPTED';issue.jiraStatus='Done';
  } else if(issue.key==='SCRUM-11177'){
    issue.status='IMPLEMENTATION_PLAN_READY_FOR_REVIEW';issue.jiraStatus='To Do';
  }
}
manifest.reviewNotes=manifest.reviewNotes.map(n=>n.includes('v1已批准的是视觉方向')?'用户于2026-10-19批准扩展v2设计；窗口/缩放矩阵是验证目标，真实产品交互、工作站和新手验收须另行验证。':n);
write('prototype-manifest.json',JSON.stringify(manifest,null,2)+'\n');
write('index.html',read('index.html').replace('视觉方向已认可 · 扩展版待评审','扩展版设计已批准 · 实施计划待审阅'));
let spec=read('design-spec.html');
const replacements=[
 ['首版视觉方向已获用户认可。本轮补齐视觉与流程原型；扩展版页面、完整交互、工作站窗口/缩放与正式文案仍待评审。没有启动WPF组件实现。','用户于2026-10-19回复“通过。请继续。”，批准本扩展版视觉与流程设计。窗口/缩放矩阵作为验证目标；真实产品交互、工作站与新手验收尚未进行。<a href="design-approval.json">批准记录</a> · <a href="../../../../docs/superpowers/plans/2026-10-19-scrum-11177-guided-theme.md">SCRUM-11177实施计划</a>已准备，尚未启动WPF产品代码。'],
 ['候选矩阵：现有1000×700窗口基线、1280×720和1920×1080代表性环境，100%/125%/150%缩放。以上仍是待工作站核实的设计目标；图片不能证明真实窗口响应式或高DPI可用。','已批准的验证目标矩阵：现有1000×700 DIP窗口基线、1280×720和1920×1080代表性环境，100%/125%/150%缩放。实施时区分显示器物理像素与窗口DIP；实际支持情况仍待工作站核实，图片不能证明真实窗口响应式或高DPI可用。'],
 ['中文人工评审；保留英文资源结构，不增加英文人工走查。负责人须核对扩展页面、三路线显隐、异常取消/返回、正式文案和支持矩阵，之后再决定实现计划。','扩展页面、三路线显隐、异常取消/返回和验证目标已获设计批准。保持中文人工验收及英文资源结构，不增加英文人工走查；具体WPF实施范围与执行方式见待审阅计划。'],
 ['需求及边界评审，不代表底层缺陷修复。','设计评审通过，Jira Done；不代表底层缺陷修复。'],
 ['扩展版组件/中文文案、真实窗口核对。','规范评审通过，Jira Done；真实窗口核对属于后续实现验证。'],
 ['完整设计评审和支持矩阵批准；不代替实际产品验收。','完整设计及验证目标批准，Jira Done；不代替实际产品验收。'],
 ['<td>未开始。</td><td>设计与实施计划评审后再实现WPF主题和组件。</td>','<td>实施计划已准备，代码未开始。</td><td>审阅具体计划并选择执行方式后，实现WPF主题和组件。</td>'],
 ['Epic和旧任务没有被标记完成。','仅SCRUM-11174～11176设计任务已完成；Epic与旧任务未被关闭。']
];
for(const [from,to] of replacements){if(!spec.includes(from)) throw new Error('Spec expected text absent: '+from);spec=spec.replace(from,to);}
write('design-spec.html',spec);
approval.hashesAfter={manifest:hash(file('prototype-manifest.json')),spec:hash(file('design-spec.html')),plan:hash(approval.implementation.plan),verification:hash(file('verification.json'))};
if(approval.hashesBefore.verification!==approval.hashesAfter.verification) throw new Error('Historical verification changed.');
approval.recorded=true;
write('design-approval.json',JSON.stringify(approval,null,2)+'\n');
console.log(JSON.stringify({designApproved:true,jiraDesignTasks:'Done',productCodeStarted:false,planReview:'PENDING',historicalVerificationPreserved:true}));
