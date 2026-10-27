"use strict";
let streaming=false, count=0, errors=0;
const mathCache=new Map();
const escapeHtml=s=>s.replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
function token(src,display){
 if(display){
  const env=/^\\begin\{(equation\*?|align\*?|gather\*?|aligned|gathered)\}/.exec(src);
  if(env){const close='\\end{'+env[1]+'}',end=src.indexOf(close,env[0].length);const raw=end<0?src:src.slice(0,end+close.length);return {type:'blockMath',raw,text:raw,display:true,complete:end>=0};}
 }
 const open=display?(src.startsWith('\\[')?'\\[':src.startsWith('$$')?'$$':null):(src.startsWith('\\(')?'\\(':src.startsWith('$')&&!src.startsWith('$$')?'$':null);
 if(!open)return;
 if(open==='$'&&/\s/.test(src[1]||' '))return;
 const close=open==='\\['?'\\]':open==='\\('?'\\)':open;
 let end=src.indexOf(close,open.length);
 while(end>0&&src[end-1]==='\\')end=src.indexOf(close,end+close.length);
 if(open==='$'&&(end>0&&/\s/.test(src[end-1])||end<0&&/^\$\d/.test(src)))return;
 return {type:display?'blockMath':'inlineMath',raw:end<0?src:src.slice(0,end+close.length),text:end<0?src.slice(open.length):src.slice(open.length,end),display,complete:end>=0};
}
function math(t){
 if(!t.complete)return '<span class="math-pending">'+(streaming?'〔公式生成中…〕':'〔公式未闭合，可复制原文检查〕')+'</span>';
 try{
  if(t.text.length>16000)throw Error('公式过长');
  const key=(t.display?'D':'I')+t.text;
  if(mathCache.has(key)){count++;return mathCache.get(key);}
  const result=katex.renderToString(t.text,{displayMode:t.display,throwOnError:true,trust:false,strict:'ignore',maxExpand:1000,maxSize:20,output:'htmlAndMathml'});
  if(mathCache.size>=64)mathCache.delete(mathCache.keys().next().value);mathCache.set(key,result);
  count++;return result;
 }catch(e){errors++;return '<span class="math-error" title="'+escapeHtml(String(e.message).slice(0,180))+'">〔此公式无法排版，可复制原文检查〕</span>';}
}
marked.use({extensions:[
 {name:'blockMath',level:'block',start:s=>s.search(/\\\[|\$\$|\\begin\{(?:equation\*?|align\*?|gather\*?|aligned|gathered)\}/),tokenizer:s=>token(s,true),renderer:math},
 {name:'inlineMath',level:'inline',start:s=>s.search(/\\\(|\$/),tokenizer:s=>token(s,false),renderer:math}
]});
window.renderAnswer=(text,size,pending)=>{
 streaming=pending;count=errors=0;const oldY=window.scrollY;
 document.documentElement.style.fontSize=Math.max(14,Math.min(28,size))+'px';
 const html=marked.parse(text.length>100000?text.slice(0,100000)+'\n\n（显示长度已达上限，复制回答可获得完整原文。）':text);
 document.getElementById('answer').innerHTML=DOMPurify.sanitize(html,{ADD_TAGS:['annotation'],ADD_ATTR:['encoding'],FORBID_TAGS:['img','iframe','object','embed','script','style','form','input'],FORBID_ATTR:['href','src','srcset','action']});
 window.scrollTo(0,oldY);
 return {count,errors};
};
document.addEventListener('click',e=>{if(e.target.closest('a'))e.preventDefault();});
window.chrome?.webview?.postMessage('ready');
