import fs from 'node:fs';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {createHash} from 'node:crypto';
const native=fs.readFileSync(new URL('../src/Theme.cs',import.meta.url),'utf8');
const web=fs.readFileSync(new URL('./todo.js',import.meta.url),'utf8');
const colors=name=>[...native.match(new RegExp(name+' = \\{([^}]+)'))[1].matchAll(/#[0-9A-F]{6}/g)].map(m=>m[0]);
const scope={};
vm.runInNewContext(web.slice(web.indexOf('const palette'),web.indexOf('let groups'))+';this.colors=palette;this.colorAt=shade;',scope);
assert.deepEqual(JSON.parse(JSON.stringify(scope.colors)),colors('ReferenceColors').map((c,i)=>[c,colors('LightColors')[i]]));
assert.match(native,/Math\.Exp\(-Math\.Max\(0, depth\) \/ 3\.0\)/);
for(let i=0;i<7;i++){
  const rgb=h=>[1,3,5].map(n=>parseInt(h.slice(n,n+2),16));
  assert.equal(scope.colorAt(i,0),'rgba('+rgb(colors('ReferenceColors')[i]).join(',')+',0.9215686275)');
  assert.equal(scope.colorAt(i+7,3),scope.colorAt(i,3));
  assert.notEqual(scope.colorAt(i,0),scope.colorAt(i,4));
}
assert.match(web,/let groups = \[\{id:'watching',title:'Watching list',items:\[\]\}\]/);
assert.doesNotMatch(web,/placeholder\s*=|const examples|extraExamples|⠿/);
const hash=p=>createHash('sha256').update(fs.readFileSync(new URL(p,import.meta.url))).digest('hex');
assert.equal(hash('./todo-icon.png'),hash('../src/app-icon.png'));
console.log('PASS: native palette, gradient contract, empty start, no placeholders or drag grips, shared icon');
