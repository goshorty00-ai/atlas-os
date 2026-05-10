const fs = require('fs');
const c = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js', 'utf8');

console.log('=== AI DIGEST BUTTON ===');
const digIdx = c.indexOf('AI Digest');
console.log(c.substring(Math.max(0,digIdx-400), digIdx+300));

console.log('\n=== z HANDLER (AI DIGEST CLICK) ===');
const zIdx = c.indexOf('z=()=>{if(!G){X(');
console.log(c.substring(zIdx, zIdx+1200));

console.log('\n=== Q STATE ===');
const qi = c.indexOf('[Q,X]=le.useState');
console.log(c.substring(qi, qi+100));

console.log('\n=== canRunRulesScan prop / Te ===');
const ti = c.indexOf('canRunRulesScan:Te');
console.log(c.substring(Math.max(0,ti-200), ti+200));

console.log('\n=== toast code ===');
const ti2 = c.indexOf('atlas-digest-toast');
console.log(ti2 !== -1 ? c.substring(ti2, ti2+400) : 'NOT FOUND');

console.log('\n=== posts to WPF? (postMessage in z handler) ===');
const zBody = c.substring(zIdx, zIdx+1200);
console.log('postMessage in z:', zBody.includes('postMessage'));
console.log('selectedMessageDetail in z:', zBody.includes('selectedMessageDetail'));
console.log('recentMessages in z:', zBody.includes('recentMessages'));
console.log('inboxMessages in z:', zBody.includes('inboxMessages'));
